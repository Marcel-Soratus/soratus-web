using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// De kostenkant van de portaalopslag in het geheugen (§3.7): het gemeten Azure-verbruik per maand.
/// </summary>
/// <remarks>
/// <para><strong>Dit is dezelfde klasse als de rest van <see cref="Vasteportaalopslag"/> en geen tweede
/// opslag.</strong> Het facturatiescherm leest het verbruik, het contract én de urenregels — de laatste
/// twee voor de bundel, het tarief en de opslag. Zouden dat drie fixtures zijn, dan is het maandbedrag
/// dat de een berekent niet dat van de ander, en dan meet een test over "Azure en uren op één totaal"
/// niets.</para>
///
/// <para><see cref="IPortalCostsStore"/> wordt hier op de partial aangegeven en niet in het bestand met
/// de contractkant. Dat is een lane-keuze en geen ontwerpkeuze: er werken meerdere sessies in deze
/// repository, en een nieuw bestand botst niet.</para>
///
/// <para><strong>Rijk gevuld, en dat is het punt.</strong> Er staan vijf maanden in, en ze dekken alle
/// vier de toestanden uit <see cref="AzureCostState"/> plus de vijfde die geen toestand is maar een
/// afwezigheid:</para>
///
/// <list type="table">
///   <item><term>augustus 2026</term><description>
///     <see cref="AzureCostState.Partial"/> — de lopende maand, met de vijf dienstnamen en de bedragen
///     zoals ze op 21 augustus 2026 werkelijk uit Cost Management kwamen.
///   </description></item>
///   <item><term>juli 2026</term><description>
///     <see cref="AzureCostState.Measured"/> — een volle maand, het enige bedrag waarop gefactureerd
///     mag worden.
///   </description></item>
///   <item><term>juni 2026</term><description>
///     <see cref="AzureCostState.NoLines"/> — een geslaagde meting zonder regels. <strong>Dit is de
///     gevaarlijkste rij van deze fixture</strong> en de reden dat hij erin staat: gemeten is dat een
///     resource group die niet bestaat en een bestaande resource group over een periode die nog niet
///     geboekt is hetzelfde antwoord geven (HTTP 200, nul rijen). Een test die hier € 0,00 vindt, heeft
///     een defect gevonden.
///   </description></item>
///   <item><term>mei 2026</term><description>
///     <see cref="AzureCostState.Unknown"/> met een reden — de mislukte meting.
///   </description></item>
///   <item><term>april 2026</term><description>
///     geen document. De afwezigheid hoort <see cref="AzureCostState.Unknown"/> op te leveren en niet
///     een maand die stilletjes uit het overzicht valt.
///   </description></item>
/// </list>
///
/// <para><strong>De bedragen zijn de gemeten bedragen en geen ronde getallen.</strong>
/// <c>37,4563985414928</c> voor App Service en <c>0,000242498791899135</c> voor Key Vault, precies
/// zoals de API ze gaf. Dat is niet uit precisie maar omdat die twee waarden twee eigenschappen
/// blootleggen die op ronde getallen onzichtbaar zijn: dat er ergens één keer wordt afgerond, en dat
/// een bedrag onder een cent niet als € 0,00 mag verschijnen.</para>
/// </remarks>
internal sealed partial class Vasteportaalopslag : IPortalCostsStore
{
    /// <summary>De scope waartegen de gezaaide metingen zijn gedaan. Operator-only (§2).</summary>
    /// <remarks>
    /// Een echte vorm en niet "test": dit veld is op het scherm het enige gereedschap tegen een tikfout
    /// in een resource-groepnaam, en een test die controleert of het er staat hoort naar iets te zoeken
    /// dat op een scope lijkt.
    /// </remarks>
    public const string Kostenscope =
        "/subscriptions/501a66d2-de54-4d4f-9f7c-1fbb55bec17f/resourceGroups/rg-acme-prod";

    /// <summary>De dienst met het grootste bedrag. Operator-only: de uitsplitsing is dat (§2).</summary>
    /// <remarks>
    /// De werkelijke naam uit de API. §3.7 noemt "Container Apps"; wat er in de uitvoer staat is
    /// <c>Azure App Service</c>. Een test die zoekt of de uitsplitsing op het klantscherm staat, zoekt
    /// naar deze tekst.
    /// </remarks>
    public const string Grootstedienst = "Azure App Service";

    /// <summary>Een dienst die minder dan een cent kost. Operator-only.</summary>
    /// <remarks>
    /// Gemeten: <c>Key Vault</c> kostte over de hele maand € 0,000242498791899135. Deze regel bestaat
    /// zodat er iets te meten valt aan de vraag of zo'n bedrag als <c>&lt; € 0,01</c> verschijnt en niet
    /// als <c>€ 0,00</c> — het verschil tussen "kost bijna niets" en "kost niets".
    /// </remarks>
    public const string Centendienst = "Key Vault";

