using Soratus.Portal.Components.Pages;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// De getallen van de contractschermen: wat er in een veld mag staan, en wat eruit komt.
/// </summary>
/// <remarks>
/// <para>Drie schermen lezen en schrijven dezelfde bedragen: de contractkaart van de klant, het
/// bewerkbare eiland van de operator en het aanmaakformulier. Dit is de enige plek waar tekst een
/// getal wordt, en het is de plek waar een fout niet opvalt: een tarief dat stil met honderd wordt
/// vermenigvuldigd gaat de factuur in, en een leeg veld dat als nul wordt weggeschreven legt een
/// afspraak vast die niemand heeft gemaakt.</para>
///
/// <para>Beide kanten staan hier: wat er geaccepteerd hoort te worden, en wat er geweigerd hoort te
/// worden. Alleen de eerste helft testen zou een parser die alles slikt goedkeuren; alleen de tweede
/// zou een parser goedkeuren die niets doorlaat.</para>
/// </remarks>
public class ContractTextTests
{
    // ── Wat er doorheen hoort te komen ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("125,50", 125.50)]
    [InlineData("125.50", 125.50)]
    [InlineData("137,5", 137.5)]
    [InlineData("8", 8)]
    [InlineData("0", 0)]
    [InlineData("0,5", 0.5)]
    [InlineData("-5", -5)]
    [InlineData("  7,5  ", 7.5)]
    public void EenKommaEnEenPuntZijnBeideEenDecimaalteken(string invoer, decimal verwacht)
    {
        // "125.50" is precies wat een browser teruggeeft voor een type="number"-veld waarin iemand
        // 125,50 typte. Zou nl-NL dat met duizendtallen aan lezen, dan werd het
        // honderdvijfentwintigduizendvijftig.
        Assert.True(ContractText.TryNumber(invoer, out var waarde));
        Assert.Equal(verwacht, waarde);
    }

    [Theory]
    [InlineData("€ 125,50", 125.50)]
    [InlineData("8 %", 8)]
    [InlineData("8%", 8)]
    [InlineData("€137,50", 137.50)]
    public void EenEenheidDieMeekomtUitEenMailWordtEraafGehaald(string invoer, decimal verwacht)
    {
        // Het euro- en procentteken staan naast het veld als eenheid, maar wie een bedrag uit een
        // mail kopieert neemt ze mee. Dat is geen tikfout die een melding verdient.
        Assert.True(ContractText.TryNumber(invoer, out var waarde));
        Assert.Equal(verwacht, waarde);
    }

