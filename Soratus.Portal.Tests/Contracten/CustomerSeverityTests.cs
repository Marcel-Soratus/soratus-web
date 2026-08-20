using Soratus.Agents.Contracts;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De sortering van het klantoverzicht: ernst eerst, dan recentheid.
/// </summary>
/// <remarks>
/// De volgorde is failed(4) &gt; degraded(3) &gt; live(2) &gt; idle(1) &gt; geen agents(0). De
/// numerieke waarde van <see cref="AgentStatus"/> <em>is</em> de ernstrang, dus deze tests zetten
/// die getallen ook expliciet vast: een wijziging daarin verandert de schermvolgorde zonder dat er
/// verder ook maar één regel code verandert.
/// </remarks>
public class CustomerSeverityTests
{
    private static readonly DateTimeOffset Nu = Testgegevens.Nu;

    // ── De rangen zelf ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeErnstrangenStaanVastOpNulTotVier()
    {
        Assert.Equal(0, (int)AgentStatus.Unknown);
        Assert.Equal(1, (int)AgentStatus.Idle);
        Assert.Equal(2, (int)AgentStatus.Live);
        Assert.Equal(3, (int)AgentStatus.Degraded);
        Assert.Equal(4, (int)AgentStatus.Failed);
    }

    // ── Samenvatten van agents naar een klantbeeld ──────────────────────────────────────────

    [Fact]
    public void EenKlantZonderAgentsLevertNone()
    {
        var beeld = CustomerSeverity.FromAgents([]);

        Assert.Equal(CustomerSeverity.None, beeld);
        Assert.Equal(AgentStatus.Unknown, beeld.Status);
        Assert.Null(beeld.LastActivityAt);
        Assert.Equal(0, beeld.AgentCount);
    }

    [Fact]
    public void HetKlantbeeldNeemtDeErnstigsteStatusVanZijnAgents()
    {
        var beeld = CustomerSeverity.FromAgents(
        [
            new AgentSeverity(AgentStatus.Live, Nu - TimeSpan.FromMinutes(1)),
            new AgentSeverity(AgentStatus.Failed, Nu - TimeSpan.FromHours(3)),
            new AgentSeverity(AgentStatus.Idle, Nu - TimeSpan.FromMinutes(2)),
        ]);

        Assert.Equal(AgentStatus.Failed, beeld.Status);
        Assert.Equal(3, beeld.AgentCount);
    }

    [Fact]
    public void HetKlantbeeldNeemtDeJongsteActiviteitOokAlHoortDieBijEenAndereAgent()
    {
        var jongste = Nu - TimeSpan.FromMinutes(1);

        var beeld = CustomerSeverity.FromAgents(
        [
            new AgentSeverity(AgentStatus.Failed, Nu - TimeSpan.FromHours(3)),
            new AgentSeverity(AgentStatus.Live, jongste),
            new AgentSeverity(AgentStatus.Live, null),
        ]);

        Assert.Equal(jongste, beeld.LastActivityAt);
    }

    // ── De volgorde op ernst ────────────────────────────────────────────────────────────────

    [Fact]
    public void SorteertFailedBovenDegradedBovenLiveBovenIdleBovenGeenAgents()
    {
        var rijen = new[]
        {
            Rij("geen-agents", AgentStatus.Unknown, null, 0),
            Rij("idle", AgentStatus.Idle, Nu),
            Rij("live", AgentStatus.Live, Nu),
            Rij("degraded", AgentStatus.Degraded, Nu),
            Rij("failed", AgentStatus.Failed, Nu),
        };

        var volgorde = CustomerSeverity.Sort(rijen, r => r.Beeld).Select(r => r.Naam).ToArray();

        Assert.Equal(new[] { "failed", "degraded", "live", "idle", "geen-agents" }, volgorde);
    }

    [Fact]
    public void IdleIsGeenProbleemEnBlijftDaaromOnderLive()
    {
        // De makkelijke fout: "idle" klinkt als "er gebeurt niets, kijk daar eens naar". Het is
        // juist normaal gedrag, dus het mag een werkende klant niet van de eerste plek duwen —
        // ook niet als de idle klant recenter actief was.
        var rijen = new[]
        {
            Rij("idle-en-net-nog-actief", AgentStatus.Idle, Nu),
            Rij("live-maar-langer-geleden", AgentStatus.Live, Nu - TimeSpan.FromHours(4)),
        };

        var volgorde = CustomerSeverity.Sort(rijen, r => r.Beeld).Select(r => r.Naam).ToArray();

        Assert.Equal(new[] { "live-maar-langer-geleden", "idle-en-net-nog-actief" }, volgorde);
    }

    [Fact]
    public void EenKlantZonderAgentsStaatOnderElkeKlantWaarvanWeIetsWeten()
    {
        var rijen = new[]
        {
            Rij("zonder-agents", AgentStatus.Unknown, null, 0),
            Rij("alleen-idle", AgentStatus.Idle, Nu - TimeSpan.FromDays(2)),
        };

        var volgorde = CustomerSeverity.Sort(rijen, r => r.Beeld).Select(r => r.Naam).ToArray();

        Assert.Equal(new[] { "alleen-idle", "zonder-agents" }, volgorde);
    }

    // ── De volgorde binnen dezelfde ernst ───────────────────────────────────────────────────

