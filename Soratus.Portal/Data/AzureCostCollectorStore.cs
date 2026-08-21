using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Soratus.Portal.Data;

/// <summary>
/// De claim van één dagelijkse kostenrun: één document per dag, in de gereserveerde partitie.
/// </summary>
/// <remarks>
/// <para><strong>Dit document bestaat omdat het portaal meer dan één instantie kan hebben.</strong>
/// Twee instanties betekent twee collectors, en dat is niet alleen dubbel werk: het aanroepbudget van
/// Cost Management hangt aan de aanroeper en niet aan de scope (de header heet
/// <c>clienttype-retry-after</c>), dus twee collectors trekken hem sámen leeg. Gemeten op 21 augustus
/// 2026 kwam er na tien minuten stilte nog een 429 omdat er een tweede aanroeper in dezelfde tenant
/// meedeed — dat is deze storing, van buiten. Twee eigen instanties zouden hem van binnen maken, elke
/// nacht.</para>
///
/// <para><strong>Dezelfde vorm als de mailclaim en met opzet niet dezelfde betekenis.</strong>
/// <c>StatementDocumentKeys.Id</c> is een slot op een <em>onherhaalbare handeling</em>: een verstuurde
/// mail is niet terug te halen, dus daar is "onbekend of het gelukt is" géén reden om het opnieuw te
/// proberen en komt het portaal alleen langs een mens uit die toestand. Een kosten<em>lezing</em> is
/// wél herhaalbaar — er gaat niets de deur uit, er wordt niets bij een klant in rekening gebracht — dus
/// dit is geen slot op herhalen maar een <strong>wederzijdse uitsluiting</strong> tussen instanties.
/// Vandaar dat er geen toestand op staat met een uitgang: er valt niets vrij te geven.</para>
///
/// <para><strong>En daarom mag een halve run gewoon blijven liggen.</strong> Valt de app om halverwege,
/// dan blijft de claim van vandaag staan en gebeurt er vandaag niets meer. Dat kost niets, en dat is
/// het eigenlijke argument voor deze vorm: <em>elke run leest de hele maand</em>. Een overgeslagen dag
/// gaat dus niet verloren, hij wordt de volgende nacht ingehaald — en de volledigheidscontrole heeft
/// er ruimte voor, want <see cref="AzureCostCompleteness.SettlementDays"/> is twee en een maand die op
/// de 3e wordt gelezen heet net zo goed volledig als een maand die op de 2e wordt gelezen. Een claim
/// met een verlooptijd zou daar niets aan verbeteren en wel iets kosten: het verschil tussen "loopt
/// nog" en "is omgevallen" is alleen door de klok te bepalen, en dat is precies de constructie die
/// <c>StatementSendState</c> afwijst.</para>
///
/// <para><strong>Wat er niet in geregeld is, eerlijk:</strong> deze documenten hebben geen verval. De
/// container <c>customers</c> staat in Bicep op <c>ttl: null</c>, dus een item-TTL doet daar niets. Het
/// zijn 365 kleine documenten per jaar in een partitie die alleen op id wordt gelezen, dus het kost
/// niets meetbaars — maar het is rommel, en het opruimen is een uitrol en geen codewijziging. Gemeld.
/// </para>
/// </remarks>
public sealed record AzureCostRunDocument
{
    /// <summary>Documentsleutel: <see cref="AzureCostDocumentKeys.ForDay"/>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Partitiesleutel. Altijd <see cref="PortalDocumentIds.ReservedPartitionKey"/>.
    /// </summary>
    /// <remarks>
    /// De claim hoort bij geen enkele klant, dus hij staat naast het markeerdocument van de migratie.
    /// Een klantslug moet met een kleine letter of cijfer beginnen (<see cref="PortalSlug"/>), dus deze
    /// partitie kan nooit met die van een klant samenvallen — en dat is hier meer dan netheid: stond de
    /// claim in de partitie van een klant, dan zou hij per klant bestaan en zouden twee instanties
    /// alsnog naast elkaar lopen, elk met een eigen deel van de klantenlijst.
    /// </remarks>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>Documentsoort. Altijd <see cref="AzureCostDocumentKeys.RunKind"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = AzureCostDocumentKeys.RunKind;

    /// <summary>De dag waarop deze run hoort te lopen, als <c>jjjj-MM-dd</c>.</summary>
    [JsonPropertyName("day")]
    public required string Day { get; init; }

    /// <summary>Wanneer de claim is gezet, in UTC.</summary>
    [JsonPropertyName("claimedAt")]
    public required DateTimeOffset ClaimedAt { get; init; }

