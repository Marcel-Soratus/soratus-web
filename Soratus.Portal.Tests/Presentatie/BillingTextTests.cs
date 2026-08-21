using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// De getalvormen en de woorden van het facturatiescherm (§3.7).
/// </summary>
/// <remarks>
/// Dit is de laatste laag waar het verschil tussen "onbekend" en "nul" stil kan sneuvelen: hier wordt
/// er verder niets meer met het getal gedaan, dus een <c>?? 0</c> hier zou nergens meer opvallen en
/// wél op de factuur staan.
/// </remarks>
public class BillingTextTests
{
    [Fact]
    public void EenOnbekendBedragIsEenStreepjeEnGeenNulEuro()
    {
        Assert.Equal("—", BillingText.Amount(null));
    }

    [Fact]
    public void EenBedragVanNulIsNulEuro()
    {
        // De spiegel, en de enige die aantoont dat "een streepje is geen nul" niet betekent "er staat
        // nooit nul". Gemeten: Bandwidth stond op exact € 0,0000 en dat is een bedrag.
        Assert.Equal("€ 0,00", BillingText.Amount(0m));
    }

    [Theory]
    [InlineData(0.000242498791899135, "< € 0,01")]
    [InlineData(0.001, "< € 0,01")]
    [InlineData(0.0049, "< € 0,01")]
    public void EenBedragOnderEenHalveCentStaatErAlsKleinerDanEenCent(double bedrag, string verwacht)
    {
        // Gemeten: Key Vault kostte over de hele maand € 0,000242498791899135. Als € 0,00 tonen zou
        // zeggen dat die dienst niets kost, en dat is dezelfde onwaarheid als € 0,00 voor een onbekend
        // bedrag — alleen kleiner.
        Assert.Equal(verwacht, BillingText.Amount((decimal)bedrag));
    }

    [Theory]
    [InlineData(0.005, "€ 0,01")]
    [InlineData(0.006, "€ 0,01")]
    public void VanafEenHalveCentStaatErEenGewoonBedrag(double bedrag, string verwacht)
    {
        // De grens ligt op een halve cent, want daarboven rondt de gewone vorm niet meer op nul af.
        // Zonder deze spiegel mag de "kleiner dan een cent"-tak elk bedrag opslokken.
        Assert.Equal(verwacht, BillingText.Amount((decimal)bedrag));
    }

    [Fact]
    public void EenNegatiefBedragOnderEenCentKrijgtDeSpiegelvorm()
    {
        // Een correctie kan het uurbedrag negatief maken (punt 16: een correctie mag negatief zijn), en
        // dan bestaat het spiegelgeval. "< € 0,01" zou daar de verkeerde kant zeggen.
        Assert.Equal("> -€ 0,01", BillingText.Amount(-0.002m));
    }

    [Fact]
    public void EenGewoonBedragStaatErInDeNederlandseVorm()
    {
        Assert.Equal("€ 37,46", BillingText.Amount(37.46m));
        Assert.Equal("€ 1234,50", BillingText.Amount(1234.5m));
    }

    [Fact]
    public void GeenAfgesprokenOpslagIsEenStreepjeEnNietNulProcent()
    {
        // Besluit 15 op het veld dat daar als het gevaarlijkste wordt aangewezen, in de laatste laag.
        Assert.Equal("—", BillingText.Percentage(null));
    }

    [Fact]
    public void NulProcentOpslagStaatErAlsNulProcent()
    {
        // De spiegel. Nul procent opslag is een afspraak die we hebben gemaakt; geen opslag ingevuld is
        // een afspraak die nog moet komen. Zonder deze test zijn die twee op het scherm hetzelfde.
        Assert.Equal("0 %", BillingText.Percentage(0m));
        Assert.Equal("8,75 %", BillingText.Percentage(8.75m));
    }

    [Fact]
    public void DeRekensomStaatErAlleenAlsAlleVierDeGetallenErZijn()
    {
        // Vier keer null in plaats van een som met een streepje erin. Dit bedrag komt op een factuur;
        // wie het niet kan navertellen kan het niet controleren, en een som waarin een streepje staat
        // is niet na te vertellen.
        Assert.Null(BillingText.ChargedSum(null, 8.75m, 3.28m, 40.74m));
        Assert.Null(BillingText.ChargedSum(37.46m, null, 3.28m, 40.74m));
        Assert.Null(BillingText.ChargedSum(37.46m, 8.75m, null, 40.74m));
        Assert.Null(BillingText.ChargedSum(37.46m, 8.75m, 3.28m, null));
    }

    [Fact]
    public void MetAlleVierStaatDeSomErVoluit()
    {
        // De spiegel. Zonder deze test mag ChargedSum altijd null teruggeven en staat er nooit een
        // tooltip bij een bedrag dat een klant kan navragen.
        Assert.Equal(
            "€ 37,46 + 8,75 % (€ 3,28) = € 40,74",
            BillingText.ChargedSum(37.46m, 8.75m, 3.28m, 40.74m));
    }