    [Fact]
    public void EenVasteSpatieUitEenWindowsBedragWordtEraafGehaald()
    {
        // Windows zet een vaste spatie (U+00A0) in bedragen. Als tekenreeks is dat geen gewone
        // spatie, dus Trim() haalt hem niet weg en de parser struikelt erover.
        Assert.True(ContractText.TryNumber("€ 1250,50", out var waarde));
        Assert.Equal(1250.50m, waarde);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EenLeegVeldIsGeenFoutEnLevertGeenBedragOp(string? invoer)
    {
        // Twee dingen in één test, want ze zijn niet los waar. Leeg mag: een klant in onboarding
        // heeft nog geen tarief, en een verplicht getalveld levert dan een verzonnen bedrag op.
        //
        // En leeg is geen waarde. Deze methode gaf eerder een out decimal en zette een leeg veld op
        // nul; de aanroepers schreven dat getal daarna in het contract. Een operator die het tarief
        // nog niet wist, legde daarmee uurTarief: 0 vast — een bedrag dat hij niet heeft ingetypt,
        // dat als afspraak in de opslag staat en dat in een berekening als nul meetelt.
        Assert.True(ContractText.TryNumber(invoer, out var waarde));
        Assert.Null(waarde);
    }

    // ── Wat er niet doorheen hoort te komen ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.250,50", "Nederlandse duizendscheiding met een komma erachter")]
    [InlineData("1,250.50", "Engelse duizendscheiding met een punt erachter")]
    [InlineData("12,5,5", "twee komma's")]
    [InlineData("12.5.5", "twee punten")]
    [InlineData("honderd", "tekst")]
    [InlineData("€", "alleen een eenheid")]
    [InlineData("12,5 euro per uur", "een zin")]
    public void EenScheidingstekenVoorDuizendenEnOnleesbareInvoerWordenGeweigerd(
        string invoer,
        string waarom)
    {
        // Een geweigerde invoer levert een melding onder het veld op; een stil verkeerd gelezen
        // bedrag levert een factuur op. Daarvan is de eerste met afstand de goedkoopste.
        Assert.False(ContractText.TryNumber(invoer, out var waarde));
        Assert.False(string.IsNullOrWhiteSpace(waarom));

        // En er komt geen getal uit. De aanroeper hoort op false te letten — NumberError staat
        // ernaast — maar als hij dat vergeet, belandt er geen bedrag in de opslag dat niemand heeft
        // getypt.
        Assert.Null(waarde);
    }

    [Theory]
    [InlineData("1.250", 1.25)]
    [InlineData("12.500", 12.5)]
    public void EenPuntIsAltijdEenDecimaaltekenOokWaarIemandDuizendenBedoelde(
        string invoer,
        decimal gelezen)
    {
        // Dit legt vast wat er nú gebeurt, en het is geen aanbeveling. "1.250" is voor een
        // Nederlandse lezer twaalfhonderdvijftig en wordt hier 1,25 — een factor duizend, stil, en
        // met true als uitkomst. De dubbele cultuur vangt "1.250,50" wél (twee scheidingstekens in
        // één getal kan in geen van beide culturen), maar een getal met alleen een punt is niet te
        // onderscheiden van een decimaalgetal.
        //
        // Dat is een bewuste afruil: "125.50" moet doorkomen, want dat is wat een browser voor een
        // type="number"-veld teruggeeft. De prijs staat hier zodat hij zichtbaar is en niet
        // opnieuw ontdekt hoeft te worden. Zie het rapport; de melding onder het veld
        // (ContractText.NumberError) vraagt daarom expliciet om het scheidingsteken weg te laten.
        Assert.True(ContractText.TryNumber(invoer, out var waarde));
        Assert.Equal(gelezen, waarde);
    }

    [Fact]
    public void DeMeldingOnderHetVeldZegtWatErWelMagEnWatNiet()
    {
        // Een melding zonder uitweg is alleen een mededeling. Deze noemt een waarde die het haalt en
        // de reden dat de invoer het niet haalde.
        var melding = ContractText.NumberError("125,50");

        Assert.Contains("125,50", melding, StringComparison.Ordinal);
        Assert.Contains("duizend", melding, StringComparison.OrdinalIgnoreCase);
    }

    // ── Heen en terug: wat het veld toont, leest het veld terug ─────────────────────────────────

    [Theory]
    [InlineData(125.50)]
    [InlineData(137.5)]
    [InlineData(8)]
    [InlineData(0)]
    [InlineData(0.5)]
    public void WatEenVeldToontLeestHetzelfdeVeldOokTerug(decimal waarde)
    {
        // Dit is de lus waar de wijzigingslijst op staat. Zou Editable iets opleveren dat TryNumber
        // anders leest, dan meldt het scherm na een conflict een wijziging die niemand heeft
        // gemaakt — "125,50" tegenover "125.5" — of het mist er juist een.
        Assert.True(ContractText.TryNumber(ContractText.Editable(waarde), out var terug));
        Assert.Equal(waarde, terug);
    }

    [Fact]
    public void EenNietVastgelegdBedragBlijftLeegHeenEnTerug()
    {
        Assert.Equal(string.Empty, ContractText.Editable(null));
        Assert.True(ContractText.TryNumber(ContractText.Editable(null), out var terug));
        Assert.Null(terug);
    }

    [Fact]
    public void NulStaatAlsNulInHetVeldEnNietAlsLeeg()
    {
        // Zou nul hier leeg worden, dan verandert een operator die een contract met een afgesproken
        // nul opent en op Bewaren drukt die nul stil in "niet vastgelegd" — zonder één toetsaanslag,
        // en zonder dat de wijzigingslijst er iets over zegt, want die vergelijkt de formuliertekst.
        Assert.Equal("0", ContractText.Editable(0m));
        Assert.NotEqual(ContractText.Editable(null), ContractText.Editable(0m));
    }

    // ── De woorden op de kaart ──────────────────────────────────────────────────────────────────

    [Fact]
    public void EenNietVastgelegdeBundelEnEenBundelVanNulLezenVerschillend()
    {
        // Drie uitkomsten en niet twee. "Geen urenbundel" over een leeg contract zei iets wat er
        // niet stond; nul is een afspraak die iemand heeft opgeschreven en hoort als getal op de
        // kaart te staan, niet als interpretatie.
        Assert.Null(ContractText.Hours(null));
        Assert.Equal("0 uur per maand", ContractText.Hours(0m));
        Assert.Equal("12 uur per maand", ContractText.Hours(12m));
        Assert.Equal("7,5 uur per maand", ContractText.Hours(7.5m));
    }

    [Fact]
    public void EenNietVastgelegdTariefEnEenTariefVanNulLezenVerschillend()
    {
        // Het verschil dat een klant wil weten voordat hij extra werk aanvraagt: "we rekenen niets"
        // tegenover "we hebben niets afgesproken".
        Assert.Null(ContractText.Rate(null, isInternal: false));
        Assert.Equal("€ 0,00 per uur buiten bundel", ContractText.Rate(0m, isInternal: false));
        Assert.Equal("€ 137,50 per uur buiten bundel", ContractText.Rate(137.5m, isInternal: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(137.5)]
    public void BijDeInterneBeheerklantIsElkBedragMisleidend(double? tarief)
    {
        // Ook een leeg veld, want dat suggereert dat iemand vergeten is een tarief in te vullen. Er
        // is niets om door te belasten, en dat is wat er staat.
        //
        // De parameter is een double? en niet een decimal?: xunit geeft een InlineData-waarde
        // ongewijzigd door, en naar een Nullable<decimal> is geen impliciete conversie. Vandaar de
        // cast, en niet drie losse tests.
        Assert.Equal(
            "intern — niet doorbelast",
            ContractText.Rate((decimal?)tarief, isInternal: true));
    }

    [Fact]
    public void DeIngangsdatumWordtGelezenAlsDatumEnGeschrevenAlsIsoDatum()
    {
        // Twee vormen met elk een reden. Op de kaart staat de Nederlandse vorm; in het veld en in de
        // opslag staat jjjj-mm-dd, want Cosmos vergelijkt tijdvelden als tekst en op dd-MM-yyyy
        // sorteert een lijst contracten stil verkeerd.
        var datum = new DateOnly(2025, 11, 1);

        Assert.Equal("01-11-2025", ContractText.Date(datum));
        Assert.Equal("2025-11-01", ContractText.IsoDate(datum));

        Assert.Null(ContractText.Date(null));
        Assert.Equal(string.Empty, ContractText.IsoDate(null));
    }

    [Theory]
    [InlineData(0, "0 personen")]
    [InlineData(1, "1 persoon")]
    [InlineData(3, "3 personen")]
    public void HetAantalMensenInDeToegangslijstStaatInWoorden(int aantal, string verwacht) =>
        Assert.Equal(verwacht, ContractText.People(aantal));
}
