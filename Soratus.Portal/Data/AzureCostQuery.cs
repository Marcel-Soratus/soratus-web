using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soratus.Portal.Data;

/// <summary>
/// Eén kolom in het antwoord van <c>Microsoft.CostManagement/query</c>.
/// </summary>
/// <param name="Name">De naam, bijvoorbeeld <c>Cost</c> of <c>UsageDate</c>.</param>
/// <param name="Type">Het type zoals de API het noemt, bijvoorbeeld <c>Number</c>.</param>
public sealed record AzureCostQueryColumn(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type);

/// <summary>Het <c>properties</c>-blok van het antwoord.</summary>
public sealed record AzureCostQueryProperties
{
    /// <summary>De kolommen, in de volgorde waarin de rijen ze bevatten.</summary>
    [JsonPropertyName("columns")]
    public IReadOnlyList<AzureCostQueryColumn> Columns { get; init; } = [];

    /// <summary>De rijen. Elke rij heeft evenveel waarden als er kolommen zijn.</summary>
    [JsonPropertyName("rows")]
    public IReadOnlyList<IReadOnlyList<JsonElement>> Rows { get; init; } = [];

    /// <summary>De vervolgpagina, of <c>null</c> als dit alles was.</summary>
    [JsonPropertyName("nextLink")]
    public string? NextLink { get; init; }
}

/// <summary>Het antwoord van <c>Microsoft.CostManagement/query</c>.</summary>
public sealed record AzureCostQueryResponse
{
    /// <summary>De gegevens.</summary>
    [JsonPropertyName("properties")]
    public AzureCostQueryProperties Properties { get; init; } = new();
}

/// <summary>
/// Wat er uit één antwoord van Cost Management te halen valt.
/// </summary>
/// <param name="Lines">
/// De diensten met hun bedragen, opgeteld per dienst. Leeg als het antwoord geen rijen had.
/// </param>
/// <param name="Days">
/// De dagen waarover er bedragen zijn, oplopend en zonder dubbele. Leeg bij een antwoord zonder
/// dagkorrel of zonder rijen.
/// </param>
/// <param name="Currency">De valuta, of <c>null</c> als er geen rijen waren.</param>
/// <param name="NextLink">De vervolgpagina, of <c>null</c>.</param>
public readonly record struct AzureCostQueryReading(
    IReadOnlyList<AzureCostLine> Lines,
    IReadOnlyList<DateOnly> Days,
    string? Currency,
    string? NextLink);

/// <summary>
/// Leest het antwoord van <c>Microsoft.CostManagement/query</c> uit. De enige plek waar dat gebeurt.
/// </summary>
/// <remarks>
/// <para><strong>Deze klasse heeft nog geen aanroeper in productie, en dat is met opzet.</strong> De
/// beheeragent <c>kosten-collector</c> (§4) bestaat nog niet. Dit is dezelfde afweging als bij
/// <see cref="HourEntryKeys.ForIntegration"/>, waar de sleutelregel er ook eerder was dan de
/// koppeling die hem gebruikt: de grens waarop een API onze getallen wordt, is de plek waar
/// € 0,00 wordt uitgevonden, en die grens hoort te bestaan vóór er iets over heen gaat. Wie hem
/// later toevoegt, voegt hem toe nadat de eerste factuur eruit is.</para>
///
/// <para><strong>De kolomvolgorde wordt uit <c>columns</c> gelezen en nooit aangenomen, en dat is een
/// gemeten valkuil.</strong> Twee aanroepen tegen dezelfde scope op 21 augustus 2026:</para>
///
/// <code>
/// granularity: None    → Cost, ServiceName, Currency
/// granularity: Daily   → Cost, UsageDate, ServiceName, Currency
/// </code>
///
/// <para>De kolom <c>ServiceName</c> staat dus op index 1 of op index 2, afhankelijk van de vraag.
/// Een lezer met vaste indices haalt bij de tweede vorm de dienstnaam uit de datumkolom — en dan
/// staat er een dienst <c>20260801</c> met het bedrag van één dag in de uitsplitsing, wat er als een
/// dienst uitziet die we niet kenden. Dat is geen crash maar een verkeerd bedrag per dienst, en het
/// valt alleen op als iemand het subtotaal natelt.</para>
///
/// <para><strong>Een onleesbaar bedrag werpt en wordt geen nul.</strong> Dat is de hele reden dat deze
/// klasse bestaat. De aanroeper hoort de uitzondering te vangen en er
/// <see cref="AzureCostState.Unknown"/> van te maken — nooit een bedrag. Zie de opmerking bij
/// <see cref="Read"/>.</para>
/// </remarks>
public static class AzureCostQuery
{
    /// <summary>De kolom met het bedrag.</summary>
    private const string CostColumn = "Cost";

