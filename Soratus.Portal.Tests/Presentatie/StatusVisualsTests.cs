using Soratus.Agents.Contracts;
using Soratus.Portal.Components.Shared;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// Elke status levert een glyph, een woordlabel en een classnaam op — en geen enkele levert iets
/// leegs.
/// </summary>
/// <remarks>
/// Deze tests lopen over <c>Enum.GetValues</c> en niet over een handmatige lijst. Wie er morgen een
/// status bij zet en vergeet hem in <see cref="StatusVisuals"/> op te nemen, krijgt hier meteen
/// rood in plaats van een leeg vakje op het scherm.
/// </remarks>
public class StatusVisualsTests
{
    public static TheoryData<AgentStatus> AlleStatussen
    {
        get
        {
            var data = new TheoryData<AgentStatus>();
            foreach (var status in Enum.GetValues<AgentStatus>())
            {
                data.Add(status);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AlleStatussen))]
    public void ElkeStatusHeeftEenGlyphDieNietLeegIs(AgentStatus status)
    {
        var glyph = StatusVisuals.Glyph(status);

        Assert.False(string.IsNullOrWhiteSpace(glyph));
    }

    [Theory]
    [MemberData(nameof(AlleStatussen))]
    public void ElkeStatusHeeftEenWoordlabelDatNietLeegIs(AgentStatus status)
    {
        var label = StatusVisuals.Label(status);

        Assert.False(string.IsNullOrWhiteSpace(label));
    }

    [Theory]
    [MemberData(nameof(AlleStatussen))]
    public void ElkeStatusHeeftEenBadgeclassEnEenStipclassDieNietLeegZijn(AgentStatus status)
    {
        Assert.StartsWith("badge", StatusVisuals.BadgeClass(status), StringComparison.Ordinal);
        Assert.StartsWith("status-dot", StatusVisuals.DotClass(status), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(StatusVisuals.DotColorVar(status)));
    }

    [Theory]
    [MemberData(nameof(AlleStatussen))]
    public void GeenEnkeleStatusLevertDezelfdeGlyphAlsEenAndere(AgentStatus status)
    {
        // Een glyph die twee statussen deelt maakt de grijstintentest waardeloos: dan draagt de
        // kleur het verschil alsnog alleen.
        var anderen = Enum.GetValues<AgentStatus>()
            .Where(s => s != status)
            .Select(StatusVisuals.Glyph);

        Assert.DoesNotContain(StatusVisuals.Glyph(status), anderen);
    }

    [Fact]
    public void DeGlyphsKomenLetterlijkUitDeSpec()
    {
        Assert.Equal("●", StatusVisuals.Glyph(AgentStatus.Live));
        Assert.Equal("◐", StatusVisuals.Glyph(AgentStatus.Degraded));
        Assert.Equal("✕", StatusVisuals.Glyph(AgentStatus.Failed));
        Assert.Equal("○", StatusVisuals.Glyph(AgentStatus.Idle));
        Assert.Equal("–", StatusVisuals.Glyph(AgentStatus.Unknown));
    }

    [Fact]
    public void UnknownKrijgtOpEenAgentrijEenAnderLabelDanOpEenKlantrij()
    {
        // "Geen agents" op een agentrij zou een onwaarheid zijn: die agent bestaat, hij meldt zich
        // alleen niet.
        Assert.Equal("Geen telemetrie", StatusVisuals.Label(AgentStatus.Unknown));
        Assert.Equal(
            "Geen agents",
            StatusVisuals.Label(AgentStatus.Unknown, StatusVisuals.UnknownCustomerLabel));
    }

    [Fact]
    public void EenLeegMeegegevenLabelValtTerugOpDeAgentvariant()
    {
        Assert.Equal(StatusVisuals.UnknownAgentLabel, StatusVisuals.Label(AgentStatus.Unknown, "   "));
    }

    [Theory]
    [InlineData(AgentStatus.Live)]
    [InlineData(AgentStatus.Degraded)]
    [InlineData(AgentStatus.Failed)]
    [InlineData(AgentStatus.Idle)]
    public void EenAlternatiefUnknownLabelRaaktDeAndereStatussenNiet(AgentStatus status)
    {
        Assert.Equal(
            StatusVisuals.Label(status),
            StatusVisuals.Label(status, StatusVisuals.UnknownCustomerLabel));
    }

    [Fact]
    public void AlleenEenMislukkingKleurtDeHeleRij()
    {
        // Een amber of groene rij zou de tabel tot een kleurenveld maken en de storing juist
        // minder zichtbaar.
        Assert.Equal("data-row--failed", StatusVisuals.RowClass(AgentStatus.Failed));

        foreach (var status in Enum.GetValues<AgentStatus>().Where(s => s != AgentStatus.Failed))
        {
            Assert.Null(StatusVisuals.RowClass(status));
        }
    }

    [Fact]
    public void DeLegendaBevatElkeStatusPreciesEenKeer()
    {
        Assert.Equal(Enum.GetValues<AgentStatus>().Length, StatusVisuals.All.Count);
        Assert.Equal(StatusVisuals.All.Count, StatusVisuals.All.Distinct().Count());

        foreach (var status in Enum.GetValues<AgentStatus>())
        {
            Assert.Contains(status, StatusVisuals.All);
        }
    }

    [Fact]
    public void AlleenUnknownHeeftGeenModifierEnDaarmeeDeNeutraleBasisvorm()
    {
        Assert.Null(StatusVisuals.Modifier(AgentStatus.Unknown));
        Assert.Equal("badge", StatusVisuals.BadgeClass(AgentStatus.Unknown));
        Assert.Equal("status-dot", StatusVisuals.DotClass(AgentStatus.Unknown));

        foreach (var status in Enum.GetValues<AgentStatus>().Where(s => s != AgentStatus.Unknown))
        {
            Assert.False(string.IsNullOrWhiteSpace(StatusVisuals.Modifier(status)));
        }
    }
}
