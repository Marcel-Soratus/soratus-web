using System.Globalization;
using System.Text.Json;
using Soratus.Agents.Contracts;
using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Logregels voor de tests, met een <c>extra</c> die eruitziet zoals hij er in de echte data
/// uitziet.
/// </summary>
/// <remarks>
/// <para><strong>Waarom deze data vijandig is.</strong> Het veld <c>extra</c> op een logregel is
/// vrije JSON: het contract schrijft niets voor over wat erin staat, en de agents die er nu al
/// zijn zetten er koppelingdetails in — een Graph-endpoint, een OAuth-scope, een stacktrace met
/// onze bronpaden. De interne agent zet er zwaarder spul in: een resource group, een toolnaam, een
/// werkitemnummer, en in één geval een lijst met de slugs van <em>andere klanten</em>.</para>
///
/// <para>Zou deze testdata een brave <c>{"ok": true}</c> bevatten, dan zouden de
/// zichtbaarheidstests groen staan omdat er niets te lekken viel. Dat is precies het soort groen
/// waar niemand iets aan heeft. Deze regels zijn dus met opzet zo geschreven dat elk woord dat een
/// klant volgens §2 niet mag zien érgens in de fixture staat; welke daarvan het scherm haalt, is
/// dan een uitkomst van de test en geen aanname van de testdata.</para>
///
/// <para>Elke <see cref="JsonElement"/> hier is een <c>Clone()</c>. Een <c>JsonElement</c> leeft
/// anders in het <see cref="JsonDocument"/> waaruit hij komt, en zodra dat document wordt
/// opgeruimd valt de uitklap in <c>LogJson</c> terug op "context niet meer beschikbaar" — dan meet
/// de test de opruiming van de fixture in plaats van het scherm.</para>
/// </remarks>
internal static class Testlogregels
{
    /// <summary>Het vaste referentiemoment van de tests.</summary>
    private static readonly DateTimeOffset Nu = Testgegevens.Nu;

    /// <summary>
    /// Woorden en waarden uit <see cref="Klantregels"/> die volgens §2 nooit op een klantscherm
    /// horen te staan.
    /// </summary>
    /// <remarks>
    /// <para>Ze staan hier als lijst omdat de test die ze zoekt niet zelf mag bepalen waar hij
    /// naar kijkt: de fixture zet de inhoud, de lijst benoemt hem, en de test vergelijkt. Komt er
    /// een veld bij in <see cref="Klantregels"/>, dan hoort het hier ook bij te komen.</para>
    ///
    /// <para>Per regel staat erbij waarom het niet mag:</para>
    /// <list type="bullet">
    ///   <item><description><c>Mail.ReadWrite</c> en het Graph-pad zijn koppelingdetails.</description></item>
    ///   <item><description>Het bronpad legt onze interne mappenstructuur bloot.</description></item>
    ///   <item><description><c>rg-…</c> is de Azure-inrichting.</description></item>
    ///   <item><description><c>bakker</c> en <c>meijer</c> zijn andere klanten.</description></item>
    /// </list>
    /// </remarks>
    public static readonly string[] VerbodenInhoud =
    [
        "Mail.ReadWrite",
        "/v1.0/me/messages/delta",
        "/src/Mail/Rules/SenderDomainRule.cs",
        "rg-acme-prod",
        "uren.boeken",
        "bakker",
        "meijer",
    ];