    /// <summary>De kolom met de dienstnaam.</summary>
    private const string ServiceColumn = "ServiceName";

    /// <summary>De kolom met de dag, alleen bij <c>granularity: Daily</c>.</summary>
    private const string DateColumn = "UsageDate";

    /// <summary>De kolom met de valuta.</summary>
    private const string CurrencyColumn = "Currency";

    /// <summary>
    /// Leest een antwoord uit tot regels per dienst en de dagen die erin voorkomen.
    /// </summary>
    /// <param name="response">Het antwoord, zoals het uit de API komt.</param>
    /// <returns>De regels, de dagen, de valuta en de vervolgpagina.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Het antwoord heeft geen <c>Cost</c>- of <c>ServiceName</c>-kolom, een rij heeft niet evenveel
    /// waarden als er kolommen zijn, of een bedrag of dag is niet te lezen.
    /// </exception>
    /// <remarks>
    /// <para><strong>Elk van die uitzonderingen betekent hetzelfde voor de aanroeper: we weten de
    /// kosten niet.</strong> Ze horen dus tot <see cref="AzureCostState.Unknown"/> te leiden en niet
    /// tot een bedrag met een ontbrekende regel erin. Een <c>catch</c> die de rij overslaat en
    /// doorgaat, levert een subtotaal op dat lager is dan de werkelijkheid — en dat is precies het
    /// soort fout dat een factuur haalt zonder dat iemand het ziet, want een bedrag dat te laag is
    /// ziet er net zo geloofwaardig uit als een bedrag dat klopt.</para>
    ///
    /// <para><strong>Nul rijen levert nul regels op en geen bedrag van nul.</strong> Gemeten: zowel
    /// een resource group die niet bestaat als een bestaande resource group over een periode die nog
    /// niet is geboekt geven <c>HTTP 200</c> met <c>"rows": []</c>. Wat dat betekent staat bij
    /// <see cref="AzureCostState.NoLines"/>; wat deze methode ermee doet is het doorgeven zonder er
    /// een getal van te maken.</para>
    ///
    /// <para><strong><see cref="AzureCostQueryReading.NextLink"/> wordt teruggegeven en niet
    /// weggegooid.</strong> Op de gemeten scope was hij altijd <c>null</c> — vijf diensten, en met
    /// dagkorrel vijfenzestig rijen — maar een grotere klant kan pagineren, en een lezer die de
    /// vervolgpagina niet ophaalt heeft een subtotaal dat te laag is. Dat is dezelfde fout als de
    /// overgeslagen rij hierboven en hij is even onzichtbaar. De aanroeper hoort te herhalen zolang
    /// deze waarde niet <c>null</c> is, en de regels van alle pagina's op te tellen.</para>
    /// </remarks>
    public static AzureCostQueryReading Read(AzureCostQueryResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var columns = response.Properties.Columns;
        var rows = response.Properties.Rows;

        if (rows.Count == 0)
        {
            return new AzureCostQueryReading([], [], Currency: null, response.Properties.NextLink);
        }

        var cost = IndexOf(columns, CostColumn, required: true);
        var service = IndexOf(columns, ServiceColumn, required: true);
        var date = IndexOf(columns, DateColumn, required: false);
        var currency = IndexOf(columns, CurrencyColumn, required: false);

        // Opgeteld per dienst, want met dagkorrel staat elke dienst er één keer per dag in. Ordinaal:
        // "Key Vault" en "key vault" zouden twee diensten zijn, maar dat komt niet van ons — de naam
        // komt uit de API, en twee schrijfwijzen samenvoegen zou betekenen dat we een naam kiezen.
        var perService = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var order = new List<string>();
        var days = new SortedSet<DateOnly>();
        string? seenCurrency = null;

        foreach (var row in rows)
        {
            if (row.Count != columns.Count)
            {
                throw new InvalidOperationException(
                    $"Een rij in het antwoord van Cost Management heeft {row.Count} waarden terwijl "
                    + $"er {columns.Count} kolommen zijn. Het antwoord is daarmee niet te lezen, en "
                    + "een deel ervan lezen zou een subtotaal opleveren dat te laag is.");
            }

            var name = Text(row[service], ServiceColumn);
            var amount = Amount(row[cost]);

            if (!perService.ContainsKey(name))
            {
                order.Add(name);
            }

            perService[name] = perService.TryGetValue(name, out var running) ? running + amount : amount;

            if (date >= 0)
            {
                days.Add(Day(row[date]));
            }

            seenCurrency ??= currency >= 0 ? Text(row[currency], CurrencyColumn) : null;
        }

        return new AzureCostQueryReading(
            [.. order.Select(name => new AzureCostLine { Service = name, Amount = perService[name] })],
            [.. days],
            seenCurrency,
            response.Properties.NextLink);
    }

