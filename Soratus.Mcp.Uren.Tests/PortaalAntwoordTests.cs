using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Soratus.Mcp.Uren;

namespace Soratus.Mcp.Uren.Tests;

/// <summary>
/// Hoe een antwoord van het portaal wordt gelezen.
/// </summary>
/// <remarks>
/// De statuscodes zijn hier geen implementatiedetail maar het contract met het portaal: 403 betekent
/// "geen operator", 404 betekent "het endpoint staat er nog niet", en een 200 met de verkeerde status
/// betekent dat de vaste regel uit §5 is gebroken. Elk van die drie krijgt een eigen melding, want
/// ze vragen elk iets anders van de lezer.
/// </remarks>
public class PortaalAntwoordTests
{
    private static readonly HourBookingRequest Verzoek = new()
    {
        CustomerId = "bakker",
        Month = "2026-08",
        Hours = 3.5m,
        Category = "Ontwikkeling",
        Note = "Koppeling met de voorraadservice afgemaakt.",
    };

    private static async Task<BookingOutcome> BoekAsync(
        HttpStatusCode status,
        string body,
        string contentType = "application/json")
    {
        using var handler = new VasteAntwoordHandler(status, body, contentType);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://portal.soratus.com/"),
        };

        var client = new PortalUrenClient(
            http,
            Options.Create(new UrenOptions
            {
                PortalBaseAddress = new Uri("https://portal.soratus.com"),
                Scope = "api://soratus-portal/.default",
            }));

