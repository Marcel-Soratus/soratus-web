using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De invariant van <see cref="AzureCostReading"/>: er is een subtotaal dan en slechts dan als er
/// regels zijn.
/// </summary>
/// <remarks>
/// Dit is het smalste en belangrijkste stuk van fase 4a. Zolang deze invariant staat, bestaat er geen
/// weg waarlangs "we weten het niet" als € 0,00 op een factuur belandt — niet doordat iemand ergens
/// een <c>if</c> heeft gezet, maar doordat het veld geen getal draagt als er niets is opgeteld.
/// </remarks>
public class AzureKostenlezingTests
{
    private static readonly DateTimeOffset Gemeten = new(2026, 8, 21, 4, 0, 0, TimeSpan.Zero);

    private static AzureCostDocument Document(
        AzureCostState toestand,
        params AzureCostLine[] regels) =>
        new()
        {
            Id = AzureCostDocumentKeys.ForMonth("2026-08"),
            PartitionKey = "acme-logistiek",
            CustomerId = "acme-logistiek",
            Month = "2026-08",
            State = toestand,
            Lines = regels,
            Currency = regels.Length > 0 ? "EUR" : null,
            Scope = "/subscriptions/501a66d2/resourceGroups/MBV",
            MeasuredAt = Gemeten,
            CoversThrough = regels.Length > 0 ? "2026-08-20" : null,
        };

    [Fact]
    public void GeenDocumentBetekentOnbekendEnGeenLegeMaandMetNulErin()
    {
        // Dezelfde regel als "geen document betekent geen status" (punt 2 van de fase-0-afwijkingen):
        // de afwezigheid van een meting is geen meting van nul. Dit is de gewone beginstand — het
        // portaal staat er, de kosten-collector heeft nog nooit gedraaid.
        var lezing = AzureCostReading.From("2026-08", "augustus 2026", document: null);

        Assert.Equal(AzureCostState.Unknown, lezing.State);
        Assert.Null(lezing.Subtotal);
        Assert.False(lezing.HasAmount);
        Assert.Empty(lezing.Lines);
        Assert.Null(lezing.MeasuredAt);
        Assert.Null(lezing.Currency);
    }

    [Fact]
    public void MetRegelsIsHetSubtotaalDeExacteSom()
    {
        // De spiegel. Zonder deze test mag Subtotal altijd null zijn en is er nooit iets te factureren.
        // Onafgerond: het afronden gebeurt één keer, op het bedrag dat wordt doorbelast.
        var lezing = AzureCostReading.From(
            "2026-08",
            "augustus 2026",
            Document(
                AzureCostState.Partial,
                new AzureCostLine { Service = "Azure App Service", Amount = 37.4563985414928m },
                new AzureCostLine { Service = "Key Vault", Amount = 0.000242498791899135m }));

        Assert.Equal(37.4563985414928m + 0.000242498791899135m, lezing.Subtotal);
        Assert.True(lezing.HasAmount);
        Assert.Equal(new DateOnly(2026, 8, 20), lezing.CoversThrough);
    }

    [Fact]
    public void RegelsDieTotNulOptellenLeverenEenSubtotaalVanNulOp()
    {
        // De andere helft van de kernregel, en de reden dat het onderscheid überhaupt in een type past.
        // In de echte uitvoer staan Bandwidth en Microsoft Entra op exact € 0,0000; een maand met alleen
        // zulke regels heeft een subtotaal van nul, en dát is een bedrag.
        var lezing = AzureCostReading.From(
            "2026-08",
            "augustus 2026",
            Document(
                AzureCostState.Measured,
                new AzureCostLine { Service = "Bandwidth", Amount = 0m },
                new AzureCostLine { Service = "Microsoft Entra", Amount = 0m }));

        Assert.Equal(0m, lezing.Subtotal);
        Assert.True(lezing.HasAmount);
    }

