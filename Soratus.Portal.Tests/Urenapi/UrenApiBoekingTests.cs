using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Soratus.Portal.Api;
using Soratus.Portal.Data;
using Soratus.Portal.Security;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Urenapi;

/// <summary>
/// Wat er van een boeking terechtkomt: de vaste regel, de velden, en de vorm van een afwijzing.
/// </summary>
[Collection(Urenapicollectie.Naam)]
public sealed class UrenApiBoekingTests
{
    private readonly Urenapihost _host;

    /// <summary>Neemt het draaiende portaal aan.</summary>
    /// <param name="host">Het portaal.</param>
    public UrenApiBoekingTests(Urenapihost host)
    {
        _host = host;
        _host.Schrijver.Reset();
    }

    /// <summary>
    /// De kernregel: een regel van dit endpoint is nooit gefiatteerd, en zijn bron is <c>mcp</c>.
    /// </summary>
    /// <remarks>
    /// Op het document van de échte schrijver en niet op dat van de test-dubbelganger, want anders meet
    /// deze test zijn eigen constante. <c>ApprovedAt</c> en <c>ApprovedBy</c> horen leeg te zijn: dit is
    /// het verschil met een boeking uit het scherm, waar de verzender zelf het akkoord van Soratus is.
    /// </remarks>
    [Fact]
    public async Task EenBoekingViaDitEndpointIsNooitGefiatteerd()
    {
        var scope = await Weergavelaag.Schrijfscope();
        var moment = DateTimeOffset.Parse("2026-08-21T09:15:00Z", null);

        var regel = CosmosMcpHoursWriter.Build(scope, Boeking(), moment);

        Assert.Equal(HourEntryStatus.Pending, regel.Status);
        Assert.Equal(HourEntrySource.Mcp, regel.Source);
        Assert.False(regel.Counts);

        Assert.Null(regel.ApprovedAt);
        Assert.Null(regel.ApprovedBy);
        Assert.Null(regel.RejectedAt);
        Assert.Null(regel.RejectionReason);

        Assert.Equal(scope.CustomerId, regel.PartitionKey);
        Assert.Equal(scope.CustomerId, regel.CustomerId);
        Assert.Equal(PortalDocumentKinds.HourEntry, regel.Kind);
        Assert.Equal(moment, regel.CreatedAt);
        Assert.Equal(HourBookingApiContract.CreatedBy, regel.CreatedBy);
        Assert.StartsWith("hourEntry-mcp-", regel.Id, StringComparison.Ordinal);

        // De koppeling heeft geen idempotentiesleutel, dus er staat er ook geen op het document. Zie
        // HourEntryKeys.ForIntegration voor waarom dat een besluit is en geen gebrek.
        Assert.Null(regel.ExternalId);
    }

    /// <summary>
    /// Dezelfde inhoud op een ander moment levert een andere sleutel op, en op hetzelfde moment
    /// dezelfde.
    /// </summary>
    /// <remarks>
    /// Dat tweede is de enige bescherming tegen een dubbele verzending die dit pad heeft: twee aanroepen
    /// binnen dezelfde milliseconde botsen op een 409. Dat eerste is waarom er géén idempotentie op de
    /// inhoud staat — twee blokken van een uur met dezelfde omschrijving op dezelfde dag is legitiem
    /// werk, en een sleutel over de inhoud zou juist die boeking weigeren.
    /// </remarks>
    [Fact]
    public async Task DeSleutelOnderscheidtOpMomentEnOpInhoud()
    {
        var scope = await Weergavelaag.Schrijfscope();
        var moment = DateTimeOffset.Parse("2026-08-21T09:15:00Z", null);

        var eerste = CosmosMcpHoursWriter.Build(scope, Boeking(), moment);
        var zelfde = CosmosMcpHoursWriter.Build(scope, Boeking(), moment);
        var later = CosmosMcpHoursWriter.Build(scope, Boeking(), moment.AddMilliseconds(1));
        var anders = CosmosMcpHoursWriter.Build(scope, Boeking() with { Hours = 4m }, moment);

        Assert.Equal(eerste.Id, zelfde.Id);
        Assert.NotEqual(eerste.Id, later.Id);
        Assert.NotEqual(eerste.Id, anders.Id);
    }

