using Soratus.Agents.Contracts;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De belangrijkste regel van het systeem: een agent publiceert zijn status nooit zelf, die wordt
/// afgeleid uit gepubliceerde feiten.
/// </summary>
/// <remarks>
/// Elke test geeft het moment als parameter mee. Er staat hier nergens
/// <c>DateTimeOffset.UtcNow</c>: een drempel van twee minuten is anders niet te testen zonder twee
/// minuten te wachten, en een test die de echte klok leest gaat ooit 's nachts rood.
/// </remarks>
public class AgentStatusCalculatorTests
{
    private static readonly DateTimeOffset Nu = Testgegevens.Nu;

    // ── 1. Zonder registratie weten we niets ────────────────────────────────────────────────

    [Fact]
    public void StatusIsUnknownZonderRegistratiedocument()
    {
        var status = AgentStatusCalculator.Calculate(null, null, Nu);

        Assert.Equal(AgentStatus.Unknown, status);
    }

    [Fact]
    public void StatusIsUnknownZonderRegistratiedocumentOokAlIsErEenMislukteRun()
    {
        // Een run zonder registratie hoort niet te bestaan, maar als hij er is telt hij niet:
        // "wij weten niets van deze agent" is dan de eerlijkste mededeling.
        var run = Testgegevens.Run(RunResult.Failed, Nu);

        Assert.Equal(AgentStatus.Unknown, AgentStatusCalculator.Calculate(null, run, Nu));
    }

    // ── 2. Falen wint ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void StatusIsFailedNaEenMislukteRunOokAlIsDeHartslagVers()
    {
        var registratie = Testgegevens.Registratie(Nu - TimeSpan.FromSeconds(5));
        var run = Testgegevens.Run(RunResult.Failed, Nu - TimeSpan.FromMinutes(3));

        Assert.Equal(AgentStatus.Failed, AgentStatusCalculator.Calculate(registratie, run, Nu));
    }

    [Fact]
    public void FailedWintVanDegradedAlsDeHartslagOokStokt()
    {
        // Ernst 4 boven 3: er is aantoonbaar iets misgegaan, en dat is een hardere mededeling dan
        // "hij meldt zich niet".
        var registratie = Testgegevens.Registratie(Nu - TimeSpan.FromHours(2));
        var run = Testgegevens.Run(RunResult.Failed, Nu - TimeSpan.FromHours(2));

        var status = AgentStatusCalculator.Calculate(registratie, run, Nu);

        Assert.Equal(AgentStatus.Failed, status);
        Assert.True((int)AgentStatus.Failed > (int)AgentStatus.Degraded);
    }

    [Theory]
    [InlineData(AgentLifecycle.IdleWaiting)]
    [InlineData(AgentLifecycle.StoppedCleanly)]
    public void EenAgentKanZichNietMetZijnLevenscyclusUitEenMislukteRunPraten(AgentLifecycle levenscyclus)
    {
        // "Ik wacht even" is een feit dat de agent zelf publiceert. Het mag een afgeronde
        // mislukking niet overstemmen, anders is de zachtste zelfgemelde waarheid de winnaar.
        var registratie = Testgegevens.Registratie(Nu - TimeSpan.FromSeconds(1), levenscyclus);
        var run = Testgegevens.Run(RunResult.Failed, Nu - TimeSpan.FromSeconds(30));

        Assert.Equal(AgentStatus.Failed, AgentStatusCalculator.Calculate(registratie, run, Nu));
    }

    // ── 3. De grens van twee minuten ────────────────────────────────────────────────────────

    [Fact]
    public void StatusIsLiveExactOpDeGrensVanTweeMinuten()
    {
        // Precies op de drempel telt de agent nog als vers. De vergelijking is <= en niet <;
        // dit legt die keuze vast zodat een refactor hem niet stilletjes omdraait.
        var registratie = Testgegevens.Registratie(Nu - TimeSpan.FromMinutes(2));

        Assert.Equal(AgentStatus.Live, AgentStatusCalculator.Calculate(registratie, null, Nu));
    }

    [Fact]
    public void StatusIsDegradedZodraDeHartslagOuderIsDanTweeMinuten()
    {
        var registratie = Testgegevens.Registratie(Nu - TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(1));

        Assert.Equal(AgentStatus.Degraded, AgentStatusCalculator.Calculate(registratie, null, Nu));
    }

