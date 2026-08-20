using Soratus.Portal.Components.Shared;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// Opslag is UTC, weergave is Europe/Amsterdam.
/// </summary>
/// <remarks>
/// Dat zijn twee verschillende dingen, en het verschil is precies waar dit soort code stukgaat.
/// Een operator die om 17:13 naar het scherm kijkt en "15:13" leest, denkt dat er iets twee uur
/// geleden gebeurde. De tabel hieronder legt beide zomertijdsprongen van 2026 vast, aan beide
/// kanten van de grens.
///
/// <see cref="TimeFormat.Iso"/> blijft UTC, óók bij invoer met een andere offset: dat attribuut is
/// machineleesbaar en mag niet meebewegen met wat het scherm toevallig toont.
/// </remarks>
public class TimeFormatTests
{
    // ── De absolute tijd in de tooltip ──────────────────────────────────────────────────────

    [Theory]
    // Zomertijd: UTC+02:00.
    [InlineData("2026-08-19T09:22:31Z", "19-08-2026 11:22:31 (UTC+02:00)")]
    // Wintertijd: UTC+01:00.
    [InlineData("2026-01-15T09:22:31Z", "15-01-2026 10:22:31 (UTC+01:00)")]
    // De minuut vóór het vooruitzetten, laatste zondag van maart.
    [InlineData("2026-03-29T00:59:00Z", "29-03-2026 01:59:00 (UTC+01:00)")]
    // Eén minuut later springt de klok van 02:00 naar 03:00.
    [InlineData("2026-03-29T01:00:00Z", "29-03-2026 03:00:00 (UTC+02:00)")]
    // De minuut vóór het terugzetten, laatste zondag van oktober.
    [InlineData("2026-10-25T00:59:00Z", "25-10-2026 02:59:00 (UTC+02:00)")]
    // Eén minuut later staat de klok weer op 02:00 — hetzelfde uur, andere offset.
    [InlineData("2026-10-25T01:00:00Z", "25-10-2026 02:00:00 (UTC+01:00)")]
    public void AbsoluteTijdWordtGetoondInNederlandseTijdMetDeOffsetErbij(string utc, string verwacht)
    {
        var moment = DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(verwacht, TimeFormat.Absolute(moment));
    }