    /// <summary>Het antwoord op een geslaagde boeking heeft de vorm die de MCP-server leest.</summary>
    [Fact]
    public async Task HetAntwoordHeeftDeVormDieDeMcpServerLeest()
    {
        using var client = _host.Client(_host.Token([PortalRoles.Operator]));

        using var antwoord = await client.PostAsJsonAsync(HourBookingApiContract.Path, Verzoek());

        Assert.Equal(HttpStatusCode.Created, antwoord.StatusCode);

        var regel = await antwoord.Content.ReadFromJsonAsync<HourBookingResponse>();

        Assert.NotNull(regel);
        Assert.Equal("pending", regel.Status);
        Assert.Equal("mcp", regel.Source);
        Assert.Equal(_host.EersteKlant, regel.CustomerId);
        Assert.Equal("2026-08", regel.Month);
        Assert.Equal(3.5m, regel.Hours);
        Assert.Equal("Ontwikkeling", regel.Category);
        Assert.Equal("Koppeling met de voorraadservice afgemaakt.", regel.Note);
        Assert.Equal(HourBookingApiContract.CreatedBy, regel.CreatedBy);
        Assert.NotEmpty(regel.Id);

        // De Location-kop wijst naar het scherm waar een operator hem fiatteert, met de maand erop.
        Assert.Equal(
            $"/klant/{_host.EersteKlant}/uren?maand=2026-08",
            antwoord.Headers.Location?.OriginalString);
    }

    /// <summary>
    /// <c>by</c> komt uit het token en niet uit het verzoek, en een meegestuurde <c>by</c> wordt
    /// geweigerd.
    /// </summary>
    /// <remarks>
    /// De tweede helft is het punt. Zou het veld stil worden genegeerd, dan is "het portaal neemt jouw
    /// by niet over" niet te onderscheiden van "het portaal neemt hem wél over" — en dat verschil is of
    /// iemand op naam van een ander kan boeken.
    /// </remarks>
    [Fact]
    public async Task ByKomtUitHetTokenEnEenMeegestuurdeByWordtGeweigerd()
    {
        using var client = _host.Client(_host.Token([PortalRoles.Operator], naam: "Marcel de Graaf"));

        using var goed = await client.PostAsJsonAsync(HourBookingApiContract.Path, Verzoek());
        Assert.Equal(HttpStatusCode.Created, goed.StatusCode);
        Assert.Equal("Marcel de Graaf", _host.Schrijver.Aangeboden?.By);

        _host.Schrijver.Reset();

        using var gesmokkeld = await client.PostAsync(
            HourBookingApiContract.Path,
            Ruw("""
                {"cid":"bakker","month":"2026-08","hours":1,"category":"Beheer",
                 "note":"Iets gedaan voor een klant.","by":"Iemand Anders"}
                """));

        Assert.Equal(HttpStatusCode.BadRequest, gesmokkeld.StatusCode);
        Assert.Equal(0, _host.Schrijver.Aanroepen);
    }

    /// <summary>Een meegestuurde <c>status</c> wordt geweigerd en niet stil genegeerd.</summary>
    [Fact]
    public async Task EenMeegestuurdeStatusWordtGeweigerd()
    {
        using var client = _host.Client(_host.Token([PortalRoles.Operator]));

        using var antwoord = await client.PostAsync(
            HourBookingApiContract.Path,
            Ruw("""
                {"cid":"bakker","month":"2026-08","hours":1,"category":"Beheer",
                 "note":"Iets gedaan voor een klant.","status":"approved"}
                """));

        Assert.Equal(HttpStatusCode.BadRequest, antwoord.StatusCode);
        Assert.Equal(0, _host.Schrijver.Aanroepen);
    }

