using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De aanroep aan Cost Management: de vraagvorm, de backoff en het onderscheid tussen "geen antwoord"
/// en "een antwoord dat we niet konden lezen".
/// </summary>
/// <remarks>
/// <para><strong>De vraagvorm hieronder is de vorm die is gemeten, en dat is de reden dat hij een test
/// heeft.</strong> Op 21 augustus 2026 tegen <c>resourceGroups/MBV</c> over juli 2026: <c>HTTP 200</c>,
/// 112 rijen, kolommen <c>Cost, UsageDate, ServiceName, Currency</c>, <c>nextLink: null</c>. Wijzigt
/// iemand <c>timeframe</c>, <c>granularity</c> of de groepering, dan is dat geen instelling maar een
/// nieuwe meting — en dan hoort deze test rood te worden.</para>
///
/// <para>De antwoorden komen uit een eigen <see cref="HttpMessageHandler"/> en niet uit Azure. Wat
/// daarmee níet is bewezen staat in het rapport: het volgen van een <c>nextLink</c> is nooit tegen de
/// echte API gelopen, want er is nooit een <c>nextLink</c> geweest.</para>
/// </remarks>
public class KostenclientTests
{
    private const string Abonnement = "501a66d2-de54-4d4f-9f7c-1fbb55bec17f";

    /// <summary>21 augustus 2026: de dag van de metingen.</summary>
    private static readonly DateOnly Vandaag = new(2026, 8, 21);

    private static readonly AzureScope Scope = Ontleed($"/subscriptions/{Abonnement}/resourceGroups/MBV");

    [Fact]
    public async Task DeGemetenVraagvormGaatDeDeurUit()
    {
        var handler = new Vasteantwoorden();
        handler.Voeg(Geslaagd());

        var (client, _) = Bouw(handler);

        await client.ReadAsync(Scope, "2026-07", Vandaag);

        var verzoek = Assert.Single(handler.Verzoeken);

        Assert.Equal(HttpMethod.Post, verzoek.Methode);
        Assert.Equal(
            "https://management.azure.com/subscriptions/" + Abonnement
            + "/resourceGroups/MBV/providers/Microsoft.CostManagement/query?api-version=2023-11-01",
            verzoek.Url);

        // Het token gaat als bearer mee. Zonder deze assertie zou een client die de header vergeet
        // hier groen blijven en in productie een 401 geven — en een 401 kost óók budget.
        Assert.Equal("Bearer", verzoek.Schema);

        using var body = JsonDocument.Parse(verzoek.Body);
        var root = body.RootElement;

        // ActualCost en niet AmortizedCost: dat tweede gaat pas iets betekenen als er reserveringen
        // worden gekocht, en die zijn er niet.
        Assert.Equal("ActualCost", root.GetProperty("type").GetString());
        Assert.Equal("Custom", root.GetProperty("timeframe").GetString());
        Assert.Equal("2026-07-01T00:00:00Z", root.GetProperty("timePeriod").GetProperty("from").GetString());
        Assert.Equal("2026-07-31T23:59:59Z", root.GetProperty("timePeriod").GetProperty("to").GetString());

        // Dagkorrel is geen extra detail maar de voorwaarde voor de volledigheidscontrole: zonder dagen
        // is niet vast te stellen of de maand af is, en dan is het bedrag niet te factureren.
        var dataset = root.GetProperty("dataset");
        Assert.Equal("Daily", dataset.GetProperty("granularity").GetString());
        Assert.Equal("Cost", dataset.GetProperty("aggregation").GetProperty("totalCost").GetProperty("name").GetString());
        Assert.Equal("Sum", dataset.GetProperty("aggregation").GetProperty("totalCost").GetProperty("function").GetString());

        var groepering = Assert.Single(dataset.GetProperty("grouping").EnumerateArray().ToArray());
        Assert.Equal("Dimension", groepering.GetProperty("type").GetString());
        Assert.Equal("ServiceName", groepering.GetProperty("name").GetString());
    }

