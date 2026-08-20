using Soratus.Agents.Contracts;
using Soratus.Portal.Data;

namespace Soratus.Portal.Views;

/// <summary>
/// De feiten over de logweergave die niet van één agent afhangen.
/// </summary>
public static class AgentLogs
{
    /// <summary>
    /// Hoeveel de historische logweergave achterloopt op de werkelijkheid.
    /// </summary>
    /// <remarks>
    /// Dit is het getal achter de eerlijke tekst bij de live tail-schakelaar: "historische logs
    /// lopen ~1 min achter" (§3.3). Het staat hier als waarde en niet als zin, om dezelfde reden als
    /// bij <see cref="CustomerAgentRow.Silence"/>: de formulering is een presentatiekeuze, het getal
    /// een feit over de opslag.
    ///
    /// Het feit komt niet uit een meting maar uit de afspraak in het agentcontract: de
    /// telemetriebibliotheek schrijft in batches weg. Verandert dat interval, dan hoort dit getal
    /// mee te veranderen.
    /// </remarks>
    public static TimeSpan HistoricalLag => TimeSpan.FromMinutes(1);
}

/// <summary>
/// Het tabblad Logs zoals de klant het ziet (§3.3).
/// </summary>
/// <remarks>
/// <para><strong>Er is geen <c>extra</c> op dit type, en dat is de hele reden dat het bestaat.</strong>
/// §2 zegt dat een klant geen koppelingdetails en geen MCP- of DevOps-informatie mag zien, en
/// <c>extra</c> is vrije JSON die de agentbouwer vult. In de huidige telemetrie staat daar bij
/// gewone klant-agents onder meer <c>endpoint</c> (<c>GET /v1.0/me/messages/delta</c>),
/// <c>scope</c> (<c>Mail.ReadWrite</c>), <c>model</c> (<c>gpt-4.1</c>), <c>containerState</c>,
/// <c>replicas</c> en stacktraces met onze bronpaden. Filteren op sleutelnamen sluit dat niet af —
/// wie zijn sleutel morgen <c>svcEndpoint</c> noemt is er langs — dus het veld is er niet. Wat er
/// niet is kan niet lekken, ook niet via een tooltip, een <c>title</c> of de geserialiseerde
/// parameters van een interactief eiland. Zie <c>docs/agent-portal/fase-0-afwijkingen.md</c> §12.
/// </para>
///
/// <para>Gevolg voor het scherm: de klantvariant van de logtabel heeft geen uitklap. Er is niets om
/// uit te klappen, dus er hoort ook geen chevron te staan die niets doet.</para>
/// </remarks>
public sealed record CustomerAgentLogsView
{
    /// <summary>De technische naam van de agent.</summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// Het moment waarop deze weergave is opgebouwd. Dit is ook de bovengrens van de query: de
    /// lijst en <see cref="Counts"/> kijken naar precies dezelfde verzameling regels.
    /// </summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>De regels, nieuwste eerst.</summary>
    public required IReadOnlyList<CustomerLogLine> Lines { get; init; }

    /// <summary>
    /// Hoeveel regels er per niveau zijn, voor de filterchips.
    /// </summary>
    /// <inheritdoc cref="OperatorAgentLogsView.Counts" path="/remarks"/>
    public required IReadOnlyDictionary<LogLevel, int> Counts { get; init; }

    /// <summary>Welke niveaus aan staan, of <c>null</c> als dat alle drie zijn.</summary>
    public IReadOnlySet<LogLevel>? ActiveLevels { get; init; }

    /// <summary>De zoekterm die is toegepast, of <c>null</c>.</summary>
    public string? Search { get; init; }

    /// <summary>De run waarop is gefilterd, of <c>null</c>.</summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Het vervolgtoken voor de volgende (oudere) pagina, of <c>null</c>. Niet geschikt voor een URL.
    /// </summary>
    public string? ContinuationToken { get; init; }

    /// <summary>
    /// Waar de live tail begint. Nooit <c>null</c>: bij een agent zonder logregels is dit
    /// <see cref="GeneratedAt"/>.
    /// </summary>
    public required LogCursor TailFrom { get; init; }

    /// <summary>Of er nog oudere regels zijn.</summary>
    public bool HasMore => ContinuationToken is not null;

    /// <summary>Of er binnen dit filter niets te zien is.</summary>
    public bool IsEmpty => Lines.Count == 0;

    /// <summary>Of er een filter aan staat.</summary>
    public bool IsFiltered => LogFilterState.IsFiltered(ActiveLevels, Search, RunId);

    /// <inheritdoc cref="AgentLogs.HistoricalLag"/>
    public static TimeSpan HistoricalLag => AgentLogs.HistoricalLag;
}