    /// <summary>Een dienst die exact nul kost. Operator-only.</summary>
    /// <remarks>
    /// Ook gemeten, en het is de spiegel van <see cref="Centendienst"/>: <c>Bandwidth</c> stond op
    /// exact € 0,0000. Dít is een echte nul en die hoort wél als € 0,00 te verschijnen. Een fixture
    /// zonder deze regel zou de test "een nul is soms waar" niet mogelijk maken, en dan is de regel
    /// "een streepje is geen nul" niet te onderscheiden van "er staat nooit nul".
    /// </remarks>
    public const string Nuldienst = "Bandwidth";

    /// <summary>Het subtotaal van de lopende maand, onafgerond, zoals de API het gaf.</summary>
    public const decimal Lopendsubtotaal = 37.4563985414928m + 0.033319210410106m
        + 0.000242498791899135m;

    /// <summary>Het subtotaal van de volle maand juli.</summary>
    /// <remarks>
    /// Met opzet een ander getal dan <see cref="Lopendsubtotaal"/> en niet een veelvoud ervan, zodat een
    /// test die op een bedrag zoekt niet per ongeluk de andere maand vindt.
    /// </remarks>
    public const decimal Vollesubtotaal = 58.2412345678901m;

    /// <summary>Waarom de meting van mei is mislukt. Operator-only.</summary>
    /// <remarks>
    /// In gewone taal en zonder statuscode, zoals <see cref="AzureCostDocument.Failure"/> vraagt: dit
    /// komt op een scherm waar een operator naar kijkt. Een test die deze tekst op het klantscherm
    /// vindt, heeft een lek gevonden.
    /// </remarks>
    public const string Meetfout =
        "Cost Management liet ons vijf keer niet door; de meting is niet gelukt.";

    /// <summary>De maand met een geslaagde meting zonder regels.</summary>
    public static string Maandzonderregels { get; } =
        HourMonths.Of(Testgegevens.Nu.AddMonths(-2));

    /// <summary>De maand met een mislukte meting.</summary>
    public static string Maandmetmeetfout { get; } =
        HourMonths.Of(Testgegevens.Nu.AddMonths(-3));

    /// <summary>De maand waarvoor er geen document is.</summary>
    public static string Maandzondermeting { get; } =
        HourMonths.Of(Testgegevens.Nu.AddMonths(-4));

    private readonly Dictionary<string, Dictionary<string, AzureCostDocument>> _kosten =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _kostengezaaid;