    /// <summary>
    /// Welke instantie de claim heeft gezet.
    /// </summary>
    /// <remarks>
    /// Alleen om na te zoeken. Dit is het enige veld waaraan te zien is dat er meer dan één instantie
    /// draait, en dat is bij "waarom heeft die run niets gedaan" de eerste vraag.
    /// </remarks>
    [JsonPropertyName("claimedBy")]
    public string? ClaimedBy { get; init; }

    /// <summary>Hoeveel klanten er bij het claimen een Azure-scope hadden.</summary>
    /// <remarks>
    /// Het aantal bij het <em>begin</em> van de run en niet het aantal dat is gelukt. Dat tweede zou
    /// een tweede schrijfactie vragen die bij een omgevallen app nooit komt — en dan zou er een getal
    /// staan dat niet is bijgewerkt. Wat er werkelijk is gemeten staat per klant per maand in het
    /// verbruiksdocument, met zijn eigen <see cref="AzureCostDocument.MeasuredAt"/>.
    /// </remarks>
    [JsonPropertyName("customers")]
    public int Customers { get; init; }

    /// <summary>De versie waarop de gelijktijdigheidscontrole loopt.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}

/// <summary>
/// Wat de collector over één maand van één klant wegschrijft.
/// </summary>
/// <param name="CustomerId">De klantslug, die ook de partitiesleutel is.</param>
/// <param name="Month">De maand als <c>jjjj-MM</c>.</param>
/// <param name="State">De toestand, uit <see cref="AzureCostCompleteness.Judge"/> of uit een onleesbaar antwoord.</param>
/// <param name="Lines">De regels per dienst. Leeg bij <see cref="AzureCostState.NoLines"/> en <see cref="AzureCostState.Unknown"/>.</param>
/// <param name="Currency">De valuta, of <c>null</c>.</param>
/// <param name="Scope">De scope waartegen is gemeten, als tekenreeks. Zie <see cref="AzureCostDocument.Scope"/>.</param>
/// <param name="MeasuredAt">Wanneer de lezing is opgehaald, in UTC.</param>
/// <param name="CoversThrough">De laatste dag waarover er bedragen zijn, of <c>null</c>.</param>
/// <param name="Failure">Waarom er niets bekend is, of <c>null</c>.</param>
/// <remarks>
/// <para>Geen subtotaal. Dat is geen vergeten veld: het subtotaal is de som van
/// <paramref name="Lines"/> en bestaat alleen als afgeleide (<see cref="AzureCostReading.Subtotal"/>).
/// Een opgeslagen som die de regels tegenspreekt is een tweede waarheid, en de verkeerde van de twee
/// zou degene zijn die niemand bijwerkt.</para>
///
/// <para>En geen opslagpercentage. Dat is een afspraak en staat op het contract; zie punt 34.</para>
/// </remarks>
public sealed record AzureCostWrite(
    string CustomerId,
    string Month,
    AzureCostState State,
    IReadOnlyList<AzureCostLine> Lines,
    string? Currency,
    string Scope,
    DateTimeOffset MeasuredAt,
    DateOnly? CoversThrough,
    string? Failure);

/// <summary>
/// Eén klant zoals de collector hem ziet: een slug met de scope die er bij hem staat.
/// </summary>
/// <param name="CustomerId">De klantslug, die ook de partitiesleutel is.</param>
/// <param name="Scope">
/// De scope zoals hij in het document staat: leeg, bruikbaar of onbruikbaar. Ongefilterd, met opzet —
/// zie <see cref="IAzureCostCollectorStore.TargetsAsync"/>.
/// </param>
public sealed record AzureCostTarget(string CustomerId, string? Scope);

/// <summary>
/// De schrijfkant van het Azure-verbruik: de dagclaim en het wegschrijven van één maand.
/// </summary>
/// <remarks>
/// <para><strong>Een eigen interface naast <see cref="IPortalCostsStore"/>, en niet twee methoden
/// erbij.</strong> Dat is geen netheid maar de rolgrens. Elke methode van
/// <see cref="IPortalCostsStore"/> neemt een <see cref="Security.CustomerScope"/> of een
/// <see cref="Security.CustomerWriteScope"/>: het bewijs dat er een mens naar een klant kijkt en dat
/// hij dat mag. De collector heeft geen mens en geen scope. Zou hij door de leesinterface moeten, dan
/// zou hij een scope moeten <em>verzinnen</em> — een operatorbewijs zonder operator — en dat is precies
/// het soort constructie waarmee een autorisatiegrens ophoudt iets te betekenen.</para>
///
/// <para><strong>Wat dat kost, eerlijk.</strong> De partitiesleutel komt hier uit een klantslug en niet
/// uit een scope, dus de isolatie-eigenschap van de leeskant ("er is geen aanroep waarmee je met de
/// scope van klant A bij klant B komt") geldt hier niet. Wat er in de plaats staat: deze interface kan
/// alleen <em>schrijven</em>, en alleen de twee soorten uit <see cref="AzureCostDocumentKeys"/>.
/// <see cref="StateAsync"/> geeft één enum terug en geen document, en dat is met opzet: hij bestaat om
/// een aanroep te vermijden en niet om iets te lezen. Het ergste dat een fout hier kan doen is een
/// verbruiksdocument in de verkeerde partitie zetten, en dát is op het scherm te zien — de bevraagde
/// scope staat eronder.</para>
///
/// <para>Er is precies één implementatie, en die is <c>internal</c>. De interface is er voor de test,
/// niet voor een tweede opslag.</para>
/// </remarks>
public interface IAzureCostCollectorStore
{
    /// <summary>
    /// De klanten met de Azure-scope die bij hen is vastgelegd.
    /// </summary>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Alle klanten uit de opslag, met hun scope zoals hij er staat — leeg of ongeldig incluis.</returns>
    /// <remarks>
    /// <para><strong>Uit de documenten en niet uit <see cref="Security.ICustomerDirectory"/>.</strong>
    /// Die lijst is een momentopname in het geheugen die bij een koude start nog de configuratielijst
    /// kan zijn (zie <see cref="PortalDirectoryRefresh"/>), en een klant uit de configuratie heeft geen
    /// scope omdat er geen scherm is dat er een in zet. Meten hoort te gebeuren op grond van wat er
    /// werkelijk is vastgelegd.</para>
    ///
    /// <para><strong>Ook klanten zónder scope en met een onbruikbare scope komen terug.</strong> Dat is
    /// geen slordigheid: de collector hoort te kunnen melden hoeveel klanten er niet worden gemeten en
    /// waarom. Zou dit filter in de query zitten, dan is een klant met een tikfout in zijn scope niet
    /// te onderscheiden van een klant die er nog geen heeft — en dat is precies het onderscheid waar
    /// deze lane om draait.</para>
    ///
    /// <para>De enige cross-partition query in deze interface, en hij haalt twee velden op van
    /// hoogstens enkele tientallen documenten. Eén keer per run.</para>
    /// </remarks>
    Task<IReadOnlyList<AzureCostTarget>> TargetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Claimt de run van vandaag.
    /// </summary>
    /// <param name="day">De dag.</param>
    /// <param name="customers">Hoeveel klanten er een Azure-scope hebben.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// <c>true</c> als deze instantie de run mag doen; <c>false</c> als iemand hem vandaag al heeft
    /// geclaimd.
    /// </returns>
    /// <remarks>
    /// <para><strong>De claim gaat vóór de eerste aanroep. Nooit andersom.</strong> Dezelfde volgorde
    /// als bij de mail en om dezelfde soort reden, met dit verschil: daar voorkomt hij een tweede mail
    /// en hier voorkomt hij dat twee instanties het aanroepbudget verdelen totdat geen van beide nog
    /// een bedrag krijgt.</para>
    ///
    /// <para><c>false</c> is een gewone uitkomst en geen fout. Hij hoort als <c>information</c> in het
    /// log en niet als waarschuwing: op een portaal met twee instanties is dit elke nacht het normale
    /// gedrag van de ene van de twee.</para>
    /// </remarks>
    Task<bool> ClaimAsync(DateOnly day, int customers, CancellationToken cancellationToken = default);

