using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Support;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Tests.Zichtbaarheid;

namespace Soratus.Portal.Tests.Support;

/// <summary>
/// Het supportscherm: wat de klant ziet, wat de operator ziet, en dat het verschil een typeverschil is.
/// </summary>
/// <remarks>
/// <para><strong>De diensten van dit scherm staan hier en niet in <c>Portaalrendertest.MeldAan</c>.</strong>
/// Dat is een werkomstandigheid: die basisklasse is van een andere sessie en er werken meerdere sessies
/// in deze repository, dus een wijziging daar is een lost update. Het gevolg staat in het rapport — tot
/// die registratie er staat, valt deze pagina wél onder het reflectievangnet en levert daar een
/// DI-fout op in plaats van markup.</para>
///
/// <para>De registraties gebeuren ná <c>MeldKlantAan()</c> en vóór de eerste render. Dat kan omdat
/// bUnit zijn container pas bij de eerste oplossing dichtzet.</para>
/// </remarks>
public class SupportschermTests : Portaalrendertest
{
    /// <summary>De pagina van het supportscherm.</summary>
    private static Type Pagina =>
        Paginaverzameling.MetRoute("/klant/{Slug}/support")
        ?? throw new InvalidOperationException(
            "Er staat geen pagina op route '/klant/{Slug}/support'. Is hij hernoemd, dan hoort deze "
            + "test mee te veranderen.");

    /// <summary>
    /// Zet de diensten van het supportscherm klaar en levert de opslag waarin ze schrijven.
    /// </summary>
    /// <param name="eerstelijn">De eerstelijn, of <c>null</c> voor "niet aangesloten".</param>
    /// <returns>De opslag.</returns>
    private Vasteportaalopslag Support(ISupportFirstLine? eerstelijn = null)
    {
        var lijst = Autorisatiebron.Standaard();

        Services.AddSingleton(VasteSupportweergaven.Weergaven(Opslag, lijst));
        Services.AddSingleton<ISupportStore>(Opslag);
        Services.AddSingleton(VasteSupportweergaven.Balie(Opslag, eerstelijn, lijst));

        return Opslag;
    }

    // ── Wat er rendert, en voor wie ─────────────────────────────────────────────────────────────