/// <summary>
/// Het tabblad Logs zoals de operator het ziet (§3.3).
/// </summary>
/// <remarks>
/// <para>Dit is de variant met <c>extra</c>: <see cref="Lines"/> bevat het volledige
/// <see cref="LogRecord"/> uit het contract, want de uitklap ís de onderliggende JSON van het
/// document. Het blijft een <c>JsonElement</c> zodat de tekst pas bij het openen wordt opgemaakt —
/// bij honderden regels met een stacktrace per stuk is vooraf formatteren megabytes die niemand
/// leest.</para>
///
/// <para>Een apart type en niet een vlag op het klanttype, om dezelfde reden als bij
/// <see cref="OperatorCustomerAgentsView"/>: het verschil tussen de rollen is een verschil tussen
/// typen. Een gedeeld type met een veld dat de ene rol wel en de andere niet mag zien is precies wat
/// we vermijden — dat veld reist mee over de serialisatiegrens van een interactief eiland en staat
/// dan in de paginabron, ongeacht welke <c>@if</c> eromheen staat.</para>
/// </remarks>
public sealed record OperatorAgentLogsView
{
    /// <summary>De technische naam van de agent.</summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// Het moment waarop deze weergave is opgebouwd, en de bovengrens van beide query's.
    /// </summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>De regels, nieuwste eerst, met hun volledige context.</summary>
    public required IReadOnlyList<LogRecord> Lines { get; init; }

    /// <summary>
    /// Hoeveel regels er per niveau zijn, voor de filterchips.
    /// </summary>
    /// <remarks>
    /// De telling is gedaan met dezelfde zoekterm, dezelfde runId en dezelfde bovengrens als de
    /// lijst, maar zónder het niveaufilter. Daarmee klopt de chip met wat het filter oplevert: staat
    /// er "error 3", dan geeft het aanzetten van die chip drie regels. Zou het niveaufilter meedoen,
    /// dan telde elke chip alleen zichzelf en stond er bij de uitgezette niveaus altijd nul.
    ///
    /// Het is een telling over alle bewaarde regels binnen het filter en niet over deze pagina. Bij
    /// meer regels dan er op een pagina passen staat er dus een hoger getal op de chip dan je kunt
    /// zien; dat is geen tegenspraak maar het verschil tussen "hoeveel er zijn" en "hoeveel er nu in
    /// beeld staan".
    /// </remarks>
    public required IReadOnlyDictionary<LogLevel, int> Counts { get; init; }

    /// <summary>Welke niveaus aan staan, of <c>null</c> als dat alle drie zijn.</summary>
    public IReadOnlySet<LogLevel>? ActiveLevels { get; init; }

    /// <summary>De zoekterm die is toegepast, of <c>null</c>.</summary>
    public string? Search { get; init; }

    /// <summary>De run waarop is gefilterd, of <c>null</c>.</summary>
    public string? RunId { get; init; }

    /// <summary>Het vervolgtoken voor de volgende (oudere) pagina, of <c>null</c>.</summary>
    public string? ContinuationToken { get; init; }

    /// <summary>Waar de live tail begint. Nooit <c>null</c>.</summary>
    public required LogCursor TailFrom { get; init; }

    /// <summary>Of er nog oudere regels zijn.</summary>
    public bool HasMore => ContinuationToken is not null;

    /// <summary>Of er binnen dit filter niets te zien is.</summary>
    public bool IsEmpty => Lines.Count == 0;

    /// <summary>Of er een filter aan staat.</summary>
    public bool IsFiltered => LogFilterState.IsFiltered(ActiveLevels, Search, RunId);

    /// <inheritdoc cref="AgentLogs.HistoricalLag"/>
    public static TimeSpan HistoricalLag => AgentLogs.HistoricalLag;
}

/// <summary>
/// Eén logregel zoals de klant hem ziet: tijd, niveau, event, bericht en runId. Niets meer.
/// </summary>
/// <remarks>
/// <para>Precies de vijf kolommen van de tabel in §3.3, en bewust geen zesde veld. Er is geen
/// <c>extra</c>, geen <c>customerId</c> en geen <c>agentName</c>: de eerste is operator-only (zie
/// <see cref="CustomerAgentLogsView"/>), de andere twee weet het scherm al uit de URL en zouden
/// alleen ruimte innemen in de parameters die over de serialisatiegrens gaan.</para>
///
/// <para><strong>Let op wat dit type niet beschermt.</strong> <see cref="Message"/> is vrije tekst
/// die een agentbouwer schrijft, en die is klantleesbaar. Staat daar een interne naam of een pad in,
/// dan lekt dat alsnog — en daar is geen enkele filter tegen te bouwen aan deze kant. Dat is een
/// afspraak in het agentcontract en de enige bescherming die daar bestaat; zie
/// <c>docs/agent-portal/agent-contract.md</c>.</para>
/// </remarks>
public sealed record CustomerLogLine
{
    /// <summary>De ULID van de regel. Nodig om de rij bij te houden als er bovenaan iets bij komt.</summary>
    public required string Id { get; init; }

    /// <summary>Wanneer de regel is geschreven.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Info, warn of error.</summary>
    public required LogLevel Level { get; init; }

    /// <summary>De gebeurtenisnaam, bijvoorbeeld <c>validation.failed</c>.</summary>
    public required string Event { get; init; }

    /// <summary>Eén zin, leesbaar voor wie de code niet kent.</summary>
    public required string Message { get; init; }