    /// <summary>
    /// De toestand die er voor deze maand in de opslag staat, of <c>null</c> als er niets staat.
    /// </summary>
    /// <param name="customerId">De klantslug.</param>
    /// <param name="month">De maand als <c>jjjj-MM</c>.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De toestand, of <c>null</c>.</returns>
    /// <remarks>
    /// <para><strong>Dit is een besparing op het schaarse ding en niet op het goedkope.</strong> Een
    /// afgesloten maand die al <see cref="AzureCostState.Measured"/> is, verandert niet meer — de
    /// volledigheidsregel eist dat de laatste dag er staat én dat er minstens twee dagen na de maand is
    /// gemeten, en aan beide is niets meer te veranderen. Hem opnieuw opvragen kost een aanroep uit een
    /// emmer die er geen over heeft; deze puntlezing kost ongeveer één RU. Voor achtentwintig van de
    /// eenendertig dagen van een maand halveert dat het aantal aanroepen per klant.</para>
    ///
    /// <para>Een puntlezing op id, en met een controle op <c>kind</c>: in dezelfde partitie liggen
    /// documenten van vijf andere soorten. Dezelfde controle en dezelfde reden als bij de puntlezing van
    /// een urenregel.</para>
    /// </remarks>
    Task<AzureCostState?> StateAsync(
        string customerId,
        string month,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schrijft de lezing van één maand weg.
    /// </summary>
    /// <param name="write">Wat er is gemeten.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Een taak.</returns>
    /// <remarks>
    /// <para><strong>Een upsert en geen create, en dat is hier veilig omdat er niets wordt
    /// opgeteld.</strong> Het document is een momentopname van een lezing en geen mutatie: de
    /// verzameling van vandaag hoort die van gisteren over dezelfde maand te <em>vervangen</em>, want
    /// een maand heeft één bedrag en niet één bedrag per meetmoment. Zie
    /// <see cref="AzureCostDocumentKeys.ForMonth"/>, waar precies dat het verschil met de afgeleide
    /// sleutel van een urenregel is.</para>
    ///
    /// <para>Zonder etagcontrole, en dat volgt daaruit: er is één schrijver en er valt niets te
    /// verliezen. Twee collectors zouden hier elkaar overschrijven met dezelfde waarde; dat is geen
    /// gegevensverlies, en het is bovendien wat de dagclaim uitsluit.</para>
    /// </remarks>
    Task WriteAsync(AzureCostWrite write, CancellationToken cancellationToken = default);
}

/// <summary>
/// De enige implementatie van <see cref="IAzureCostCollectorStore"/>.
/// </summary>
/// <remarks>
/// <para>Singleton, want <see cref="AzureCostCollector"/> is een achtergronddienst en die kan geen
/// scoped afhankelijkheid krijgen. Dezelfde reden waarom <see cref="CosmosPortalDataStore"/> singleton
/// is en <see cref="CosmosPortalCostsStore"/> scoped. Deze klasse houdt geen staat vast.</para>
///
/// <para><strong>Een onbereikbare opslag werpt en wordt niet stil overgeslagen.</strong> Zonder
/// portaalopslag is er niets te schrijven, en dan hoort de collector niet te gaan meten: de aanroepen
/// zouden budget kosten en het antwoord zou nergens landen. Dat is de omgekeerde afweging van
/// <see cref="CosmosPortalCostsStore"/> — daar hoort een onbereikbare opslag luidruchtig te zijn omdat
/// het scherm anders "onbekend" toont bij een gezonde collector.</para>
/// </remarks>
internal sealed class CosmosAzureCostCollectorStore(
    CosmosContainerProvider containers,
    IOptions<PortalDataOptions> options,
    TimeProvider timeProvider,
    ILogger<CosmosAzureCostCollectorStore> logger) : IAzureCostCollectorStore
{
    private readonly PortalDataOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AzureCostTarget>> TargetsAsync(
        CancellationToken cancellationToken = default)
    {
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        // Alleen de twee velden die de collector nodig heeft. Een SELECT * zou hier het hele
        // klantdocument van elke klant ophalen om er één veld uit te lezen, en dat is de enige query in
        // deze klasse die over partities loopt.
        var definition = new QueryDefinition(
                "SELECT c.cid AS customerId, c.azureScope AS scope FROM c WHERE c.kind = @kind")
            .WithParameter("@kind", PortalDocumentKinds.Customer);

        var results = new List<AzureCostTarget>();
        var charge = 0d;

        using var iterator = container.GetItemQueryIterator<TargetRow>(definition);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            charge += response.RequestCharge;

            foreach (var row in response)
            {
                if (row.CustomerId is { Length: > 0 } slug)
                {
                    results.Add(new AzureCostTarget(slug, row.Scope));
                }
            }
        }

        logger.LogDebug(
            "De kostencollector kent {Count} klant(en) uit de opslag. {Charge} RU.",
            results.Count,
            charge);

        return results;
    }

