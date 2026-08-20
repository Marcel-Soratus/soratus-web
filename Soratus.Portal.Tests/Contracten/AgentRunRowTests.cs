using Soratus.Agents.Contracts;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// Wat <c>AgentRunRow.From</c> van een rundocument maakt, en vooral: wat het van een lopende run
/// níet maakt.
/// </summary>
/// <remarks>
/// <para><c>RunRecord.ItemsProcessed</c> en <c>ItemsFailed</c> zijn gewone <c>int</c>-velden. Een
/// document van een run die nog bezig is heeft ze dus op 0 staan, en 0 is een getal — het scherm
/// zou "0 items verwerkt" tonen voor een run die net begon en er misschien duizend gaat doen. Dat
/// is geen ontbrekende informatie maar een onwaarheid, en het verschil tussen die twee is precies
/// wat deze omzetting moet maken.</para>
///
/// <para>Daarom rekent <c>From</c> ze om naar <c>null</c> zolang de run loopt, en daarom staat er
/// in <c>RunsTable</c> een streepje waar niets is. Deze test legt die grens vast; zonder hem is een
/// weggehaalde <c>running ? null :</c> een wijziging die niemand ziet tot iemand naar een lopende
/// run kijkt.</para>
/// </remarks>
public class AgentRunRowTests
{
    /// <summary>Een lopende run met items en een duur al gevuld in het document.</summary>
    /// <remarks>
    /// De aantallen staan met opzet op iets anders dan 0. Zou de test ze op 0 laten staan, dan is
    /// niet te zien of <c>null</c> uit de omzetting komt of uit het document.
    /// </remarks>
    private static RunRecord LopendeRun(long? duurMs = null) => new()
    {
        Id = "r-loopt",
        PartitionKey = RunRecord.BuildPartitionKey("factuur-intake", Testgegevens.Nu),
        CustomerId = "acme-logistiek",
        AgentName = "factuur-intake",
        StartedAt = Testgegevens.Nu - TimeSpan.FromSeconds(40),
        FinishedAt = null,
        DurationMs = duurMs,
        Result = RunResult.Running,
        ItemsProcessed = 137,
        ItemsFailed = 4,
        Trigger = TriggerKind.Timer,
        Version = "1.4.2",
    };

    [Fact]
    public void EenLopendeRunHeeftGeenResultaatEnGeenAantallen()
    {
        var rij = Rij(LopendeRun());

        Assert.True(rij.IsRunning);
        Assert.Null(rij.Outcome);
        Assert.Null(rij.ItemsProcessed);
        Assert.Null(rij.ItemsFailed);
    }

    [Fact]
    public void DeAantallenVanEenLopendeRunKomenAlsNullDoorEnNietAlsNul()
    {
        // Het onderscheid waar het om gaat: 0 is een meting, null is de afwezigheid ervan. Zou hier
        // 0 staan, dan beweert het scherm dat er niets is verwerkt.
        var rij = Rij(LopendeRun());

        Assert.NotEqual(0, rij.ItemsProcessed);
        Assert.NotEqual(0, rij.ItemsFailed);
        Assert.Null(rij.ItemsProcessed);
        Assert.Null(rij.ItemsFailed);
    }

    [Fact]
    public void EenLopendeRunZonderDuurInHetDocumentHeeftGeenDuur()
    {
        var rij = Rij(LopendeRun());

        Assert.Null(rij.Duration);
        Assert.Null(rij.FinishedAt);
    }

    [Fact]
    public void EenLopendeRunHoudtGeenDuurOokAlStaatErEenInHetDocument()
    {
        // Deze test faalde toen hij werd geschreven: From zette Outcome en de aantallen op null
        // zolang de run loopt, maar liet Duration ongefilterd uit RunRecord.DurationMs komen. De
        // toelichting boven RunsTable.razor zei het anders, en het scherm gedroeg zich daarnaar —
        // de tooltip "de run is nog bezig" boven een kolom die een eindduur toonde. Een agent die
        // durationMs alvast meeschrijft op een run die nog loopt leverde daarmee een eindduur op
        // iets wat geen einde heeft. Nu stelt From dezelfde running-vraag bij alle vier de velden.
        var rij = Rij(LopendeRun(duurMs: 40_000));

        Assert.True(rij.IsRunning);
        Assert.Null(rij.Duration);
    }

    [Theory]
    [InlineData(RunResult.Ok)]
    [InlineData(RunResult.Failed)]
    [InlineData(RunResult.Skipped)]
    public void EenAfgeslotenRunHoudtZijnAfloopEnZijnAantallen(RunResult afloop)
    {
        var run = Testgegevens.Run(afloop, Testgegevens.Nu) with
        {
            ItemsProcessed = 31,
            ItemsFailed = 2,
        };

        var rij = Rij(run);

        Assert.False(rij.IsRunning);
        Assert.Equal(afloop, rij.Outcome);
        Assert.Equal(31, rij.ItemsProcessed);
        Assert.Equal(2, rij.ItemsFailed);
        Assert.Equal(TimeSpan.FromSeconds(12), rij.Duration);
        Assert.Equal(Testgegevens.Nu, rij.FinishedAt);
    }

    [Fact]
    public void DeOverigeVeldenKomenOngewijzigdMee()
    {
        // Alles wat niet over de afloop gaat hoort de omzetting niet aan te raken. Een foutmelding
        // die verdwijnt op een mislukte run is erger dan geen rij.
        var run = Testgegevens.Run(RunResult.Failed, Testgegevens.Nu) with { RolledBack = true };

        var rij = Rij(run);

        Assert.Equal(run.Id, rij.RunId);
        Assert.Equal(run.StartedAt, rij.StartedAt);
        Assert.Equal(run.Version, rij.Version);
        Assert.Equal(run.Trigger, rij.Trigger);
        Assert.Equal("System.TimeoutException", rij.ErrorType);
        Assert.Equal(run.ErrorMessage, rij.ErrorMessage);
        Assert.True(rij.RolledBack);
    }

    [Fact]
    public void EenRunZonderDocumentIsGeenRij()
    {
        Assert.Throws<ArgumentNullException>(() => Rij(null!));
    }

    /// <summary>
    /// Roept <c>AgentRunRow.From</c> aan. De methode is <c>internal</c>; het testproject ziet hem
    /// via de <c>InternalsVisibleTo</c> in <c>Soratus.Portal.csproj</c>.
    /// </summary>
    private static AgentRunRow Rij(RunRecord run) => AgentRunRow.From(run);
}