    [Theory]
    [InlineData(AzureCostState.Measured, "volledig gemeten")]
    [InlineData(AzureCostState.Partial, "loopt nog")]
    [InlineData(AzureCostState.NoLines, "geen regels")]
    [InlineData(AzureCostState.Unknown, "onbekend")]
    public void ElkeToestandHeeftEenEigenWoord(AzureCostState toestand, string verwacht)
    {
        // Vier woorden en niet drie. "Geen regels" en "onbekend" zijn verschillende mededelingen en ze
        // vragen een verschillende handeling: de eerste is nakijken of we de juiste omgeving bevragen,
        // de tweede is opnieuw proberen.
        Assert.Equal(verwacht, BillingText.StateLabel(toestand));
    }

    [Fact]
    public void DeToelichtingBijGeenRegelsNoemtAlleDrieDeMogelijkheden()
    {
        // De belangrijkste tekst van dit scherm. Die drie zijn gemeten, ze geven hetzelfde
        // HTTP-antwoord, en de code kan ze niet uit elkaar halen — wie dit leest wel, door naar de
        // bevraagde omgeving eronder te kijken.
        var tekst = BillingText.StateTitle(AzureCostState.NoLines);

        Assert.Contains("niets verbruikt", tekst, StringComparison.Ordinal);
        Assert.Contains("nog niet geboekt", tekst, StringComparison.Ordinal);
        Assert.Contains("bestaat niet", tekst, StringComparison.Ordinal);
        Assert.Contains("geen nul", tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void ElkeToestandHeeftEenEigenToelichting()
    {
        // Vier verschillende teksten. Zonder deze test mag StateTitle voor alle vier hetzelfde
        // teruggeven en blijft de test hierboven groen.
        var teksten = new[]
        {
            AzureCostState.Measured,
            AzureCostState.Partial,
            AzureCostState.NoLines,
            AzureCostState.Unknown,
        }.Select(BillingText.StateTitle).ToArray();

        Assert.Equal(4, teksten.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ZonderGatenIsErGeenMelding()
    {
        Assert.Null(BillingText.GapReason(MonthlyChargeGap.None));
    }

    [Fact]
    public void AlleGatenStaanInDeMeldingEnNietAlleenDeEerste()
    {
        // Waarom de gaten vlaggen zijn: een operator die er één ziet gaat die oplossen en houdt dan een
        // totaal dat nog steeds ontbreekt.
        var melding = BillingText.GapReason(
            MonthlyChargeGap.AzureUnknown
            | MonthlyChargeGap.NoSurchargeAgreed
            | MonthlyChargeGap.NoBundleAgreed);

        Assert.NotNull(melding);
        Assert.Contains("niet gemeten", melding, StringComparison.Ordinal);
        Assert.Contains("beheeropslag", melding, StringComparison.Ordinal);
        Assert.Contains("urenbundel", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void DeDekkingNoemtDeDagEnHetMeetmoment()
    {
        // Twee gegevens die apart niets zeggen: "gemeten tot en met de 20e" zonder het meetmoment laat
        // open of dat gisteren of vorige maand is, en het meetmoment zonder de gedekte dag laat open
        // hoeveel van de maand erin zit.
        var tekst = BillingText.Coverage(
            new DateOnly(2026, 8, 20),
            new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero));

        Assert.NotNull(tekst);
        Assert.Contains("20-08-2026", tekst, StringComparison.Ordinal);
        Assert.Contains("21-08-2026", tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void ZonderDagEnZonderMeetmomentIsErGeenDekkingstekst()
    {
        Assert.Null(BillingText.Coverage(null, null));
    }

    [Fact]
    public void ZonderDagMaarMetMeetmomentStaatErDatErGeenBedragenZijn()
    {
        // Het geval van een geslaagde meting zonder regels. Er ís gemeten, en het meetmoment is dan het
        // antwoord op de enige vraag die er is: hoe oud is wat ik hier zie.
        var tekst = BillingText.Coverage(null, new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero));

        Assert.NotNull(tekst);
        Assert.Contains("zonder bedragen", tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void HetJaarStaatAltijdInHetPadVanEenMaand()
    {
        // Anders springt het overzicht naar het jaar van die maand zodra iemand een rij openklapt, en
        // dat is een andere pagina dan waar hij op stond. Dit is het omgekeerde van de keuze op het
        // urenscherm, waar de maand juist het jaar bepaalt omdat hij daar filtert in plaats van
        // openklapt.
        Assert.Equal(
            "/klant/acme/facturatie?jaar=2026&maand=2026-08",
            BillingText.MonthPath("acme", 2026, "2026-08"));
    }

    [Fact]
    public void EenSlugMetEenVreemdTekenWordtGeescaped()
    {
        Assert.Equal("/klant/a%20b/facturatie", BillingText.Path("a b"));
    }
}
