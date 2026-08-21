using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Soratus.Portal.Data;

/// <summary>
/// Wat er van één aanroep aan Cost Management is teruggekomen.
/// </summary>
/// <remarks>
/// <para><strong>Drie waarden, en het verschil tussen de laatste twee is de hele opgave.</strong> Een
/// antwoord dat we niet konden lezen is iets anders dan geen antwoord, en die twee vragen een
/// verschillende handeling: het eerste is een defect in ónze lezer en hoort zichtbaar te worden, het
/// tweede is een emmer die leeg is en hoort de vorige meting te laten staan. Zou er één waarde
/// "mislukt" zijn, dan zou een kapotte lezer maanden lang lijken op een klant met een druk netwerk.
/// </para>
/// </remarks>
public enum AzureCostAnswerKind
{
    /// <summary>
    /// Er is niets binnengekomen: een 429 waarvan de pogingen op zijn, de 404 uit §2, of een tijdslimiet.
    /// </summary>
    /// <remarks>
    /// <para><strong>De eerste waarde, zodat een niet-gezette uitkomst hier terechtkomt.</strong>
    /// Dezelfde keuze als bij <see cref="AzureCostState.Unknown"/> en
    /// <c>StatementSendState.Unknown</c>: de standaardwaarde van deze enum hoort de waarde te zijn
    /// waarop er niets wordt weggeschreven.</para>
    ///
    /// <para><strong>Hier volgt geen document uit.</strong> Zie <see cref="AzureCostCollector"/>: de
    /// lezing van gisteren blijft staan, met haar eigen tijdstip erbij. Dat is wat §32 als het
    /// eerlijkere antwoord aanwijst — het bewaarde getal is werkelijk gemeten, een mislukte aanroep
    /// heeft niets gemeten.</para>
    /// </remarks>
    NotAvailable,

    /// <summary>Cost Management heeft geantwoord en het antwoord is gelezen.</summary>
    /// <remarks>
    /// Ook bij nul regels. Dat is geen mislukking maar een meting met een eigen betekenis; zie
    /// <see cref="AzureCostState.NoLines"/>.
    /// </remarks>
    Answered,

    /// <summary>
    /// Cost Management heeft geantwoord en het antwoord was niet te lezen.
    /// </summary>
    /// <remarks>
    /// Een ontbrekende kolom, een rij met een ander aantal waarden, een bedrag dat geen getal is. §33
    /// wijst dit uitdrukkelijk aan als <see cref="AzureCostState.Unknown"/> en niet als een subtotaal
    /// met een regel minder: een bedrag dat te laag is ziet er net zo geloofwaardig uit als een bedrag
    /// dat klopt.
    /// </remarks>
    Unreadable,
}

/// <summary>
/// Het antwoord van één maandvraag aan Cost Management.
/// </summary>
/// <param name="Kind">Wat er is teruggekomen.</param>
/// <param name="Lines">De regels per dienst, opgeteld over de hele periode. Leeg tenzij <see cref="AzureCostAnswerKind.Answered"/>.</param>
/// <param name="Days">De dagen waarover er bedragen zijn.</param>
/// <param name="Currency">De valuta, of <c>null</c>.</param>
/// <param name="Reason">
/// Waarom er niets of niets leesbaars is, in gewone taal, of <c>null</c>. Zie
/// <see cref="AzureCostDocument.Failure"/>: dit komt op een operatorscherm en niet in een logregel.
/// </param>
/// <param name="Calls">Hoeveel keer er werkelijk een respons is opgehaald. Elke respons kost budget.</param>
public readonly record struct AzureCostAnswer(
    AzureCostAnswerKind Kind,
    IReadOnlyList<AzureCostLine> Lines,
    IReadOnlyList<DateOnly> Days,
    string? Currency,
    string? Reason,
    int Calls);

