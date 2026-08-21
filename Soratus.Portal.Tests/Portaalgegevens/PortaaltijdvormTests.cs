using System.Text.Json;
using Azure.Core;
using Soratus.Agents.Contracts;
using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Portaalgegevens;

/// <summary>
/// De schrijfkant van het portaal schrijft tijdstempels canoniek: UTC, zeven decimalen, een
/// afsluitende <c>Z</c>.
/// </summary>
/// <remarks>
/// <para><strong>Wat hier de moeite is, is nadrukkelijk niet de vorm.</strong> Dat de canonieke vorm
/// klopt staat in <c>TijdvormTests</c> in <c>Soratus.Agents.Telemetry.Tests</c>, op de bibliotheek
/// zelf. Wat daar per definitie niet kan staan is de vraag die dit portaal kostte: <em>krijgt de
/// Cosmos-SDK die normalisatie werkelijk mee?</em> Het portaal had de converter niet, en dat was aan
/// geen enkele test te zien omdat er geen test was die de opties van het portaal zelf aanraakte.
/// Een test die zijn eigen opties opbouwt "net zoals het portaal het doet" zou die fout niet hebben
/// gevonden — hij zou hem hebben nagebouwd en groen zijn.</para>
///
/// <para><strong>Vandaar dat alles hier aan één object hangt.</strong>
/// <see cref="CosmosClientCache.SerializerOptions"/> is het exemplaar dat als
/// <c>UseSystemTextJsonSerializerWithOptions</c> aan de SDK gaat.
/// <see cref="DeSdkKrijgtPreciesDezeOptiesMee"/> pint dat vast op identiteit en niet op inhoud, en de
/// overige tests serialiseren echte portaaldocumenten met datzelfde object. Zou iemand de opties
/// splitsen in "de opties" en "de opties die de SDK krijgt", dan valt die eerste test om.</para>
///
/// <para><strong>Waarom er echte documenttypen in staan.</strong> Een <c>DateTimeOffset</c> los
/// serialiseren bewijst niets over een property: een <c>[JsonConverter]</c> op een property gaat
/// vóór een converter in de opties, en een tijdveld dat als <c>string</c> in het document is
/// gemodelleerd gaat er helemaal langs heen. Beide zijn aan de opties niet te zien. Daarom gaan
/// <c>createdAt</c>, <c>changedAt</c>, <c>grantedAt</c> en <c>ranAt</c> hier door de serializer zoals
/// ze werkelijk gemodelleerd staan.</para>
///
/// <para>Zie punt 7 en punt 25 van <c>docs/agent-portal/fase-0-afwijkingen.md</c>.</para>
/// </remarks>
public class PortaaltijdvormTests
{
    /// <summary>Het moment uit het foute document, uitgedrukt in +02:00.</summary>
    /// <remarks>
    /// Bewust met een offset én met drie gevulde decimalen: dat is exact de invoer die er als
    /// <c>2026-08-20T15:04:05.678+00:00</c> uitkwam. Zou de invoer al UTC met zeven decimalen zijn,
    /// dan zou deze suite ook groen staan zonder converter.
    /// </remarks>
    private static readonly DateTimeOffset Moment =
        new DateTimeOffset(2026, 8, 20, 17, 4, 5, 678, TimeSpan.FromHours(2));

    private const string Canoniek = "2026-08-20T15:04:05.6780000Z";

    /// <summary>De opties waarmee het portaal werkelijk naar Cosmos schrijft.</summary>
    private static JsonSerializerOptions Opties => CosmosClientCache.SerializerOptions;

    // ── De koppeling tussen de opties en de SDK ─────────────────────────────────────────────────

    [Fact]
    public void DeSdkKrijgtPreciesDezeOptiesMee()
    {
        // Een client aanmaken doet geen netwerkverkeer; de endpoint hoeft dus niet te bestaan. Wat
        // dit meet is tweeledig. Eén: de SDK accepteert deze opties — ze zijn bevroren met
        // MakeReadOnly, en een SDK die er nog een naamgevingsbeleid op wil zetten zou hier werpen.
        // Twee: het object dat de SDK krijgt is hetzelfde object dat de rest van deze suite meet.
        var cache = new CosmosClientCache(new Nepcredential());

        using var client = cache.For("https://soratus-tijdvorm.documents.azure.com:443/");

        Assert.Same(Opties, client.ClientOptions.UseSystemTextJsonSerializerWithOptions);
    }

    [Fact]
    public void DeNormalisatieZitOpDeOptiesEnDeAssertieHoudtDatVast()
    {
        // Als deze assertie hier groen is, is hij het ook bij het opstarten van het portaal — het is
        // dezelfde aanroep op hetzelfde object. Zij loopt in de statische initialisatie van
        // CosmosClientCache, dus feitelijk is dit een tweede uitvoering; hij staat hier zodat een
        // rood signaal de reden noemt in plaats van een TypeInitializationException.
        TimestampNormalization.AssertCanonical(Opties);
    }

    [Fact]
    public void DeOptiesZijnBevrorenZodatErNietsUitGehaaldKanWorden()
    {
        // Zonder dit is "bevroren" een opmerking in een comment. Een converter kunnen toevoegen is
        // genoeg om de normalisatie te kunnen overrulen: een later toegevoegde converter voor
        // hetzelfde type gaat vóór.
        Assert.True(Opties.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => Opties.Converters.Clear());
    }

    // ── De echte documenten van de schrijfkant ─────────────────────────────────────────────────

