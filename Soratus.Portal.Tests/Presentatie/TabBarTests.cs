using Bunit;
using Soratus.Portal.Components.Shared;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// De tabbalk zet <c>aria-current="page"</c> op precies één tab.
/// </summary>
/// <remarks>
/// <para>Precies één, en dat is aan twee kanten scherp. Nul betekent dat een schermlezer nergens
/// hoort waar hij is; twee betekent dat hij het op twee plekken hoort en dus nergens. Het is
/// bovendien het enige signaal dat de actieve tab in tekst draagt: de 2px onderlijn uit §8 is
/// kleur en vorm, en die leest niemand voor.</para>
///
/// <para>De tabs zijn links en geen knoppen, en er staat geen <c>role="tab"</c> op. Dat besluit
/// staat toegelicht in <c>TabBar.razor</c>; hier wordt het vastgelegd, want een latere overstap
/// naar <c>role="tab"</c> belooft pijltjesnavigatie die er niet is.</para>
/// </remarks>
public class TabBarTests : BunitContext
{
    private static readonly TabItem[] Tabs =
    [
        new("logs", "Logs"),
        new("runs", "Runs"),
        new("configuratie", "Configuratie"),
    ];

    [Fact]
    public void PreciesEenTabDraagtAriaCurrent()
    {
        var cut = Render<TabBar>(p => p
            .Add(c => c.Tabs, Tabs)
            .Add(c => c.ActiveId, "runs"));

        var huidig = cut.FindAll("a[aria-current]");

        Assert.Single(huidig);
        Assert.Equal("page", huidig[0].GetAttribute("aria-current"));
        Assert.Equal("Runs", huidig[0].TextContent);
    }

    [Theory]
    [InlineData("logs", "Logs")]
    [InlineData("runs", "Runs")]
    [InlineData("configuratie", "Configuratie")]
    public void ElkeTabKanDeHuidigeZijnEnDanIsHijDeEnige(string id, string label)
    {
        var cut = Render<TabBar>(p => p
            .Add(c => c.Tabs, Tabs)
            .Add(c => c.ActiveId, id));

        var huidig = Assert.Single(cut.FindAll("a[aria-current]"));

        Assert.Equal(label, huidig.TextContent);
        Assert.Equal(3, cut.FindAll("a.tab").Count);
    }

    [Fact]
    public void DeVergelijkingIsHoofdletterongevoeligWantHijKomtUitDeQuerystring()
    {
        // ?tab=Runs is dezelfde vraag als ?tab=runs. Een mens kan die URL hebben getypt.
        var cut = Render<TabBar>(p => p
            .Add(c => c.Tabs, Tabs)
            .Add(c => c.ActiveId, "RUNS"));

        var huidig = Assert.Single(cut.FindAll("a[aria-current]"));

        Assert.Equal("Runs", huidig.TextContent);
    }

    [Fact]
    public void EenOnbekendeOfAfwezigeTabLaatDeBalkZonderHuidigeStaan()
    {
        // De balk verzint niet dat de eerste tab dan wel de bedoeling was: welk tabblad er bij een
        // onbekende waarde open gaat is een besluit van de pagina.
        foreach (var actief in new string?[] { null, string.Empty, "bestaatniet" })
        {
            var cut = Render<TabBar>(p => p
                .Add(c => c.Tabs, Tabs)
                .Add(c => c.ActiveId, actief));

            Assert.Empty(cut.FindAll("a[aria-current]"));
            Assert.Equal(3, cut.FindAll("a.tab").Count);
        }
    }

    [Fact]
    public void DeActieveTabDraagtNaastAriaCurrentOokDeClassActive()
    {
        // Twee dragers voor hetzelfde feit, met opzet: de class is de onderlijn voor wie ziet, het
        // attribuut is het antwoord voor wie hoort. Raken ze los van elkaar, dan zegt het scherm
        // iets anders dan de schermlezer.
        var cut = Render<TabBar>(p => p
            .Add(c => c.Tabs, Tabs)
            .Add(c => c.ActiveId, "configuratie"));

        var actief = Assert.Single(cut.FindAll("a.tab.active"));

        Assert.Equal("page", actief.GetAttribute("aria-current"));
    }

    [Fact]
    public void DeTabsZijnLinksEnGeenKnoppenEnBelovenGeenTabpatroon()
    {
        var cut = Render<TabBar>(p => p
            .Add(c => c.Tabs, Tabs)
            .Add(c => c.ActiveId, "logs"));

        Assert.Empty(cut.FindAll("button"));
        Assert.Empty(cut.FindAll("[role='tab']"));
        Assert.Empty(cut.FindAll("[role='tablist']"));

        var balk = cut.Find("nav.tabs");

        Assert.False(string.IsNullOrWhiteSpace(balk.GetAttribute("aria-label")));
        Assert.Equal(3, cut.FindAll("a[href]").Count);
    }

    [Fact]
    public void EenTabZonderEigenHrefWijstNaarDeQueryparameter()
    {
        // De kale ?tab=… vervangt de hele querystring. Dat is de standaard en dat mag, zolang de
        // pagina niets anders in de query heeft staan; heeft ze dat wel, dan geeft ze een eigen
        // Href mee. Beide vormen horen te werken.
        var cut = Render<TabBar>(p => p
            .Add(c => c.Tabs, [new TabItem("runs", "Runs"), new TabItem("logs", "Logs", "/klant/acme/agents/x?tab=logs&q=fout")])
            .Add(c => c.ActiveId, "runs"));

        var links = cut.FindAll("a.tab");

        Assert.Equal("?tab=runs", links[0].GetAttribute("href"));
        Assert.Equal("/klant/acme/agents/x?tab=logs&q=fout", links[1].GetAttribute("href"));
    }
}