/// <summary>
/// Vraagt het Azure-verbruik van één scope over één maand op.
/// </summary>
/// <remarks>
/// Eén methode, en die neemt een <see cref="AzureScope"/> en geen tekenreeks. Dat is de grens: er is
/// geen aanroep waarmee een niet-gevalideerde scope de deur uit gaat.
/// </remarks>
public interface IAzureCostClient
{
    /// <summary>
    /// Leest het verbruik van één scope over één maand.
    /// </summary>
    /// <param name="scope">De scope. Gevalideerd, want dit type kan niet anders bestaan.</param>
    /// <param name="month">De maand als <c>jjjj-MM</c>.</param>
    /// <param name="observedOn">
    /// De dag waarop wordt gemeten, in UTC. Bepaalt tot welke dag er wordt gevraagd; zie de
    /// implementatie voor waarom dat gisteren is en niet vandaag.
    /// </param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het antwoord.</returns>
    Task<AzureCostAnswer> ReadAsync(
        AzureScope scope,
        string month,
        DateOnly observedOn,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// De enige implementatie: <c>POST …/providers/Microsoft.CostManagement/query</c>, met de backoff die
/// de metingen vragen.
/// </summary>
/// <remarks>
/// <para><strong>Eén vraagvorm, en het is de enige die is gemeten.</strong>
/// <c>type: ActualCost</c>, <c>timeframe: Custom</c> met een periode die volledig in het verleden
/// ligt, <c>granularity: Daily</c>, gegroepeerd op <c>ServiceName</c>. Gemeten op 21 augustus 2026
/// tegen <c>resourceGroups/MBV</c> over juli 2026: <c>HTTP 200</c>, 112 rijen, kolommen
/// <c>Cost, UsageDate, ServiceName, Currency</c>, <c>nextLink: null</c>.</para>
///
/// <para><strong>Waarom <c>Custom</c> en niet <c>MonthToDate</c>, en waarom de periode tot gisteren
/// loopt.</strong> <c>MonthToDate</c> werkt alleen voor de lopende maand, dus een afgesloten maand
/// vraagt hoe dan ook <c>Custom</c> — en dan is één vraagvorm beter dan twee, want de tweede is de
/// vorm die op de dag dat hij misgaat niet is gemeten. Een <c>to</c> in de toekomst is niet gemeten
/// en wordt daarom niet gebruikt: de periode loopt tot en met <em>gisteren</em>. Dat kost niets, want
/// de boeking van Cost Management loopt ongeveer acht uur achter (punt 31) en de run staat om 04:00
/// UTC — van vandaag is er op dat moment nog niets.</para>
///
/// <para><strong>En het levert een besparing op precies de dag waar punt 30 over gaat.</strong> Op de
/// 1e van de maand om 04:00 valt "gisteren" in de vorige maand, dus is de periode voor de nieuwe maand
/// leeg. Er wordt dan niet gevraagd, in plaats van een 200 met nul rijen op te halen die als
/// <see cref="AzureCostState.NoLines"/> zou worden weggeschreven. Niet vragen is hier eerlijker dan
/// vragen: "wij hebben niet gemeten" is iets anders dan "de API zei nul regels", en het scheelt een
/// aanroep uit een emmer die er geen over heeft.</para>
///
/// <para><strong>De backoff, en waarom hij niet op de hint alleen leunt.</strong> Gemeten waarden voor
/// <c>x-ms-ratelimit-microsoft.costmanagement-clienttype-retry-after</c> op deze scope: 1, 2, 3, 4, 8,
/// 12, 16, 17, 19, 22, 25, 26, 29, 34, 35. De 1, de 3, de 4 en de 12 waren aantoonbaar te kort — na
/// een wachttijd van 53 en van 165 seconden kwam er nog een 429. De hint wordt dus gelezen als hij er
/// is, samen met <c>entity-retry-after</c> die alleen verschijnt zodra de entiteitsteller op nul
/// staat; de grootste van de twee wint, en daaronder ligt
/// <see cref="AzureCostOptions.BackoffSeconds"/> als vloer.</para>
///
/// <para><strong>Vier headers die niet worden gebruikt, en dat is gemeten en niet vergeten.</strong>
/// <c>x-ms-ratelimit-remaining-subscription-resource-requests</c> stond in élke meting op 1099, ook op
/// een 429. De drie cost-management-tellers
/// (<c>entity-requests</c>, <c>tenant-requests</c>, <c>qpu-remaining</c>) staan niet op een 200 — een
/// geslaagd antwoord draagt géén ratelimietheader — en ze stonden op de 429's van 21 augustus ruim in
/// de plus terwijl het verzoek werd geweigerd. Er wordt dus niet op gepland en niet op bewaakt.</para>
///
/// <para><strong>Een laatste ding dat gemeten is en het ontwerp raakt: de emmer wordt gedeeld met
/// aanroepers die wij niet kennen.</strong> Tussen twee eigen metingen liep
/// <c>qpu QueriesPerHour</c> van 597 naar 595 terwijl er één eigen aanroep tussen zat, en na tien
/// minuten stilte kwam er alsnog een 429. De emmer hangt aan de aanroeper op tenantniveau, niet aan
/// deze klasse. Daaruit volgt de belangrijkste gedragsregel van deze lane: <strong>een 429 is geen
/// mislukte run</strong> — zie <see cref="AzureCostCollector"/>.</para>
/// </remarks>
internal sealed class AzureCostClient(
    IHttpClientFactory clients,
    TokenCredential credential,
    IOptions<AzureCostOptions> options,
    TimeProvider timeProvider,
    ILogger<AzureCostClient> logger) : IAzureCostClient
{
    /// <summary>
    /// De naam van de <see cref="HttpClient"/> in de fabriek.
    /// </summary>
    /// <remarks>
    /// <para><strong>Een fabriek en geen geïnjecteerde <see cref="HttpClient"/>, en dat is geen
    /// stijlkeuze.</strong> Deze klasse hangt aan <see cref="AzureCostCollector"/> en die is een
    /// achtergronddienst: hij leeft zolang het portaal draait. Een geïnjecteerde <c>HttpClient</c> zou
    /// daarmee jaren dezelfde handler vasthouden, en dan volgt hij een DNS-wijziging van
    /// <c>management.azure.com</c> niet meer. De fabriek roteert die handler zelf.</para>
    ///
    /// <para>Dezelfde afweging als bij <c>AcsStatementMailSender</c>, die zijn <c>EmailClient</c> per
    /// verzending maakt in plaats van hem vast te houden.</para>
    /// </remarks>
    internal const string HttpClientName = "azure-cost";

    /// <summary>De tokenscope van Azure Resource Manager.</summary>
    private static readonly string[] TokenScope = ["https://management.azure.com/.default"];

    /// <summary>De hint die verschijnt zodra de entiteitsteller op nul staat. De grootste van de twee.</summary>
    private const string EntityRetryAfter =
        "x-ms-ratelimit-microsoft.costmanagement-entity-retry-after";

    /// <summary>De hint die er meestal wel is, en die te lage waarden geeft.</summary>
    private const string ClientTypeRetryAfter =
        "x-ms-ratelimit-microsoft.costmanagement-clienttype-retry-after";

    /// <summary>
    /// Hoeveel pagina's er hoogstens worden opgehaald.
    /// </summary>
    /// <remarks>
    /// <para>Op de gemeten scope was <c>nextLink</c> altijd <c>null</c> — vijf diensten, met dagkorrel
    /// 112 rijen over een maand. Dat het vervolg tóch wordt gevolgd is geen luxe: een lezer die een
    /// pagina laat liggen heeft een subtotaal dat te laag is, en dat is even onzichtbaar als de
    /// overgeslagen rij uit punt 33.</para>
    ///
    /// <para><strong>Dat pad is niet gemeten.</strong> Er is nooit een <c>nextLink</c> geweest om te
    /// volgen, dus dat het een POST met dezelfde body naar dat adres is, komt uit de documentatie en
    /// niet uit een respons. De grens hieronder is er zodat een verkeerde aanname geen eindeloze lus
    /// wordt die de emmer leegtrekt: raakt hij op, dan is het antwoord
    /// <see cref="AzureCostAnswerKind.Unreadable"/> en geen halve som.</para>
    /// </remarks>
    private const int MaximumPages = 20;

    private readonly AzureCostOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<AzureCostAnswer> ReadAsync(
        AzureScope scope,
        string month,
        DateOnly observedOn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(month);

        var (first, last) = AzureCostCompleteness.Bounds(month);
        var yesterday = observedOn.AddDays(-1);
        var through = last <= yesterday ? last : yesterday;

        if (through < first)
        {
            // De maand is nog niet begonnen, of alleen vandaag valt erin en die is niet geboekt. Er
            // wordt niet gevraagd: zie de toelichting bij deze klasse — "wij hebben niet gemeten" is
            // iets anders dan "de API zei nul regels", en het scheelt een aanroep.
            return new AzureCostAnswer(
                AzureCostAnswerKind.NotAvailable,
                [],
                [],
                Currency: null,
                Reason: $"Van {month} is er nog geen dag geboekt om te bevragen.",
                Calls: 0);
        }

        var body = Body(first, through);
        var url = QueryUrl(scope);
        var lines = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var order = new List<string>();
        var days = new SortedSet<DateOnly>();
        string? currency = null;
        var calls = 0;

        for (var page = 0; page < MaximumPages; page++)
        {
            var (reading, failure, used) = await PageAsync(url, body, cancellationToken)
                .ConfigureAwait(false);

            calls += used;

            if (failure is not null)
            {
                return failure.Value with { Calls = calls };
            }

            foreach (var line in reading.Lines)
            {
                if (!lines.ContainsKey(line.Service))
                {
                    order.Add(line.Service);
                }

                lines[line.Service] = lines.TryGetValue(line.Service, out var running)
                    ? running + line.Amount
                    : line.Amount;
            }

            foreach (var day in reading.Days)
            {
                days.Add(day);
            }

            currency ??= reading.Currency;

            if (reading.NextLink is not { Length: > 0 } next)
            {
                return new AzureCostAnswer(
                    AzureCostAnswerKind.Answered,
                    [.. order.Select(name => new AzureCostLine { Service = name, Amount = lines[name] })],
                    [.. days],
                    currency,
                    Reason: null,
                    calls);
            }

            url = next;

            // Een vervolgpagina kost net zoveel budget als een eerste. Dezelfde stilte ertussen.
            await Task
                .Delay(_options.Pause, timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }

        // Meer pagina's dan er kunnen zijn. Dat is geen antwoord met een pagina minder maar een
        // antwoord dat we niet begrijpen — en dus geen bedrag.
        return new AzureCostAnswer(
            AzureCostAnswerKind.Unreadable,
            [],
            [],
            Currency: null,
            Reason: $"Cost Management bleef na {MaximumPages} pagina's naar een volgende verwijzen. "
                + "Het antwoord is daarmee niet af, en een deel ervan optellen zou een bedrag "
                + "opleveren dat te laag is.",
            calls);
    }

    /// <summary>
    /// Haalt één pagina op, met de pogingen en de backoff die de metingen vragen.
    /// </summary>
    /// <param name="url">Het adres van deze pagina.</param>
    /// <param name="body">De vraag.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De lezing, óf een antwoord dat zegt waarom er geen lezing is, plus het aantal responsen.</returns>
    private async Task<(AzureCostQueryReading Reading, AzureCostAnswer? Failure, int Calls)> PageAsync(
        string url,
        object body,
        CancellationToken cancellationToken)
    {
        var calls = 0;
        string? last = null;

        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            HttpResponseMessage response;

            try
            {
                var token = await credential
                    .GetTokenAsync(new TokenRequestContext(TokenScope), cancellationToken)
                    .ConfigureAwait(false);

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(body),
                };

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

                using var http = clients.CreateClient(HttpClientName);

                response = await http
                    .SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                // Een tijdslimiet of een verbinding die wegvalt. Er is geen respons, dus er is geen
                // budget verbruikt waarvan we het weten — en er is niets gemeten.
                last = "Cost Management was niet te bereiken.";
                logger.LogWarning(
                    exception,
                    "api.retry — poging {Attempt} van {Max} aan Cost Management is niet aangekomen.",
                    attempt,
                    _options.MaxAttempts);

                // Wachten hoort hier en niet aan het begin van de lus. Stond het daar, dan zou er ná een
                // geweigerd verzoek twee keer worden gewacht — één keer op de hint en één keer op de
                // vloer — en dan is de vloer niet meer te meten en de wachttijd het dubbele van wat er
                // staat. Gevonden met een mutatie: het weghalen van de vloer maakte niets rood, omdat
                // die tweede wachttijd dezelfde waarde had.
                if (attempt < _options.MaxAttempts)
                {
                    await Task
                        .Delay(_options.Backoff, timeProvider, cancellationToken)
                        .ConfigureAwait(false);
                }

                continue;
            }

            using (response)
            {
                calls++;

                if (response.IsSuccessStatusCode)
                {
                    var read = await ReadAsync(response, calls, cancellationToken)
                        .ConfigureAwait(false);

                    return (read.Reading, read.Failure, calls);
                }

                last = Refusal(response);

                // Een 429 én een 404 betekenen hier hetzelfde: probeer het nog eens. De 404 is de
                // gevaarlijkste bevinding van het onderzoek — GtmDimensionDataProvider…returns null,
                // tweemaal in ruim twintig aanroepen, op een verzoek dat er vlak ervoor 200 op gaf.
                // Een normale client rendert daar € 0,00 op, en op een factuur is € 0,00 geen lege
                // waarde maar een verkeerd bedrag.
                var retryable = response.StatusCode
                    is HttpStatusCode.TooManyRequests
                    or HttpStatusCode.NotFound
                    or HttpStatusCode.RequestTimeout
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout;

                logger.LogWarning(
                    "api.retry — Cost Management gaf {Status} op poging {Attempt} van {Max}. "
                    + "Wachthints: {Entity} / {ClientType}.",
                    (int)response.StatusCode,
                    attempt,
                    _options.MaxAttempts,
                    Hint(response, EntityRetryAfter),
                    Hint(response, ClientTypeRetryAfter));

                if (!retryable)
                {
                    break;
                }

                var wait = Wait(response);

                if (attempt < _options.MaxAttempts)
                {
                    await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return (
            default,
            new AzureCostAnswer(
                AzureCostAnswerKind.NotAvailable,
                [],
                [],
                Currency: null,
                last,
                calls),
            calls);
    }

    /// <summary>Leest het lichaam van een geslaagde respons.</summary>
    /// <param name="response">De respons.</param>
    /// <param name="calls">Hoeveel responsen er tot hier zijn opgehaald.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De lezing, of een antwoord dat zegt waarom hij niet te lezen was.</returns>
    /// <remarks>
    /// De uitzonderingen van <see cref="AzureCostQuery.Read"/> worden hier gevangen en worden
    /// <see cref="AzureCostAnswerKind.Unreadable"/>. Niet <see cref="AzureCostAnswerKind.NotAvailable"/>:
    /// er ís geantwoord, en dat een antwoord onleesbaar is hoort op het scherm te komen in plaats van de
    /// vorige meting te laten staan. Zie punt 33.
    /// </remarks>
    private static async Task<(AzureCostQueryReading Reading, AzureCostAnswer? Failure)> ReadAsync(
        HttpResponseMessage response,
        int calls,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content
                .ReadFromJsonAsync<AzureCostQueryResponse>(cancellationToken)
                .ConfigureAwait(false);

            return payload is null
                ? (default, Unreadable("Cost Management gaf een leeg antwoord.", calls))
                : (AzureCostQuery.Read(payload), null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return (default, Unreadable(exception.Message, calls));
        }
    }

    /// <summary>Een antwoord dat er wél was en niet te lezen viel.</summary>
    /// <param name="reason">Waarom.</param>
    /// <param name="calls">Hoeveel responsen er zijn opgehaald.</param>
    /// <returns>Het antwoord.</returns>
    private static AzureCostAnswer Unreadable(string reason, int calls) =>
        new(AzureCostAnswerKind.Unreadable, [], [], Currency: null, reason, calls);

    /// <summary>
    /// Hoe lang er na een geweigerd verzoek gewacht wordt.
    /// </summary>
    /// <param name="response">De respons met de hints.</param>
    /// <returns>De wachttijd.</returns>
    /// <remarks>
    /// De grootste van de twee hints, met <see cref="AzureCostOptions.BackoffSeconds"/> als vloer. Die
    /// vloer is niet netjesheid: gemeten waarden 1, 3, 4 en 12 waren te kort, en na 53 en na 165
    /// seconden stilte kwam er nog een 429.
    /// </remarks>
    private TimeSpan Wait(HttpResponseMessage response)
    {
        var hinted = Math.Max(Seconds(response, EntityRetryAfter), Seconds(response, ClientTypeRetryAfter));

        return hinted > _options.Backoff.TotalSeconds
            ? TimeSpan.FromSeconds(hinted)
            : _options.Backoff;
    }

    /// <summary>De waarde van een wachthint in seconden, of nul als hij er niet is.</summary>
    /// <param name="response">De respons.</param>
    /// <param name="header">De naam van de hint.</param>
    /// <returns>Het aantal seconden, of nul.</returns>
    private static double Seconds(HttpResponseMessage response, string header) =>
        Hint(response, header) is { } text
        && double.TryParse(text, CultureInfo.InvariantCulture, out var seconds)
        && seconds > 0
            ? seconds
            : 0;

    /// <summary>De ruwe waarde van een hintheader, of <c>null</c>.</summary>
    /// <param name="response">De respons.</param>
    /// <param name="header">De naam.</param>
    /// <returns>De waarde, of <c>null</c> — en die ontbreekt vaker dan hij er is.</returns>
    private static string? Hint(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values) ? values.FirstOrDefault() : null;

    /// <summary>De melding bij een geweigerd verzoek, in taal voor een operator.</summary>
    /// <param name="response">De respons.</param>
    /// <returns>De melding.</returns>
    /// <remarks>
    /// Geen statuscode en geen uitzonderingstekst; zie <see cref="AzureCostDocument.Failure"/>. De
    /// technische vorm staat in de logregel ernaast, met <c>api.retry</c> ervoor.
    /// </remarks>
    private static string Refusal(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.TooManyRequests => "Cost Management liet ons niet door.",
        HttpStatusCode.NotFound => "Cost Management zei tijdelijk dat deze omgeving niet bestaat.",
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized =>
            "Het portaal mag de kosten van deze omgeving niet lezen.",
        _ => "Cost Management gaf geen bruikbaar antwoord.",
    };

    /// <summary>Het adres van de query voor één scope.</summary>
    /// <param name="scope">De scope.</param>
    /// <returns>De volledige URL.</returns>
    private string QueryUrl(AzureScope scope) => string.Create(
        CultureInfo.InvariantCulture,
        $"{_options.ManagementEndpoint.TrimEnd('/')}{scope.Path}"
        + $"/providers/Microsoft.CostManagement/query?api-version={_options.ApiVersion}");

    /// <summary>
    /// De vraag, in de enige vorm die is gemeten.
    /// </summary>
    /// <param name="from">De eerste dag.</param>
    /// <param name="through">De laatste dag; volledig in het verleden.</param>
    /// <returns>Het lichaam van de POST.</returns>
    /// <remarks>
    /// <para><c>ActualCost</c> en niet <c>AmortizedCost</c>: dat tweede gaat pas iets betekenen als er
    /// reserveringen worden gekocht, en die zijn er niet.</para>
    ///
    /// <para><c>granularity: Daily</c> is geen extra detail maar de voorwaarde voor
    /// <see cref="AzureCostCompleteness"/>: zonder dagen is niet vast te stellen of de maand af is, en
    /// dan is het bedrag niet te factureren. Dat het antwoord daardoor een kolom
    /// <c>UsageDate</c> heeft en <c>ServiceName</c> opschuift, is precies de valkuil van punt 33 — en
    /// die wordt in <see cref="AzureCostQuery"/> op naam opgelost en niet op index.</para>
    /// </remarks>
    private static object Body(DateOnly from, DateOnly through) => new
    {
        type = "ActualCost",
        timeframe = "Custom",
        timePeriod = new
        {
            from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z",
            to = through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59Z",
        },
        dataset = new
        {
            granularity = "Daily",
            aggregation = new
            {
                totalCost = new { name = "Cost", function = "Sum" },
            },
            grouping = new[]
            {
                new { type = "Dimension", name = "ServiceName" },
            },
        },
    };
}