    /// <summary>
    /// De drie logregels die op het klantscherm terechtkomen: één per niveau, met een
    /// <c>extra</c> zoals de echte agents hem schrijven.
    /// </summary>
    /// <returns>Oudste eerst; de weergavelaag draait ze om.</returns>
    public static IReadOnlyList<LogRecord> Klantregels() =>
    [
        Regel(
            id: "01K3F0MJ4T0000000000000001",
            moment: Nu - TimeSpan.FromMinutes(9),
            niveau: LogLevel.Info,
            gebeurtenis: "delta.opgehaald",
            bericht: "14 nieuwe berichten opgehaald uit het postvak.",
            extra: """
            {
              "endpoint": "GET /v1.0/me/messages/delta",
              "scope": "Mail.ReadWrite",
              "requestId": "c9f2a1e4-77b1-4a3b-9f0e-2d5c8b6a1f33",
              "durationMs": 412
            }
            """),
        Regel(
            id: "01K3F0MJ4T0000000000000002",
            moment: Nu - TimeSpan.FromMinutes(6),
            niveau: LogLevel.Warn,
            gebeurtenis: "afzender.onbekend",
            bericht: "Twee berichten kwamen van een afzender die niet in de regels staat.",
            extra: """
            {
              "rule": "SenderDomainRule",
              "resourceGroup": "rg-acme-prod",
              "tool": "uren.boeken",
              "workItemId": 48213
            }
            """),
        Regel(
            id: "01K3F0MJ4T0000000000000003",
            moment: Nu - TimeSpan.FromMinutes(4),
            niveau: LogLevel.Error,
            gebeurtenis: "run.mislukt",
            bericht: "De bron antwoordde niet binnen 30 seconden.",
            extra: """
            {
              "exception": "System.TimeoutException",
              "stackTrace": "at Soratus.Mail.Rules.SenderDomainRule.Apply(Message message) in /src/Mail/Rules/SenderDomainRule.cs:line 34\n   at Soratus.Mail.Pipeline.Run(CancellationToken token) in /src/Mail/Pipeline.cs:line 118",
              "customerIds": ["bakker", "meijer"]
            }
            """),
    ];

    /// <summary>Het fragment dat een bronpad van ons verraadt.</summary>
    public const string Bronpad = "/src/";

    /// <summary>Het fragment waarmee een .NET-stacktraceregel begint.</summary>
    public const string Stacktrace = "at Soratus";

    /// <summary>
    /// Een logregel met een stacktrace in het <em>bericht</em> in plaats van in <c>extra</c>.
    /// </summary>
    /// <remarks>
    /// <para>Dit is geen bedacht geval. In de opslag staat bij <c>bakker-voorraad-sync</c> een regel
    /// met gebeurtenis <c>payload.dump</c> van 3349 tekens, met zestien regels .NET-stacktrace
    /// inclusief onze <c>/src/</c>-paden — in <c>msg</c>. En <c>msg</c> is een veld dat de klant
    /// hóórt te zien: daar staat wat er is gebeurd.</para>
    ///
    /// <para>De vorm is met opzet precies die van de echte regel: één geldige Nederlandse zin, dan
    /// een regelovergang, dan de frames. Een agentbibliotheek die het bericht op de eerste
    /// regelovergang knipt houdt de zin over en laat de frames weg; een die dat niet doet zet ze
    /// allemaal op het klantscherm.</para>
    /// </remarks>
    /// <returns>De logregel.</returns>
    public static LogRecord BerichtMetStacktrace() =>
        Regel(
            id: "01K3F0MJ4T0000000000000004",
            moment: Nu - TimeSpan.FromMinutes(2),
            niveau: LogLevel.Error,
            gebeurtenis: "payload.dump",
            bericht: "De voorraadregel kon niet worden weggeschreven.\n"
                + "at SoratusAgent.Voorraad.Writer.Write(StockLine line) in /src/Voorraad/Writer.cs:line 88\n"
                + "at SoratusAgent.Voorraad.Pipeline.Run(CancellationToken token) in /src/Voorraad/Pipeline.cs:line 143\n"
                + "at SoratusAgent.Hosting.AgentHost.Tick(CancellationToken token) in /src/Hosting/AgentHost.cs:line 61",
            extra: """
            {
              "attempt": 3
            }
            """);

