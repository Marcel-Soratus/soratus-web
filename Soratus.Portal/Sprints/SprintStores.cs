using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Sprints;

/// <summary>
/// De enige toegang tot de opgeslagen sprintlezing van één klant (§3.4).
/// </summary>
/// <remarks>
/// <para><strong>Alleen lezen, en dat is geen tijdelijke beperking.</strong> Deze documenten worden
/// geschreven door <see cref="SprintCollector"/> en door niets anders. Er is geen scherm en geen formulier
/// dat een sprint of een work item vastlegt, en er hoort er ook geen te komen: <strong>DevOps is leidend en
/// het portaal schrijft nooit terug</strong> (§3.4). Dat is hier scherper dan bij de kosten, waar het
/// argument "een bedrag dat een mens kan intypen komt naast de meting te staan" luidt — hier zou een
/// wijziging in het portaal niets op het bord veranderen en zou hij binnen een kwartier zijn overschreven.
/// Een knop die dat doet is een knop die liegt.</para>
///
/// <para><strong>De schrijfkant is daarom een andere interface en niet twee methoden hier.</strong> Elke
/// methode hieronder vraagt een scope: het bewijs dat er een mens naar een klant kijkt en dat hij dat mag.
/// De collector heeft geen mens en dus geen scope. Zie <see cref="ISprintCollectorStore"/>.</para>
///
/// <para><strong>Twee overloads met dezelfde naam, en het verschil is het bewijs en niet de
/// verzameling.</strong> Beide rollen lezen hetzelfde document; wat de klant niet mag zien — de bevraagde
/// scope, de reden van een mislukking, de e-mailadressen op een work item, de paden van de iteraties zonder
/// datums — verdwijnt in het viewmodel en niet in de query. Dezelfde keuze en dezelfde verantwoording als
/// bij <see cref="IPortalCostsStore"/>: het verboden gegeven is hier een <em>veld</em> op een document
/// waarvan de klant de rest wél mag zien, en een veld valt niet uit een query weg te filteren zonder het
/// document te verminken.</para>
/// </remarks>
public interface IPortalSprintStore
{
    /// <summary>
    /// De laatste sprintlezing van deze klant, of <c>null</c> als er nooit is gelezen.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant. Dit is de partitiesleutel, en daarmee de grens.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het document, of <c>null</c>.</returns>
    /// <remarks>
    /// <para><strong><c>null</c> betekent "er is niet gelezen" en niet "er is geen werk".</strong> Dat
    /// onderscheid moet de aanroeper maken en niet vergeten; <see cref="SprintState.Unknown"/> is waar de
    /// afwezigheid van een document op uitkomt, en dat gebeurt op één plek — in de weergavelaag.</para>
    /// </remarks>
    Task<SprintDocument?> GetSprintAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// De laatste sprintlezing van deze klant, voor de operator (§2).
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant, als bewijs van de rol.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het document, of <c>null</c>.</returns>
    /// <remarks>
    /// <para>Vraagt een schrijfbewijs om te lezen, net als
    /// <see cref="IPortalCostsStore.GetAzureCostsAsync(CustomerWriteScope, int, CancellationToken)"/>. Er
    /// wordt geen recht mee opgerekt en er valt hier niets te schrijven: het is het bewijs dat de aanroeper
    /// een operator is die naar déze klant kijkt, en dat is de voorwaarde om de bevraagde scope, de reden
    /// van een mislukking en de adressen op een work item te mogen zien.</para>
    ///
    /// <para><see cref="CustomerWriteScope"/> en niet <see cref="OperatorCustomerScope"/>: dat laatste
    /// vraagt een ingerichte telemetrie-opslag, en de sprint van een klant is te bekijken voordat zijn
    /// agents zijn uitgerold. Dezelfde afweging als op het urenscherm en het facturatiescherm.</para>
    ///
    /// <para><strong>En dit is de tweede plek waar een type dat <c>Write</c> heet uitsluitend een rol
    /// bewijst op een pad waar niets te schrijven valt.</strong> Dat is geen fout maar een verkeerde naam
    /// met twee gebruikers; bij een derde hoort hij te heten wat hij bewijst. Opgeschreven als vervolgpunt,
    /// met de stand erbij, zodat de volgende sessie hoeft te tellen in plaats van af te wegen.</para>
    /// </remarks>
    Task<SprintDocument?> GetSprintAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Eén klant zoals de sprintcollector hem ziet: een slug met het bord dat er bij hem staat.
/// </summary>
/// <param name="CustomerId">De klantslug, die ook de partitiesleutel is.</param>
/// <param name="Scope">
/// Het bord zoals het in het document staat: leeg, bruikbaar of onbruikbaar. Ongefilterd, met opzet — zie
/// <see cref="ISprintCollectorStore.TargetsAsync"/>.
/// </param>
public sealed record SprintTarget(string CustomerId, string? Scope);

/// <summary>
/// Wat de collector over één klant wegschrijft.
/// </summary>
/// <param name="CustomerId">De klantslug, die ook de partitiesleutel is.</param>
/// <param name="State">De toestand, uit <see cref="SprintSelection"/> of uit een onleesbaar antwoord.</param>
/// <param name="Scope">Het bord waartegen is gelezen, als tekenreeks.</param>
/// <param name="ReadAt">Wanneer de lezing is opgehaald, in UTC.</param>
/// <param name="Sprint">De huidige sprint, of <c>null</c>.</param>
/// <param name="Items">De work items van die sprint.</param>
/// <param name="Undated">De iteraties zonder datums.</param>
/// <param name="Overlapping">De iteraties die vandaag allemaal bevatten, of leeg.</param>
/// <param name="DatedCount">Hoeveel iteraties er datums hebben.</param>
/// <param name="Failure">Waarom er niets bekend is, of <c>null</c>.</param>
/// <remarks>
/// Geen statistieken. Dat is geen vergeten veld: <see cref="SprintTally"/> is de som over
/// <paramref name="Items"/> en bestaat alleen als afgeleide. Een opgeslagen aantal dat de lijst tegenspreekt
/// is een tweede waarheid, en de verkeerde van de twee zou degene zijn die niemand bijwerkt — hetzelfde
/// argument als bij het ontbrekende subtotaal op een verbruiksdocument.
/// </remarks>
public sealed record SprintWrite(
    string CustomerId,
    SprintState State,
    string Scope,
    DateTimeOffset ReadAt,
    DevOpsIteration? Sprint,
    IReadOnlyList<SprintWorkItem> Items,
    IReadOnlyList<SprintIterationRef> Undated,
    IReadOnlyList<SprintIterationRef> Overlapping,
    int DatedCount,
    string? Failure);

/// <summary>
/// De schrijfkant van de sprintlezing: de klantenlijst, het tijdstip van de vorige lezing, en het
/// wegschrijven.
/// </summary>
/// <remarks>
/// <para><strong>Een eigen interface naast <see cref="IPortalSprintStore"/>, en niet drie methoden
/// erbij.</strong> Dat is geen netheid maar de rolgrens, en het is dezelfde verantwoording als bij
/// <see cref="IAzureCostCollectorStore"/>: elke methode van de leeskant neemt een
/// <see cref="CustomerScope"/> of een <see cref="CustomerWriteScope"/>, en de collector heeft geen mens en
/// geen scope. Zou hij door de leesinterface moeten, dan zou hij er een moeten <em>verzinnen</em> — een
/// operatorbewijs zonder operator, en dat is precies de constructie waarmee een autorisatiegrens ophoudt
/// iets te betekenen.</para>
///
/// <para><strong>Wat dat kost, eerlijk.</strong> De partitiesleutel komt hier uit een klantslug en niet uit
/// een scope, dus de isolatie-eigenschap van de leeskant geldt hier niet. Wat er in de plaats staat: deze
/// interface kan alleen sprintdocumenten schrijven, en <see cref="ReadAtAsync"/> geeft één tijdstip terug
/// en geen document — hij bestaat om een aanroep te vermijden en niet om iets te lezen.</para>
/// </remarks>
public interface ISprintCollectorStore
{
    /// <summary>
    /// De klanten met het DevOps-bord dat bij hen is vastgelegd.
    /// </summary>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Alle klanten uit de opslag, met hun bord zoals het er staat — leeg of ongeldig incluis.</returns>
    /// <remarks>
    /// <para><strong>Uit de documenten en niet uit <see cref="ICustomerDirectory"/>.</strong> Die lijst is
    /// een momentopname in het geheugen die bij een koude start nog de configuratielijst kan zijn (zie
    /// <see cref="PortalDirectoryRefresh"/>). En er is hier een sterker argument dan bij de kosten: een
    /// klant die alleen in de configuratie staat <em>kán</em> geen bord hebben, want het bord is een veld op
    /// het klantdocument en er is geen scherm dat hem elders zet. De verzameling "klanten met een bord" is
    /// dus per constructie een deelverzameling van "klanten met een document" — er valt niets over te slaan.
    /// </para>
    ///
    /// <para><strong>Ook klanten zónder bord en met een onbruikbaar bord komen terug.</strong> Dat is geen
    /// slordigheid: de collector hoort te kunnen melden hoeveel klanten er niet worden opgehaald en waarom.
    /// Zou dat filter in de query zitten, dan is een klant met een tikfout in zijn bord niet te
    /// onderscheiden van een klant die er nog geen heeft — en dat is precies het onderscheid waar deze lane
    /// om draait.</para>
    ///
    /// <para>De enige cross-partition query in deze interface, en hij haalt twee velden op van hoogstens
    /// enkele tientallen documenten. Eén keer per ronde.</para>
    /// </remarks>
    Task<IReadOnlyList<SprintTarget>> TargetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Wanneer er voor deze klant het laatst is gelezen, of <c>null</c> als er nooit is gelezen.
    /// </summary>
    /// <param name="customerId">De klantslug.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het tijdstip, of <c>null</c>.</returns>
    /// <remarks>
    /// <para><strong>Dit is de wederzijdse uitsluiting tussen twee portaalinstanties, en hij is met opzet
    /// géén claimdocument.</strong> Bij de kosten is dat er wel een (punt 38): één document per dag met een
    /// <c>CreateItemAsync</c>, zodat de tweede instantie een 409 krijgt. Per kwartier zou dat
    /// zesennegentig documenten per dag zijn in een container zonder TTL — rommel die niemand opruimt, voor
    /// een budget dat niet is gemeten.</para>
    ///
    /// <para><strong>Wat het níet is, is een slot:</strong> twee instanties die binnen dezelfde seconde
    /// tikken komen er beide langs. De prijs is een verdubbeling van het aantal aanroepen en niet een
    /// verkeerd getal op het scherm — er wordt niets opgeteld en de tweede lezing overschrijft de eerste met
    /// dezelfde waarde. Blijkt er ooit een emmer te zijn die dat niet verdraagt, dan is de claimvorm van
    /// punt 38 de opwaardering, en dat is een kleine wijziging.</para>
    ///
    /// <para>Een puntlezing op id, en met een controle op <c>kind</c>: in dezelfde partitie liggen
    /// documenten van zes andere soorten, en een id die per ongeluk samenvalt zou hier als sprintdocument
    /// worden gelezen met alle velden op hun standaardwaarde. Dezelfde controle en dezelfde reden als bij de
    /// puntlezing van een verbruiksmaand.</para>
    /// </remarks>
    Task<DateTimeOffset?> ReadAtAsync(string customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schrijft de lezing van één klant weg.
    /// </summary>
    /// <param name="write">Wat er is gelezen.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Een taak.</returns>
    /// <remarks>
    /// <para>Een upsert en geen create, en dat is hier veilig omdat er niets wordt opgeteld: het document is
    /// een momentopname van een lezing en geen mutatie. De lezing van dit kwartier hoort die van het vorige
    /// te vervangen — zie <see cref="SprintDocumentKeys.Id"/>.</para>
    ///
    /// <para>Zonder etagcontrole, en dat volgt daaruit: er is één schrijver en er valt niets te verliezen.
    /// Twee collectors zouden elkaar overschrijven met dezelfde waarde.</para>
    /// </remarks>
    Task WriteAsync(SprintWrite write, CancellationToken cancellationToken = default);
}

/// <summary>
/// De enige implementatie van <see cref="IPortalSprintStore"/>: één puntlezing binnen de partitie van de
/// klant.
/// </summary>
/// <remarks>
/// <para><strong>Een puntlezing en geen query, en dat is de goedkoopste vorm die er is.</strong> Er is één
/// sprintdocument per klant en de sleutel is vast, dus er valt niets te zoeken. Er is ook geen
/// cross-partition query in deze klasse: de partitiesleutel komt uit de scope, dus er is geen aanroep
/// waarmee je met de scope van klant A de sprint van klant B leest.</para>
///
/// <para><strong>Een onbereikbare opslag werpt en levert geen <c>null</c> op.</strong> Dat is hier zwaarder
/// dan het lijkt: <c>null</c> wordt door de weergavelaag <see cref="SprintState.Unknown"/>, en dat is de
/// juiste uitkomst voor "er is nog niet gelezen" — maar niet voor "de opslag is niet ingericht". Die tweede
/// is een inrichtingsfout en hoort luidruchtig te zijn, want anders staat er weken lang "nog niet
/// opgehaald" op een scherm terwijl de collector netjes zijn werk doet. Zelfde afweging als bij
/// <see cref="CosmosPortalCostsStore"/>.</para>
///
/// <para><strong>Er wordt niets geschreven.</strong> Deze klasse heeft geen enkele methode die dat doet.
/// </para>
/// </remarks>
internal sealed class CosmosPortalSprintStore(
    CosmosContainerProvider containers,
    IOptions<PortalDataOptions> options,
    ILogger<CosmosPortalSprintStore> logger) : IPortalSprintStore
{
    private readonly PortalDataOptions _options = options.Value;

    /// <inheritdoc />
    public Task<SprintDocument?> GetSprintAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return ReadAsync(scope.CustomerId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SprintDocument?> GetSprintAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return ReadAsync(scope.CustomerId, cancellationToken);
    }

    /// <summary>
    /// Leest het sprintdocument van één klant.
    /// </summary>
    /// <param name="customerId">
    /// De klantslug. Dit is de partitiesleutel, en daarmee de isolatiegrens: deze waarde komt uit de scope
    /// en nergens anders vandaan.
    /// </param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het document, of <c>null</c>.</returns>
    private async Task<SprintDocument?> ReadAsync(
        string customerId,
        CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await container
                .ReadItemAsync<SprintDocument>(
                    SprintDocumentKeys.Id,
                    new PartitionKey(customerId),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            logger.LogDebug(
                "Sprint van {CustomerId} gelezen: {State}, {Items} work item(s). {Charge} RU.",
                customerId,
                response.Resource.State,
                response.Resource.Items.Count,
                response.RequestCharge);

            // De kind-controle: in deze partitie liggen documenten van zes andere soorten, en een id die
            // per ongeluk samenvalt zou hier als sprintdocument worden gelezen met alle velden op hun
            // standaardwaarde — dus met State op Unknown, en dat is een toestand die iets betekent. Liever
            // niets dan een verzonnen toestand.
            return string.Equals(
                response.Resource.Kind,
                SprintDocumentKeys.Kind,
                StringComparison.Ordinal)
                ? response.Resource
                : null;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc cref="CosmosPortalCostsStore" />
    private async Task<Container> ContainerAsync(CancellationToken cancellationToken)
    {
        var location = _options.Location()
            ?? throw new PortalDataNotProvisionedException(
                "De portaalopslag is niet ingericht: PortalData:AccountEndpoint is leeg. De sprint is "
                + "daarmee niet te lezen. Het sprintscherm hoort dat te melden in plaats van een lege "
                + "sprint te tonen — 'wij hebben niet gekeken' en 'er is geen werk' zien er op een leeg "
                + "scherm hetzelfde uit, en dat is precies het verschil dat een klant belt.");

        return await containers.CustomersAsync(location, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// De enige implementatie van <see cref="ISprintCollectorStore"/>.
/// </summary>
/// <remarks>
/// <para>Singleton, want <see cref="SprintCollector"/> is een achtergronddienst en die kan geen scoped
/// afhankelijkheid krijgen. Dezelfde reden waarom <c>CosmosAzureCostCollectorStore</c> singleton is en
/// <see cref="CosmosPortalSprintStore"/> scoped. Deze klasse houdt geen staat vast.</para>
///
/// <para><strong>Een onbereikbare opslag werpt en wordt niet stil overgeslagen.</strong> Zonder
/// portaalopslag is er niets te schrijven, en dan hoort de collector niet te gaan lezen: de aanroepen
/// zouden budget kosten en het antwoord zou nergens landen.</para>
///
/// <para><strong>Deze klasse heeft geen test, en dat is hetzelfde eerlijke gat als bij
/// <c>CosmosAzureCostCollectorStore</c> (punt 41).</strong> Hij praat met Cosmos, en een test ertegen zou
/// óf naar Cosmos schrijven óf de documentvorm nábouwen — en dan meet de test de nabouw. De mapping van
/// <see cref="SprintWrite"/> naar <see cref="SprintDocument"/> staat daarom in
/// <see cref="ToDocument"/>: <c>internal static</c> en puur, zodat een test de <em>productiemapping</em>
/// kan aanroepen zonder de opslag. Dat is precies het voorstel dat punt 41 voor de klantdocumentmapping
/// doet, hier meteen gedaan omdat dit een nieuw bestand is en het niets kost.</para>
/// </remarks>
internal sealed class CosmosSprintCollectorStore(
    CosmosContainerProvider containers,
    IOptions<PortalDataOptions> options,
    ILogger<CosmosSprintCollectorStore> logger) : ISprintCollectorStore
{
    private readonly PortalDataOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SprintTarget>> TargetsAsync(
        CancellationToken cancellationToken = default)
    {
        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        // Alleen de twee velden die de collector nodig heeft. Een SELECT * zou het hele klantdocument van
        // elke klant ophalen om er één veld uit te lezen, en dit is de enige query in deze klasse die over
        // partities loopt.
        var definition = new QueryDefinition(
                "SELECT c.cid AS customerId, c.devOpsScope AS scope FROM c WHERE c.kind = @kind")
            .WithParameter("@kind", PortalDocumentKinds.Customer);

        var results = new List<SprintTarget>();
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
                    results.Add(new SprintTarget(slug, row.Scope));
                }
            }
        }

        logger.LogDebug(
            "De sprintcollector kent {Count} klant(en) uit de opslag. {Charge} RU.",
            results.Count,
            charge);

        return results;
    }

    /// <summary>Eén rij uit de projectie van <see cref="TargetsAsync"/>.</summary>
    /// <remarks>
    /// Een eigen type en niet <see cref="CustomerDocument"/>: dat type heeft <c>required</c>-velden die een
    /// projectie met twee kolommen niet vult, en dan werpt de deserialisatie.
    /// </remarks>
    private sealed record TargetRow
    {
        /// <summary>De klantslug.</summary>
        [JsonPropertyName("customerId")]
        public string? CustomerId { get; init; }

        /// <summary>Het bord zoals het in het document staat.</summary>
        [JsonPropertyName("scope")]
        public string? Scope { get; init; }
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> ReadAtAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await container
                .ReadItemAsync<SprintDocument>(
                    SprintDocumentKeys.Id,
                    new PartitionKey(customerId),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return string.Equals(
                response.Resource.Kind,
                SprintDocumentKeys.Kind,
                StringComparison.Ordinal)
                ? response.Resource.ReadAt
                : null;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(SprintWrite write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);

        var container = await ContainerAsync(cancellationToken).ConfigureAwait(false);
        var document = ToDocument(write);

        var response = await container
            .UpsertItemAsync(
                document,
                new PartitionKey(write.CustomerId),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Sprint van {CustomerId} vastgelegd: {State}, sprint {Sprint}, {Items} work item(s), "
            + "{Undated} iteratie(s) zonder datums. {Charge} RU.",
            write.CustomerId,
            write.State,
            document.SprintName ?? "geen",
            write.Items.Count,
            write.Undated.Count,
            response.RequestCharge);
    }

    /// <summary>
    /// Zet een lezing om in het document dat in de opslag komt.
    /// </summary>
    /// <param name="write">De lezing.</param>
    /// <returns>Het document.</returns>
    /// <remarks>
    /// <para><c>internal static</c> en puur, zodat een test de <em>productiemapping</em> kan aanroepen in
    /// plaats van hem na te bouwen. Dat is niet cosmetisch: punt 41 meldt als echt gat dat de mutatie "de
    /// echte opslag schrijft de scope niet op het klantdocument" niets rood maakte, omdat de testfixture
    /// die mapping nábouwde. Hier kan dat niet gebeuren.</para>
    ///
    /// <para>De datums gaan als <c>jjjj-MM-dd</c> naar de opslag en niet als moment. Zie
    /// <see cref="SprintDocument.Start"/>.</para>
    /// </remarks>
    internal static SprintDocument ToDocument(SprintWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        return new SprintDocument
        {
            Id = SprintDocumentKeys.Id,
            PartitionKey = write.CustomerId,
            CustomerId = write.CustomerId,
            State = write.State,
            Scope = write.Scope,
            ReadAt = write.ReadAt,
            SprintId = write.Sprint?.Id,
            SprintName = write.Sprint?.Name,
            BoardPath = write.Sprint?.Path,
            Start = Day(write.Sprint?.Start),
            Finish = Day(write.Sprint?.Finish),
            Items = write.Items,
            Undated = write.Undated,
            Overlapping = write.Overlapping,
            DatedCount = write.DatedCount,
            Failure = write.Failure,
        };
    }

    /// <summary>De opslagvorm van een dag, of <c>null</c>.</summary>
    /// <param name="day">De dag.</param>
    /// <returns>De dag als <c>jjjj-MM-dd</c>, of <c>null</c>.</returns>
    private static string? Day(DateOnly? day) =>
        day?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc cref="CosmosPortalSprintStore" />
    private async Task<Container> ContainerAsync(CancellationToken cancellationToken)
    {
        var location = _options.Location()
            ?? throw new PortalDataNotProvisionedException(
                "De portaalopslag is niet ingericht: PortalData:AccountEndpoint is leeg. De "
                + "sprintcollector leest daarom niet. Dat is met opzet die kant op: een lezing die nergens "
                + "landt kost wél aanroepen en levert geen sprint op het scherm.");

        return await containers.CustomersAsync(location, cancellationToken).ConfigureAwait(false);
    }
}
