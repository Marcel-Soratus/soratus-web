using Soratus.Agents.Contracts;

// Zowel het contract als Microsoft.Extensions.Logging kent een LogLevel. In het portaal bedoelen we
// altijd die van het contract: drie niveaus, geschreven voor een operator die wil weten of er iets
// mis is. Het alias staat er zodat dat nergens per ongeluk verwisseld raakt.
using LogLevel = Soratus.Agents.Contracts.LogLevel;

namespace Soratus.Portal.Data;

/// <summary>
/// Eén pagina runs, nieuwste eerst.
/// </summary>
/// <param name="Runs">De runs op deze pagina.</param>
/// <param name="ContinuationToken">
/// De sleutel voor de volgende pagina, of <c>null</c> als dit de laatste was.
/// </param>
/// <remarks>
/// Het vervolgtoken is dat van Cosmos zelf en wordt niet geïnterpreteerd. Zet het niet in een URL:
/// het is lang, ondoorzichtig en bevat de interne queryvorm.
/// </remarks>
public sealed record RunPage(IReadOnlyList<RunRecord> Runs, string? ContinuationToken)
{
    /// <summary>Een lege pagina.</summary>
    public static RunPage Empty { get; } = new([], null);

    /// <summary>Of er nog een pagina achteraan komt.</summary>
    public bool HasMore => ContinuationToken is not null;
}

/// <summary>
/// Eén pagina logregels, nieuwste eerst.
/// </summary>
/// <param name="Lines">De logregels op deze pagina.</param>
/// <param name="ContinuationToken">
/// De sleutel voor de volgende (oudere) pagina, of <c>null</c> als dit de laatste was.
/// </param>
/// <param name="Newest">
/// De cursor die bij de nieuwste regel op deze pagina hoort, of <c>null</c> als de pagina leeg is.
/// Geef die terug in <see cref="LogQuery.Since"/> om alleen de regels te halen die er daarna bij
/// zijn gekomen.
/// </param>
public sealed record LogPage(
    IReadOnlyList<LogRecord> Lines,
    string? ContinuationToken,
    LogCursor? Newest)
{
    /// <summary>Een lege pagina.</summary>
    public static LogPage Empty { get; } = new([], null, null);

    /// <summary>Of er nog oudere regels zijn.</summary>
    public bool HasMore => ContinuationToken is not null;
}

/// <summary>
/// De plek in de logstroom waar de lezer is gebleven.
/// </summary>
/// <param name="Timestamp">Het tijdstip van de nieuwste al geziene regel.</param>
/// <param name="Id">De ULID van die regel.</param>
/// <remarks>
/// Twee velden en niet één, omdat twee regels dezelfde tijdstempel kunnen hebben. Met alleen een
/// tijdstempel moet je kiezen tussen een regel overslaan (<c>&gt;</c>) en er eentje dubbel tonen
/// (<c>&gt;=</c>); met de ULID erbij hoeft dat niet. De ULID loopt op in de tijd, dus binnen
/// dezelfde tijdstempel is de volgorde eenduidig.
/// </remarks>
public readonly record struct LogCursor(DateTimeOffset Timestamp, string Id);

/// <summary>
/// Wat de logweergave wil zien: welk niveau, welke zoekterm, en vanaf waar.
/// </summary>
/// <remarks>
/// Eén type in plaats van vijf parameters, zodat de live tail en de gewone weergave dezelfde
/// query gebruiken en niet uit elkaar kunnen lopen.
/// </remarks>
public sealed record LogQuery
{
    /// <summary>
    /// De niveaus die de lezer aan heeft staan. <c>null</c> of leeg betekent alle niveaus.
    /// </summary>
    public IReadOnlyCollection<LogLevel>? Levels { get; init; }

    /// <summary>
    /// Vrije zoekterm over event, bericht en runId. Hoofdletterongevoelig.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>
    /// Alleen regels die ná deze cursor zijn geschreven. Dit is de live tail.
    /// </summary>
    /// <remarks>
    /// Is deze gezet, dan komt de nieuwste regel weer bovenaan te staan en is
    /// <see cref="LogPage.ContinuationToken"/> niet interessant: de tail haalt het staartje op,
    /// niet de historie.
    /// </remarks>
    public LogCursor? Since { get; init; }

    /// <summary>
    /// Alleen regels van deze run. Gebruikt vanaf het rundetail.
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>Hoeveel regels deze pagina maximaal bevat.</summary>
    public int? PageSize { get; init; }

    /// <summary>Het vervolgtoken van de vorige pagina.</summary>
    public string? ContinuationToken { get; init; }
}

/// <summary>
/// Hoeveel runs er in een tijdvenster waren, uitgesplitst naar afloop.
/// </summary>
/// <param name="Ok">Geslaagde runs.</param>
/// <param name="Failed">Mislukte runs.</param>
/// <param name="Skipped">Runs die niets te doen hadden. Geen fout.</param>
/// <param name="Running">Runs die op dit moment nog lopen.</param>
/// <remarks>
/// Uitgesplitst en niet als totaal-plus-mislukt, zodat het scherm zelf kan bepalen wat het
/// noemer maakt. Het foutpercentage rekent met <see cref="Completed"/> en niet met
/// <see cref="Total"/>: een lopende run is nog niets, en die meetellen zou het percentage laten
/// dalen zodra er werk begint.
/// </remarks>
public readonly record struct RunTally(int Ok, int Failed, int Skipped, int Running)
{
    /// <summary>Geen runs.</summary>
    public static RunTally Empty { get; }

    /// <summary>Alle runs die in het venster zijn gestart.</summary>
    public int Total => Ok + Failed + Skipped + Running;

    /// <summary>De runs die klaar zijn.</summary>
    public int Completed => Ok + Failed + Skipped;

    /// <summary>
    /// Het aandeel mislukte runs, of <c>null</c> als er niets is afgerond.
    /// </summary>
    /// <remarks>
    /// <c>null</c> en niet nul: nul procent fout suggereert dat het goed ging, en er ging niets.
    /// </remarks>
    public double? ErrorRate => Completed == 0 ? null : (double)Failed / Completed;
}