    [Fact]
    public void EenWachtendeAgentBlijftIdleExactOpDeGrensVanTweeMinuten()
    {
        var registratie = Testgegevens.Registratie(
            Nu - TimeSpan.FromMinutes(2),
            AgentLifecycle.IdleWaiting);

        Assert.Equal(AgentStatus.Idle, AgentStatusCalculator.Calculate(registratie, null, Nu));
    }

    [Fact]
    public void EenWachtendeAgentWordtDegradedEenSecondeNaDeGrens()
    {
        // Bewust: een agent die zichzelf idle noemt en daarna zwijgt blijft niet eeuwig groen.
        // Zijn eigen laatste mededeling houdt hem niet langer overeind dan de drempel.
        var registratie = Testgegevens.Registratie(
            Nu - TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(1),
            AgentLifecycle.IdleWaiting);

        Assert.Equal(AgentStatus.Degraded, AgentStatusCalculator.Calculate(registratie, null, Nu));
    }

    [Fact]
    public void EenNetjesGestopteAgentWordtDegradedEenSecondeNaDeGrens()
    {
        var registratie = Testgegevens.Registratie(
            Nu - TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(1),
            AgentLifecycle.StoppedCleanly);

        Assert.Equal(AgentStatus.Degraded, AgentStatusCalculator.Calculate(registratie, null, Nu));
    }

    [Fact]
    public void DeDrempelsStaanOpTweeEnTienMinuten()
    {
        // Zodra iemand aan deze getallen draait, verandert de betekenis van elk scherm en van de
        // storingsmelder tegelijk. Dan hoort er een test rood te worden.
        Assert.Equal(TimeSpan.FromMinutes(2), AgentStatusThresholds.Degraded);
        Assert.Equal(TimeSpan.FromMinutes(10), AgentStatusThresholds.Alert);
        Assert.True(AgentStatusThresholds.HeartbeatInterval < AgentStatusThresholds.Degraded);
    }

    // ── 4. Skipped en running zijn geen fout ────────────────────────────────────────────────

    [Theory]
    [InlineData(RunResult.Ok)]
    [InlineData(RunResult.Skipped)]
    [InlineData(RunResult.Running)]
    public void EenRunDieNietIsMisluktLevertGeenFailedOp(RunResult afloop)
    {
        var registratie = Testgegevens.Registratie(Nu - TimeSpan.FromSeconds(20));
        var run = Testgegevens.Run(afloop, Nu - TimeSpan.FromMinutes(1));

        Assert.Equal(AgentStatus.Live, AgentStatusCalculator.Calculate(registratie, run, Nu));
    }

    [Fact]
    public void EenOvergeslagenRunIsGeenFoutOokNietBijEenStokkendeHartslag()
    {
        // "Niets te doen gehad" is geen mislukking. De agent zakt hier naar degraded om de
        // stilte, niet om de run.
        var registratie = Testgegevens.Registratie(Nu - TimeSpan.FromMinutes(5));
        var run = Testgegevens.Run(RunResult.Skipped, Nu - TimeSpan.FromMinutes(5));

        Assert.Equal(AgentStatus.Degraded, AgentStatusCalculator.Calculate(registratie, run, Nu));
    }

    // ── 5. Klokverschil ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenKlokDieVooruitloopOpDeAgentLevertGeenNegatieveStilteOp()
    {
        // De hartslag ligt een minuut in de toekomst — dat gebeurt met twee machines die net niet
        // gelijk lopen. Dat mag geen negatieve stilte opleveren en zeker geen rare status.
        var registratie = Testgegevens.Registratie(Nu + TimeSpan.FromMinutes(1));

        var stilte = AgentStatusCalculator.SilenceFor(registratie, Nu);

        Assert.Equal(TimeSpan.Zero, stilte);
        Assert.Equal(AgentStatus.Live, AgentStatusCalculator.Calculate(registratie, null, Nu));
        Assert.True(AgentStatusCalculator.IsHeartbeatFresh(registratie, Nu));
    }

    [Fact]
    public void StilteIsNooitNegatiefHoeVerDeKlokOokVooruitloopt()
    {
        var registratie = Testgegevens.Registratie(Nu + TimeSpan.FromDays(3));

        Assert.Equal(TimeSpan.Zero, AgentStatusCalculator.SilenceFor(registratie, Nu));
    }

    // ── 6. Stilte en versheid ───────────────────────────────────────────────────────────────

    [Fact]
    public void StilteIsOnbekendZonderRegistratiedocument()
    {
        // Null en niet TimeSpan.Zero: er is nooit iets geweest om stil van te vallen.
        Assert.Null(AgentStatusCalculator.SilenceFor(null, Nu));
    }