    /// <summary>
    /// Een onbekende categorie levert een afwijzing op die de geldige waarden noemt.
    /// </summary>
    /// <remarks>
    /// Dit is de enige plek waar de aanroeper die lijst leert: er is met opzet geen metadata-endpoint.
    /// De waarden komen uit <see cref="HourCategories.Bookable"/> en zijn hier niet overgeschreven.
    /// </remarks>
    [Fact]
    public async Task EenOnbekendeCategorieWordtGeweigerdMetDeGeldigeWaardenErbij()
    {
        using var client = _host.Client(_host.Token([PortalRoles.Operator]));

        using var antwoord = await client.PostAsJsonAsync(
            HourBookingApiContract.Path,
            Verzoek(categorie: "Koffie"));

        Assert.Equal(HttpStatusCode.UnprocessableContent, antwoord.StatusCode);
        Assert.Equal("application/problem+json", antwoord.Content.Headers.ContentType?.MediaType);

        var probleem = await Probleem(antwoord);

        Assert.Contains("Koffie", probleem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(
            HourCategories.Bookable,
            probleem.GetProperty("categories").EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    /// <summary>
    /// De categorie <c>Correctie</c> is van buiten verboden.
    /// </summary>
    /// <remarks>
    /// Besluit 16 van de afwijkingennotitie: een correctie is een handmatige handeling van een operator
    /// in het portaal, met een eigen aanroep en negatieve uren. Kon een koppeling erop boeken, dan is
    /// een correctie niet meer van een boeking te onderscheiden en is de tooltip uit §3.6 niet te
    /// vullen.
    /// </remarks>
    [Fact]
    public async Task DeCategorieCorrectieKomtHierNietDoor()
    {
        using var client = _host.Client(_host.Token([PortalRoles.Operator]));

        using var antwoord = await client.PostAsJsonAsync(
            HourBookingApiContract.Path,
            Verzoek(categorie: HourCategories.Correction));

        Assert.Equal(HttpStatusCode.UnprocessableContent, antwoord.StatusCode);

        var probleem = await Probleem(antwoord);
        var geldig = probleem.GetProperty("categories").EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();

        Assert.DoesNotContain(HourCategories.Correction, geldig);
    }

    /// <summary>Een onbekende klant levert een afwijzing op die de bekende slugs noemt.</summary>
    [Fact]
    public async Task EenOnbekendeKlantWordtGeweigerdMetDeBekendeSlugsErbij()
    {
        using var client = _host.Client(_host.Token([PortalRoles.Operator]));

        using var antwoord = await client.PostAsJsonAsync(
            HourBookingApiContract.Path,
            Verzoek(klant: "Bakker Techniek B.V."));

        Assert.Equal(HttpStatusCode.UnprocessableContent, antwoord.StatusCode);
        Assert.Equal(0, _host.Schrijver.Aanroepen);

        var probleem = await Probleem(antwoord);

        Assert.Contains(
            _host.EersteKlant,
            probleem.GetProperty("customers").EnumerateArray().Select(item => item.GetString()!));
    }

    /// <summary>Een maand die geen maand is, en uren die niet kunnen, worden geweigerd.</summary>
    /// <param name="maand">De maand.</param>
    /// <param name="uren">De uren.</param>
    [Theory]
    [InlineData("augustus", 3.5)]
    [InlineData("08-2026", 3.5)]
    [InlineData("2026-8", 3.5)]
    [InlineData("2026-08", 0)]
    [InlineData("2026-08", -2)]
    [InlineData("2026-08", 20)]
    [InlineData("2026-08", 500)]
    public async Task WatDeDatalaagWeigertWeigertDitEndpointOok(string maand, double uren)
    {
        using var client = _host.Client(_host.Token([PortalRoles.Operator]));

        using var antwoord = await client.PostAsJsonAsync(
            HourBookingApiContract.Path,
            Verzoek(maand: maand, uren: (decimal)uren));

        Assert.Equal(HttpStatusCode.UnprocessableContent, antwoord.StatusCode);
        Assert.Equal("application/problem+json", antwoord.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrWhiteSpace((await Probleem(antwoord)).GetProperty("detail").GetString()));
    }

    /// <summary>
    /// Een conflict uit de opslag komt als <c>409</c> aan en niet als een algemene fout.
    /// </summary>
    /// <remarks>
    /// Het duurste onderscheid dat de MCP-server maakt. Een <c>5xx</c> betekent bij hem "ONBEKEND of er
    /// geboekt is" — dan mag een aanroeper het nog eens proberen — en een <c>409</c> betekent "niet
    /// geboekt, er staat er al een". Verdwijnt dat verschil in een algemene fout, dan staat er twee keer
    /// hetzelfde uur.
    /// </remarks>
    [Fact]
    public async Task EenConflictUitDeOpslagKomtAls409Aan()
    {
        _host.Schrijver.Antwoord = PortalWriteResult<HourEntryDocument>.Conflict(
            "Deze urenregel staat er al.",
            current: null);

        using var client = _host.Client(_host.Token([PortalRoles.Operator]));

        using var antwoord = await client.PostAsJsonAsync(HourBookingApiContract.Path, Verzoek());

        Assert.Equal(HttpStatusCode.Conflict, antwoord.StatusCode);
        Assert.Equal("application/problem+json", antwoord.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            "staat er al",
            (await Probleem(antwoord)).GetProperty("detail").GetString(),
            StringComparison.Ordinal);
    }

    /// <summary>Een leeg lichaam levert een afwijzing op en geen storing.</summary>
    [Fact]
    public async Task EenLeegLichaamLevertEenAfwijzingOp()
    {
        using var client = _host.Client(_host.Token([PortalRoles.Operator]));

        using var antwoord = await client.PostAsync(HourBookingApiContract.Path, Ruw("null"));

        Assert.Equal(HttpStatusCode.BadRequest, antwoord.StatusCode);
        Assert.Equal(0, _host.Schrijver.Aanroepen);
    }

    /// <summary>
    /// De grenzen van het portaal zijn strakker dan die van de MCP-server, en dat is een afwijking van
    /// <c>mcp-uren.md</c> die hier wordt vastgepind in plaats van weggewerkt.
    /// </summary>
    /// <remarks>
    /// <para>Dat document zegt <c>uren ≤ 200</c> en <c>omschrijving 5–500 tekens</c>; de datalaag van
    /// het portaal staat 16 uur per regel toe (<see cref="HourLimits.MaximumPerEntry"/>) en 400 tekens
    /// (<see cref="HourLimits.MaximumNoteLength"/>). Er zit dus een band waarin de client een boeking
    /// doorlaat en het portaal hem weigert.</para>
    ///
    /// <para>Dat is geen storing — het portaal is de eigenaar van deze grenzen en de afwijzing komt met
    /// een leesbare reden bij de aanroeper terecht, dus de aanroeper kan het herstellen. Het is wél een
    /// tekst in <c>mcp-uren.md</c> die niet klopt, en dat is gemeld. Deze test staat er zodat de
    /// discrepantie niet stil de andere kant op wordt "opgelost" door de portaalgrens op te rekken naar
    /// een getal uit een document.</para>
    /// </remarks>
    [Fact]
    public void DeGrenzenVanHetPortaalZijnStrakkerDanDieVanDeClient()
    {
        Assert.True(HourLimits.MaximumPerEntry < 200m);
        Assert.True(HourLimits.MaximumNoteLength < 500);
    }

    private static HourBooking Boeking() => new()
    {
        Month = "2026-08",
        Hours = 3.5m,
        Category = HourCategories.Development,
        By = "Marcel de Graaf",
        Note = "Koppeling met de voorraadservice afgemaakt.",
    };

    private static StringContent Ruw(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> Probleem(HttpResponseMessage antwoord) =>
        JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync()).RootElement;

    private object Verzoek(
        string? klant = null,
        string maand = "2026-08",
        decimal uren = 3.5m,
        string categorie = "Ontwikkeling") => new
        {
            cid = klant ?? _host.EersteKlant,
            month = maand,
            hours = uren,
            category = categorie,
            note = "Koppeling met de voorraadservice afgemaakt.",
        };
}