    [Fact]
    public void DeKlantZietDeDraadEnDeUitwegNaarEenMens()
    {
        MeldKlantAan();
        var opslag = Support();

        opslag.ZetSupportbericht(SupportdraadTests.Bericht(
            "Draait de voorraad-sync nog?",
            SupportAuthor.Customer));

        var cut = RenderPagina(Pagina);

        Assert.Contains("Draait de voorraad-sync nog?", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(SupportText.HumanEscape, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeOperatorZietDeDraadEnEenAntwoordveldEnGeenUitwegNaarEenMens()
    {
        // De spiegel van de test hierboven, en de eis van §3.8: in de operatorrol antwoordt een mens en
        // springt de agent er niet tussen. Er staat dus geen uitweg naar een mens en geen mededeling
        // over de eerstelijn.
        MeldOperatorAan();
        var opslag = Support();

        opslag.ZetSupportbericht(SupportdraadTests.Bericht(
            "Draait de voorraad-sync nog?",
            SupportAuthor.Customer));

        var cut = RenderPagina(Pagina);

        Assert.Contains("Draait de voorraad-sync nog?", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Antwoorden", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(SupportText.HumanEscape, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenAntwoordVanDeEerstelijnDraagtHetMerktekenEnDeBronbijBeideRollen()
    {
        // Twee eisen uit §3.8 in één meting: het merkteken "AI · eerstelijn" én de bron. En ze gelden
        // voor beide rollen, want een operator hoort te kunnen nakijken wat er namens Soratus is
        // gezegd.
        foreach (var operator_ in new[] { false, true })
        {
            using var context = new SupportschermTests();

            if (operator_)
            {
                context.MeldOperatorAan();
            }
            else
            {
                context.MeldKlantAan();
            }

            var opslag = context.Support();

            opslag.ZetSupportbericht(SupportdraadTests.Bericht(
                "In juli 2026 staan 3 u gefiatteerde uren op een bundel van 12 u.",
                SupportAuthor.FirstLine,
                kind: SupportGroundKind.Hours,
                key: "2026-07",
                wie: null));

            var markup = context.RenderPagina(Pagina).Markup;

            Assert.Contains(SupportText.FirstLineBadge, markup, StringComparison.Ordinal);
            Assert.Contains("Uren · juli 2026", markup, StringComparison.Ordinal);
            Assert.Contains("/klant/acme-logistiek/uren?maand=2026-07", markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EenAntwoordVanDeEerstelijnZonderBronKomtNietOpHetKlantschermEnWelBijDeOperator()
    {
        // Dit is de spiegel die elke "de klant ziet dit niet" hoort te hebben. Zonder de tweede helft
        // zou een projectie die álles weglaat ook groen staan.
        var bericht = SupportdraadTests.Bericht(
            "Over juli 2026 staat € 0,00 door te belasten.",
            SupportAuthor.FirstLine,
            kind: null,
            key: null,
            wie: null);

        MeldKlantAan();
        var opslag = Support();
        opslag.ZetSupportbericht(bericht);

        var klantmarkup = RenderPagina(Pagina).Markup;

        Assert.DoesNotContain("0,00", klantmarkup, StringComparison.Ordinal);
        Assert.DoesNotContain(SupportText.FirstLineBadge, klantmarkup, StringComparison.Ordinal);

        using var operatorcontext = new SupportschermTests();
        operatorcontext.MeldOperatorAan();
        var operatoropslag = operatorcontext.Support();
        operatoropslag.ZetSupportbericht(bericht);

        var operatormarkup = operatorcontext.RenderPagina(Pagina).Markup;

        Assert.Contains("Niet in het portaal van de klant", operatormarkup, StringComparison.Ordinal);
        Assert.Contains(bericht.Id, operatormarkup, StringComparison.Ordinal);

        // En de tékst staat ook bij de operator niet in de bubbel: hij is niet te tonen, en de sleutel
        // is genoeg om hem in de opslag te vinden.
        Assert.DoesNotContain("0,00", operatormarkup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenBerichtZonderToeTeWijzenAfzenderKomtNietOpHetKlantscherm()
    {
        var bericht = SupportdraadTests.Bericht(
            "Wij hebben uw factuur gecrediteerd.",
            SupportAuthor.Unknown,
            wie: null);

        MeldKlantAan();
        var opslag = Support();
        opslag.ZetSupportbericht(bericht);

        var klantmarkup = RenderPagina(Pagina).Markup;

        Assert.DoesNotContain("gecrediteerd", klantmarkup, StringComparison.Ordinal);

        using var operatorcontext = new SupportschermTests();
        operatorcontext.MeldOperatorAan();
        operatorcontext.Support().ZetSupportbericht(bericht);

        Assert.Contains(
            "Niet in het portaal van de klant",
            operatorcontext.RenderPagina(Pagina).Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DeEscalatieredenStaatBijDeOperatorEnNietBijDeKlant()
    {
        var bericht = SupportdraadTests.Bericht(
            SupportText.Handoff(),
            SupportAuthor.FirstLine,
            escalatie: SupportEscalation.OutsideTheData,
            wie: null);

        MeldKlantAan();
        var opslag = Support();
        opslag.ZetSupportbericht(bericht);

        var klantmarkup = RenderPagina(Pagina).Markup;

        // De klant leest de zin en het merkteken, en de reactietermijn uit het contract. Geen reden.
        Assert.Contains(SupportText.FirstLineBadge, klantmarkup, StringComparison.Ordinal);
        Assert.Contains("Doorgezet", klantmarkup, StringComparison.Ordinal);

        foreach (var reden in Enum.GetValues<SupportEscalation>())
        {
            Assert.DoesNotContain(
                SupportText.EscalationLabel(reden),
                klantmarkup,
                StringComparison.OrdinalIgnoreCase);
        }

        using var operatorcontext = new SupportschermTests();
        operatorcontext.MeldOperatorAan();
        operatorcontext.Support().ZetSupportbericht(bericht);

        Assert.Contains(
            SupportText.EscalationLabel(SupportEscalation.OutsideTheData),
            operatorcontext.RenderPagina(Pagina).Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DeReactietermijnKomtUitHetContractEnWordtNietVerzonnen()
    {
        // §3.8: escaleren gebeurt naar het team binnen de SLA. Het contract heeft daar één veld voor, en
        // dat veld gaat door — er wordt niets omgerekend.
        MeldKlantAan();
        var opslag = Support();

        opslag.ZetSupportbericht(SupportdraadTests.Bericht(
            SupportText.Handoff(),
            SupportAuthor.FirstLine,
            escalatie: SupportEscalation.NotSure,
            wie: null));

        var markup = RenderPagina(Pagina).Markup;
        var sla = opslag.Contract()?.Sla;

        Assert.False(string.IsNullOrWhiteSpace(sla));
        Assert.Contains(sla!, markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenVuilBerichtUitDeOpslagWordtBijHetWeergevenGeschoond()
    {
        // Dit gat is door de mutatieronde gevonden: het schonen staat op twee plekken -- bij het
        // schrijven en in de projectie -- en de tweede had geen test. Elke test zette een bericht neer
        // dat al schoon was, dus een mutatie die de projectie oversloeg maakte niets rood.
        //
        // Punt 13 van de fase-0-afwijkingen zegt met zoveel woorden dat een knip op twee van de drie
        // plekken geen knip is. Wat deze tweede plek dekt is een document dat langs een ander pad in de
        // container terecht is gekomen; de identiteit van het portaal heeft schrijfrecht op de hele
        // container customers, dus dat is geen theorie.
        MeldKlantAan();
        var opslag = Support();

        opslag.ZetSupportbericht(SupportdraadTests.Bericht(
            "Betaald \u202Etxt.exe\u202C\r\n\r\n\r\n\r\nnu\u200B",
            SupportAuthor.Customer));

        var markup = RenderPagina(Pagina).Markup;

        Assert.DoesNotContain('\u202E', markup);
        Assert.DoesNotContain('\u202C', markup);
        Assert.DoesNotContain('\u200B', markup);
        Assert.Contains("txt.exe", markup, StringComparison.Ordinal);
    }

    // ── Het rolverschil als typeverschil ────────────────────────────────────────────────────────

    [Fact]
    public void HetKlanttypeHeeftGeenEscalatieredenEnHetOperatortypeGeenEerstelijnstoestand()
    {
        // De harde vorm van het rolverschil. Geen filter, geen @if: de velden bestaan niet op het
        // andere type, dus ze kunnen niet in de paginabron belanden.
        var klantvelden = Velden<CustomerSupportView>();
        var operatorvelden = Velden<OperatorSupportView>();

        Assert.DoesNotContain("Handoffs", klantvelden);
        Assert.DoesNotContain("Unusable", klantvelden);
        Assert.Contains("FirstLine", klantvelden);

        Assert.Contains("Handoffs", operatorvelden);
        Assert.Contains("Unusable", operatorvelden);
        Assert.DoesNotContain("FirstLine", operatorvelden);
    }

    [Fact]
    public void DeTweeWeergavecomponentenNemenPreciesEenParameter()
    {
        // Dezelfde reflectietest als bij MonthlyStatementCard, en om dezelfde reden: een component met
        // één parameter van het roltype kan de andere rol niet renderen, ook niet per ongeluk.
        Assert.Equal(["View"], Parameters(typeof(CustomerSupport)));
        Assert.Equal(["View"], Parameters(typeof(OperatorSupport)));
    }

    [Fact]
    public void EenAntwoordbubbelIsZonderBronNietTeMaken()
    {
        // De bronregel is geen afspraak in de markup maar een eigenschap van het type. Deze test is de
        // enige plek waar dat te meten is zonder een document te verzinnen.
        // Op parameteraantal en niet met Single(): een record heeft naast zijn eigen constructor ook
        // de kopieconstructor die "with" gebruikt, en die is bij een sealed record privé. Single()
        // vond er dus twee.
        var bouwer = typeof(SupportAnswerBubble)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length == 4);

        foreach (var (label, pad) in new[] { ("", "/pad"), ("Uren · juli", ""), ("", "") })
        {
            var fout = Assert.Throws<TargetInvocationException>(() =>
                bouwer.Invoke([Testgegevens.Nu, "tekst", label, pad]));

            Assert.IsType<ArgumentException>(fout.InnerException);
        }
    }

    // ── Een GET schrijft niets ──────────────────────────────────────────────────────────────────

    [Fact]
    public void EenGetOpDitSchermSchrijftNiets()
    {
        // §29.9 van de fase-0-afwijkingen: een GET wordt aangeroepen door een prefetch, een
        // linkchecker, een spamfilter dat elke URL in een bericht opent, en een tabblad dat na een
        // herstart zijn adressen opnieuw bezoekt. Op dit scherm zou dat een bericht kunnen plaatsen of
        // de eerstelijn kunnen wekken, en beide zijn niet terug te draaien.
        MeldKlantAan();

        var eerstelijn = new Vasteeerstelijn(_ =>
            SupportAnswer.Escalate(SupportEscalation.NotSure));

        var opslag = Support(eerstelijn);

        opslag.ZetSupportbericht(SupportdraadTests.Bericht("Een vraag", SupportAuthor.Customer));

        var voor = opslag.Supportberichten().Count;

        Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>()
            .NavigateTo($"/klant/acme-logistiek/support?{SupportText.OlderQuery}=supportMessage-x");

        RenderPagina(Pagina);
        RenderPagina(Pagina);

        Assert.Equal(voor, opslag.Supportberichten().Count);
        Assert.Empty(eerstelijn.Verzoeken);
        Assert.Empty(opslag.Verzoeken);
    }

    // ── De meting die main nodig heeft voor zijn twee vastgelegde lijsten ───────────────────────

    [Fact]
    public void DeSupportpaginaRendertVoorEenKlantInhoudEnZetEenTitel()
    {
        // Gemeten en niet beredeneerd: dit is het antwoord op de vraag in welke van de twee lijsten in
        // PaginatitelTests deze pagina hoort. Inhoud én een titel, dus in
        // DePaginasWaarvanEenKlantDeTitelTeZienKrijgtStaanVast.
        MeldKlantAan();
        Support();

        var cut = RenderPagina(Pagina);

        Assert.False(string.IsNullOrWhiteSpace(cut.Markup));
        Assert.NotEmpty(cut.FindComponents<PageTitle>());
    }

    [Fact]
    public void OpDeSlugVanEenVreemdeKlantRendertDeSupportpaginaNietsEnZetHijGeenTitel()
    {
        // De theorie die vier pagina's heeft betrapt. Een supportdraad met de naam van een andere klant
        // in de titelbalk is precies het lek dat die theorie beschrijft: een PageTitle rendert in de
        // HeadOutlet en het vangnet op verboden woorden ziet hem niet.
        MeldKlantAan();
        Support();

        var cut = RenderPagina(Pagina, "bakker-bv");

        Assert.True(string.IsNullOrWhiteSpace(cut.Markup));
        Assert.Empty(cut.FindComponents<PageTitle>());
    }

    [Fact]
    public void DeTitelDieEenKlantKrijgtBevatGeenOperatorwoord()
    {
        MeldKlantAan();
        Support();

        var titels = RenderPagina(Pagina).FindComponents<PageTitle>();
        var titel = string.Join(
            " ",
            titels.Select(t => Render(t.Instance.ChildContent!).Markup.Trim()));

        Assert.Contains("Support", titel, StringComparison.Ordinal);
        Assert.Contains("Acme Logistiek", titel, StringComparison.Ordinal);

        foreach (var woord in Zichtbaarheid.KlantVangnetTests.VerbodenWoorden)
        {
            Assert.DoesNotContain(woord, titel, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string[] Velden<T>() =>
    [
        .. typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal),
    ];

    private static string[] Parameters(Type component) =>
    [
        .. component
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal),
    ];
}
