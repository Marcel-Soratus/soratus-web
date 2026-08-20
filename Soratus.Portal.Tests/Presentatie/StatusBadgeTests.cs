using Bunit;
using Soratus.Agents.Contracts;
using Soratus.Portal.Components.Shared;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// Status is nooit alleen kleur.
/// </summary>
/// <remarks>
/// Elke badge draagt drie dingen: een glyph, een woordlabel en een kleurvlak. Zet het scherm in
/// grijstinten en de informatie moet nog compleet zijn. Deze tests renderen de badge echt en
/// kijken naar de markup, want de belofte gaat over wat er op het scherm staat en niet over wat
/// <see cref="StatusVisuals"/> teruggeeft.
///
/// De glyph is <c>aria-hidden</c>: een schermlezer zou bij ✕ "vermenigvuldigingsteken" voorlezen,
/// en dat is verwarrender dan behulpzaam. Het woordlabel is echte tekst en geen <c>aria-label</c>,
/// zodat het meetelt in de toegankelijke naam van de rij.
/// </remarks>
public class StatusBadgeTests : BunitContext
{
    [Theory]
    [InlineData(AgentStatus.Live)]
    [InlineData(AgentStatus.Degraded)]
    [InlineData(AgentStatus.Failed)]
    [InlineData(AgentStatus.Idle)]
    [InlineData(AgentStatus.Unknown)]
    public void DeBadgeToontAltijdHetWoordlabelEnDeGlyph(AgentStatus status)
    {
        var cut = Render<StatusBadge>(p => p.Add(b => b.Status, status));

        var glyph = cut.Find(".badge__glyph");

        Assert.Equal(StatusVisuals.Glyph(status), glyph.TextContent);
        Assert.Contains(StatusVisuals.Label(status), cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AgentStatus.Live)]
    [InlineData(AgentStatus.Degraded)]
    [InlineData(AgentStatus.Failed)]
    [InlineData(AgentStatus.Idle)]
    [InlineData(AgentStatus.Unknown)]
    public void DeGlyphIsAriaHiddenZodatEenSchermlezerHemNietVoorleest(AgentStatus status)
    {
        var cut = Render<StatusBadge>(p => p.Add(b => b.Status, status));

        var glyph = cut.Find(".badge__glyph");

        Assert.Equal("true", glyph.GetAttribute("aria-hidden"));
    }

    [Theory]
    [InlineData(AgentStatus.Live)]
    [InlineData(AgentStatus.Degraded)]
    [InlineData(AgentStatus.Failed)]
    [InlineData(AgentStatus.Idle)]
    [InlineData(AgentStatus.Unknown)]
    public void DeBadgeIsNooitAlleenKleur(AgentStatus status)
    {
        // Haal alle classnamen weg — dus alle kleur — en er moet nog steeds leesbare tekst
        // overblijven die de status noemt.
        var cut = Render<StatusBadge>(p => p.Add(b => b.Status, status));

        var tekst = cut.Find("span.badge").TextContent.Trim();

        Assert.False(string.IsNullOrWhiteSpace(tekst));
        Assert.Contains(StatusVisuals.Label(status), tekst, StringComparison.Ordinal);
        Assert.Contains(StatusVisuals.Glyph(status), tekst, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AgentStatus.Live, "badge badge--live")]
    [InlineData(AgentStatus.Degraded, "badge badge--degraded")]
    [InlineData(AgentStatus.Failed, "badge badge--failed")]
    [InlineData(AgentStatus.Idle, "badge badge--idle")]
    [InlineData(AgentStatus.Unknown, "badge")]
    public void DeBadgeKrijgtDeClassnaamUitStatusVisuals(AgentStatus status, string verwacht)
    {
        var cut = Render<StatusBadge>(p => p.Add(b => b.Status, status));

        Assert.Equal(verwacht, cut.Find("span").GetAttribute("class"));
    }

    [Fact]
    public void OpEenKlantrijStaatErGeenAgentsInPlaatsVanGeenTelemetrie()
    {
        var cut = Render<StatusBadge>(p => p
            .Add(b => b.Status, AgentStatus.Unknown)
            .Add(b => b.UnknownLabel, StatusVisuals.UnknownCustomerLabel));

        Assert.Contains("Geen agents", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Geen telemetrie", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeStandaardVoorUnknownIsDeAgentvariant()
    {
        var cut = Render<StatusBadge>(p => p.Add(b => b.Status, AgentStatus.Unknown));

        Assert.Contains("Geen telemetrie", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DeKleineVariantHoudtLabelEnGlyph()
    {
        var cut = Render<StatusBadge>(p => p
            .Add(b => b.Status, AgentStatus.Degraded)
            .Add(b => b.Small, true));

        Assert.Contains("badge--sm", cut.Find("span").GetAttribute("class")!, StringComparison.Ordinal);
        Assert.Contains("Degraded", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("◐", cut.Find(".badge__glyph").TextContent);
    }

    [Fact]
    public void DeStipIsWelKleurAlleenEnIsDaaromVoorEenSchermlezerOnzichtbaar()
    {
        // De stip mag alleen naast tekst staan die de status óók noemt. Deze test legt vast dat
        // hij zichzelf niet als informatiedrager aanbiedt; wie hem zonder tekst gebruikt, laat een
        // schermlezer met niets achter.
        var cut = Render<StatusDot>(p => p.Add(d => d.Status, AgentStatus.Failed));

        var stip = cut.Find("span");

        Assert.Equal("true", stip.GetAttribute("aria-hidden"));
        Assert.Equal(string.Empty, stip.TextContent);
    }
}
