using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Support;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Tests.Zichtbaarheid;

namespace Soratus.Portal.Tests.Support;

/// <summary>
/// Wat het supportscherm werkelijk wegschrijft (§3.8).
/// </summary>
/// <remarks>
/// <para><strong>Wat hier wél wordt gemeten en wat niet.</strong> Dezelfde grens als bij
/// <c>UrenschrijfactieTests</c>: bUnit doet geen echt HTTP-verzoek, dus de modelbinding van static SSR
/// komt hier niet langs. Wat er wel langskomt is alles daarna — het indienen, de veldcontrole, de
/// aanroep naar de balie of de opslag, wat er in de draad belandt en waar de pagina daarna naartoe
/// stuurt.</para>
///
/// <para>Dat de <c>POST</c> een <c>POST</c> met een antiforgery-token is, volgt hier uit de vorm
/// (<c>FormCard</c> met een <c>FormName</c>, dezelfde vorm als de drie formulieren op het urenscherm)
/// en niet uit een meting. Dat staat als niet-gemeten in het rapport; het is dezelfde beperking die
/// §29.9 bij de maandoverzichtkaart benoemt.</para>
/// </remarks>
public class SupportschrijfactieTests : Portaalrendertest
{
    private static Type Pagina =>
        Paginaverzameling.MetRoute("/klant/{Slug}/support")
        ?? throw new InvalidOperationException(
            "Er staat geen pagina op route '/klant/{Slug}/support'.");

    private Vasteeerstelijn? _eerstelijn;

    private void Support(Func<SupportEnquiry, SupportAnswer?>? antwoord = null)
    {
        var lijst = Autorisatiebron.Standaard();

        _eerstelijn = antwoord is null ? null : new Vasteeerstelijn(antwoord);

        Services.AddSingleton(VasteSupportweergaven.Weergaven(Opslag, lijst));
        Services.AddSingleton<ISupportStore>(Opslag);
        Services.AddSingleton(VasteSupportweergaven.Balie(Opslag, _eerstelijn, lijst));
    }

    [Fact]
    public void EenLeegVraagformulierLegtNietsVastEnMeldtDat()
    {
        MeldKlantAan();
        Support();

        var cut = RenderPagina(Pagina);

        Verstuur(cut, "Stel je vraag");

        Assert.Empty(Opslag.Supportberichten());
        Assert.Contains("Typ een bericht", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenIngevuldeVraagKomtInDeDraadEnStuurtTerugNaarDeNieuwsteBerichten()
    {
        MeldKlantAan();
        Support();

        var cut = RenderPagina(Pagina);

        cut.FindComponent<Soratus.Portal.Components.Pages.Klant.Support>().Instance.Vraag =
            new SupportQuestionForm { Text = "Draait de voorraad-sync nog?" };

        Verstuur(cut, "Stel je vraag");

        var bericht = Assert.Single(Opslag.Supportberichten());

        Assert.Equal(SupportAuthor.Customer, bericht.Author);
        Assert.Equal("Draait de voorraad-sync nog?", bericht.Text);

        // POST → redirect → GET, naar de draad zonder ?voor=. Zonder die redirect stuurt een
        // verversing hetzelfde formulier opnieuw, en dat levert een tweede bericht op.
        Assert.Equal($"/klant/{EigenKlant}/support", Doorstuurdoel());
    }

    [Fact]
    public void DeUitwegNaarEenMensLevertEenKlantberichtOpEnGeenEnkeleAiBubbel()
    {
        // §3.8: "Toch een mens van Soratus spreken". Dit is de meting die het pad vastlegt: de uitweg
        // loopt langs de opslag en niet langs de balie, dus de eerstelijn krijgt hem nooit te zien.
        // Zou hij langs de balie lopen, dan krijgt een klant die om een mens vraagt een agent die hem
        // uitlegt dat hij een agent is.
        MeldKlantAan();
        Support(_ => SupportAnswer.Escalate(SupportEscalation.NotSure));

        var cut = RenderPagina(Pagina);

        // Op id en niet op name: bUnit rendert een EditForm als <form blazor:onsubmit="1"> en niet als
        // <form method="post"> met een naam. Dat is de renderer van bUnit en niet die van static SSR;
        // zie de opmerking bovenaan deze klasse over wat hier niet wordt gemeten.
        cut.Find("form#support-mens").Submit();

        var bericht = Assert.Single(Opslag.Supportberichten());

        Assert.Equal(SupportAuthor.Customer, bericht.Author);
        Assert.Equal(SupportText.HumanRequest, bericht.Text);

        Assert.Empty(_eerstelijn!.Verzoeken);
        Assert.Empty(Opslag.Verzoeken);
        Assert.DoesNotContain(
            Opslag.Supportberichten(),
            m => m.Author == SupportAuthor.FirstLine);
    }

    [Fact]
    public void EenVraagVanDeKlantWordtWelAanDeEerstelijnVoorgelegd()
    {
        // De onmisbare tegenhanger van de test hierboven. Zonder deze zou "de uitweg raakt de
        // eerstelijn niet" ook groen staan als de eerstelijn nóóit iets te zien krijgt.
        MeldKlantAan();
        Support(_ => SupportAnswer.Escalate(SupportEscalation.NotSure));

        var cut = RenderPagina(Pagina);

        cut.FindComponent<Soratus.Portal.Components.Pages.Klant.Support>().Instance.Vraag =
            new SupportQuestionForm { Text = "Hoeveel uren heb ik nog?" };

        Verstuur(cut, "Stel je vraag");

        Assert.Single(_eerstelijn!.Verzoeken);
        Assert.Contains(
            Opslag.Supportberichten(),
            m => m.Author == SupportAuthor.FirstLine && m.Escalation == SupportEscalation.NotSure);
    }

    [Fact]
    public void EenAntwoordVanDeOperatorKomtInDeDraadOpZijnEigenNaam()
    {
        MeldOperatorAan();
        Support();

        var cut = RenderPagina(Pagina);

        cut.FindComponent<Soratus.Portal.Components.Pages.Klant.Support>().Instance.Antwoord =
            new SupportReplyForm { Text = "De sync liep vast op een locatiecode.\n\nWe pakken het op." };

        Verstuur(cut, "Antwoorden");

        var bericht = Assert.Single(Opslag.Supportberichten());

        Assert.Equal(SupportAuthor.Soratus, bericht.Author);
        Assert.False(string.IsNullOrWhiteSpace(bericht.Who));

        // De tweede alinea staat er nog. Dat is de afwijking van punt 13 die deze map met opzet maakt.
        Assert.Contains("We pakken het op.", bericht.Text, StringComparison.Ordinal);

        // En de operator wekt de eerstelijn niet: er is geen pad van een operatorbericht naar de balie.
        Assert.Empty(Opslag.Verzoeken);
        Assert.DoesNotContain(
            Opslag.Supportberichten(),
            m => m.Author == SupportAuthor.FirstLine);
    }

    /// <summary>Verstuurt het formulier van de kaart met deze kop.</summary>
    private static void Verstuur(
        IRenderedComponent<Bunit.Rendering.ContainerFragment> cut,
        string kop) =>
        cut.Find($"section[aria-label='{kop}'] form").Submit();
}