    /// <summary>
    /// Een klantzichtbaar bericht van 1417 tekens op precies één regel.
    /// </summary>
    /// <remarks>
    /// <para>1417 is geen willekeurig getal: dat is de langste legitieme eerste regel die over de
    /// klantzichtbare logregels is gemeten, en het is de reden dat de bovengrens in
    /// <c>CustomerMessage.MaxLength</c> op 8000 staat en niet ergens in het middengebied. Een grens
    /// van 200 of 500 zou dit bericht middenin verminken en tegelijk een stacktrace deels
    /// doorlaten.</para>
    ///
    /// <para>Deze regel bestaat om de knip aan de andere kant vast te zetten. Zonder hem kapt
    /// iemand later "voor de zekerheid" alsnog op lengte, en dan gaat er een geldig bericht kapot in
    /// plaats van een stacktrace. De tekst is bewust één doorlopende zin zonder regelovergang, zodat
    /// er niets is om op te knippen, en zonder <c>&amp;</c>, <c>&lt;</c> of <c>&gt;</c> zodat de
    /// HTML-codering hem letterlijk laat.</para>
    /// </remarks>
    /// <returns>De logregel.</returns>
    public static LogRecord LangBerichtOpEenRegel() =>
        Regel(
            id: "01K3F0MJ4T0000000000000005",
            moment: Nu - TimeSpan.FromMinutes(1),
            niveau: LogLevel.Info,
            gebeurtenis: "afstemming.rapport",
            bericht: LangeZin);

    /// <summary>De tekst van <see cref="LangBerichtOpEenRegel"/>: 1417 tekens, één regel.</summary>
    /// <remarks>
    /// Begint met <c>AFSTEMMING</c> en eindigt met <c>EINDE-AFSTEMMING.</c>, zodat een test kan
    /// vaststellen dat niet alleen het begin maar ook het slot de rit heeft gehaald. Een knip op
    /// lengte laat het begin namelijk wél staan.
    /// </remarks>
    public static readonly string LangeZin = Bouw();

    /// <summary>De id van de regel waarin de zwaarste inhoud staat: de error-regel.</summary>
    /// <remarks>
    /// Los benoemd zodat een test hem kan uitklappen zonder aan te nemen dat hij de derde is.
    /// </remarks>
    public const string ZwaarsteRegelId = "01K3F0MJ4T0000000000000003";

    /// <summary>
    /// Eén logregel, met alleen die velden gezet die de tests gebruiken.
    /// </summary>
    /// <param name="id">Het id; oplopend, zodat de gelijkspelclausule op de id te testen is.</param>
    /// <param name="moment">De tijdstempel.</param>
    /// <param name="niveau">Het niveau.</param>
    /// <param name="gebeurtenis">De gebeurtenisnaam.</param>
    /// <param name="bericht">Het bericht.</param>
    /// <param name="extra">De vrije context als JSON-tekst, of <c>null</c>.</param>
    /// <param name="runId">De run waar de regel bij hoort.</param>
    /// <param name="agentNaam">De technische naam van de agent.</param>
    /// <param name="klantId">De klant-slug.</param>
    /// <returns>De logregel.</returns>
    public static LogRecord Regel(
        string id,
        DateTimeOffset moment,
        LogLevel niveau,
        string gebeurtenis = "run.voortgang",
        string bericht = "Voortgang.",
        string? extra = null,
        string? runId = "r-8f3c",
        string agentNaam = "factuur-intake",
        string klantId = "acme-logistiek") =>
        new()
        {
            Id = id,
            PartitionKey = LogRecord.BuildPartitionKey(agentNaam, moment),
            Timestamp = moment,
            Level = niveau,
            Event = gebeurtenis,
            Message = bericht,
            RunId = runId,
            Extra = extra is null ? null : Json(extra),
            CustomerId = klantId,
            AgentName = agentNaam,
        };