    /// <inheritdoc />
    public Task<IReadOnlyList<AzureCostDocument>> GetAzureCostsAsync(
        CustomerScope scope,
        int year,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult(Lees(scope.CustomerId, year));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AzureCostDocument>> GetAzureCostsAsync(
        CustomerWriteScope scope,
        int year,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult(Lees(scope.CustomerId, year));
    }

    /// <summary>
    /// Haalt alle metingen weg, zodat elke maand op "onbekend" uitkomt.
    /// </summary>
    /// <remarks>
    /// Voor de toestand die in productie de gewone beginstand is: het portaal staat er, de
    /// <c>kosten-collector</c> heeft nog nooit gedraaid. Dat hoort een scherm met streepjes op te
    /// leveren en geen scherm met nullen, en dat verschil is niet te meten zonder deze methode.
    /// </remarks>
    public void GeenKosten()
    {
        _kostengezaaid = true;
        _kosten.Clear();
    }

    /// <summary>
    /// Zet een meting neer, buiten de collector om.
    /// </summary>
    /// <param name="meting">De meting. De etag wordt door deze opslag gezet.</param>
    /// <param name="klant">De klantslug.</param>
    /// <remarks>
    /// Er is geen schrijfpad op <see cref="IPortalCostsStore"/> en dat blijft zo (zie die interface),
    /// dus een test die een bijzondere toestand nodig heeft moet hem hier neerzetten. Dat is dezelfde
    /// vorm als <c>LegUrenregelVast</c> voor een regel uit een koppeling.
    /// </remarks>
    public void LegMetingVast(AzureCostDocument meting, string klant = Standaardklant)
    {
        ArgumentNullException.ThrowIfNull(meting);

        Kostenlijst(klant)[meting.Month] = meting with { ETag = NieuweEtag() };
    }

    /// <summary>De metingen zoals ze nu in de opslag staan, nieuwste maand eerst.</summary>
    /// <param name="klant">De klantslug.</param>
    /// <returns>De documenten.</returns>
    public IReadOnlyList<AzureCostDocument> Metingen(string klant = Standaardklant) =>
    [
        .. Kostenlijst(klant).Values.OrderByDescending(document => document.Month, StringComparer.Ordinal),
    ];

    private IReadOnlyList<AzureCostDocument> Lees(string klant, int jaar) =>
    [
        .. Kostenlijst(klant)
            .Values
            .Where(document => HourMonths.YearOf(document.Month) == jaar)
            .OrderByDescending(document => document.Month, StringComparer.Ordinal),
    ];

    /// <summary>
    /// De metingen van één klant, met de standaardgegevens erin bij de eerste aanraking.
    /// </summary>
    /// <remarks>
    /// Lui gezaaid en niet in de constructor, om dezelfde reden als bij de uren: de constructor staat in
    /// het andere deel van deze klasse, en een test die <see cref="GeenKosten"/> aanroept hoort dat te
    /// kunnen doen vóór de eerste lezing.
    /// </remarks>
    private Dictionary<string, AzureCostDocument> Kostenlijst(string klant)
    {
        if (!_kosten.TryGetValue(klant, out var lijst))
        {
            lijst = new Dictionary<string, AzureCostDocument>(StringComparer.Ordinal);
            _kosten[klant] = lijst;
        }

        if (!_kostengezaaid && string.Equals(klant, Standaardklant, StringComparison.OrdinalIgnoreCase))
        {
            _kostengezaaid = true;
            Zaaikosten(lijst);
        }

        return lijst;
    }

    /// <summary>Zet de vijf maanden neer die bovenaan dit bestand staan beschreven.</summary>
    private void Zaaikosten(Dictionary<string, AzureCostDocument> lijst)
    {
        var lopend = HourMonths.Of(Testgegevens.Nu);
        var vorig = HourMonths.Of(Testgegevens.Nu.AddMonths(-1));

        // De lopende maand: bedragen tot en met gisteren, dus niet volledig. Precies de toestand die
        // §3.7 "concept met live berekende bedragen" noemt.
        lijst[lopend] = Meting(
            lopend,
            AzureCostState.Partial,
            [
                new AzureCostLine { Service = Grootstedienst, Amount = 37.4563985414928m },
                new AzureCostLine { Service = "Azure Cosmos DB", Amount = 0.033319210410106m },
                new AzureCostLine { Service = Nuldienst, Amount = 0m },
                new AzureCostLine { Service = Centendienst, Amount = 0.000242498791899135m },
            ],
            coversThrough: Testgegevens.Nu.AddDays(-1).ToString("yyyy-MM-dd", null));

        // De volle vorige maand. Vier diensten en niet vijf: een dienst die de ene maand wel en de
        // andere maand niet voorkomt is de gewone gang van zaken, en een uitsplitsing die uit een vaste
        // lijst zou komen kan dat niet weergeven.
        lijst[vorig] = Meting(
            vorig,
            AzureCostState.Measured,
            [
                new AzureCostLine { Service = Grootstedienst, Amount = 58.1912345678901m },
                new AzureCostLine { Service = "Azure Cosmos DB", Amount = 0.05m },
                new AzureCostLine { Service = "Microsoft Entra", Amount = 0m },
            ],
            coversThrough: AzureCostCompleteness.Bounds(vorig).Last.ToString("yyyy-MM-dd"));

        // Een geslaagde meting zonder regels. Geen bedrag, geen valuta, geen gedekte dag — en met
        // opzet wél een scope, want dat is het enige waarmee een mens kan uitsluiten dat we de
        // verkeerde omgeving bevragen.
        lijst[Maandzonderregels] = Meting(
            Maandzonderregels,
            AzureCostState.NoLines,
            [],
            coversThrough: null);

        // Een mislukte meting, met de reden erbij.
        lijst[Maandmetmeetfout] = Meting(
            Maandmetmeetfout,
            AzureCostState.Unknown,
            [],
            coversThrough: null) with
        {
            Failure = Meetfout,
        };

        // En Maandzondermeting krijgt met opzet géén document.
    }

    private AzureCostDocument Meting(
        string maand,
        AzureCostState toestand,
        IReadOnlyList<AzureCostLine> regels,
        string? coversThrough) =>
        new()
        {
            Id = AzureCostDocumentKeys.ForMonth(maand),
            PartitionKey = Standaardklant,
            CustomerId = Standaardklant,
            Month = maand,
            State = toestand,
            Lines = regels,

            // Geen valuta zonder bedragen. Niet standaard "EUR": een verzonnen valuta naast een bedrag
            // dat we niet hebben is een tweede onwaarheid op dezelfde regel.
            Currency = regels.Count > 0 ? "EUR" : null,
            Scope = Kostenscope,
            MeasuredAt = Testgegevens.Nu - TimeSpan.FromHours(7),
            CoversThrough = coversThrough,
            ETag = NieuweEtag(),
        };
}
