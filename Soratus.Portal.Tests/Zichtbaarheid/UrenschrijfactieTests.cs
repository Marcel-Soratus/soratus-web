using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// Wat het urenscherm werkelijk wegschrijft: boeken, corrigeren, fiatteren en afwijzen (§3.6).
/// </summary>
/// <remarks>
/// <para><strong>Wat hier wél wordt gemeten en wat niet.</strong> bUnit rendert interactief en doet
/// geen echt HTTP-verzoek, dus de modelbinding van static SSR — de stap die een
/// <c>name</c>-attribuut aan een eigenschap knoopt — komt hier niet langs. Wat er wel langskomt is
/// alles daarna: het indienen van het formulier, de veldcontroles, de aanroep naar
/// <see cref="IPortalHoursStore"/>, wat er in de opslag belandt en waar de pagina daarna naartoe
/// stuurt.</para>
///
/// <para>Die ene ontbrekende stap wordt apart gemeten, in
/// <see cref="UrenformulierbindingTests"/>: daar wordt vastgelegd dat elk <c>name</c>-attribuut in
/// de markup overeenkomt met een eigenschap van het model dat bij die formuliernaam hoort. Dat is de
/// fout die anders stil doorloopt — een verkeerd voorvoegsel laat een veld nergens aankomen — en het
/// is precies de fout die in dit werk één keer bijna is opgeleverd.</para>
///
/// <para><strong>Het formuliermodel wordt gezet zoals de modelbinder het zou zetten.</strong> Dat is
/// een simulatie van één stap en niet van het gedrag: de eigenschap is publiek en wordt in productie
/// door het framework uit de POST gevuld. Wat erna gebeurt is echte productiecode.</para>
/// </remarks>
public class UrenschrijfactieTests : Portaalrendertest
{
    private static Type Urenpagina =>
        Paginaverzameling.MetRoute("/klant/{Slug}/uren")
        ?? throw new InvalidOperationException(
            "Er staat geen pagina op route '/klant/{Slug}/uren'.");