    [Fact]
    public void BinnenDezelfdeErnstStaatDeMeestRecenteActiviteitBovenaan()
    {
        var rijen = new[]
        {
            Rij("oud", AgentStatus.Failed, Nu - TimeSpan.FromHours(9)),
            Rij("nieuw", AgentStatus.Failed, Nu - TimeSpan.FromMinutes(4)),
            Rij("midden", AgentStatus.Failed, Nu - TimeSpan.FromHours(1)),
        };

        var volgorde = CustomerSeverity.Sort(rijen, r => r.Beeld).Select(r => r.Naam).ToArray();

        Assert.Equal(new[] { "nieuw", "midden", "oud" }, volgorde);
    }

    [Fact]
    public void EenKlantZonderActiviteitStaatAchteraanBinnenZijnEigenStatusgroep()
    {
        var rijen = new[]
        {
            Rij("nooit-iets", AgentStatus.Degraded, null),
            Rij("lang-geleden", AgentStatus.Degraded, Nu - TimeSpan.FromDays(30)),
        };

        var volgorde = CustomerSeverity.Sort(rijen, r => r.Beeld).Select(r => r.Naam).ToArray();

        Assert.Equal(new[] { "lang-geleden", "nooit-iets" }, volgorde);
    }

    // ── Stabiliteit ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeSorteringIsStabielZodatRijenNietVanPlekSpringenBijHetVerversen()
    {
        // Vier klanten die op ernst én op recentheid niet te scheiden zijn. Ze moeten in hun
        // oorspronkelijke volgorde blijven staan, anders wisselt het overzicht elke keer dat je
        // ververst van volgorde en denk je dat er iets is gebeurd.
        var moment = Nu - TimeSpan.FromMinutes(7);
        var rijen = new[]
        {
            Rij("acme", AgentStatus.Live, moment),
            Rij("bakker", AgentStatus.Live, moment),
            Rij("cordaan", AgentStatus.Live, moment),
            Rij("deltavis", AgentStatus.Live, moment),
        };

        var eersteKeer = CustomerSeverity.Sort(rijen, r => r.Beeld).Select(r => r.Naam).ToArray();
        var tweedeKeer = CustomerSeverity.Sort(rijen, r => r.Beeld).Select(r => r.Naam).ToArray();

        Assert.Equal(new[] { "acme", "bakker", "cordaan", "deltavis" }, eersteKeer);
        Assert.Equal(eersteKeer, tweedeKeer);
    }

    [Fact]
    public void StabielBlijftStabielBinnenElkeStatusgroep()
    {
        var moment = Nu - TimeSpan.FromMinutes(7);
        var rijen = new[]
        {
            Rij("live-a", AgentStatus.Live, moment),
            Rij("failed-a", AgentStatus.Failed, moment),
            Rij("live-b", AgentStatus.Live, moment),
            Rij("failed-b", AgentStatus.Failed, moment),
        };

        var volgorde = CustomerSeverity.Sort(rijen, r => r.Beeld).Select(r => r.Naam).ToArray();

        Assert.Equal(new[] { "failed-a", "failed-b", "live-a", "live-b" }, volgorde);
    }

    // ── De comparer zelf ────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeComparerNoemtTweeGelijkeKlantbeeldenGelijk()
    {
        var comparer = CustomerSeverity.SeverityFirst;
        var beeld = new CustomerSeverity(AgentStatus.Live, Nu, 3);

        Assert.Equal(0, comparer.Compare(beeld, beeld));
    }

    [Fact]
    public void DeComparerNoemtTweeKlantenZonderActiviteitGelijk()
    {
        var comparer = CustomerSeverity.SeverityFirst;

        Assert.Equal(0, comparer.Compare(CustomerSeverity.None, CustomerSeverity.None));
    }

    [Fact]
    public void DeComparerZetErnstigerNegatiefEnIsDaarmeeOmgekeerdAanDeGetallen()
    {
        // Een hogere AgentStatus hoort eerder in de lijst, dus de comparer keert de natuurlijke
        // ordening om. Daarom is dit een aparte comparer en geen IComparable op het type zelf.
        var comparer = CustomerSeverity.SeverityFirst;
        var failed = new CustomerSeverity(AgentStatus.Failed, Nu, 1);
        var live = new CustomerSeverity(AgentStatus.Live, Nu, 1);

        Assert.True(comparer.Compare(failed, live) < 0);
        Assert.True(comparer.Compare(live, failed) > 0);
    }

    [Fact]
    public void SorterenWerptOpEenNullLijstOfEenNullKiezer()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CustomerSeverity.Sort<Klantrij>(null!, r => r.Beeld));
        Assert.Throws<ArgumentNullException>(() =>
            CustomerSeverity.Sort<Klantrij>([], null!));
        Assert.Throws<ArgumentNullException>(() => CustomerSeverity.FromAgents(null!));
    }

    private static Klantrij Rij(
        string naam,
        AgentStatus status,
        DateTimeOffset? laatsteActiviteit,
        int aantalAgents = 2) =>
        new(naam, new CustomerSeverity(status, laatsteActiviteit, aantalAgents));

    /// <summary>Een rij van het overzicht, gereduceerd tot wat de sortering nodig heeft.</summary>
    private sealed record Klantrij(string Naam, CustomerSeverity Beeld);
}