    [Fact]
    public void StilteIsHetVerschilTussenNuEnDeLaatsteHartslag()
    {
        var registratie = Testgegevens.Registratie(Nu - TimeSpan.FromMinutes(37));

        Assert.Equal(TimeSpan.FromMinutes(37), AgentStatusCalculator.SilenceFor(registratie, Nu));
    }

    [Fact]
    public void EenAgentZonderRegistratiedocumentHeeftGeenVerseHartslag()
    {
        Assert.False(AgentStatusCalculator.IsHeartbeatFresh(null, Nu));
    }

    [Theory]
    [InlineData(119, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void VersheidGebruiktDezelfdeGrensAlsDegraded(int stilteInSeconden, bool verwachtVers)
    {
        var registratie = Testgegevens.Registratie(Nu - TimeSpan.FromSeconds(stilteInSeconden));

        Assert.Equal(verwachtVers, AgentStatusCalculator.IsHeartbeatFresh(registratie, Nu));
    }

    // ── 7. De storingsmelder ────────────────────────────────────────────────────────────────

    [Fact]
    public void EenMislukteRunWordtMeteenGemeld()
    {
        var registratie = Testgegevens.Registratie(Nu - TimeSpan.FromSeconds(5));
        var run = Testgegevens.Run(RunResult.Failed, Nu - TimeSpan.FromSeconds(10));

        Assert.True(AgentStatusCalculator.ShouldAlert(registratie, run, Nu));
    }

    [Fact]
    public void EenStokkendeHartslagWordtBinnenTienMinutenNietGemeld()
    {
        // Wel degraded op het scherm, nog geen mail. Een gemiste hartslag tijdens een uitrol is
        // geen storing, en een melder die dáárover mailt wordt binnen een week weggefilterd.
        var registratie = Testgegevens.Registratie(Nu - TimeSpan.FromMinutes(5));

        Assert.Equal(AgentStatus.Degraded, AgentStatusCalculator.Calculate(registratie, null, Nu));
        Assert.False(AgentStatusCalculator.ShouldAlert(registratie, null, Nu));
    }

    [Fact]
    public void MeldenGebeurtNietExactOpDeGrensVanTienMinuten()
    {
        var registratie = Testgegevens.Registratie(Nu - TimeSpan.FromMinutes(10));

        Assert.False(AgentStatusCalculator.ShouldAlert(registratie, null, Nu));
    }

    [Fact]
    public void MeldenGebeurtEenSecondeNaDeGrensVanTienMinuten()
    {
        var registratie = Testgegevens.Registratie(
            Nu - TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));

        Assert.True(AgentStatusCalculator.ShouldAlert(registratie, null, Nu));
    }

    [Fact]
    public void ErWordtNietGemeldOverEenAgentZonderTelemetrie()
    {
        // Status Unknown is een uitrolvraag, geen storing. Zonder deze regel mailt de melder over
        // elke agent die nog niet bestaat.
        Assert.Equal(AgentStatus.Unknown, AgentStatusCalculator.Calculate(null, null, Nu));
        Assert.False(AgentStatusCalculator.ShouldAlert(null, null, Nu));
    }

    [Fact]
    public void ErWordtNietGemeldOverEenAgentZonderTelemetrieMaarMetEenOudeMislukteRun()
    {
        var run = Testgegevens.Run(RunResult.Failed, Nu - TimeSpan.FromHours(1));

        Assert.False(AgentStatusCalculator.ShouldAlert(null, run, Nu));
    }

    [Theory]
    [InlineData(AgentLifecycle.Running)]
    [InlineData(AgentLifecycle.IdleWaiting)]
    public void ErWordtNietGemeldOverEenGezondeAgent(AgentLifecycle levenscyclus)
    {
        var registratie = Testgegevens.Registratie(Nu - TimeSpan.FromSeconds(20), levenscyclus);
        var run = Testgegevens.Run(RunResult.Ok, Nu - TimeSpan.FromMinutes(1));

        Assert.False(AgentStatusCalculator.ShouldAlert(registratie, run, Nu));
    }

    [Fact]
    public void SchermEnStoringsmelderGebruikenDezelfdeDrempel()
    {
        // De melddrempel is ruimer dan de degraded-drempel. Zou hij gelijk of kleiner zijn, dan
        // mailt de melder over iets dat het scherm nog niet toont.
        Assert.True(AgentStatusThresholds.Alert > AgentStatusThresholds.Degraded);
    }
}