    [Fact]
    public void EenGeslaagdeMetingZonderRegelsHeeftGeenSubtotaal()
    {
        // Gemeten: dit is het antwoord van een resource group die niet bestaat én van een bestaande
        // omgeving over een periode die nog niet is geboekt. Beide keren HTTP 200 met nul rijen.
        var lezing = AzureCostReading.From(
            "2026-08",
            "augustus 2026",
            Document(AzureCostState.NoLines));

        Assert.Equal(AzureCostState.NoLines, lezing.State);
        Assert.Null(lezing.Subtotal);
        Assert.False(lezing.HasAmount);

        // En de scope komt wél mee: die is het enige gereedschap waarmee een mens de derde
        // mogelijkheid — we bevragen de verkeerde omgeving — kan uitsluiten.
        Assert.Contains("MBV", lezing.Scope, StringComparison.Ordinal);
    }

    [Fact]
    public void EenDocumentDatVolledigZegtMaarGeenRegelsHeeftLevertGeenBedragOp()
    {
        // Een kapotte collector, en de vraag is welke kant het opvalt. De invariant staat boven de
        // bewering van het document: geen regels betekent geen subtotaal, ook als er "gemeten" op staat.
        //
        // Het gevolg is dat het scherm "volledig gemeten" naast een streepje zet, en dat is zichtbaar
        // verkeerd. Dat is met opzet de veilige richting: de fout is dan een collector die gerepareerd
        // moet worden, en niet een bedrag dat te laag is. Zou de toestand hier stil naar NoLines worden
        // verlaagd, dan lijkt een kapotte collector maanden lang op een klant zonder verbruik.
        var lezing = AzureCostReading.From(
            "2026-08",
            "augustus 2026",
            Document(AzureCostState.Measured));

        Assert.Equal(AzureCostState.Measured, lezing.State);
        Assert.Null(lezing.Subtotal);
    }

    [Fact]
    public void DeRegelsStaanOpBedragGesorteerdEnNietOpNaam()
    {
        // Een operator die naar de uitsplitsing kijkt, kijkt naar wat de kosten drijft. Alfabetisch zou
        // Azure App Service — 99,7% van het bedrag in de gemeten maand — willekeurig ergens in de lijst
        // staan.
        // De namen staan met opzet in de omgekeerde alfabetische orde van hun bedrag. De eerste versie
        // van deze test gebruikte de échte diensten — Azure App Service duur, Key Vault goedkoop — en
        // daar is alfabetisch per ongeluk hetzelfde als op bedrag. Een mutatie die OrderByDescending in
        // OrderBy(Service) veranderde maakte niets rood; dit is de reparatie.
        var lezing = AzureCostReading.From(
            "2026-08",
            "augustus 2026",
            Document(
                AzureCostState.Measured,
                new AzureCostLine { Service = "Azure App Service", Amount = 0.03m },
                new AzureCostLine { Service = "Bandwidth", Amount = 37.45m },
                new AzureCostLine { Service = "Key Vault", Amount = 1.20m }));

        Assert.Equal(
            ["Bandwidth", "Key Vault", "Azure App Service"],
            lezing.Lines.Select(regel => regel.Service));
    }

    [Fact]
    public void EenOnleesbareGedekteDagWordtNullEnRaaktHetBedragNiet()
    {
        // Een kapotte datum hoort geen bedrag te beïnvloeden. Het gevolg is dat het scherm niet kan
        // zeggen tot wanneer de meting loopt, en dat is de juiste verhouding tussen die twee.
        var lezing = AzureCostReading.From(
            "2026-08",
            "augustus 2026",
            Document(AzureCostState.Partial, new AzureCostLine { Service = "Bandwidth", Amount = 1m })
                with { CoversThrough = "20-08-2026" });

        Assert.Null(lezing.CoversThrough);
        Assert.Equal(1m, lezing.Subtotal);
    }

    [Fact]
    public void DeSleutelVanEenMaandIsAfgeleidEnNietWillekeurig()
    {
        // Eén document per klant per maand, zodat de verzameling van vandaag die van gisteren vervangt.
        // Met een willekeurige sleutel zou er per dag een document bijkomen en zou de leeskant moeten
        // kiezen welke van de dertig de waarheid is.
        Assert.Equal("azureCost-2026-08", AzureCostDocumentKeys.ForMonth("2026-08"));
        Assert.Equal("azureCost", AzureCostDocumentKeys.Kind);
    }
}