    /// <summary>Eén rij uit de projectie van <see cref="TargetsAsync"/>.</summary>
    /// <remarks>
    /// Een eigen type en niet <see cref="CustomerDocument"/>: dat type heeft <c>required</c>-velden die
    /// een projectie met twee kolommen niet vult, en dan werpt de deserialisatie.
    /// </remarks>
    private sealed record TargetRow
    {
        /// <summary>De klantslug.</summary>
        [JsonPropertyName("customerId")]
        public string? CustomerId { get; init; }

        /// <summary>De scope zoals hij in het document staat.</summary>
        [JsonPropertyName("scope")]
        public string? Scope { get; init; }
    }

    /// <inheritdoc />
    public async Task<bool> ClaimAsync(
        DateOnly day,
        int customers,
        CancellationToken cancellationToken = default)
    {
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var document = new AzureCostRunDocument
        {
            Id = AzureCostDocumentKeys.ForDay(day),
            PartitionKey = PortalDocumentIds.ReservedPartitionKey,
            Day = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ClaimedAt = timeProvider.GetUtcNow(),
            ClaimedBy = Instance,
            Customers = customers,
        };

        try
        {
            // CreateItemAsync en geen upsert. Dit is het slot: bestaat het document al, dan komt hier
            // een 409 en heeft een andere instantie de run vandaag al. Met een upsert zouden beide
            // instanties denken dat ze mogen, en dan halen ze samen het aanroepbudget leeg.
            var response = await container
                .CreateItemAsync(
                    document,
                    new PartitionKey(PortalDocumentIds.ReservedPartitionKey),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Kostenrun van {Day} geclaimd door {Instance}, {Customers} klant(en) met een "
                + "Azure-scope. {Charge} RU.",
                document.Day,
                Instance,
                customers,
                response.RequestCharge);

            return true;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // Information en geen warning: op een portaal met twee instanties is dit elke nacht het
            // normale gedrag van de ene van de twee.
            logger.LogInformation(
                "De kostenrun van {Day} is al geclaimd; deze instantie ({Instance}) doet niets.",
                document.Day,
                Instance);

            return false;
        }
    }