        return await client.BookAsync(Verzoek, CancellationToken.None);
    }

    [Fact]
    public async Task EenGeslaagdAntwoordOpPendingIsEenBoeking()
    {
        BookingOutcome uitkomst = await BoekAsync(
            HttpStatusCode.Created,
            """
            {
              "id": "hourEntry-mcp-01K9",
              "cid": "bakker",
              "createdAt": "2026-08-20T14:03:11.4820000Z",
              "month": "2026-08",
              "hours": 3.5,
              "category": "Ontwikkeling",
              "note": "Koppeling met de voorraadservice afgemaakt.",
              "source": "mcp",
              "by": "Claude Code — Marcel",
              "status": "pending"
            }
            """);

        BookingOutcome.Booked geboekt = Assert.IsType<BookingOutcome.Booked>(uitkomst);
        Assert.Equal("hourEntry-mcp-01K9", geboekt.Entry.Id);
        Assert.Equal("pending", geboekt.Entry.Status);
    }

    [Fact]
    public async Task EenGeslaagdAntwoordOpApprovedIsVerdachtEnGeenBoeking()
    {
        // De belangrijkste test in dit bestand. Zou het portaal ondanks alles een gefiatteerde regel
        // teruggeven, dan is het gevaarlijkste wat deze server kan doen dat als geslaagd melden.
        BookingOutcome uitkomst = await BoekAsync(
            HttpStatusCode.Created,
            """{ "id": "x", "cid": "bakker", "month": "2026-08", "status": "approved", "source": "mcp" }""");

        BookingOutcome.Suspect verdacht = Assert.IsType<BookingOutcome.Suspect>(uitkomst);
        Assert.Contains("approved", verdacht.Reason, StringComparison.Ordinal);
        Assert.Contains("§5", verdacht.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EenAntwoordZonderStatusIsVerdacht()
    {
        // Geen status is niet hetzelfde als pending. Een portaal dat het veld vergeet, kan de regel
        // net zo goed op approved hebben gezet.
        BookingOutcome uitkomst = await BoekAsync(
            HttpStatusCode.Created,
            """{ "id": "x", "cid": "bakker", "month": "2026-08" }""");

        Assert.IsType<BookingOutcome.Suspect>(uitkomst);
    }

    [Fact]
    public async Task EenAndereBronIsVerdacht()
    {
        BookingOutcome uitkomst = await BoekAsync(
            HttpStatusCode.Created,
            """{ "id": "x", "cid": "bakker", "status": "pending", "source": "portaal" }""");

        BookingOutcome.Suspect verdacht = Assert.IsType<BookingOutcome.Suspect>(uitkomst);
        Assert.Contains("portaal", verdacht.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EenProblemJsonWordtEenAfwijzingMetDeGeldigeCategorieenErbij()
    {
        BookingOutcome uitkomst = await BoekAsync(
            HttpStatusCode.BadRequest,
            """
            {
              "title": "Ongeldige boeking",
              "detail": "De categorie 'Koffie' bestaat niet.",
              "categories": ["Ontwikkeling", "Beheer", "Support", "Advies"]
            }
            """,
            "application/problem+json");

        BookingOutcome.Refused geweigerd = Assert.IsType<BookingOutcome.Refused>(uitkomst);
        Assert.True(geweigerd.Sent);
        Assert.Contains("De categorie 'Koffie' bestaat niet.", geweigerd.Reasons);
        Assert.Contains(
            geweigerd.Reasons,
            reason => reason.Contains("Ontwikkeling", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EenModelvalidatieAntwoordWordtPerVeldGelezen()
    {
        BookingOutcome uitkomst = await BoekAsync(
            HttpStatusCode.UnprocessableContent,
            """{ "title": "Ongeldig", "errors": { "hours": ["Moet groter zijn dan nul."] } }""",
            "application/problem+json");

        BookingOutcome.Refused geweigerd = Assert.IsType<BookingOutcome.Refused>(uitkomst);
        Assert.Contains("hours: Moet groter zijn dan nul.", geweigerd.Reasons);
    }

    [Fact]
    public async Task EenAfwijzingZonderToelichtingLevertGeenLegeMelding()
    {
        BookingOutcome uitkomst = await BoekAsync(HttpStatusCode.BadRequest, "", "text/plain");

        BookingOutcome.Refused geweigerd = Assert.IsType<BookingOutcome.Refused>(uitkomst);
        Assert.Contains(
            geweigerd.Reasons,
            reason => reason.Contains("zonder toelichting", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EenVerbodenAntwoordNoemtDeOperatorrol()
    {
        BookingOutcome uitkomst = await BoekAsync(HttpStatusCode.Forbidden, "");

        BookingOutcome.Unavailable niet = Assert.IsType<BookingOutcome.Unavailable>(uitkomst);
        Assert.Contains("Operator", niet.Reason, StringComparison.Ordinal);
        Assert.False(niet.MayHaveLanded);
    }

    [Fact]
    public async Task EenOnbekendEndpointZegtDatHetErNogNietIs()
    {
        BookingOutcome uitkomst = await BoekAsync(HttpStatusCode.NotFound, "");

        BookingOutcome.Unavailable niet = Assert.IsType<BookingOutcome.Unavailable>(uitkomst);
        Assert.Contains("nog niet uitgerold", niet.Reason, StringComparison.Ordinal);
        Assert.False(niet.MayHaveLanded);
    }

    [Fact]
    public async Task EenServerfoutLaatOpenOfDeRegelIsGeland()
    {
        BookingOutcome uitkomst = await BoekAsync(HttpStatusCode.InternalServerError, "");

        BookingOutcome.Unavailable niet = Assert.IsType<BookingOutcome.Unavailable>(uitkomst);
        Assert.True(niet.MayHaveLanded);
    }

    [Fact]
    public async Task EenOnleesbaarGeslaagdAntwoordLaatOpenOfDeRegelIsGeland()
    {
        BookingOutcome uitkomst = await BoekAsync(HttpStatusCode.Created, "dit is geen json");

        BookingOutcome.Unavailable niet = Assert.IsType<BookingOutcome.Unavailable>(uitkomst);
        Assert.True(niet.MayHaveLanded);
    }

    [Fact]
    public async Task InProefdraaimodusGaatErNietsDeDeurUit()
    {
        using var handler = new VasteAntwoordHandler(HttpStatusCode.Created, "{}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://portal.soratus.com/") };

        var client = new PortalUrenClient(
            http,
            Options.Create(new UrenOptions
            {
                PortalBaseAddress = new Uri("https://portal.soratus.com"),
                DryRun = true,
            }));

        BookingOutcome uitkomst = await client.BookAsync(Verzoek, CancellationToken.None);

        Assert.IsType<BookingOutcome.DryRun>(uitkomst);
        Assert.Equal(0, handler.Aanroepen);
    }

    /// <summary>Geeft altijd hetzelfde antwoord en houdt bij hoe vaak hij is aangeroepen.</summary>
    private sealed class VasteAntwoordHandler(HttpStatusCode status, string body, string contentType = "application/json")
        : HttpMessageHandler
    {
        public int Aanroepen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Aanroepen++;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType),
            });
        }
    }
}
