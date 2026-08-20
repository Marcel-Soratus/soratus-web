using Soratus.Agents.Contracts;
using Soratus.Portal.Components.Shared;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// Elke runafloop levert een glyph, een woordlabel en een classnaam op — en een lopende run levert
/// een neutrale badge in plaats van een streepje.
/// </summary>
/// <remarks>
/// <para>De tests lopen over <c>Enum.GetValues</c> plus <c>null</c> en niet over een handmatige
/// lijst. Wie er morgen een <see cref="RunResult"/> bij zet en vergeet hem in
/// <see cref="RunResultVisuals"/> op te nemen, krijgt hier rood in plaats van een leeg vakje in de
/// resultaatkolom.</para>
///
/// <para><c>null</c> hoort in die lijst thuis: <c>AgentRunRow.Outcome</c> is <c>null</c> zolang de
/// run loopt, en dat is de meest voorkomende bijzondere waarde die deze afbeelding te verwerken
/// krijgt.</para>
/// </remarks>
public class RunResultVisualsTests
{
    /// <summary>Elke afloop, plus <c>null</c> voor een lopende run.</summary>
    public static TheoryData<RunResult?> AlleAflopen
    {
        get
        {
            var data = new TheoryData<RunResult?>
            {
                (RunResult?)null,
            };

            foreach (var result in Enum.GetValues<RunResult>())
            {
                data.Add(result);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AlleAflopen))]
    public void ElkeAfloopHeeftEenGlyphDieNietLeegIs(RunResult? afloop)
    {
        Assert.False(string.IsNullOrWhiteSpace(RunResultVisuals.Glyph(afloop)));
    }

    [Theory]
    [MemberData(nameof(AlleAflopen))]
    public void ElkeAfloopHeeftEenWoordlabelDatNietLeegIs(RunResult? afloop)
    {
        // Het woordlabel is de drager, niet de glyph. Een lezer die de tekens niet ziet — of de
        // kleuren niet onderscheidt — houdt hier het antwoord over.
        Assert.False(string.IsNullOrWhiteSpace(RunResultVisuals.Label(afloop)));
    }

    [Theory]
    [MemberData(nameof(AlleAflopen))]
    public void ElkeAfloopHeeftEenBadgeclassDieMetBadgeBegint(RunResult? afloop)
    {
        Assert.StartsWith("badge", RunResultVisuals.BadgeClass(afloop), StringComparison.Ordinal);
    }

    [Fact]
    public void GeenTweeAfgerondeAflopenDelenEenGlyphOfEenLabel()
    {
        // Deelden twee aflopen hun glyph, dan droeg de kleur het verschil alleen — en dan is de
        // resultaatkolom in grijstinten niet meer te lezen.
        var afgerond = Enum.GetValues<RunResult>()
            .Where(r => r != RunResult.Running)
            .ToArray();

        var glyphs = afgerond.Select(r => RunResultVisuals.Glyph(r)).ToArray();
        var labels = afgerond.Select(r => RunResultVisuals.Label(r)).ToArray();

        Assert.Equal(afgerond.Length, glyphs.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(afgerond.Length, labels.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EenLopendeRunKrijgtEenNeutraleBadgeEnGeenStreepje()
    {
        // De opdracht vroeg in de resultaatkolom een streepje voor een lopende run. RunsTable wijkt
        // daar bewust van af: een streepje is niet te onderscheiden van ontbrekende data, terwijl
        // "Loopt" een waar antwoord is op de vraag die de kolom stelt. Die afwijking hoort een test
        // te hebben, anders is het over een half jaar een vergissing in plaats van een besluit.
        Assert.Equal("Loopt", RunResultVisuals.Label(null));
        Assert.Equal("badge", RunResultVisuals.BadgeClass(null));
        Assert.DoesNotContain("badge--", RunResultVisuals.BadgeClass(null), StringComparison.Ordinal);

        Assert.NotEqual("—", RunResultVisuals.Glyph(null));
        Assert.NotEqual("-", RunResultVisuals.Glyph(null));
        Assert.NotEqual("–", RunResultVisuals.Glyph(null));
    }

    [Fact]
    public void NullEnRunningGevenPreciesHetzelfdeBeeld()
    {
        // De runlijst zet Outcome op null voor een lopende run; het contract kent daarnaast de
        // waarde running in het document zelf. Wie de een op de ander laat lijken bouwt twee
        // schermen die hetzelfde ding anders zeggen.
        Assert.Equal(RunResultVisuals.Glyph(RunResult.Running), RunResultVisuals.Glyph(null));
        Assert.Equal(RunResultVisuals.Label(RunResult.Running), RunResultVisuals.Label(null));
        Assert.Equal(RunResultVisuals.BadgeClass(RunResult.Running), RunResultVisuals.BadgeClass(null));
        Assert.Equal(RunResultVisuals.Tint(RunResult.Running), RunResultVisuals.Tint(null));
    }

    [Fact]
    public void DeGlyphVanEenLopendeRunIsGeenStatusglyph()
    {
        // ▸ staat in geen van beide bronnen en is bewust geen van de vier statusglyphs: een lopende
        // run mag niet op een afgeronde lijken, en al niet op een agentstatus.
        var statusglyphs = Enum.GetValues<AgentStatus>().Select(StatusVisuals.Glyph).ToArray();

        Assert.DoesNotContain(RunResultVisuals.Glyph(null), statusglyphs);
    }

    [Fact]
    public void AlleenEenMislukkingKleurtDeRij()
    {
        Assert.Equal(AgentStatus.Failed, RunResultVisuals.Tint(RunResult.Failed));

        Assert.Null(RunResultVisuals.Tint(null));

        foreach (var afloop in Enum.GetValues<RunResult>().Where(r => r != RunResult.Failed))
        {
            Assert.Null(RunResultVisuals.Tint(afloop));
        }
    }

    [Fact]
    public void DeAflopenLenenDeVlakkenVanDeStatussenEnVerzinnenGeenTweedeGroen()
    {
        // Dezelfde kleur betekent in dit portaal overal hetzelfde. Een eigen groen voor een
        // geslaagde run zou daar het eerste gat in slaan.
        Assert.Equal(StatusVisuals.BadgeClass(AgentStatus.Live), RunResultVisuals.BadgeClass(RunResult.Ok));
        Assert.Equal(StatusVisuals.BadgeClass(AgentStatus.Failed), RunResultVisuals.BadgeClass(RunResult.Failed));
        Assert.Equal(StatusVisuals.BadgeClass(AgentStatus.Idle), RunResultVisuals.BadgeClass(RunResult.Skipped));
    }
}
