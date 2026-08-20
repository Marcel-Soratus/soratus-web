using Soratus.Mcp.Uren;

namespace Soratus.Mcp.Uren.Tests;

/// <summary>
/// De machineleesbare stand naast de tekst.
/// </summary>
/// <remarks>
/// Een aanroeper die alleen naar <c>isError</c> kijkt, ziet bij een geslaagde boeking <c>false</c> en
/// kan daaruit concluderen dat het klaar is. Deze stand bestaat om die conclusie onmogelijk te maken:
/// "moet nog gefiatteerd worden" en "telt niet mee in het maandtotaal" staan als veld en niet alleen
/// in proza.
/// </remarks>
public class StandTests
{
    private static readonly Uri Portaal = new("https://portal.soratus.com");

    private static readonly HourBookingResponse Regel = new()
    {
        Id = "hourEntry-mcp-01K9",
        CustomerId = "bakker",
        Month = "2026-08",
        Hours = 3.5m,
        Source = "mcp",
        Status = "pending",
    };

    private static readonly HourBookingRequest Verzoek = new()
    {
        CustomerId = "bakker",
        Month = "2026-08",
        Hours = 3.5m,
        Category = "Ontwikkeling",
        Note = "Iets gedaan.",
    };

    [Fact]
    public void EenGeboekteRegelStaatOpVastgelegdMaarNietMeetellend()
    {
        BookingState stand = BookingState.From(new BookingOutcome.Booked(Regel), Portaal);

        Assert.Equal("booked", stand.Outcome);
        Assert.True(stand.Recorded);
        Assert.Equal("pending", stand.ApprovalStatus);
        Assert.True(stand.RequiresSoratusApproval);
        Assert.False(stand.CountsTowardMonthTotal);
        Assert.Equal("hourEntry-mcp-01K9", stand.EntryId);
        Assert.Equal(
            "https://portal.soratus.com/klant/bakker/uren?maand=2026-08",
            stand.ReviewUrl);
    }

    [Theory]
    [InlineData("booked")]
    [InlineData("dryRun")]
    [InlineData("refused")]
    [InlineData("unavailable")]
    [InlineData("suspect")]
    public void ElkeUitkomstZegtDatSoratusMoetFiatteren(string verwacht)
    {
        // Dit is een eigenschap van de koppeling en niet van deze aanroep. Zou het veld bij een
        // afwijzing false zijn, dan moet een lezer per geval nadenken; nu zegt het iets over de tool.
        BookingOutcome uitkomst = verwacht switch
        {
            "booked" => new BookingOutcome.Booked(Regel),
            "dryRun" => new BookingOutcome.DryRun(Verzoek),
            "refused" => new BookingOutcome.Refused(["uren: 0 kan niet."], Sent: false),
            "unavailable" => new BookingOutcome.Unavailable("Niet bereikbaar.", MayHaveLanded: false),
            _ => new BookingOutcome.Suspect(Regel with { Status = "approved" }, "approved"),
        };

        BookingState stand = BookingState.From(uitkomst, Portaal);

        Assert.Equal(verwacht, stand.Outcome);
        Assert.True(stand.RequiresSoratusApproval);
    }

    [Fact]
    public void EenOnbekendeUitkomstLaatVastgelegdOpNull()
    {
        // Drie waarden en niet twee. Bij een tijdslimiet kan het verzoek zijn aangekomen en alleen het
        // antwoord zijn weggevallen; 'false' zou daar een bewering zijn die niet waar te maken is.
        BookingState stand = BookingState.From(
            new BookingOutcome.Unavailable("Geen antwoord.", MayHaveLanded: true),
            Portaal);

        Assert.Null(stand.Recorded);
        Assert.Null(stand.CountsTowardMonthTotal);
    }

    [Fact]
    public void EenVerdachtAntwoordLaatMeetellenOpNull()
    {
        // Als de status niet 'pending' is, kan de regel al meetellen. 'false' neerzetten zou de
        // gevaarlijkste van de twee onwaarheden zijn.
        BookingState stand = BookingState.From(
            new BookingOutcome.Suspect(Regel with { Status = "approved" }, "test"),
            Portaal);

        Assert.True(stand.Recorded);
        Assert.Equal("approved", stand.ApprovalStatus);
        Assert.Null(stand.CountsTowardMonthTotal);
    }

    [Fact]
    public void EenProefdraaiStaatOpNietVastgelegd()
    {
        BookingState stand = BookingState.From(new BookingOutcome.DryRun(Verzoek), Portaal);

        Assert.False(stand.Recorded);
        Assert.Null(stand.EntryId);
    }

    [Fact]
    public void DeStandSerialiseertNaarDeVerwachteVeldnamen()
    {
        System.Text.Json.JsonElement json = BookingState
            .From(new BookingOutcome.Booked(Regel), Portaal)
            .ToJson();

        Assert.Equal("booked", json.GetProperty("outcome").GetString());
        Assert.True(json.GetProperty("recorded").GetBoolean());
        Assert.True(json.GetProperty("requiresSoratusApproval").GetBoolean());
        Assert.False(json.GetProperty("countsTowardMonthTotal").GetBoolean());
        Assert.Equal("pending", json.GetProperty("approvalStatus").GetString());
    }
}