    [Fact]
    public async Task DeLopendeMaandLooptTotEnMetGisteren()
    {
        // Een `to` in de toekomst is niet gemeten en wordt daarom niet gebruikt. Dat kost niets: de
        // boeking van Cost Management loopt ongeveer acht uur achter en de run staat om 04:00 UTC, dus
        // van vandaag is er op dat moment nog niets.
        var handler = new Vasteantwoorden();
        handler.Voeg(Geslaagd());

        var (client, _) = Bouw(handler);

        await client.ReadAsync(Scope, "2026-08", Vandaag);

        using var body = JsonDocument.Parse(Assert.Single(handler.Verzoeken).Body);
        var periode = body.RootElement.GetProperty("timePeriod");

        Assert.Equal("2026-08-01T00:00:00Z", periode.GetProperty("from").GetString());
        Assert.Equal("2026-08-20T23:59:59Z", periode.GetProperty("to").GetString());
    }

    [Fact]
    public async Task OpDeEersteVanDeMaandWordtDeNieuweMaandNietBevraagd()
    {
        // Punt 30, en dit is de goedkope helft van het antwoord erop. Op de 1e om 04:00 valt "gisteren"
        // in de vorige maand, dus is er van de nieuwe maand geen dag geboekt om te bevragen. Niet vragen
        // is eerlijker dan vragen: "wij hebben niet gemeten" is iets anders dan "de API zei nul regels",
        // en het scheelt een aanroep uit een emmer die er geen over heeft.
        var handler = new Vasteantwoorden();
        var (client, _) = Bouw(handler);

        var antwoord = await client.ReadAsync(Scope, "2026-09", new DateOnly(2026, 9, 1));

        Assert.Equal(AzureCostAnswerKind.NotAvailable, antwoord.Kind);
        Assert.Equal(0, antwoord.Calls);
        Assert.Empty(handler.Verzoeken);
    }

    [Fact]
    public async Task EenGeslaagdAntwoordWordtGelezenTotRegelsPerDienstEnDagen()
    {
        var handler = new Vasteantwoorden();
        handler.Voeg(Geslaagd());

        var (client, _) = Bouw(handler);

        var antwoord = await client.ReadAsync(Scope, "2026-07", Vandaag);

        Assert.Equal(AzureCostAnswerKind.Answered, antwoord.Kind);
        Assert.Equal("EUR", antwoord.Currency);
        Assert.Equal(1, antwoord.Calls);

        // Opgeteld per dienst over de dagen. Twee dagen App Service is één regel met twee bedragen erin.
        var appservice = antwoord.Lines.Single(regel => regel.Service == "Azure App Service");
        Assert.Equal(2.5m, appservice.Amount);
        Assert.Equal([new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2)], antwoord.Days);