    [Fact]
    public void EenKlantdocumentKrijgtCanoniekeTijden()
    {
        // createdAt is niet-nullable, changedAt is DateTimeOffset?. Die tweede is het pad dat de
        // fout in het gemeten document droeg, en het pad dat alleen werkt doordat System.Text.Json
        // de converter voor DateTimeOffset zelf doorgeeft aan DateTimeOffset?.
        string json = JsonSerializer.Serialize(
            new CustomerDocument
            {
                Id = PortalDocumentIds.Customer,
                PartitionKey = "bakker",
                CustomerId = "bakker",
                Name = "Bakker Logistiek",
                CreatedAt = Moment,
                ChangedAt = Moment,
            },
            Opties);

        Assert.Contains($"\"createdAt\":\"{Canoniek}\"", json, StringComparison.Ordinal);
        Assert.Contains($"\"changedAt\":\"{Canoniek}\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("+00:00", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantDieNooitIsGewijzigdHoudtEenLegeChangedAt()
    {
        // De converter mag geen tijdstempel verzinnen. Een klant met changedAt op "1 januari 0001"
        // zou in het scherm als gewijzigd verschijnen en bij sorteren vooraan komen.
        string json = JsonSerializer.Serialize(
            new CustomerDocument
            {
                Id = PortalDocumentIds.Customer,
                PartitionKey = "bakker",
                CustomerId = "bakker",
                Name = "Bakker Logistiek",
                CreatedAt = Moment,
                ChangedAt = null,
            },
            Opties);

        Assert.Contains("\"changedAt\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EenContractdocumentKrijgtEenCanoniekeChangedAt()
    {
        string json = JsonSerializer.Serialize(
            new ContractDocument
            {
                Id = PortalDocumentIds.Contract,
                PartitionKey = "bakker",
                CustomerId = "bakker",
                ChangedAt = Moment,
            },
            Opties);

        Assert.Contains($"\"changedAt\":\"{Canoniek}\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("+00:00", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EenToegangsdocumentKrijgtEenCanoniekeGrantedAt()
    {
        string json = JsonSerializer.Serialize(
            new AccessDocument
            {
                Id = PortalDocumentIds.Access("jan@bakker.nl"),
                PartitionKey = "bakker",
                CustomerId = "bakker",
                Email = "jan@bakker.nl",
                Role = "member",
                GrantedAt = Moment,
            },
            Opties);

        Assert.Contains($"\"grantedAt\":\"{Canoniek}\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("+00:00", json, StringComparison.Ordinal);
    }

    [Fact]
    public void HetBootstrapdocumentKrijgtEenCanoniekeRanAt()
    {
        string json = JsonSerializer.Serialize(
            new BootstrapDocument
            {
                Id = PortalDocumentIds.Bootstrap,
                PartitionKey = PortalDocumentIds.ReservedPartitionKey,
                RanAt = Moment,
                Customers = 1,
                Slugs = ["bakker"],
            },
            Opties);

        Assert.Contains($"\"ranAt\":\"{Canoniek}\"", json, StringComparison.Ordinal);
    }

    // ── De eigenschap waar de reparatie voor bestaat ────────────────────────────────────────────

    [Fact]
    public void OpTekstSorterenGeeftDezelfdeVolgordeAlsOpTijd()
    {
        // Dit is wat een ORDER BY in Cosmos doet: hij vergelijkt de tekst van het veld. Vier
        // momenten uit één werkdag, in vier vormen waarin ze werkelijk aangeleverd worden.
        DateTimeOffset[] momenten =
        [
            new DateTimeOffset(2026, 8, 20, 15, 13, 19, 944, TimeSpan.Zero).AddTicks(9045),
            new DateTimeOffset(2026, 8, 20, 17, 4, 5, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 8, 20, 15, 4, 5, 678, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero),
        ];

        string[] opTekst = [.. momenten.Select(Veld).Order(StringComparer.Ordinal)];
        string[] opTijd = [.. momenten.Order().Select(Veld)];

        Assert.Equal(opTijd, opTekst);
        Assert.All(opTekst, tekst => Assert.Equal(TimestampNormalization.Width, tekst.Length));
    }

    /// <summary>De tekst die Cosmos in <c>createdAt</c> van een klantdocument te zien krijgt.</summary>
    /// <remarks>
    /// Via het echte document en niet via een losse <c>DateTimeOffset</c>: dat is het verschil
    /// tussen "de opties normaliseren" en "dit veld gaat genormaliseerd de opslag in".
    /// </remarks>
    private static string Veld(DateTimeOffset moment)
    {
        string json = JsonSerializer.Serialize(
            new CustomerDocument
            {
                Id = PortalDocumentIds.Customer,
                PartitionKey = "bakker",
                CustomerId = "bakker",
                Name = "Bakker Logistiek",
                CreatedAt = moment,
            },
            Opties);

        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("createdAt").GetString()!;
    }

    /// <summary>
    /// Een credential die nooit om een token gevraagd wordt.
    /// </summary>
    /// <remarks>
    /// Een <c>CosmosClient</c> aanmaken doet geen netwerkverkeer en vraagt dus ook geen
    /// token. Een echte <c>DefaultAzureCredential</c> zou hier wel de omgeving van de bouwmachine
    /// gaan aftasten, en dan hangt een test over serialisatievorm aan de aanwezigheid van een
    /// Azure-aanmelding.
    /// </remarks>
    private sealed class Nepcredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Er hoort in deze test geen token opgevraagd te worden.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Er hoort in deze test geen token opgevraagd te worden.");
    }
}
