using Soratus.Mcp.Uren;

namespace Soratus.Mcp.Uren.Tests;

/// <summary>
/// Wat de aanroeper terugkrijgt, en vooral: wat er niet in mag staan.
/// </summary>
/// <remarks>
/// <para>Claude Code toont deze tekst aan een mens, en die mens besluit op grond daarvan of hij nog
/// iets moet doen. Een melding die "geboekt" zegt en de fiattering niet noemt, is technisch waar en
/// praktisch onwaar — de boeker denkt dat hij klaar is, terwijl de uren op <c>pending</c> blijven
/// staan tot iemand zich afvraagt waarom de factuur te laag is.</para>
///
/// <para>Deze tests kijken naar woorden. Dat is grof, en het is precies grof genoeg: de fout die
/// hier moet worden voorkomen is dat iemand de melding later "korter" maakt en de waarschuwing als
/// eerste sneuvelt, want die is de langste regel.</para>
/// </remarks>
public class MeldingTests
{
    private static readonly Uri Portaal = new("https://portal.soratus.com");

    /// <summary>Woorden die beweren dat er niets meer hoeft te gebeuren.</summary>
    private static readonly string[] StilleOnwaarheden =
    [
        "verwerkt",
        "goedgekeurd",
        "gefiatteerd",
        "gefactureerd",
        "afgerond",
        "meegeteld in het maandtotaal",
    ];

    private static HourBookingResponse Regel() => new()
    {
        Id = "hourEntry-mcp-01K9",
        CustomerId = "bakker",
        CreatedAt = new DateTimeOffset(2026, 8, 20, 14, 3, 11, TimeSpan.Zero),
        Month = "2026-08",
        Hours = 3.5m,
        Category = "Ontwikkeling",
        Note = "Koppeling met de voorraadservice afgemaakt.",
        Source = "mcp",
        BookedBy = "Claude Code — Marcel",
        Status = "pending",
    };

    [Fact]
    public void EenGeslaagdeBoekingZegtDatHijNogGefiatteerdMoetWorden()
    {
        (string tekst, bool isFout) = BookingReport.Write(new BookingOutcome.Booked(Regel()), Portaal);

        Assert.False(isFout);
        Assert.Contains("TE FIATTEREN", tekst, StringComparison.Ordinal);
        Assert.Contains("telt NIET mee", tekst, StringComparison.Ordinal);
        Assert.Contains("facturatie", tekst, StringComparison.Ordinal);
        Assert.Contains("niet af", tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void EenGeslaagdeBoekingBeweertNergensDatHetKlaarIs()
    {
        (string tekst, _) = BookingReport.Write(new BookingOutcome.Booked(Regel()), Portaal);

        foreach (string woord in StilleOnwaarheden)
        {
            // "gefiatteerde regels" mag wel, want dat is de uitleg van het maandtotaal. De toets is
            // dus op de bewering over déze regel, en die staat in de eerste regel van de melding.
            string eersteRegel = tekst.Split('\n')[0];
            Assert.DoesNotContain(woord, eersteRegel, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EenGeslaagdeBoekingWijstNaarDeMaandInHetPortaal()
    {
        (string tekst, _) = BookingReport.Write(new BookingOutcome.Booked(Regel()), Portaal);

        Assert.Contains(
            "https://portal.soratus.com/klant/bakker/uren?maand=2026-08",
            tekst,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EenGeslaagdeBoekingNoemtDeRegelIdEnDeBoeker()
    {
        (string tekst, _) = BookingReport.Write(new BookingOutcome.Booked(Regel()), Portaal);

        Assert.Contains("hourEntry-mcp-01K9", tekst, StringComparison.Ordinal);
        Assert.Contains("Claude Code — Marcel", tekst, StringComparison.Ordinal);
        Assert.Contains("3,5 u", tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void EenVeldDatHetPortaalNietMeestuurdeKrijgtGeenStreepje()
    {
        // Een leeg veld met een streepje belooft dat er ooit een waarde komt. Bij een antwoord van
        // buiten betekent een ontbrekend veld iets anders: het portaal heeft het niet meegestuurd.
        (string tekst, _) = BookingReport.Write(
            new BookingOutcome.Booked(Regel() with { BookedBy = null }),
            Portaal);

        Assert.DoesNotContain("geboekt door", tekst, StringComparison.Ordinal);
        Assert.DoesNotContain("—\n", tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void EenAfwijzingZegtDatErNietsIsAchtergebleven()
    {
        (string tekst, bool isFout) = BookingReport.Write(
            new BookingOutcome.Refused(["uren: 0 kan niet."], Sent: false),
            Portaal);

        Assert.True(isFout);
        Assert.Contains("NIET geboekt", tekst, StringComparison.Ordinal);
        Assert.Contains("niets naar het portaal gestuurd", tekst, StringComparison.Ordinal);
        Assert.Contains("uren: 0 kan niet.", tekst, StringComparison.Ordinal);
        Assert.Contains("niets achtergebleven", tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void EenAfwijzingDoorHetPortaalZegtDatHijDaarVandaanKomt()
    {
        (string tekst, _) = BookingReport.Write(
            new BookingOutcome.Refused(["Geldige categorieën: Ontwikkeling, Beheer."], Sent: true),
            Portaal);

        Assert.Contains("Het portaal heeft de boeking geweigerd", tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void EenTijdslimietZegtDatHetOnbekendIsEnNietDatHetMislukteIs()
    {
        // Dit is het geval waar de neiging het sterkst is om "mislukt" te zeggen, en waar dat de
        // duurste gok is: gaat de aanroeper het opnieuw proberen, dan staat er straks twee keer
        // hetzelfde.
        (string tekst, bool isFout) = BookingReport.Write(
            new BookingOutcome.Unavailable("Geen antwoord binnen 30 seconden.", MayHaveLanded: true),
            Portaal);

        Assert.True(isFout);
        Assert.Contains("ONBEKEND of er geboekt is", tekst, StringComparison.Ordinal);
        Assert.Contains("Kijk in het portaal", tekst, StringComparison.Ordinal);
        Assert.DoesNotContain("NIET geboekt", tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOnbereikbaarPortaalZegtDatErNietsIsVastgelegd()
    {
        (string tekst, _) = BookingReport.Write(
            new BookingOutcome.Unavailable("Niet bereikbaar.", MayHaveLanded: false),
            Portaal);

        Assert.Contains("NIET geboekt", tekst, StringComparison.Ordinal);
        Assert.DoesNotContain("ONBEKEND", tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void EenProefdraaiZegtInDeEersteRegelDatErNietsIsGeboekt()
    {
        var verzoek = new HourBookingRequest
        {
            CustomerId = "bakker",
            Month = "2026-08",
            Hours = 3.5m,
            Category = "Ontwikkeling",
            Note = "Iets gedaan.",
        };

        (string tekst, bool isFout) = BookingReport.Write(new BookingOutcome.DryRun(verzoek), Portaal);

        Assert.False(isFout);
        Assert.StartsWith("PROEFDRAAI", tekst, StringComparison.Ordinal);
        Assert.Contains("NIETS geboekt", tekst, StringComparison.Ordinal);
        Assert.Contains(UrenConfiguration.DryRunKey, tekst, StringComparison.Ordinal);
    }
}
