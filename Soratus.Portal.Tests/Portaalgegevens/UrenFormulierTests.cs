using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Portaalgegevens;

/// <summary>
/// De drie formulieren van het urenscherm (§3.6): welke meldingen bij één veld horen, en wat er van
/// de ingetypte tekst in de opslag terechtkomt.
/// </summary>
/// <remarks>
/// <para>Deze formulieren gaan als POST naar de server (static SSR) en daarna als één document naar
/// Cosmos. Er is dus precies één moment waarop tekst uit een browser een urenregel wordt, en dat
/// moment staat hier onder de loep. Wat er daarna nog gebeurt is
/// <see cref="HourBooking.Validate"/> en <see cref="HourCorrection.Validate"/>, en die staan bij de
/// datalaag.</para>
///
/// <para>De scheiding tussen die twee is de moeite waard om vast te leggen: <c>FieldErrors</c> doet
/// alleen wat aan één veld hangt, zodat de melding onder dát veld kan komen; de grenzen zelf —
/// groter dan nul, niet meer dan een etmaal, een correctie mag geen nul zijn — komen uit de datalaag
/// en komen als blok boven de knop. Twee plekken, en geen twee definities van "klopt dit".</para>
/// </remarks>
public class UrenFormulierTests
{
    // ── Het urenveld: leeg is niet nul ──────────────────────────────────────────────────────────

    [Fact]
    public void EenLeegUrenveldLegtGeenRegelVanNulUurVast()
    {
        // Punt 15, en hier scherper dan bij het contract. Bij een contractbedrag levert een leeg veld
        // dat als nul wordt bewaard een afspraak op die niemand heeft gemaakt; hier zou het een
        // urenregel van nul uur opleveren die wél op de specificatie van de klant verschijnt.
        var meldingen = Boeking(f => f.Hours = null).FieldErrors();

        Assert.True(
            meldingen.ContainsKey(nameof(HourBookingForm.Hours)),
            "Een leeg urenveld levert geen melding onder dat veld op. De sleutels die er wel zijn: " +
            string.Join(", ", meldingen.Keys));
    }

    [Fact]
    public void EenGevuldUrenveldLevertGeenMeldingOp()
    {
        // De spiegel: een controle die altijd iets vindt is even nutteloos als een die nooit iets
        // vindt.
        Assert.Empty(Boeking().FieldErrors());
    }

    [Fact]
    public void EenOnleesbaarUrenveldLegtOokGeenNulVast()
    {
        var formulier = Boeking(f => f.Hours = "twee en een half");

        Assert.True(formulier.FieldErrors().ContainsKey(nameof(HourBookingForm.Hours)));

        // En als iemand de volgorde omdraait en toch omzet: dan komt er nul uit, en nul is precies
        // wat HourBooking.Validate weigert. Er is dus geen pad waarlangs deze invoer een regel wordt.
        Assert.NotNull(formulier.ToBooking().Validate());
    }

    [Theory]
    [InlineData("2,5", 2.5)]
    [InlineData("2.5", 2.5)]
    [InlineData("3", 3)]
    public void EenUrenveldLeestEenKommaEnEenPuntAlsHetzelfdeGetal(string invoer, decimal verwacht) =>
        // Dezelfde parser als het contractscherm, en met opzet zonder scheidingsteken voor
        // duizendtallen: "125.50" leest in nl-NL anders honderdvijfentwintigduizendvijftig.
        Assert.Equal(verwacht, Boeking(f => f.Hours = invoer).ToBooking().Hours);

    [Fact]
    public void EenScheidingstekenVoorDuizendenIsEenMeldingOnderHetUrenveld() =>
        Assert.True(Boeking(f => f.Hours = "1.250,5")
            .FieldErrors()
            .ContainsKey(nameof(HourBookingForm.Hours)));

