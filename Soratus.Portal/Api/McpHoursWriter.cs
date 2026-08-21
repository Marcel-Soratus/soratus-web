using System.Globalization;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Api;

/// <summary>
/// Het schrijfpad van een koppeling: legt een urenregel vast die <em>nooit</em> gefiatteerd is.
/// </summary>
/// <remarks>
/// <para><strong>Waarom dit naast <see cref="IPortalHoursStore"/> bestaat en niet erop.</strong> Die
/// interface bedient het scherm van de operator. Zijn <see cref="IPortalHoursStore.BookHoursAsync"/>
/// legt een regel meteen als gefiatteerd vast, en dat is daar juist: een operator die het formulier
/// van §3.6 verstuurt <em>ís</em> het akkoord van Soratus. Dit endpoint is het tegenovergestelde
/// geval — §5 zegt dat alles wat een koppeling inschiet als te fiatteren landt — en dat is geen vlag
/// op dezelfde methode maar een ander schrijfpad met een andere uitkomst.</para>
///
/// <para><strong>Er is geen statusparameter, en dat is de manier waarop de vaste regel hier een
/// eigenschap is in plaats van een gewoonte.</strong> Deze interface heeft één methode, die methode
/// heeft geen status en geen bron, en er is geen tweede methode. Het endpoint erboven kan dus niet
/// "per ongeluk" een gefiatteerde regel schrijven; er is geen aanroep die dat uitdrukt en zo'n aanroep
/// zou niet compileren. Datzelfde geldt voor fiatteren: dat staat op
/// <see cref="IPortalHoursStore"/>, en dat is de interface die dit endpoint niet in handen heeft.
/// </para>
///
/// <para><strong>Wat hier bewust niet staat: een methode om te fiatteren, af te wijzen of te
/// corrigeren.</strong> Fiatteren is een handeling van een mens in het portaal (§3.6). Zou de kant die
/// inschiet ook kunnen fiatteren, dan is §5 een formaliteit.</para>
/// </remarks>
internal interface IMcpHoursWriter
{
    /// <summary>
    /// Legt de boeking vast als te fiatteren regel met bron <see cref="HourEntrySource.Mcp"/>.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant. Levert de partitiesleutel.</param>
    /// <param name="booking">De maand, de uren, de categorie, de boeker en de omschrijving.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De nieuwe regel, of een melding als de invoer niet klopt, of een conflict als dezelfde regel al
    /// bestaat.
    /// </returns>
    Task<PortalWriteResult<HourEntryDocument>> BookPendingAsync(
        CustomerWriteScope scope,
        HourBooking booking,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// De enige implementatie: één document in de container <c>customers</c>, op de partitie van de klant.
/// </summary>
/// <remarks>
/// <para>Bewust dicht bij <c>CosmosPortalHoursStore.CreateAsync</c> gehouden: dezelfde container,
/// dezelfde sleutelbouwer (<see cref="HourEntryKeys"/>), dezelfde afhandeling van een <c>409</c> en
/// van een <c>403</c>. Wat verschilt is precies wat moet verschillen — de stand, de bron en de
/// afwezigheid van een fiatteringsspoor.</para>
///
/// <para><strong>Dat dit een tweede schrijver van <c>hourEntry</c>-documenten in dit portaal is, is
/// het zwakke punt van deze indeling en het staat hier zodat het niet wegzakt.</strong> De juiste
/// plek voor deze methode is <see cref="IPortalHoursStore"/>, waar de andere vijf schrijfpaden
/// staan: dan bestaat er één klasse die weet hoe een urenregel wordt weggeschreven. Hij staat hier
/// omdat <c>Soratus.Portal/Data/</c> in deze sessie niet gewijzigd mocht worden. Wat er níet
/// verdubbeld is: de documentvorm (<see cref="HourEntryDocument"/>), de sleutelregel
/// (<see cref="HourEntryKeys"/>), de validatie (<see cref="HourBooking.Validate"/>) en de
/// tijdvormnormalisatie (die zit op de opties van de Cosmos-SDK, punt 25 van de
/// afwijkingennotitie). Wat wél verdubbeld is: de containerlezing en de foutafhandeling eromheen,
/// zo'n dertig regels. Verhuizen is een bestandsverplaatsing en geen herontwerp.</para>
/// </remarks>
internal sealed class CosmosMcpHoursWriter(
    CosmosContainerProvider containers,
    IOptions<PortalDataOptions> options,
    TimeProvider timeProvider,
    ILogger<CosmosMcpHoursWriter> logger) : IMcpHoursWriter
{
    private readonly PortalDataOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<PortalWriteResult<HourEntryDocument>> BookPendingAsync(
        CustomerWriteScope scope,
        HourBooking booking,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(booking);

        if (booking.Validate() is { } error)
        {
            return PortalWriteResult<HourEntryDocument>.Invalid(error);
        }

        var location = _options.Location()
            ?? throw new PortalDataNotProvisionedException(
                "De portaalopslag is niet ingericht: PortalData:AccountEndpoint is leeg. Een boeking " +
                "via de MCP-koppeling is daarmee niet vast te leggen. Dat hoort een storing te zijn en " +
                "geen geslaagd verzoek — een aanroeper die 'vastgelegd' terugkrijgt terwijl er niets " +
                "staat, vertelt zijn opdrachtgever dat de uren geboekt zijn.");

        var container = await containers.CustomersAsync(location, cancellationToken).ConfigureAwait(false);
        var document = Build(scope, booking, timeProvider.GetUtcNow());

        try
        {
            var response = await container
                .CreateItemAsync(
                    document,
                    new PartitionKey(scope.CustomerId),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Urenregel {EntryId} ({Hours} u, {Category}) op klant {CustomerId} ingeschoten via de " +
                "MCP-koppeling door {By} voor maand {Month}; stand te fiatteren. {Charge} RU.",
                document.Id,
                document.Hours,
                document.Category,
                scope.CustomerId,
                document.By,
                document.Month,
                response.RequestCharge);

            return PortalWriteResult<HourEntryDocument>.Saved(response.Resource);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            logger.LogWarning(
                "Urenregel {EntryId} op klant {CustomerId} bestond al; er is niets bijgeschreven.",
                document.Id,
                scope.CustomerId);

            return PortalWriteResult<HourEntryDocument>.Conflict(
                "Deze urenregel staat er al. Er is één regel vastgelegd en geen twee; ga hem in het " +
                "portaal na voordat je het opnieuw probeert.",
                current: null);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new PortalDataNotProvisionedException(
                "Het portaal mag niet schrijven in de portaalopslag. De managed identity heeft " +
                "'Cosmos DB Built-in Data Contributor' nodig op de database platform, als " +
                "sqlRoleAssignment op het Cosmos-dataplane. Een Reader-rol laat het urenscherm gewoon " +
                "vullen en pas het boeken falen.",
                exception);
        }
    }

    /// <summary>
    /// Het document zoals het naar de opslag gaat.
    /// </summary>
    /// <param name="scope">Het schrijfrecht; levert de partitiesleutel en de klantslug.</param>
    /// <param name="booking">De gevalideerde boeking.</param>
    /// <param name="now">Het moment van vastleggen, in UTC.</param>
    /// <returns>Het document.</returns>
    /// <remarks>
    /// <para>Staat apart van <see cref="BookPendingAsync"/> zodat er op de vaste regel uit §5 een test
    /// kan staan zónder Cosmos aan te raken. Dat is niet alleen gemak: "een boeking via dit endpoint is
    /// nooit gefiatteerd" is de kernregel van dit hele pad, en een regel die alleen te meten is door
    /// naar productie te schrijven wordt niet gemeten.</para>
    ///
    /// <para><c>ApprovedAt</c> en <c>ApprovedBy</c> blijven leeg. Niet omdat het moet, maar omdat er
    /// niets te zetten is: er heeft nog niemand naar deze regel gekeken. Dat is precies het verschil
    /// met <c>CosmosPortalHoursStore</c>, dat ze bij een boeking uit het scherm wél vult — daar ís de
    /// verzender het akkoord van Soratus.</para>
    /// </remarks>
    internal static HourEntryDocument Build(
        CustomerWriteScope scope,
        HourBooking booking,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(booking);

        var month = booking.Month.Trim();
        var category = booking.Category.Trim();
        var by = booking.By.Trim();
        var note = booking.Note.Trim();

        return new HourEntryDocument
        {
            Id = PortalDocumentIds.HourEntry(Key(now, month, category, booking.Hours, by, note)),
            PartitionKey = scope.CustomerId,
            CustomerId = scope.CustomerId,

            Month = month,
            Category = category,
            Note = note,
            Hours = booking.Hours,

            // Bron en stand staan hier vast en zijn geen parameter. Dat is de hele reden dat dit
            // endpoint bestaat in plaats van dat de MCP-server zelf naar Cosmos schrijft: zo staat §5
            // op één plek, in code die de aanroeper niet in handen heeft.
            Source = HourEntrySource.Mcp,
            Status = HourEntryStatus.Pending,

            // De mens uit het token, en daarnaast de koppeling die de regel wegschreef. Zie
            // HourEntryDocument.CreatedBy: één veld voor beide maakt "wie heeft dit in de opslag gezet"
            // onbeantwoordbaar, en dat is de vraag bij een factuurdiscussie.
            By = by,
            CreatedAt = now,
            CreatedBy = HourBookingApiContract.CreatedBy,
        };
    }

    /// <summary>
    /// De sleutel van deze regel: bron, moment en een korte hash van de inhoud.
    /// </summary>
    /// <remarks>
    /// <para><strong>Deze koppeling heeft geen idempotentiesleutel, en dat is een besluit en geen
    /// gebrek.</strong> Zie <see cref="HourEntryKeys.ForIntegration"/>: de JSON-RPC-request-id
    /// verandert bij een herhaling, en een sleutel over de inhoud
    /// (<c>cid|month|hours|category|note</c>) zou een tweede legitieme boeking van een uur op dezelfde
    /// dag met dezelfde omschrijving weigeren — dan faalt de tool op precies de boeking die klopt. Wat
    /// het risico draagt is de stand: een dubbele regel landt op te fiatteren en kan dus niet ongezien
    /// op een factuur komen.</para>
    ///
    /// <para>Wat het tijdstempel plus de inhoudshash wél dekt is de aanroep die binnen dezelfde
    /// milliseconde twee keer wordt gedaan. Dat is dezelfde bescherming die het boekformulier van de
    /// operator heeft, en de reden dat hier <see cref="HourEntryKeys.ForPortal"/> wordt aangeroepen: die
    /// methode is de recept "tijdstempel plus vier bytes hash", niet iets dat aan het portaalformulier
    /// hangt. Zijn naam zegt dat wel; hem herdopen naar iets bronneutraals hoort bij een wijziging in
    /// <c>Data/</c> en staat als punt in het rapport.</para>
    ///
    /// <para><see cref="HourEntryDocument.ExternalId"/> blijft daarom <c>null</c>. Dat veld is de
    /// sleutel <em>van de koppeling</em>, en deze koppeling heeft er geen; er iets in zetten wat wij
    /// zelf hebben bedacht zou de vraag "heeft deze aanroep al een regel opgeleverd" een antwoord geven
    /// dat niet waar is.</para>
    /// </remarks>
    private static string Key(
        DateTimeOffset now,
        string month,
        string category,
        decimal hours,
        string by,
        string note) =>
        HourEntryKeys.ForIntegration(
            HourEntrySource.Mcp,
            HourEntryKeys.ForPortal(
                now,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{HourEntryKeys.Serialize(HourEntrySource.Mcp)}|{month}|{category}|{hours}|{by}|{note}")));
}