    /// <summary>De run waarbinnen deze regel viel, of <c>null</c> voor regels buiten een run.</summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Projecteert een logregel naar de klantvariant.
    /// </summary>
    /// <param name="record">De logregel.</param>
    /// <returns>De vijf velden die de klant mag zien.</returns>
    /// <remarks>
    /// Een expliciete projectie en geen automatische mapping. Komt er morgen een veld bij op
    /// <see cref="LogRecord"/>, dan komt het hier niet stilzwijgend mee: iemand moet er een regel
    /// voor schrijven, en dat is precies het moment waarop de vraag "mag de klant dit zien" hoort te
    /// vallen.
    /// </remarks>
    internal static CustomerLogLine From(LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new CustomerLogLine
        {
            Id = record.Id,
            Timestamp = record.Timestamp,
            Level = record.Level,
            Event = record.Event,

            // Ook hier knippen, en niet alleen bij het wegschrijven. Zie CustomerMessage: dit is de
            // laatste plek voordat de tekst de HTML in gaat, en hij dekt de documenten die er al
            // staan, een agent op een oudere bibliotheekversie en een agent die de bibliotheek niet
            // gebruikt. Het scherm kan dit niet overnemen: de ellipsis in de cel is beeld, de
            // volledige tekst staat in de paginabron.
            Message = CustomerMessage.FirstLine(record.Message),
            RunId = record.RunId,
        };
    }
}

/// <summary>
/// Wat de live tail eraan vond, in de klantvariant.
/// </summary>
/// <param name="Lines">De nieuwe regels, oudste eerst.</param>
/// <param name="Cursor">Waar de volgende tik verder gaat. Nooit <c>null</c>.</param>
/// <param name="HasMore">Of er meer klaarstond dan er in deze tik paste.</param>
/// <param name="Counts">De bijgewerkte tellingen voor de filterchips.</param>
/// <inheritdoc cref="OperatorAgentLogTail" path="/remarks"/>
public sealed record CustomerAgentLogTail(
    IReadOnlyList<CustomerLogLine> Lines,
    LogCursor Cursor,
    bool HasMore,
    IReadOnlyDictionary<LogLevel, int> Counts)
{
    /// <summary>Of er iets bij is gekomen.</summary>
    public bool IsEmpty => Lines.Count == 0;
}

/// <summary>
/// Wat de live tail eraan vond, in de operatorvariant.
/// </summary>
/// <param name="Lines">De nieuwe regels, oudste eerst.</param>
/// <param name="Cursor">Waar de volgende tik verder gaat. Nooit <c>null</c>.</param>
/// <param name="HasMore">Of er meer klaarstond dan er in deze tik paste.</param>
/// <param name="Counts">De bijgewerkte tellingen voor de filterchips.</param>
/// <remarks>
/// <para>Oudste eerst: de tail schuift de cursor aaneengesloten door en slaat dus niets over. Het
/// scherm zet de regels in omgekeerde volgorde bovenaan de tabel.</para>
///
/// <para><see cref="Cursor"/> is nooit <c>null</c>, ook niet bij een leeg antwoord — dan komt de
/// meegegeven cursor er onveranderd uit. Anders moet elke aanroeper een <c>?? vorige</c> schrijven,
/// en die wordt precies één keer vergeten, waarna de tail vanaf het begin herleest.</para>
///
/// <para><see cref="HasMore"/> betekent: er stond meer klaar dan er in deze tik paste. Vraag dan
/// direct opnieuw in plaats van het interval af te wachten.</para>
///
/// <para>De tellingen komen mee, en dat is geen luxe: zonder dat blijft de chip "error 3" staan
/// terwijl de tail een vierde foutregel in de tabel schuift — twee getallen op hetzelfde scherm die
/// elkaar tegenspreken. Ze zijn begrensd op <see cref="Cursor"/>, dus op precies wat er na deze tik
/// in de tabel staat.</para>
/// </remarks>
public sealed record OperatorAgentLogTail(
    IReadOnlyList<LogRecord> Lines,
    LogCursor Cursor,
    bool HasMore,
    IReadOnlyDictionary<LogLevel, int> Counts)
{
    /// <summary>Of er iets bij is gekomen.</summary>
    public bool IsEmpty => Lines.Count == 0;
}

/// <summary>
/// Of er op de logweergave een filter aan staat.
/// </summary>
/// <remarks>
/// Eén regel op één plek, gedeeld door de klant- en de operatorweergave. Bepaalt of de lege staat
/// "deze agent heeft nog niets gelogd" hoort te zeggen of "geen regels die hieraan voldoen" — en dat
/// hoort voor beide rollen hetzelfde te zijn.
/// </remarks>
internal static class LogFilterState
{
    internal static bool IsFiltered(
        IReadOnlySet<LogLevel>? activeLevels,
        string? search,
        string? runId) =>
        (activeLevels is { Count: > 0 } && activeLevels.Count < Enum.GetValues<LogLevel>().Length)
        || !string.IsNullOrWhiteSpace(search)
        || !string.IsNullOrWhiteSpace(runId);
}
