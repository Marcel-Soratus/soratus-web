using Soratus.Agents.Contracts;

// LogLevel is hier het niveau van een agent-logregel uit het contract, niet dat van
// Microsoft.Extensions.Logging. Dat regelt één alias op projectniveau in Soratus.Portal.csproj;
// een using-regel per bestand hoort hier dus niet meer bij te komen.

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
/// Geef die aan <see cref="LogQuery.Tail"/> om alleen de regels te halen die er daarna bij zijn
/// gekomen.
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
/// Wat de live tail eraan vond sinds de vorige aanroep.
/// </summary>
/// <param name="Lines">De nieuwe regels, oudste eerst.</param>
/// <param name="Cursor">
/// Waar de volgende aanroep verder gaat. <strong>Nooit <c>null</c></strong>, ook niet als er
/// niets nieuws was: dan komt de meegegeven cursor er onveranderd uit.
/// </param>
/// <param name="HasMore">
/// Of er op dit moment nog meer nieuwe regels klaarstaan dan er in deze aanroep pasten. De
/// aanroeper hoort dan niet op de volgende tik te wachten maar direct nog eens te vragen.
/// </param>
/// <remarks>
/// <para><strong>Oudste eerst, en dat is geen willekeur.</strong> De tail levert wat er ná de
/// cursor bij kwam en schuift de cursor door naar de laatste regel die hij meegeeft. Zou hij
/// nieuwste-eerst leveren en er meer nieuwe regels zijn dan er in één aanroep passen, dan wijst
/// de cursor naar de nieuwste van die groep en zijn de oudere voorgoed overgeslagen. Oudste
/// eerst schuift de cursor aaneengesloten door en slaat dus niets over. Het scherm zet de
/// regels bovenaan de tabel in omgekeerde volgorde.</para>
///
/// <para>Dat <see cref="Cursor"/> nooit <c>null</c> is, is opzet. Een tail die bij een leeg
/// antwoord <c>null</c> teruggeeft dwingt elke aanroeper tot een <c>?? vorige</c>, en die wordt
/// precies één keer vergeten — waarna de tail bij elke tik weer vanaf het begin leest en de hele
/// tabel opnieuw toont.</para>
/// </remarks>
public sealed record LogTail(IReadOnlyList<LogRecord> Lines, LogCursor Cursor, bool HasMore)
{
    /// <summary>Er kwam niets bij; de cursor blijft waar hij stond.</summary>
    /// <param name="cursor">De cursor van de vorige aanroep.</param>
    /// <returns>Een leeg antwoord.</returns>
    public static LogTail Nothing(LogCursor cursor) => new([], cursor, false);
}