    /// <summary>
    /// De index van een kolom op naam.
    /// </summary>
    /// <param name="columns">De kolommen uit het antwoord.</param>
    /// <param name="name">De naam.</param>
    /// <param name="required">Of het antwoord zonder deze kolom onbruikbaar is.</param>
    /// <returns>De index, of <c>-1</c> als de kolom er niet is en niet verplicht is.</returns>
    /// <remarks>
    /// Hoofdletterongevoelig. De gemeten namen zijn <c>Cost</c>, <c>UsageDate</c>,
    /// <c>ServiceName</c> en <c>Currency</c>, maar dit is een naam uit een API-antwoord en niet een
    /// sleutel die wij zetten: een schrijfwijze die verschuift hoort hier niet stil nul regels op te
    /// leveren.
    /// </remarks>
    private static int IndexOf(IReadOnlyList<AzureCostQueryColumn> columns, string name, bool required)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            if (string.Equals(columns[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        if (required)
        {
            throw new InvalidOperationException(
                $"Het antwoord van Cost Management heeft geen kolom '{name}'. Aanwezig: "
                + $"{string.Join(", ", columns.Select(column => column.Name))}. Zonder die kolom is "
                + "er geen bedrag of geen dienst, en dan is het antwoord niet te lezen.");
        }

        return -1;
    }

    /// <summary>Een bedrag uit een rij.</summary>
    /// <param name="value">De waarde.</param>
    /// <returns>Het bedrag.</returns>
    /// <remarks>
    /// De echte waarden staan in wetenschappelijke notatie zodra ze klein zijn
    /// (<c>1.0543425734745e-05</c> voor een dag Key Vault) en met vijftien cijfers achter de komma
    /// zodra ze groot zijn. <see cref="JsonElement.TryGetDecimal"/> leest beide; wat hij niet leest is
    /// een getal buiten het bereik van <see cref="decimal"/>, en dat is geen bedrag maar een defect.
    /// </remarks>
    private static decimal Amount(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var amount))
        {
            return amount;
        }

        throw new InvalidOperationException(
            $"Een bedrag in het antwoord van Cost Management is niet als getal te lezen: "
            + $"'{value}' ({value.ValueKind}). Dit wordt met opzet geen nul: nul is een bedrag en "
            + "dit is de afwezigheid van een bedrag.");
    }

    /// <summary>De dag uit de kolom <c>UsageDate</c>.</summary>
    /// <param name="value">De waarde, als getal in de vorm <c>20260801</c>.</param>
    /// <returns>De dag.</returns>
    /// <remarks>
    /// Een getal en geen tekst, gemeten: de kolom heeft type <c>Number</c> en de waarde is
    /// <c>20260801</c>. Er wordt via tekst geparseerd en niet met rekenkunde op dat getal, want
    /// <c>jaar = n / 10000</c> is precies het soort omrekening dat op een ongeldige waarde stil een
    /// bestaande datum oplevert.
    /// </remarks>
    private static DateOnly Day(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var packed)
            && DateOnly.TryParseExact(
                packed.ToString("D8", CultureInfo.InvariantCulture),
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var day))
        {
            return day;
        }

        throw new InvalidOperationException(
            $"De kolom '{DateColumn}' in het antwoord van Cost Management bevat '{value}' en dat is "
            + "geen dag in de vorm jjjjmmdd. Zonder de dagen is de volledigheid van de maand niet "
            + "vast te stellen, en dan is het bedrag niet te factureren.");
    }

    /// <summary>Een tekstwaarde uit een rij.</summary>
    /// <param name="value">De waarde.</param>
    /// <param name="column">De kolomnaam, voor de melding.</param>
    /// <returns>De tekst.</returns>
    private static string Text(JsonElement value, string column) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : throw new InvalidOperationException(
                $"De kolom '{column}' in het antwoord van Cost Management bevat '{value}' "
                + $"({value.ValueKind}) en geen tekst. Het antwoord is daarmee niet te lezen.");
}