    // ── Boeken ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenLeegBoekformulierLegtNietsVastEnMaaktGeenRegelVanNulUur()
    {
        // Punt 15, gemeten door het scherm heen. Het urenveld is leeg; er hoort een melding onder dat
        // veld te komen en géén urenregel van nul uur op de specificatie van de klant.
        MeldOperatorAan();

        var cut = Render();
        var voor = Opslag.Urenregels().Count;

        Verstuur(cut, "Uren boeken");

        Assert.Equal(voor, Opslag.Urenregels().Count);
        Assert.Empty(Opslag.Boekingen);
        Assert.Contains("Vul het aantal uren in", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenIngevuldBoekformulierLegtEenGefiatteerdeRegelVastEnStuurtNaarDieMaand()
    {
        // De spiegel van de test hierboven, en de gewone weg. De regel landt meteen als gefiatteerd:
        // een operator in het portaal ís het akkoord van Soratus (§5), en de bron is portaal.
        MeldOperatorAan();

        var cut = Render();

        Formulier(cut).Boeking = new HourBookingForm
        {
            Month = Vasteportaalopslag.Dezemaand,
            Hours = "2,5",
            Category = HourCategories.Development,
            By = "Sanne de Wit",
            Note = "Nieuwe koppeling ingericht",
        };

        Verstuur(cut, "Uren boeken");

        var regel = Opslag.Urenregels().Single(r =>
            string.Equals(r.Note, "Nieuwe koppeling ingericht", StringComparison.Ordinal));

        Assert.Equal(2.5m, regel.Hours);
        Assert.Equal(HourEntryStatus.Approved, regel.Status);
        Assert.Equal(HourEntrySource.Portal, regel.Source);
        Assert.Equal(Vasteportaalopslag.Dezemaand, regel.Month);

        // POST → redirect → GET, naar de maand waar de boeking over ging. Zonder die redirect stuurt
        // een verversing hetzelfde formulier opnieuw, en dat is een tweede gefactureerd uur.
        Assert.Equal(
            $"/klant/{EigenKlant}/uren?maand={Vasteportaalopslag.Dezemaand}",
            Doorstuurdoel());
    }

    [Fact]
    public void EenTweedeVerzendingVanHetzelfdeFormulierLevertEenConflictEnGeenTweedeRegel()
    {
        // Dit is de énige bescherming tegen dubbel indienen die dit scherm op static SSR heeft: de
        // documentsleutel is afgeleid van het moment en de inhoud (HourEntryKeys.ForPortal), dus twee
        // verzendingen binnen dezelfde milliseconde botsen. De klok staat in deze test stil, dus dit
        // meet precies dat geval — en niet het geval waarin de operator drie seconden later opnieuw
        // klikt. Dat gat staat in het rapport van fase 3.
        MeldOperatorAan();

        var cut = Render();
        var boeking = new HourBookingForm
        {
            Month = Vasteportaalopslag.Dezemaand,
            Hours = "2",
            Category = HourCategories.Support,
            By = "Sanne de Wit",
            Note = "Vraag over de intake",
        };

        Formulier(cut).Boeking = boeking;
        Verstuur(cut, "Uren boeken");

        var na = Opslag.Urenregels().Count;

        Formulier(cut).Boeking = boeking;
        Verstuur(cut, "Uren boeken");

        Assert.Equal(na, Opslag.Urenregels().Count);
        Assert.Contains("staat er al", cut.Markup, StringComparison.Ordinal);
    }

    // ── Corrigeren ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenCorrectieLegtEenExtraGefiatteerdeRegelVastEnWijzigtNiets()
    {
        // Besluit 16 in één test. Het maandtotaal schuift op met de correctie, er komt een regel bij
        // in plaats van dat er één verandert, en geen enkele bestaande regel is aangeraakt — want dan
        // zou het totaal van vorige maand niet meer dat van vorige maand zijn.
        MeldOperatorAan();

        var cut = Render();
        var voor = Opslag.Urenregels();

        Formulier(cut).Correctie = new HourCorrectionForm
        {
            Month = Vasteportaalopslag.Dezemaand,
            Hours = "-1,5",
            By = "Sanne de Wit",
            Note = "Dubbele boeking teruggedraaid",
        };

        Verstuur(cut, "Correctie plaatsen");

        var na = Opslag.Urenregels();

        Assert.Equal(voor.Count + 1, na.Count);

        var correctie = na.Single(r =>
            string.Equals(r.Note, "Dubbele boeking teruggedraaid", StringComparison.Ordinal));

        Assert.Equal(-1.5m, correctie.Hours);
        Assert.Equal(HourCategories.Correction, correctie.Category);
        Assert.Equal(HourEntrySource.Portal, correctie.Source);
        Assert.Equal(HourEntryStatus.Approved, correctie.Status);

        // Geen enkele bestaande regel is van waarde veranderd.
        foreach (var oud in voor)
        {
            var huidig = na.Single(r => string.Equals(r.Id, oud.Id, StringComparison.Ordinal));

            Assert.Equal(oud.Hours, huidig.Hours);
            Assert.Equal(oud.Status, huidig.Status);
            Assert.Equal(oud.ETag, huidig.ETag);
        }
    }

    [Fact]
    public void EenCorrectieVanNulUurLegtNietsVast()
    {
        // De melding komt uit HourCorrection.Validate en dus als blok boven de knop: het is geen fout
        // in één veld maar in wat de operator wil. En de opslag heeft de aanroep wél gezien, dus dit
        // meet de weigering en niet een formulier dat niets verstuurde.
        MeldOperatorAan();

        var cut = Render();
        var voor = Opslag.Urenregels().Count;

        Formulier(cut).Correctie = new HourCorrectionForm
        {
            Month = Vasteportaalopslag.Dezemaand,
            Hours = "0",
            By = "Sanne de Wit",
            Note = "Niets",
        };

        Verstuur(cut, "Correctie plaatsen");

        Assert.Equal(voor, Opslag.Urenregels().Count);
        Assert.Single(Opslag.Correcties);
        Assert.Contains("verandert niets", cut.Markup, StringComparison.Ordinal);
    }

    // ── Fiatteren ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FiatterenGaatOverTweeAdressenEnLaatDeRegelDaarnaMeetellen()
    {
        // De hele stroom: de rij-actie is een link naar een tweede adres, daar staat de regel voluit
        // met de waarschuwing dat het onomkeerbaar is, en één knop legt het besluit vast.
        MeldOperatorAan();

        var wachtend = Tefiatteren();

        var cut = Render($"?maand={wachtend.Month}&beoordeel={wachtend.Id}&actie=fiatteren");

        Assert.Contains(wachtend.Note, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("niet ongedaan worden gemaakt", cut.Markup, StringComparison.Ordinal);

        Verstuur(cut, "Regel fiatteren");

        var na = Opslag.Urenregels().Single(r => string.Equals(r.Id, wachtend.Id, StringComparison.Ordinal));

        Assert.Equal(HourEntryStatus.Approved, na.Status);
        Assert.Equal(Testgegevens.Nu, na.ApprovedAt);
        Assert.Equal($"/klant/{EigenKlant}/uren?maand={wachtend.Month}", Doorstuurdoel());
    }

    [Fact]
    public void DeFiatteerlinkVanEenRegelWijstNaarDeMaandVanDieRegel()
    {
        // Zodat de operator na zijn besluit op de maand landt die hij heeft veranderd, en het
        // maandtotaal ziet dat erdoor is opgeschoven.
        MeldOperatorAan();

        var wachtend = Tefiatteren();
        var cut = Render($"?maand={wachtend.Month}");

        var links = cut.FindAll(".row-actions a")
            .Select(a => a.GetAttribute("href") ?? string.Empty)
            .Where(href => href.Contains(wachtend.Id, StringComparison.Ordinal))
            .ToArray();

        Assert.Contains(links, href => href.Contains($"maand={wachtend.Month}", StringComparison.Ordinal));
        Assert.Contains(links, href => href.Contains("actie=fiatteren", StringComparison.Ordinal));
        Assert.Contains(links, href => href.Contains("actie=afwijzen", StringComparison.Ordinal));
    }

    [Fact]
    public void HetSchermGeeftGeenEtagMeeBijHetFiatteren()
    {
        // Een besluit met een reden, en het staat hier zodat het niet stil verandert. Op static SSR
        // is de enige plek om een etag tussen twee verzoeken vast te houden de paginabron, en daar
        // hoort een schrijfvoorwaarde niet te staan (zie UrenschermTests). Wat de bescherming dan
        // levert is de overgangstoets aan de schrijfkant, en die wordt in de test hieronder gemeten.
        MeldOperatorAan();

        var wachtend = Tefiatteren();
        var cut = Render($"?maand={wachtend.Month}&beoordeel={wachtend.Id}&actie=fiatteren");

        Verstuur(cut, "Regel fiatteren");

        Assert.Equal((wachtend.Id, null), Opslag.Fiatteringen.Single());
    }

    [Fact]
    public void EenGedeeldeLinkNaarEenAlGefiatteerdeRegelGeeftDeRedenEnGeenKnop()
    {
        // De vervanging van de etag: de overgangsregel uit HourEntryTransitions, en niet een eigen
        // vergelijking. Een tweede operator die net eerder was levert dus een mededeling op en geen
        // knop die een melding geeft — en zeker geen stille overschrijving.
        MeldOperatorAan();

        var wachtend = Tefiatteren();

        Opslag.EenAndereOperatorBeoordeeltDeRegel(wachtend.Id, HourEntryStatus.Approved);

        var cut = Render($"?maand={wachtend.Month}&beoordeel={wachtend.Id}&actie=fiatteren");

        Assert.Contains("al gefiatteerd", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("section[aria-label='Regel fiatteren']"));
    }

    [Fact]
    public void EenBeoordelingslinkNaarEenRegelDieHierNietStaatMeldtDatOok()
    {
        // Een gedeelde link naar een regel uit een andere maand, of een verzonnen id. Zwijgen zou de
        // operator laten denken dat zijn klik niets deed.
        MeldOperatorAan();

        var cut = Render("?beoordeel=hourEntry-bestaat-niet&actie=fiatteren");

        Assert.Contains("staat niet in deze weergave", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("section[aria-label='Regel fiatteren']"));
    }

    [Fact]
    public void EenVerzonnenActieInDeUrlLevertGeenBeoordelingskaartOp()
    {
        // De veilige uitkomst: er gebeurt niets, en er staat ook geen melding — met een geldige id en
        // een onbekende actie is er niets aan de hand om te melden.
        MeldOperatorAan();

        var wachtend = Tefiatteren();
        var cut = Render($"?beoordeel={wachtend.Id}&actie=verwijderen");

        Assert.Empty(cut.FindAll("section[aria-label='Regel fiatteren']"));
        Assert.Empty(cut.FindAll("section[aria-label='Regel afwijzen']"));
    }

    // ── Afwijzen ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AfwijzenZonderRedenLegtNietsVast()
    {
        // Punt 17: de regel blijft staan met de reden erbij, en zonder reden is hij over een maand
        // niet meer te verklaren. De controle staat vóór de aanroep naar de opslag, want een lege
        // reden is een fout in het formulier en niet in de opslag.
        MeldOperatorAan();

        var wachtend = Tefiatteren();
        var cut = Render($"?maand={wachtend.Month}&beoordeel={wachtend.Id}&actie=afwijzen");

        Verstuur(cut, "Regel afwijzen");

        Assert.Empty(Opslag.Afwijzingen);
        Assert.Equal(
            HourEntryStatus.Pending,
            Opslag.Urenregels().Single(r => string.Equals(r.Id, wachtend.Id, StringComparison.Ordinal)).Status);
    }

    [Fact]
    public void AfwijzenMetEenRedenLaatDeRegelStaanEnHaaltHemUitDeSpecificatie()
    {
        // De spiegel, en de eigenschap die punt 17 oplevert: het document blijft bestaan, telt niet
        // mee, en staat voor de operator in de lijst eronder. Er wordt niets verwijderd — een
        // koppeling die zijn aanroep herhaalt botst op dit document en het besluit blijft dus staan.
        MeldOperatorAan();

        var wachtend = Tefiatteren();
        var cut = Render($"?maand={wachtend.Month}&beoordeel={wachtend.Id}&actie=afwijzen");

        Formulier(cut).Beoordeling = new HourJudgementForm { Reason = "Buiten de opdracht" };

        Verstuur(cut, "Regel afwijzen");

        var na = Opslag.Urenregels().Single(r => string.Equals(r.Id, wachtend.Id, StringComparison.Ordinal));

        Assert.Equal(HourEntryStatus.Rejected, na.Status);
        Assert.Equal("Buiten de opdracht", na.RejectionReason);
        Assert.Equal(Testgegevens.Nu, na.RejectedAt);
    }

    [Fact]
    public void EenAfgewezenRegelKanAlsnogGefiatteerdWorden()
    {
        // Punt 18, de andere kant: afwijzen is een besluit van een mens en mensen klikken mis. Was
        // dat onomkeerbaar, dan was de enige uitweg de koppeling opnieuw laten inschieten — en dat kan
        // niet, want de idempotentiesleutel botst op het document dat er al staat.
        MeldOperatorAan();

        var afgewezen = Opslag.Urenregels().First(r => r.Status == HourEntryStatus.Rejected);
        var cut = Render($"?maand={afgewezen.Month}&beoordeel={afgewezen.Id}&actie=fiatteren");

        Verstuur(cut, "Regel fiatteren");

        var na = Opslag.Urenregels().Single(r => string.Equals(r.Id, afgewezen.Id, StringComparison.Ordinal));

        Assert.Equal(HourEntryStatus.Approved, na.Status);

        // De afwijzing wordt gewist en niet naast de fiattering bewaard: een document met beide erop
        // is niet te lezen, want het scherm moet dan kiezen welke van de twee het toont.
        Assert.Null(na.RejectionReason);
    }

    // ── Een klant kan hier niets ────────────────────────────────────────────────────────────────

    [Fact]
    public void EenKlantHeeftGeenEnkelFormulierOmTeVersturen()
    {
        // De spiegel van alles hierboven, en de reden dat de schrijfmethoden een CustomerWriteScope
        // vragen: een klantpagina heeft dat argument niet en kan het niet maken.
        MeldKlantAan();

        var cut = Render($"?beoordeel={Tefiatteren().Id}&actie=fiatteren");

        Assert.Empty(cut.FindAll("form"));
        Assert.Empty(Opslag.Fiatteringen);
        Assert.Empty(Opslag.Boekingen);
    }

    // ── Gereedschap ─────────────────────────────────────────────────────────────────────────────

    private IRenderedComponent<Bunit.Rendering.ContainerFragment> Render(string? query = null)
    {
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/klant/{EigenKlant}/uren{query}");

        return RenderPagina(Urenpagina);
    }

    /// <summary>De eerste te fiatteren regel uit de standaardgegevens.</summary>
    private HourEntryDocument Tefiatteren() =>
        Opslag.Urenregels().First(regel => regel.Status == HourEntryStatus.Pending);

    /// <summary>
    /// De pagina zelf, om een formuliermodel op te zetten zoals de modelbinder dat zou doen.
    /// </summary>
    /// <remarks>
    /// Dit is de enige plek in deze tests waar er in het component wordt gereikt, en het is precies
    /// de stap die bUnit niet kan doen: er is geen echt HTTP-verzoek, dus er is geen POST waaruit het
    /// model gevuld wordt. De eigenschap is publiek omdat het framework hem in productie vult; hier
    /// wordt dat nagedaan en niets anders.
    /// </remarks>
    private static Uren Formulier(IRenderedComponent<Bunit.Rendering.ContainerFragment> cut) =>
        cut.FindComponent<Uren>().Instance;

    /// <summary>Verstuurt het formulier van de kaart met deze kop.</summary>
    private static void Verstuur(
        IRenderedComponent<Bunit.Rendering.ContainerFragment> cut,
        string kop) =>
        cut.Find($"section[aria-label='{kop}'] form").Submit();
}