    [Fact]
    public void HetDubbeleUurInDeNachtVanDeTerugstellingIsAlleenAanDeOffsetTeZien()
    {
        // Twee verschillende momenten, allebei "02:30" op de Nederlandse klok. Zonder de offset
        // erbij zou de tooltip twee keer hetzelfde zeggen over twee verschillende momenten.
        var eersteKeer = DateTimeOffset.Parse(
            "2026-10-25T00:30:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var tweedeKeer = DateTimeOffset.Parse(
            "2026-10-25T01:30:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("25-10-2026 02:30:00 (UTC+02:00)", TimeFormat.Absolute(eersteKeer));
        Assert.Equal("25-10-2026 02:30:00 (UTC+01:00)", TimeFormat.Absolute(tweedeKeer));
    }

    [Fact]
    public void EenAndereZoneWordtGevolgdInPlaatsVanDeStandaard()
    {
        // De zone is een parameter en geen constante binnenin, zodat een klant in een andere zone
        // niet vastzit en zomertijd te testen is zonder de machineklok te verzetten.
        var moment = DateTimeOffset.Parse(
            "2026-08-19T09:22:31Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("19-08-2026 09:22:31 (UTC+00:00)", TimeFormat.Absolute(moment, TimeZoneInfo.Utc));
    }

    // ── De kloktijd in een logtabel ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-08-19T09:22:31Z", "11:22:31")]
    [InlineData("2026-01-15T09:22:31Z", "10:22:31")]
    public void DeKloktijdVolgtDezelfdeZoneAlsDeTooltip(string utc, string verwacht)
    {
        var moment = DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(verwacht, TimeFormat.Clock(moment));
    }

    // ── Het machineleesbare attribuut ───────────────────────────────────────────────────────

    [Fact]
    public void IsoBlijftUtcBijInvoerDieAlInUtcStaat()
    {
        var moment = new DateTimeOffset(2026, 8, 19, 9, 22, 31, TimeSpan.Zero);

        Assert.Equal("2026-08-19T09:22:31Z", TimeFormat.Iso(moment));
    }

    [Fact]
    public void IsoBlijftUtcBijInvoerMetDeNederlandseZomertijdoffset()
    {
        // Hetzelfde moment, geschreven als 11:22:31+02:00. Het attribuut hoort er identiek uit te
        // komen als de UTC-variant hierboven, anders wijkt de machineleesbare vorm af van wat
        // erin ging.
        var moment = new DateTimeOffset(2026, 8, 19, 11, 22, 31, TimeSpan.FromHours(2));

        Assert.Equal("2026-08-19T09:22:31Z", TimeFormat.Iso(moment));
    }

    [Fact]
    public void IsoBlijftUtcBijEenOffsetAanDeAndereKantVanDeNulmeridiaan()
    {
        var moment = new DateTimeOffset(2026, 8, 19, 4, 22, 31, TimeSpan.FromHours(-5));

        Assert.Equal("2026-08-19T09:22:31Z", TimeFormat.Iso(moment));
    }

    [Fact]
    public void IsoBeweegtNietMeeMetDeWeergaveZone()
    {
        // Als iemand ooit Iso door dezelfde zoneconversie haalt als Absolute, valt deze om.
        var moment = new DateTimeOffset(2026, 8, 19, 9, 22, 31, TimeSpan.Zero);

        Assert.EndsWith("Z", TimeFormat.Iso(moment), StringComparison.Ordinal);
        Assert.DoesNotContain("11:22", TimeFormat.Iso(moment), StringComparison.Ordinal);
        Assert.Contains("11:22", TimeFormat.Absolute(moment), StringComparison.Ordinal);
    }

    // ── De zone zelf ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeStandaardzoneIsEuropeAmsterdam()
    {
        Assert.Equal("Europe/Amsterdam", TimeFormat.DefaultZoneId);
        Assert.NotNull(TimeFormat.DefaultZone);
        Assert.True(TimeFormat.DefaultZone.SupportsDaylightSavingTime);
    }

    [Fact]
    public void EenOnbekendeZoneValtNietStilletjesTerugOpUtc()
    {
        // Stilletjes de verkeerde klok tonen is precies de onwaarheid die dit portaal niet maakt.
        Assert.Throws<TimeZoneNotFoundException>(() => TimeFormat.Resolve("Mars/Olympus_Mons"));
    }

    [Fact]
    public void EenLegeZoneIdIsEenProgrammeerfoutEnGeenTerugval()
    {
        Assert.Throws<ArgumentException>(() => TimeFormat.Resolve("  "));
    }

    // ── Relatieve tijd ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "0 sec geleden")]
    [InlineData(44, "44 sec geleden")]
    [InlineData(45, "1 min geleden")]
    [InlineData(660, "11 min geleden")]
    [InlineData(3600, "1 u 0 min geleden")]
    [InlineData(9000, "2 u 30 min geleden")]
    [InlineData(172_800, "2 d geleden")]
    public void RelatieveTijdKiestDeGrofsteEenheidDieNogIetsZegt(int secondenGeleden, string verwacht)
    {
        var nu = Hulpmiddelen.Testgegevens.Nu;
        var moment = nu - TimeSpan.FromSeconds(secondenGeleden);

        Assert.Equal(verwacht, TimeFormat.Relative(moment, nu));
    }

    [Fact]
    public void EenMomentInDeToekomstLeestAlsOver()
    {
        var nu = Hulpmiddelen.Testgegevens.Nu;

        Assert.Equal("5 min geleden", TimeFormat.Relative(nu - TimeSpan.FromMinutes(5), nu));
        Assert.Equal("over 5 min", TimeFormat.Relative(nu + TimeSpan.FromMinutes(5), nu));
    }

    [Fact]
    public void RelatieveTijdIsZoneonafhankelijk()
    {
        // Een verschil tussen twee momenten is overal even groot. Twee schrijfwijzen van hetzelfde
        // moment horen dus dezelfde tekst op te leveren.
        var nu = new DateTimeOffset(2026, 8, 19, 9, 22, 31, TimeSpan.Zero);
        var inNederlandseTijd = new DateTimeOffset(2026, 8, 19, 11, 12, 31, TimeSpan.FromHours(2));
        var inUtc = new DateTimeOffset(2026, 8, 19, 9, 12, 31, TimeSpan.Zero);

        Assert.Equal(
            TimeFormat.Relative(inUtc, nu),
            TimeFormat.Relative(inNederlandseTijd, nu));
        Assert.Equal("10 min geleden", TimeFormat.Relative(inNederlandseTijd, nu));
    }
}