    /// <inheritdoc />
    public async Task<AzureCostState?> StateAsync(
        string customerId,
        string month,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(month);

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await container
                .ReadItemAsync<AzureCostDocument>(
                    AzureCostDocumentKeys.ForMonth(month),
                    new PartitionKey(customerId),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // De kind-controle: in deze partitie liggen documenten van vijf andere soorten, en een id
            // die per ongeluk samenvalt zou hier als verbruiksdocument worden gelezen met alle velden
            // op hun standaardwaarde — dus met State op Unknown, en dat is een toestand die iets
            // betekent. Liever niets dan een verzonnen toestand.
            return string.Equals(
                response.Resource.Kind,
                AzureCostDocumentKeys.Kind,
                StringComparison.Ordinal)
                ? response.Resource.State
                : null;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(AzureCostWrite write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        var document = new AzureCostDocument
        {
            Id = AzureCostDocumentKeys.ForMonth(write.Month),
            PartitionKey = write.CustomerId,
            CustomerId = write.CustomerId,
            Month = write.Month,
            State = write.State,
            Lines = write.Lines,
            Currency = write.Currency,
            Scope = write.Scope,
            MeasuredAt = write.MeasuredAt,
            CoversThrough = write.CoversThrough?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Failure = write.Failure,
        };

        var response = await container
            .UpsertItemAsync(
                document,
                new PartitionKey(write.CustomerId),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Azure-verbruik van {CustomerId} over {Month} vastgelegd: {State}, {Lines} dienst(en), "
            + "gemeten tot {Through}. {Charge} RU.",
            write.CustomerId,
            write.Month,
            write.State,
            write.Lines.Count,
            document.CoversThrough ?? "geen dag",
            response.RequestCharge);
    }

    /// <summary>
    /// De naam van deze instantie, voor het claimdocument en het log.
    /// </summary>
    /// <remarks>
    /// <c>WEBSITE_INSTANCE_ID</c> is wat App Service zet en het is het enige gegeven waarmee twee
    /// instanties van elkaar te onderscheiden zijn. Lokaal is die er niet en dan is de machinenaam het
    /// beste dat er is.
    /// </remarks>
    private static string Instance { get; } =
        Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") is { Length: > 0 } id
            ? id[..Math.Min(12, id.Length)]
            : Environment.MachineName;

    /// <inheritdoc cref="CosmosPortalCostsStore" />
    private async Task<Container> ContainerAsync(CancellationToken cancellationToken)
    {
        var location = _options.Location()
            ?? throw new PortalDataNotProvisionedException(
                "De portaalopslag is niet ingericht: PortalData:AccountEndpoint is leeg. De "
                + "kostencollector meet daarom niet. Dat is met opzet die kant op: een lezing die "
                + "nergens landt kost wél een aanroep uit een budget dat gemeten schaars is, en levert "
                + "geen bedrag op het scherm.");

        return await containers.CustomersAsync(location, cancellationToken).ConfigureAwait(false);
    }
}