        // Een dienst met nul is een gewone regel en geen ontbrekende. In de echte uitvoer staan
        // Bandwidth € 0,0000 en Microsoft Entra € 0,0000; die maken samen een subtotaal dat er is.
        Assert.Contains(antwoord.Lines, regel => regel.Service == "Bandwidth" && regel.Amount == 0m);
    }

    [Fact]
    public async Task NulRijenIsEenAntwoordEnGeenMislukking()
    {
        // Punt 30. Een resource group die niet bestaat en een bestaande resource group over een periode
        // die nog niet is geboekt geven bééide HTTP 200 met "rows": []. Dat is een meting met een eigen
        // betekenis en géén mislukking — de aanroeper maakt er NoLines van en nooit nul.
        var handler = new Vasteantwoorden();
        handler.Voeg(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""{"properties":{"nextLink":null,"columns":[],"rows":[]}}"""),
        });

        var (client, _) = Bouw(handler);

        var antwoord = await client.ReadAsync(Scope, "2026-07", Vandaag);

        Assert.Equal(AzureCostAnswerKind.Answered, antwoord.Kind);
        Assert.Empty(antwoord.Lines);
        Assert.Null(antwoord.Currency);
    }

    [Fact]
    public async Task EenAntwoordZonderKostenkolomWordtOnleesbaarEnGeenBedrag()
    {
        // Punt 33: een onleesbaar antwoord wordt Unknown en geen subtotaal met een regel minder. Een
        // bedrag dat te laag is ziet er net zo geloofwaardig uit als een bedrag dat klopt.
        var handler = new Vasteantwoorden();
        handler.Voeg(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(
                """{"properties":{"columns":[{"name":"ServiceName","type":"String"}],"rows":[["Azure App Service"]]}}"""),
        });

        var (client, _) = Bouw(handler);

        var antwoord = await client.ReadAsync(Scope, "2026-07", Vandaag);

        Assert.Equal(AzureCostAnswerKind.Unreadable, antwoord.Kind);
        Assert.Empty(antwoord.Lines);
        Assert.NotNull(antwoord.Reason);
    }

    [Fact]
    public async Task Een429WordtOpnieuwGeprobeerdEnLuktDeTweedeKeer()
    {
        // Gemeten: vijf van tien aanroepen mislukten op één per elf seconden. Eén 429 mag dus geen
        // verloren maand zijn — en de spiegel van de test hieronder, waar de pogingen wél opraken.
        var handler = new Vasteantwoorden();
        handler.Voeg(Geweigerd(HttpStatusCode.TooManyRequests, entity: "38", clientType: "19"));
        handler.Voeg(Geslaagd());

        var (client, klok) = Bouw(handler);

        var antwoord = await client.ReadAsync(Scope, "2026-07", Vandaag);

        Assert.Equal(AzureCostAnswerKind.Answered, antwoord.Kind);

        // Elke respons kost budget, ook de mislukte. Dat aantal komt terug zodat het in een logregel kan.
        Assert.Equal(2, antwoord.Calls);

        // De grootste van de twee hints wint, want entity-retry-after verschijnt alleen zodra de
        // entiteitsteller op nul staat en is dan de grotere. En hij haalt de eigen vloer niet, dus die
        // vloer geldt: gemeten waarden 1, 3, 4 en 12 waren aantoonbaar te kort.
        Assert.Contains(TimeSpan.FromSeconds(240), klok.Wachttijden);
    }

    [Fact]
    public async Task EenHintDieGroterIsDanDeEigenVloerWordtGevolgd()
    {
        // De hint wordt gelezen als hij er is. De vloer is een ondergrens en geen vaste waarde: zou de
        // hint worden weggegooid, dan negeren we het enige signaal dat Azure geeft.
        var handler = new Vasteantwoorden();
        handler.Voeg(Geweigerd(HttpStatusCode.TooManyRequests, entity: "600", clientType: "19"));
        handler.Voeg(Geslaagd());

        var (client, klok) = Bouw(handler);

        await client.ReadAsync(Scope, "2026-07", Vandaag);

        Assert.Contains(TimeSpan.FromSeconds(600), klok.Wachttijden);
    }

    [Fact]
    public async Task EenGeweigerdVerzoekZonderEnkeleHintWachtOpDeEigenVloer()
    {
        // Gemeten: bij een meting op laag tempo kwamen zes 429's waarvan vier zónder enige hintheader.
        // De backoff mag daar niet op omvallen en mag er zeker niet nul van maken.
        var handler = new Vasteantwoorden();
        handler.Voeg(Geweigerd(HttpStatusCode.TooManyRequests));
        handler.Voeg(Geslaagd());

        var (client, klok) = Bouw(handler);

        await client.ReadAsync(Scope, "2026-07", Vandaag);

        Assert.Contains(TimeSpan.FromSeconds(240), klok.Wachttijden);
    }

    [Fact]
    public async Task Een404WordtOpnieuwGeprobeerdEnNooitNul()
    {
        // De gevaarlijkste bevinding van het onderzoek: GtmDimensionDataProvider…returns null, tweemaal
        // in ruim twintig aanroepen, op een verzoek dat er vlak ervoor 200 op gaf. Een normale client
        // behandelt 404 als "bestaat niet" en rendert € 0,00 — en op een factuur is € 0,00 geen lege
        // waarde maar een verkeerd bedrag.
        var handler = new Vasteantwoorden();
        handler.Voeg(Geweigerd(HttpStatusCode.NotFound));
        handler.Voeg(Geslaagd());

        var (client, _) = Bouw(handler);

        var antwoord = await client.ReadAsync(Scope, "2026-07", Vandaag);

        Assert.Equal(AzureCostAnswerKind.Answered, antwoord.Kind);
        Assert.Equal(2, antwoord.Calls);
    }

    [Fact]
    public async Task AlsDePogingenOpZijnKomtErGeenBedragEnGeenNul()
    {
        var handler = new Vasteantwoorden();
        handler.Voeg(Geweigerd(HttpStatusCode.TooManyRequests));
        handler.Voeg(Geweigerd(HttpStatusCode.TooManyRequests));
        handler.Voeg(Geslaagd());

        var (client, _) = Bouw(handler);

        var antwoord = await client.ReadAsync(Scope, "2026-07", Vandaag);

        Assert.Equal(AzureCostAnswerKind.NotAvailable, antwoord.Kind);
        Assert.Empty(antwoord.Lines);
        Assert.NotNull(antwoord.Reason);

        // Twee pogingen en niet drie: elke respons kost budget. Gemeten liep qpu-remaining over
        // eenentwintig aanroepen van 599 naar 578 terwijl de meeste ervan mislukten, dus een derde
        // poging kost de vólgende klant zijn meting.
        Assert.Equal(2, antwoord.Calls);
        Assert.Equal(2, handler.Verzoeken.Count);
    }

    [Fact]
    public async Task EenVerbodenVerzoekWordtNietOpnieuwGeprobeerd()
    {
        // Een 403 gaat niet over van zichzelf: dat is een ontbrekende rolverlening. Hem herhalen kost
        // budget en verandert niets.
        var handler = new Vasteantwoorden();
        handler.Voeg(Geweigerd(HttpStatusCode.Forbidden));
        handler.Voeg(Geslaagd());

        var (client, _) = Bouw(handler);

        var antwoord = await client.ReadAsync(Scope, "2026-07", Vandaag);

        Assert.Equal(AzureCostAnswerKind.NotAvailable, antwoord.Kind);
        Assert.Single(handler.Verzoeken);
    }

    [Fact]
    public async Task EenVervolgpaginaWordtGevolgdEnDeRegelsWordenOpgeteld()
    {
        // Op de gemeten scope was nextLink altijd null. Dat het tóch wordt gevolgd is geen luxe: een
        // lezer die een pagina laat liggen heeft een subtotaal dat te laag is, en dat is even
        // onzichtbaar als de overgeslagen rij uit punt 33.
        var handler = new Vasteantwoorden();
        handler.Voeg(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(
                """
                {"properties":{"nextLink":"https://management.azure.com/pagina-2","columns":[
                  {"name":"Cost","type":"Number"},{"name":"UsageDate","type":"Number"},
                  {"name":"ServiceName","type":"String"},{"name":"Currency","type":"String"}],
                  "rows":[[1.0,20260701,"Azure App Service","EUR"]]}}
                """),
        });
        handler.Voeg(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(
                """
                {"properties":{"nextLink":null,"columns":[
                  {"name":"Cost","type":"Number"},{"name":"UsageDate","type":"Number"},
                  {"name":"ServiceName","type":"String"},{"name":"Currency","type":"String"}],
                  "rows":[[2.0,20260702,"Azure App Service","EUR"]]}}
                """),
        });

        var (client, klok) = Bouw(handler);

        var antwoord = await client.ReadAsync(Scope, "2026-07", Vandaag);

        Assert.Equal(AzureCostAnswerKind.Answered, antwoord.Kind);
        Assert.Equal(3.0m, Assert.Single(antwoord.Lines).Amount);
        Assert.Equal(2, antwoord.Days.Count);
        Assert.Equal("https://management.azure.com/pagina-2", handler.Verzoeken[1].Url);

        // Een vervolgpagina kost net zoveel budget als een eerste, dus dezelfde stilte ertussen.
        Assert.Contains(TimeSpan.FromSeconds(240), klok.Wachttijden);
    }

    [Fact]
    public async Task EenEindelozeReeksVervolgpaginasLevertGeenHalveSom()
    {
        // Het pad naar nextLink is niet gemeten — er is nooit een nextLink geweest om te volgen. De
        // grens is er zodat een verkeerde aanname geen eindeloze lus wordt die de emmer leegtrekt, en de
        // uitkomst is Unreadable en geen deelbedrag.
        var handler = new Vasteantwoorden { Eindeloos = true };

        var (client, _) = Bouw(handler);

        var antwoord = await client.ReadAsync(Scope, "2026-07", Vandaag);

        Assert.Equal(AzureCostAnswerKind.Unreadable, antwoord.Kind);
        Assert.Empty(antwoord.Lines);
        Assert.Equal(20, handler.Verzoeken.Count);
    }

    /// <summary>Het antwoord dat 21 augustus 2026 werkelijk terugkwam, verkort tot twee dagen.</summary>
    private static HttpResponseMessage Geslaagd() =>
        new(HttpStatusCode.OK)
        {
            Content = Json(
                """
                {"properties":{"nextLink":null,"columns":[
                  {"name":"Cost","type":"Number"},{"name":"UsageDate","type":"Number"},
                  {"name":"ServiceName","type":"String"},{"name":"Currency","type":"String"}],
                  "rows":[
                    [1.28180846072839,20260701,"Azure App Service","EUR"],
                    [0.00334322840021034,20260701,"Azure Cosmos DB","EUR"],
                    [1.21819153927161,20260702,"Azure App Service","EUR"],
                    [0.0,20260702,"Bandwidth","EUR"],
                    [0.0,20260702,"Microsoft Entra","EUR"]]}}
                """),
        };

    /// <summary>Een geweigerd verzoek, met de hints die gemeten zijn.</summary>
    private static HttpResponseMessage Geweigerd(
        HttpStatusCode status,
        string? entity = null,
        string? clientType = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = Json("""{"error":{"code":"429","message":"Too many requests. Please retry."}}"""),
        };

        if (entity is not null)
        {
            response.Headers.TryAddWithoutValidation(
                "x-ms-ratelimit-microsoft.costmanagement-entity-retry-after",
                entity);
        }

        if (clientType is not null)
        {
            response.Headers.TryAddWithoutValidation(
                "x-ms-ratelimit-microsoft.costmanagement-clienttype-retry-after",
                clientType);
        }

        // De header die in élke meting op 1099 stond, óók op de 429's. Hij staat hier om te bewijzen dat
        // hij niets doet: wie hierop plant, ziet nooit dat hij tegen de limiet aanloopt.
        response.Headers.TryAddWithoutValidation(
            "x-ms-ratelimit-remaining-subscription-resource-requests",
            "1099");

        return response;
    }

    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    private static AzureScope Ontleed(string pad) =>
        AzureScope.TryParse(pad, out var scope) && scope is not null
            ? scope
            : throw new InvalidOperationException($"'{pad}' is in deze test geen geldige scope.");

    private static (IAzureCostClient Client, Snelleklok Klok) Bouw(Vasteantwoorden handler)
    {
        var klok = new Snelleklok(new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero));

        var client = new AzureCostClient(
            new Vastefabriek(handler),
            new Vastebron(),
            Options.Create(new AzureCostOptions()),
            klok,
            NullLogger<AzureCostClient>.Instance);

        return (client, klok);
    }

    /// <summary>
    /// Een fabriek die altijd dezelfde handler oplevert.
    /// </summary>
    /// <remarks>
    /// De productiecode vraagt een <see cref="IHttpClientFactory"/> en geen <see cref="HttpClient"/>,
    /// omdat hij aan een achtergronddienst hangt die zolang het portaal draait blijft leven — een vaste
    /// <c>HttpClient</c> zou jaren dezelfde handler vasthouden en een DNS-wijziging van
    /// <c>management.azure.com</c> niet meer volgen. Deze fabriek geeft telkens een verse
    /// <c>HttpClient</c> op dezelfde handler, zodat de verzoeken van alle aanroepen in één lijst staan.
    /// </remarks>
    private sealed class Vastefabriek(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false);
    }

    /// <summary>Een tokenbron die niet met Entra praat.</summary>
    private sealed class Vastebron : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext context, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(context, cancellationToken));
    }

    /// <summary>Eén verzoek zoals het de deur uit ging.</summary>
    private sealed record Verzoek(HttpMethod Methode, string Url, string? Schema, string Body);

    /// <summary>Een Cost Management die antwoordt wat de test in de rij heeft gezet.</summary>
    private sealed class Vasteantwoorden : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _antwoorden = new();

        /// <summary>Elk verzoek dat is gedaan, in volgorde.</summary>
        public List<Verzoek> Verzoeken { get; } = [];

        /// <summary>Of elk antwoord naar een volgende pagina verwijst.</summary>
        public bool Eindeloos { get; init; }

        /// <summary>Zet een antwoord achter in de rij.</summary>
        public void Voeg(HttpResponseMessage antwoord) => _antwoorden.Enqueue(antwoord);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Verzoeken.Add(new Verzoek(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.Authorization?.Scheme,
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));

            if (Eindeloos)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = Json(
                        """{"properties":{"nextLink":"https://management.azure.com/volgende","columns":[],"rows":[]}}"""),
                };
            }

            return _antwoorden.Count > 0
                ? _antwoorden.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = Json("""{"error":"deze test had geen antwoord meer in de rij"}"""),
                };
        }
    }
}