    /// <summary>
    /// Een reeks regels op dezelfde tijdstempel, met oplopende id's.
    /// </summary>
    /// <param name="moment">De tijdstempel die ze allemaal delen.</param>
    /// <param name="aantal">Hoeveel regels.</param>
    /// <param name="voorvoegsel">Het voorvoegsel van de id's.</param>
    /// <returns>De regels, op id gesorteerd.</returns>
    /// <remarks>
    /// Gelijke tijdstempels zijn geen theoretisch geval: een agent die in één batch schrijft geeft
    /// tientallen regels dezelfde <c>ts</c> mee. De cursor van de live tail moet daar tegen kunnen
    /// zonder een regel dubbel te tonen of over te slaan, en dat is alleen te testen met een
    /// verzameling waarin het gebeurt.
    /// </remarks>
    public static IReadOnlyList<LogRecord> GelijkeTijdstempels(
        DateTimeOffset moment,
        int aantal,
        string voorvoegsel = "eq") =>
    [
        .. Enumerable.Range(1, aantal).Select(i => Regel(
            id: $"{voorvoegsel}-{i:D4}",
            moment: moment,
            niveau: LogLevel.Info,
            gebeurtenis: "batch.regel",
            bericht: $"Regel {i} uit dezelfde batch."))
    ];

    /// <summary>De telling per niveau over een reeks regels.</summary>
    /// <param name="regels">De regels.</param>
    /// <returns>Alle drie de niveaus, ook die met nul.</returns>
    /// <remarks>
    /// Alle drie altijd aanwezig, net als <c>PortalViews.Counts</c> doet: een ontbrekend niveau en
    /// een niveau met nul regels zijn op het scherm niet te onderscheiden, dus horen ze in de
    /// gegevens al hetzelfde te zijn.
    /// </remarks>
    public static IReadOnlyDictionary<LogLevel, int> Tellingen(IEnumerable<LogRecord> regels)
    {
        var lijst = regels.ToArray();

        return new Dictionary<LogLevel, int>(3)
        {
            [LogLevel.Info] = lijst.Count(r => r.Level == LogLevel.Info),
            [LogLevel.Warn] = lijst.Count(r => r.Level == LogLevel.Warn),
            [LogLevel.Error] = lijst.Count(r => r.Level == LogLevel.Error),
        };
    }

    /// <summary>De cursor die bij de jongste regel van een reeks hoort.</summary>
    /// <param name="regels">De regels.</param>
    /// <returns>De cursor, of de cursor op <see cref="Testgegevens.Nu"/> als er niets is.</returns>
    public static LogCursor Cursor(IReadOnlyList<LogRecord> regels)
    {
        if (regels.Count == 0)
        {
            return LogCursor.From(Nu);
        }

        var jongste = regels
            .OrderBy(r => r.Timestamp)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .Last();

        return new LogCursor(jongste.Timestamp, jongste.Id);
    }

    /// <summary>
    /// Bouwt de lange zin op precies 1417 tekens, zonder enige regelovergang.
    /// </summary>
    /// <remarks>
    /// Opgebouwd in plaats van uitgetypt, want een letterlijke tekenreeks van 1417 tekens in de
    /// bron is niet te controleren op zijn lengte — en juist de lengte is hier het punt. De
    /// vulling loopt in nummers op, zodat een test die per ongeluk de helft meet dat ziet.
    /// </remarks>
    private static string Bouw()
    {
        const string kop = "AFSTEMMING: ";
        const string staart = " EINDE-AFSTEMMING.";
        const int totaal = 1417;

        var romp = new System.Text.StringBuilder();

        for (var nummer = 1; kop.Length + romp.Length + staart.Length < totaal; nummer++)
        {
            romp.Append(CultureInfo.InvariantCulture, $"regel {nummer} afgestemd, geen verschil; ");
        }

        var zin = kop + romp + staart;

        // Precies op maat, en de staart blijft de staart: het slot is wat een knip op lengte als
        // eerste weghaalt, dus daar moet de test naar kunnen kijken.
        return string.Concat(zin.AsSpan(0, totaal - staart.Length), staart);
    }

    /// <summary>
    /// Zet JSON-tekst om in een <see cref="JsonElement"/> die zijn eigen document niet nodig
    /// heeft.
    /// </summary>
    /// <param name="json">De JSON.</param>
    /// <returns>Het losgekoppelde element.</returns>
    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }
}