    [Theory]
    [InlineData("1.250")]
    [InlineData("1,250")]
    public void DrieCijfersAchterEenScheidingstekenVraagtOmEenKommaEnZegtWaarom(string invoer)
    {
        // Punt 23, op het urenveld. "1.250" wordt geweigerd — dat deed het al — maar de melding was de
        // algemene ("vul een getal in"), en die is bij een getal dat er staat geen antwoord. Deze
        // melding bestaat precies voor dit geval; hij komt er alleen als HoursError de invoer meegeeft
        // aan ContractText.NumberError, en dat argument is optioneel. Vandaar deze test: zonder hem
        // valt het weglaten van dat argument nergens op.
        //
        // Beide tekens, want de regel is "drie cijfers achter een scheidingsteken is een groep" en
        // niet "de punt is verdacht": nl-NL leest "1,250" als 1,25 en dat is dezelfde factor duizend
        // de andere kant op.
        var melding = Boeking(f => f.Hours = invoer).FieldErrors()[nameof(HourBookingForm.Hours)];

        Assert.Contains("factor duizend", melding, StringComparison.Ordinal);
        Assert.Contains("komma", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void EenGewoonOnleesbaarUrenveldKrijgtDeAlgemeneMeldingEnNietDieVraag() =>
        // De spiegel. Zonder deze test zou één melding voor alle gevallen ook groen staan, en dan is
        // de scherpe melding geen onderscheid meer maar de enige tekst die er is.
        Assert.DoesNotContain(
            "factor duizend",
            Boeking(f => f.Hours = "twee en een half").FieldErrors()[nameof(HourBookingForm.Hours)],
            StringComparison.Ordinal);

    // ── De overige velden van het boekformulier ─────────────────────────────────────────────────

    [Fact]
    public void EenOnbekendeCategorieIsEenMeldingOnderDatVeld()
    {
        // De categorie komt uit onze eigen keuzelijst. Staat er iets anders, dan is het formulier
        // omzeild, en dan hoort dat te blijken bij het veld en niet pas bij het opslaan.
        var meldingen = Boeking(f => f.Category = "Verzonnen").FieldErrors();

        Assert.True(meldingen.ContainsKey(nameof(HourBookingForm.Category)));
    }

    [Fact]
    public void DeCategorieCorrectieIsGeenBoekbareCategorie()
    {
        // Besluit 16: een correctie is een eigen aanroep met een eigen type. Zou hij hier boekbaar
        // zijn, dan is een correctie niet meer van een boeking te onderscheiden en is de tooltip van
        // §3.6 niet te vullen.
        var meldingen = Boeking(f => f.Category = HourCategories.Correction).FieldErrors();

        Assert.True(meldingen.ContainsKey(nameof(HourBookingForm.Category)));
    }

    [Fact]
    public void EenMeerregeligeOmschrijvingWordtGeweigerdEnNietStilAfgekapt()
    {
        // Aan de leeskant knipt CustomerHourRow de omschrijving af op de eerste regelovergang. Dat is
        // een vangnet voor wat er al staat; hier, waar een operator zelf typt, hoort een tweede regel
        // een melding op te leveren. Stil afkappen zou zijn tweede regel weggooien zonder dat hij
        // het merkt.
        var meldingen = Boeking(f => f.Note = "Eerste regel\nTweede regel").FieldErrors();

        Assert.True(meldingen.ContainsKey(nameof(HourBookingForm.Note)));
    }

    [Fact]
    public void EenOnleesbareMaandIsEenMeldingOnderHetMaandveld() =>
        Assert.True(Boeking(f => f.Month = "augustus")
            .FieldErrors()
            .ContainsKey(nameof(HourBookingForm.Month)));

    [Fact]
    public void WitruimteInEenVeldGaatNietMeeDeOpslagIn()
    {
        var boeking = Boeking(f =>
        {
            f.By = "  Sanne de Wit  ";
            f.Note = "  Koppeling afgerond  ";
        }).ToBooking();

        Assert.Equal("Sanne de Wit", boeking.By);
        Assert.Equal("Koppeling afgerond", boeking.Note);
    }

    // ── Het correctieformulier ──────────────────────────────────────────────────────────────────

    [Fact]
    public void EenCorrectieMagNegatiefZijn()
    {
        // Dit is het enige verschil met een boeking, en de hele reden dat het een eigen type is. Met
        // één formulier en een aanvinkvak zou "mag dit negatief" een if op dat vakje worden, en dan
        // is een negatieve boeking één verkeerd geschreven if ver weg.
        var correctie = Correctie(f => f.Hours = "-1,5");

        Assert.Empty(correctie.FieldErrors());
        Assert.Null(correctie.ToCorrection().Validate());
        Assert.Equal(-1.5m, correctie.ToCorrection().Hours);
    }

    [Fact]
    public void EenBoekingMagNietNegatiefZijn()
    {
        // De spiegel van de test hierboven, en de reden dat de twee formulieren gescheiden zijn.
        var boeking = Boeking(f => f.Hours = "-1,5").ToBooking();

        Assert.NotNull(boeking.Validate());
    }

    [Fact]
    public void EenCorrectieVanNulUurWordtGeweigerd()
    {
        // Nul verandert niets, en een handeling die niets doet hoort niet stil te slagen. De melding
        // komt uit HourCorrection.Validate en dus als blok boven de knop: het is geen fout in één
        // veld maar in wat de operator wil.
        var correctie = Correctie(f => f.Hours = "0");

        Assert.Empty(correctie.FieldErrors());
        Assert.NotNull(correctie.ToCorrection().Validate());
    }

    [Fact]
    public void EenCorrectieZonderRedenWordtGeweigerd() =>
        // Besluit 16: de omschrijving ís de audittrail. §9 van de spec houdt open of er per correctie
        // een audittrail komt; met dit besluit vervalt die vraag, en dan mag hij niet leeg zijn.
        Assert.True(Correctie(f => f.Note = null)
            .FieldErrors()
            .ContainsKey(nameof(HourCorrectionForm.Note)));

    // ── Het beoordelingsformulier ───────────────────────────────────────────────────────────────

    [Fact]
    public void EenAfwijzingZonderRedenWordtGeweigerd()
    {
        // Punt 17: de regel blijft staan, en een afgewezen regel zonder reden is over een maand niet
        // meer te verklaren tegenover de klant die vraagt waarom er iets niet op zijn factuur staat.
        Assert.NotNull(new HourJudgementForm().ReasonError());
        Assert.Null(new HourJudgementForm { Reason = "Al geboekt" }.ReasonError());
    }

    [Fact]
    public void DeMeldingOnderHetRedenveldKomtUitDeSchrijfkantEnNietUitEenEigenTekst()
    {
        // Zou het formulier zijn eigen tekst verzinnen, dan bestaan er twee definities van "een reden
        // is verplicht" en laat de ene iets door dat de andere weigert.
        var formulier = new HourJudgementForm { Reason = new string('x', 401) };

        Assert.Equal(
            new HourRejection { EntryId = "-", Reason = formulier.Reason! }.Validate(),
            formulier.ReasonError());
    }

    [Fact]
    public void EenAfwijzingGaatZonderEtagDeOpslagIn()
    {
        // Een besluit met een reden, en de reden staat in Uren.razor: op static SSR is de enige plek
        // om een etag tussen twee verzoeken vast te houden de paginabron, en daar hoort een
        // schrijfvoorwaarde niet te staan. Deze test legt het besluit vast in plaats van het stil te
        // laten — verandert het ooit, dan gaat hij rood en is dat het moment om te kijken waarom.
        var afwijzing = new HourJudgementForm { Reason = "Al geboekt" }.ToRejection("hourEntry-abc");

        Assert.Null(afwijzing.BasedOnETag);
        Assert.Equal("hourEntry-abc", afwijzing.EntryId);
        Assert.Null(afwijzing.Validate());
    }

    /// <summary>Een volledig ingevuld boekformulier, en daarna wat de test nodig heeft.</summary>
    private static HourBookingForm Boeking(Action<HourBookingForm>? vul = null)
    {
        var formulier = new HourBookingForm
        {
            Month = "2026-08",
            Hours = "2,5",
            Category = HourCategories.Development,
            By = "Sanne de Wit",
            Note = "Koppeling voorraadstanden afgerond",
        };

        vul?.Invoke(formulier);

        return formulier;
    }

    /// <summary>Een volledig ingevuld correctieformulier, en daarna wat de test nodig heeft.</summary>
    private static HourCorrectionForm Correctie(Action<HourCorrectionForm>? vul = null)
    {
        var formulier = new HourCorrectionForm
        {
            Month = "2026-08",
            Hours = "-0,5",
            By = "Sanne de Wit",
            Note = "Verkeerde maand gecorrigeerd",
        };

        vul?.Invoke(formulier);

        return formulier;
    }
}
