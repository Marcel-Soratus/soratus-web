using Bunit;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// Per pagina, op selectors: het operator-only onderdeel is er wél voor een operator en níet voor
/// een klant.
/// </summary>
/// <remarks>
/// <para>Beide kanten, en dat is de hele reden dat deze tests er zijn. Een test die alleen
/// afwezigheid controleert blijft groen als de pagina helemaal stukgaat — dan is er niets, dus ook
/// niet het verboden element. Elke test hieronder bewijst daarom eerst dat het onderdeel voor een
/// operator bestáát, en pas daarna dat het voor een klant weg is.</para>
///
/// <para><strong>Dit is geen beveiliging.</strong> Zie <see cref="Portaalrendertest"/>: de echte
/// grens ligt in de datalaag — een klantgebruiker krijgt geen operatorscope, en zonder dat
/// argument is de aanroep niet te schrijven — en bij de autorisatie op de endpoints. Wat hier
/// wordt getest is het vangnet daaronder.</para>
/// </remarks>
public class OperatorZichtbaarheidTests : Portaalrendertest
{
    private static Type Overzicht =>
        Paginaverzameling.MetNaam("Soratus.Portal.Components.Pages.Overzicht")
        ?? throw new InvalidOperationException(
            "De pagina Soratus.Portal.Components.Pages.Overzicht bestaat niet. Is hij hernoemd, " +
            "dan hoort deze test mee te verhuizen — niet te verdwijnen.");

    private static Type Agents =>
        Paginaverzameling.MetRoute("/klant/{Slug}/agents")
        ?? throw new InvalidOperationException(
            "Er staat geen pagina op route '/klant/{Slug}/agents'. Is de route hernoemd, dan " +
            "hoort deze test mee te verhuizen — niet te verdwijnen.");

    // ── Het klantoverzicht (§3.1): operator-only in zijn geheel ─────────────────────────────

    [Fact]
    public void EenOperatorZietDeKpiRijVanHetKlantoverzicht()
    {
        MeldOperatorAan();

        var cut = RenderPagina(Overzicht);

        Assert.NotNull(cut.Find(".kpi-grid"));
        Assert.Contains("Soratus-overzicht", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietHetKlantoverzichtHelemaalNiet()
    {
        // Niet "de gevoelige blokken vallen weg" maar "de pagina blijft leeg". Het beleid staat op
        // de pagina, en de scope die de gegevens ophaalt komt er voor een klant niet.
        MeldKlantAan();

        var cut = RenderPagina(Overzicht);

        Assert.Empty(cut.FindAll(".kpi-grid"));
        Assert.DoesNotContain("Soratus-overzicht", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietGeenAndereKlantnaamOpHetOverzicht()
    {
        // De scherpste vorm van de vorige test: het gaat niet om een kop maar om de gegevens van
        // een andere klant.
        MeldKlantAan();

        var cut = RenderPagina(Overzicht);

        Assert.DoesNotContain("Bakker B.V.", cut.Markup, StringComparison.Ordinal);
    }

    // ── De agentlijst van één klant (§3.2): dezelfde pagina, twee weergaven ─────────────────

    [Fact]
    public void EenOperatorZietDeOmgevingskolomOpDeAgentlijst()
    {
        MeldOperatorAan();

        var cut = RenderPagina(Agents);

        Assert.Contains(">Omgeving<", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("alle omgevingen", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietGeenOmgevingskolomOpDeAgentlijst()
    {
        // Een acceptatie-agent die omvalt is geen storing voor de klant. Het klantviewmodel heeft
        // het veld daarom niet — er valt niets te verbergen, want er is niets.
        MeldKlantAan();

        var cut = RenderPagina(Agents);

        Assert.DoesNotContain(">Omgeving<", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("alle omgevingen", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOperatorZietWelkeAgentsOpAcceptatieDraaien()
    {
        MeldOperatorAan();

        var cut = RenderPagina(Agents);

        Assert.Contains("acceptatie", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietNergensDatErEenAcceptatieOfOntwikkelomgevingIs()
    {
        // Niet alleen de kolom, ook het bestaan van die omgevingen is operatorinformatie.
        MeldKlantAan();

        var cut = RenderPagina(Agents);

        Assert.DoesNotContain("acceptatie", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ontwikkeling", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("proefopstelling", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietDeContractversieVanDeTelemetriebibliotheekNiet()
    {
        // Dat een agent op een oude contractvorm staat is een uitrolzaak van Soratus. Het staat
        // niet op het klantviewmodel, dus het kan hier ook niet per ongeluk verschijnen.
        MeldKlantAan();

        var cut = RenderPagina(Agents);

        Assert.DoesNotContain("contract v", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ContractVersion", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOperatorZietDeVolledigeOmgevingsaanduiding()
    {
        MeldOperatorAan();

        var cut = RenderPagina(Agents);

        Assert.Contains("rg-acme-prod", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietDeSubscriptionEnDeResourceGroupNiet()
    {
        MeldKlantAan();

        var cut = RenderPagina(Agents);

        Assert.DoesNotContain("rg-acme-prod", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("sub-soratus", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOperatorZietDeTerugkoppelingNaarHetKlantoverzicht()
    {
        MeldOperatorAan();

        var cut = RenderPagina(Agents);

        Assert.NotNull(cut.Find(".toolbar"));
        Assert.Contains("Alle klanten", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietGeenTerugkoppelingNaarHetKlantoverzicht()
    {
        // Een link naar een pagina waar je niet mag komen is een dode link, en hij verklapt dat er
        // een overzicht van andere klanten bestaat.
        MeldKlantAan();

        var cut = RenderPagina(Agents);

        Assert.Empty(cut.FindAll(".toolbar"));
        Assert.DoesNotContain("Alle klanten", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeKlantweergaveVanDeAgentlijstIsErWelDegelijk()
    {
        // Zonder deze test zijn alle "de klant ziet het niet"-tests hierboven te bevredigen door
        // de pagina stuk te maken.
        MeldKlantAan();

        var cut = RenderPagina(Agents);

        Assert.Contains("Acme Logistiek", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("factuur-intake", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find(".page-head"));
    }

    [Fact]
    public void EenKlantKomtNietBijDeAgentlijstVanEenAndereKlant()
    {
        // De datalaag, niet de markup: de resolver geeft geen scope, dus er valt niets op te
        // bouwen en de pagina hoort 404 te geven in plaats van 403.
        MeldKlantAan();

        var cut = Render(builder =>
        {
#pragma warning disable ASP0006
            builder.OpenComponent(0, Agents);
            builder.AddComponentParameter(1, "Slug", "bakker-bv");
#pragma warning restore ASP0006
            builder.CloseComponent();
        });

        Assert.DoesNotContain("Bakker", cut.Markup, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void EenOperatorKomtWelBijDeAgentlijstVanElkeKlant()
    {
        MeldOperatorAan();

        var cut = Render(builder =>
        {
#pragma warning disable ASP0006
            builder.OpenComponent(0, Agents);
            builder.AddComponentParameter(1, "Slug", "bakker-bv");
#pragma warning restore ASP0006
            builder.CloseComponent();
        });

        Assert.Contains("Bakker B.V.", cut.Markup, StringComparison.Ordinal);
    }
}
