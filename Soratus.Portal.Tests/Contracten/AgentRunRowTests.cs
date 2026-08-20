using Soratus.Agents.Contracts;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// Wat de twee runprojecties van een rundocument maken: wat ze van een lopende run níet maken, en
/// waar ze bewust van elkaar verschillen.
/// </summary>
/// <remarks>
/// <para><c>RunRecord.ItemsProcessed</c> en <c>ItemsFailed</c> zijn gewone <c>int</c>-velden. Een
/// document van een run die nog bezig is heeft ze dus op 0 staan, en 0 is een getal — het scherm
/// zou "0 items verwerkt" tonen voor een run die net begon en er misschien duizend gaat doen. Dat
/// is geen ontbrekende informatie maar een onwaarheid, en het verschil tussen die twee is precies
/// wat deze omzetting moet maken.</para>
///
/// <para>Daarom rekenen de projecties ze om naar <c>null</c> zolang de run loopt, en daarom staat er
/// in <c>RunsTable</c> een streepje waar niets is. Deze test legt die grens vast; zonder hem is een
/// weggehaalde <c>running ? null :</c> een wijziging die niemand ziet tot iemand naar een lopende
/// run kijkt.</para>
///
/// <para><strong>Alles wat over "loopt nog" gaat wordt tweemaal getest, één keer per rol.</strong>
/// Er zijn twee projecties sinds <c>errorType</c> operator-only is, en die beslissing over lopende
/// runs hoort in beide hetzelfde te vallen. Ze delen daarvoor één methode
/// (<c>AgentRunRow.Settled</c>); deze theories bewijzen dat dat zo blijft. Zou de ene projectie ooit
/// zijn eigen antwoord gaan geven, dan staat er op het klantscherm iets anders dan op het
/// operatorscherm over dezelfde run — de tegenspraak die het portaal nergens mag hebben.</para>
/// </remarks>
public class AgentRunRowTests
{
    /// <summary>De twee rollen, als naam voor een theory.</summary>
    public const string Klant = "klant";

    /// <summary>De operatorrol.</summary>
    public const string Operator = "operator";

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

    [Theory]
    [InlineData(Klant)]
    [InlineData(Operator)]
    public void EenLopendeRunHeeftGeenResultaatEnGeenAantallen(string rol)
    {
        var rij = Rij(LopendeRun(), rol);

        Assert.True(rij.IsRunning);
        Assert.Null(rij.Outcome);
        Assert.Null(rij.ItemsProcessed);
        Assert.Null(rij.ItemsFailed);
    }

    [Theory]
    [InlineData(Klant)]
    [InlineData(Operator)]
    public void DeAantallenVanEenLopendeRunKomenAlsNullDoorEnNietAlsNul(string rol)
    {
        // Het onderscheid waar het om gaat: 0 is een meting, null is de afwezigheid ervan. Zou hier
        // 0 staan, dan beweert het scherm dat er niets is verwerkt.
        var rij = Rij(LopendeRun(), rol);

        Assert.NotEqual(0, rij.ItemsProcessed);
        Assert.NotEqual(0, rij.ItemsFailed);
        Assert.Null(rij.ItemsProcessed);
        Assert.Null(rij.ItemsFailed);
    }

    [Theory]
    [InlineData(Klant)]
    [InlineData(Operator)]
    public void EenLopendeRunZonderDuurInHetDocumentHeeftGeenDuur(string rol)
    {
        var rij = Rij(LopendeRun(), rol);

        Assert.Null(rij.Duration);
        Assert.Null(rij.FinishedAt);
    }

    [Theory]
    [InlineData(Klant)]
    [InlineData(Operator)]
    public void EenLopendeRunHoudtGeenDuurOokAlStaatErEenInHetDocument(string rol)
    {
        // Deze test faalde toen hij werd geschreven: From zette Outcome en de aantallen op null
        // zolang de run loopt, maar liet Duration ongefilterd uit RunRecord.DurationMs komen. De
        // toelichting boven RunsTable.razor zei het anders, en het scherm gedroeg zich daarnaar —
        // de tooltip "de run is nog bezig" boven een kolom die een eindduur toonde. Een agent die
        // durationMs alvast meeschrijft op een run die nog loopt leverde daarmee een eindduur op
        // iets wat geen einde heeft. Nu stelt Settled dezelfde running-vraag bij alle vier de
        // velden, voor beide rollen.
        var rij = Rij(LopendeRun(duurMs: 40_000), rol);

        Assert.True(rij.IsRunning);
        Assert.Null(rij.Duration);
    }

    [Theory]
    [InlineData(RunResult.Ok, Klant)]
    [InlineData(RunResult.Failed, Klant)]
    [InlineData(RunResult.Skipped, Klant)]
    [InlineData(RunResult.Ok, Operator)]
    [InlineData(RunResult.Failed, Operator)]
    [InlineData(RunResult.Skipped, Operator)]
    public void EenAfgeslotenRunHoudtZijnAfloopEnZijnAantallen(RunResult afloop, string rol)
    {
        var run = Testgegevens.Run(afloop, Testgegevens.Nu) with
        {
            ItemsProcessed = 31,
            ItemsFailed = 2,
        };

        var rij = Rij(run, rol);

        Assert.False(rij.IsRunning);
        Assert.Equal(afloop, rij.Outcome);
        Assert.Equal(31, rij.ItemsProcessed);
        Assert.Equal(2, rij.ItemsFailed);
        Assert.Equal(TimeSpan.FromSeconds(12), rij.Duration);
        Assert.Equal(Testgegevens.Nu, rij.FinishedAt);
    }