/// <summary>
/// Hoeveel logregels er per niveau zijn, binnen hetzelfde filter als de lijst.
/// </summary>
/// <param name="Info">Aantal regels op <see cref="LogLevel.Info"/>.</param>
/// <param name="Warn">Aantal regels op <see cref="LogLevel.Warn"/>.</param>
/// <param name="Error">Aantal regels op <see cref="LogLevel.Error"/>.</param>
/// <remarks>
/// Dit is wat er op de filterchips staat ("error 3"). De telling wordt bewust met dezelfde
/// zoekterm, dezelfde runId en dezelfde bovengrens gedaan als de lijst, en <em>zonder</em> het
/// niveaufilter — anders telt elke chip alleen zichzelf. Zo levert het aanzetten van een chip
/// precies het aantal regels dat erop stond.
/// </remarks>
public readonly record struct LogLevelTally(int Info, int Warn, int Error)
{
    /// <summary>Geen regels.</summary>
    public static LogLevelTally Empty { get; }

    /// <summary>Alle regels binnen het filter, over de drie niveaus.</summary>
    public int Total => Info + Warn + Error;

    /// <summary>Het aantal op één niveau.</summary>
    /// <param name="level">Het niveau.</param>
    /// <returns>Het aantal, of nul voor een niveau dat we niet kennen.</returns>
    public int For(LogLevel level) => level switch
    {
        LogLevel.Info => Info,
        LogLevel.Warn => Warn,
        LogLevel.Error => Error,
        _ => 0,
    };

    /// <summary>Telt er een niveau bij op.</summary>
    /// <param name="level">Het niveau.</param>
    /// <param name="count">Het aantal.</param>
    /// <returns>De bijgewerkte telling.</returns>
    internal LogLevelTally Add(LogLevel level, int count) => level switch
    {
        LogLevel.Info => this with { Info = Info + count },
        LogLevel.Warn => this with { Warn = Warn + count },
        LogLevel.Error => this with { Error = Error + count },
        _ => this,
    };
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
public readonly record struct LogCursor(DateTimeOffset Timestamp, string Id)
{
    /// <summary>
    /// De cursor voor een lezer die nog geen regel heeft gezien: alles vanaf dit moment.
    /// </summary>
    /// <param name="moment">Vanaf wanneer meegelezen wordt.</param>
    /// <returns>De cursor.</returns>
    /// <remarks>
    /// Bestaat voor de agent die (nog) geen enkele logregel heeft. Zonder dit zou de live tail op
    /// dat scherm geen beginpunt hebben en zou het scherm er zelf een moeten verzinnen. De lege
    /// ULID werkt hier omdat de vergelijking <c>ts &gt; @since OR (ts = @since AND id &gt; @sinceId)</c>
    /// bij een lege id neerkomt op "alles met een latere tijdstempel", en dat is precies de
    /// bedoeling.
    /// </remarks>
    public static LogCursor From(DateTimeOffset moment) => new(moment, string.Empty);
}

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
    /// Alleen regels van deze run. Gebruikt vanaf het rundetail.
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    /// De bovengrens: alleen regels tot en met dit moment. <c>null</c> betekent geen grens.
    /// </summary>
    /// <remarks>
    /// <para><strong>Hier voorkomt één parameter een tegenspraak op het scherm.</strong> De lijst
    /// en de niveautellingen zijn twee query's en dus twee momenten. Landt er tussen die twee een
    /// nieuwe foutregel, dan staat er "error 4" op de chip terwijl de lijst er drie toont — twee
    /// getallen op hetzelfde scherm die elkaar tegenspreken. Met dezelfde bovengrens in beide
    /// query's kan dat niet: ze kijken naar precies dezelfde verzameling, ongeacht de volgorde
    /// waarin ze lopen.</para>
    ///
    /// <para>De live tail heeft deze grens níet — die wil juist wat er ná dit moment bij kwam.
    /// Een regel die door klokverschil een tijdstempel in de toekomst heeft valt daarmee buiten de
    /// lijst, maar wordt door de tail alsnog opgepikt: hij ligt immers vóór de cursor noch erop.
    /// </para>
    /// </remarks>
    public DateTimeOffset? AsOf { get; init; }

    /// <summary>Hoeveel regels deze pagina maximaal bevat.</summary>
    public int? PageSize { get; init; }

    /// <summary>Het vervolgtoken van de vorige pagina.</summary>
    public string? ContinuationToken { get; init; }

    /// <summary>
    /// Dezelfde filters, maar als opdracht voor de live tail.
    /// </summary>
    /// <param name="since">Waar de lezer is gebleven.</param>
    /// <returns>De tailopdracht.</returns>
    /// <remarks>
    /// Bestaat zodat de tail niet zijn eigen filters kan hebben. Zou het scherm die twee los
    /// samenstellen, dan komt er vroeg of laat een regel binnendruppelen die door het niveaufilter
    /// of de zoekterm van de tabel had moeten worden tegengehouden — en dan is de tabel iets anders
    /// dan zijn filters beweren. <see cref="AsOf"/> en <see cref="ContinuationToken"/> gaan
    /// bewust <em>niet</em> mee: het eerste is een bovengrens en de tail kijkt juist verder, het
    /// tweede hoort bij de historie.
    /// </remarks>
    public LogTailQuery Tail(LogCursor since) => new()
    {
        Since = since,
        Levels = Levels,
        Search = Search,
        RunId = RunId,
        MaxLines = PageSize,
    };
}

/// <summary>
/// Wat de live tail wil zien: dezelfde filters als de tabel, plus waar de lezer was gebleven.
/// </summary>
/// <remarks>
/// Een eigen type en niet <see cref="LogQuery"/> met een cursor erin, omdat de cursor hier
/// verplicht is. Een tail zonder cursor is geen tail maar een volledige lijst, en dat verschil
/// hoort in het typesysteem te zitten en niet in een <c>if</c> halverwege de implementatie. Maak
/// hem met <see cref="LogQuery.Tail"/>, zodat de filters van de tabel er per definitie in staan.
/// </remarks>
public sealed record LogTailQuery
{
    /// <summary>Waar de lezer is gebleven.</summary>
    public required LogCursor Since { get; init; }

    /// <summary>De niveaus die de lezer aan heeft staan. <c>null</c> of leeg is alle niveaus.</summary>
    public IReadOnlyCollection<LogLevel>? Levels { get; init; }

    /// <summary>Vrije zoekterm over event, bericht en runId.</summary>
    public string? Search { get; init; }

    /// <summary>Alleen regels van deze run.</summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Hoeveel regels één tik maximaal oplevert, of <c>null</c> voor de standaard.
    /// </summary>
    /// <remarks>
    /// Een grens is nodig: een tabblad dat een kwartier stil heeft gestaan achter een agent die
    /// per minuut logt vraagt anders in één keer alles op. Wat er niet in past blijft staan en
    /// komt bij de volgende aanroep; <see cref="LogTail.HasMore"/> zegt dat dat zo is.
    /// </remarks>
    public int? MaxLines { get; init; }
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