    [Theory]
    [InlineData(Klant)]
    [InlineData(Operator)]
    public void DeOverigeVeldenKomenOngewijzigdMee(string rol)
    {
        // Alles wat niet over de afloop en niet over de rolscheiding gaat hoort de omzetting niet
        // aan te raken. Een foutmelding die verdwijnt op een mislukte run is erger dan geen rij.
        var run = Testgegevens.Run(RunResult.Failed, Testgegevens.Nu) with { RolledBack = true };

        var rij = Rij(run, rol);

        Assert.Equal(run.Id, rij.RunId);
        Assert.Equal(run.StartedAt, rij.StartedAt);
        Assert.Equal(run.Version, rij.Version);
        Assert.Equal(run.Trigger, rij.Trigger);
        Assert.Equal(run.ErrorMessage, rij.ErrorMessage);
        Assert.True(rij.RolledBack);
    }

    [Theory]
    [InlineData(Klant)]
    [InlineData(Operator)]
    public void EenRunZonderDocumentIsGeenRij(string rol)
    {
        Assert.Throws<ArgumentNullException>(() => Rij(null!, rol));
    }

    [Fact]
    public void DeKlantrijDraagtDeTypenaamVanDeUitzonderingNietEnDeOperatorrijWel()
    {
        // De kern van het besluit, op het niveau van de projectie. Beide kanten in één test, want ze
        // zijn samen de bewering: het veld staat op precies één van de twee. Zonder de tweede helft
        // zou "de klant ziet het niet" ook waar zijn als niemand het meer ziet, en dan is de
        // diagnose weg terwijl de test tevreden is.
        var run = Testruns.Mislukt(
            "r-8f3c",
            Testgegevens.Nu,
            Testruns.Typenaam,
            Testruns.Foutmelding);

        var operatorrij = OperatorRunRow.From(run);

        Assert.Equal(Testruns.Typenaam, operatorrij.ErrorType);
        Assert.Contains(Testruns.Typenaam, operatorrij.FailureDetail!, StringComparison.Ordinal);
        Assert.Contains(Testruns.Foutmelding, operatorrij.FailureDetail!, StringComparison.Ordinal);

        var klantrij = CustomerRunRow.From(run);

        Assert.Equal(Testruns.Foutmelding, klantrij.FailureDetail);
        Assert.DoesNotContain("SoratusAgent", klantrij.FailureDetail!, StringComparison.Ordinal);

        // En niet ingekort tot de korte typenaam, want dat is de reparatie die zich aanbiedt en
        // niets oplost: voor een klant is "ValidationException" even betekenisloos als de hele
        // naam, en voor de operator is het het verlies van het onderscheid tussen Sync en Mail.
        Assert.DoesNotContain(
            "ValidationException",
            klantrij.FailureDetail!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EenMislukteRunZonderFoutmeldingLeverdeDeTypenaamOpEnDoetDatNietMeer()
    {
        // Dit was het gat waar het lek werkelijk door liep. De tooltip nam de typenaam als
        // terugvaloptie zodra de foutmelding leeg was, dus in de gewone weergave zag de operator hem
        // nooit en de klant precies dán wel — de verkeerde kant op, bij beide rollen.
        var run = Testruns.Mislukt("r-leeg", Testgegevens.Nu, Testruns.Typenaam, string.Empty);

        Assert.Null(CustomerRunRow.From(run).FailureDetail);
        Assert.Equal(Testruns.Typenaam, OperatorRunRow.From(run).FailureDetail);
    }

    [Fact]
    public void DeKlantprojectieKniptEenMeerregeligeFoutmeldingTerugTotDeEersteRegel()
    {
        // errorMessage is klantzichtbaar en de bibliotheek knipt hem sinds kort bij het
        // wegschrijven. Runs blijven 400 dagen staan, dus elk document dat er vandaag is heeft die
        // knip nooit gezien — en de foutmelding staat op het klantscherm in de tooltip van de
        // resultaatbadge. Daarom knipt de projectie ook, net als bij een logregel.
        var run = Testruns.Mislukt(
            "r-7c04",
            Testgegevens.Nu,
            Testruns.TweedeTypenaam,
            Testruns.MeerregeligeFoutmelding);

        var klantrij = CustomerRunRow.From(run);

        Assert.Equal(Testruns.EersteRegel + MessageTruncation.Marker, klantrij.ErrorMessage);
        Assert.DoesNotContain("/src/", klantrij.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain("at SoratusAgent", klantrij.ErrorMessage!, StringComparison.Ordinal);

        // De operator houdt de hele tekst. Dat is dezelfde keuze als bij het bericht van een
        // logregel: hij hoort te lezen wat er werkelijk in het document staat, ook als dat een halve
        // pagina diagnostiek is. Zonder deze helft zou de knip "voor de zekerheid" ook op het
        // operatorpad kunnen belanden, en dan is de stacktrace weg in plaats van verplaatst.
        Assert.Equal(Testruns.MeerregeligeFoutmelding, OperatorRunRow.From(run).ErrorMessage);
    }

    /// <summary>
    /// Roept de projectie van de gevraagde rol aan. Beide methodes zijn <c>internal</c>; het
    /// testproject ziet ze via de <c>InternalsVisibleTo</c> in <c>Soratus.Portal.csproj</c>.
    /// </summary>
    private static AgentRunRow Rij(RunRecord run, string rol) => rol switch
    {
        Klant => CustomerRunRow.From(run),
        Operator => OperatorRunRow.From(run),
        _ => throw new ArgumentOutOfRangeException(nameof(rol), rol, "Onbekende rol."),
    };
}
