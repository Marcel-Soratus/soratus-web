using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Soratus.Portal.Sprints;

/// <summary>
/// Wat er van één sprintlezing bij Azure DevOps is teruggekomen.
/// </summary>
/// <remarks>
/// Dezelfde drie waarden en dezelfde reden als bij <see cref="Data.AzureCostAnswerKind"/>: een antwoord dat
/// we niet konden lezen is iets anders dan geen antwoord, en die twee vragen een verschillende handeling.
/// Het eerste is een defect in ónze lezer en hoort zichtbaar te worden; het tweede is een deur die dicht
/// zat en hoort de vorige lezing te laten staan.
/// </remarks>
public enum SprintAnswerKind
{
    /// <summary>
    /// Er is niets bruikbaars binnengekomen: geen recht, een bord dat niet bestaat, of een tijdslimiet.
    /// </summary>
    /// <remarks>
    /// De eerste waarde, zodat een niet-gezette uitkomst hier terechtkomt. Hier volgt géén document uit:
    /// de vorige lezing blijft staan met haar eigen tijdstip erbij.
    /// </remarks>
    NotAvailable,

    /// <summary>DevOps heeft geantwoord en het antwoord is gelezen.</summary>
    /// <remarks>
    /// Ook als er geen huidige sprint is. Dat is geen mislukking maar een lezing met een eigen betekenis;
    /// zie <see cref="SprintState"/> voor de vijf vormen die dat kan hebben.
    /// </remarks>
    Answered,

    /// <summary>DevOps heeft geantwoord en het antwoord was niet te lezen.</summary>
    /// <remarks>
    /// Een ontbrekend verplicht veld, een state waarvan de categorie niet te bepalen is, meer items of
    /// meer werkitemsoorten dan de grens. Dit wordt <see cref="SprintState.Unknown"/> met een reden en
    /// overschrijft dus een goede lezing van een kwartier eerder — de juiste richting, om dezelfde reden
    /// als bij de kosten (punt 39): het betekent dat onze lezer niet meer bij de API past, en dat is een
    /// defect dat zichtbaar hoort te zijn.
    /// </remarks>
    Unreadable,
}

/// <summary>Het antwoord van één sprintlezing.</summary>
/// <param name="Kind">Wat er is teruggekomen.</param>
/// <param name="Choice">De iteratiekeuze. <c>default</c> tenzij <see cref="SprintAnswerKind.Answered"/>.</param>
/// <param name="Items">De work items van de huidige sprint. Leeg tenzij er een huidige sprint is.</param>
/// <param name="Reason">
/// Waarom er niets of niets leesbaars is, in gewone taal, of <c>null</c>. Zie
/// <see cref="SprintDocument.Failure"/>: dit komt op een operatorscherm en niet in een logregel.
/// </param>
/// <param name="Calls">Hoeveel keer er werkelijk een respons is opgehaald.</param>
public readonly record struct SprintAnswer(
    SprintAnswerKind Kind,
    SprintChoice Choice,
    IReadOnlyList<SprintWorkItem> Items,
    string? Reason,
    int Calls);

/// <summary>
/// Leest de sprint van één DevOps-bord.
/// </summary>
/// <remarks>
/// Eén methode, en die neemt een <see cref="DevOpsScope"/> en geen tekenreeks. Dat is de grens: er is geen
/// aanroep waarmee een niet-gevalideerde scope de deur uit gaat. Dezelfde vorm als
/// <see cref="Data.IAzureCostClient"/>.
/// </remarks>
public interface IDevOpsSprintClient
{
    /// <summary>
    /// Leest de sprint van één bord.
    /// </summary>
    /// <param name="scope">De scope. Gevalideerd, want dit type kan niet anders bestaan.</param>
    /// <param name="today">
    /// De dag waarop wordt gekeken, in de weergavezone van het portaal. Bepaalt welke iteratie de huidige
    /// sprint is; zie <see cref="SprintSelection"/>.
    /// </param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het antwoord.</returns>
    Task<SprintAnswer> ReadAsync(
        DevOpsScope scope,
        DateOnly today,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// De enige implementatie: vier soorten aanroepen aan de REST-API van Azure DevOps, met een Entra-token
/// van de managed identity van het portaal.
/// </summary>
/// <remarks>
/// <para><strong>Geen personal access token, en dat is geen voorkeur maar de lijn van dit hele
/// project.</strong> Een PAT is een geheim dat kopieerbaar is, aan een persoon hangt, en verloopt zonder
/// dat iemand het merkt. Er staat in dit portaal nergens een accountsleutel en op de Cosmos-accounts is
/// local auth uitgezet zodat er geen kan bestaan. Azure DevOps accepteert Entra-tokens op zijn REST-API,
/// dus er is geen reden om hier een uitzondering te maken. Het token komt van de
/// <see cref="TokenCredential"/> die al in de container staat — in productie de user-assigned identity
/// <c>id-soratus-portal</c>, dezelfde die Cosmos en Key Vault gebruikt.</para>
///
/// <para><strong>Wat dat kost, en dat is de eerlijke helft: de identiteit moet lid zijn van de
/// organisatie.</strong> Gemeten op 22 augustus 2026 is dat vandaag niet zo — een identiteitszoekopdracht
/// op <c>id-soratus-portal</c> in de organisatie <c>soratus</c> geeft "geen identiteiten gevonden", terwijl
/// dezelfde zoekopdracht op <c>marcel@soratus.com</c> hem wél vindt. Zolang die stap niet is gezet levert
/// élke aanroep hier een geweigerd verzoek op, en dan blijft de vorige lezing staan en zegt het
/// operatorscherm dat het portaal dit bord niet mag lezen. Dat is met opzet een andere mededeling dan
/// "geen bord vastgelegd": de handeling erachter is een rolverlening en niet een veld invullen.</para>
///
/// <para><strong>Wat er is gemeten en wat niet — en dit is de belangrijkste alinea van dit bestand.</strong>
/// De metingen van 22 augustus 2026 zijn gedaan via een MCP-server die als <c>marcel@</c> praat en het
/// antwoord bewerkt voordat het bij mij komt. Daaruit volgt een scherpe scheiding:</para>
///
/// <list type="bullet">
///   <item><description>
///     <strong>Gemeten:</strong> de veldnamen (<c>System.IterationId</c>, <c>System.Tags</c>,
///     <c>Microsoft.VSTS.Scheduling.RemainingWork</c>, …), dat een leeg veld <em>niet in het woordenboek
///     staat</em> in plaats van als <c>null</c>, dat een veld dat een werkitemsoort niet heeft géén fout
///     geeft maar simpelweg ontbreekt, dat de teamiteratielijst <c>startDate</c>/<c>finishDate</c> als
///     datum-op-middernacht geeft, dat de iteratie-workitems-aanroep <c>workItemRelations</c> met
///     <c>target.id</c> teruggeeft, en dat een werkitemsoort een <c>states</c>-lijst heeft met
///     <c>name</c> en <c>category</c>.
///   </description></item>
///   <item><description>
///     <strong>Niet gemeten:</strong> de omhulsels. Dat een lijstantwoord <c>{ "count": n, "value": [ … ] }</c>
///     is, komt uit de documentatie — de MCP-server pakte het uit. Hetzelfde geldt voor de vorm van een
///     identiteitsveld: de server gaf <c>"Dennis Verhamme &lt;dennis@soratus.com&gt;"</c> als tekenreeks,
///     terwijl de REST-API volgens de documentatie een object met <c>displayName</c> en
///     <c>uniqueName</c> geeft. <see cref="ReadIdentity"/> leest daarom <em>beide</em> vormen, en dat is
///     geen gok naar twee kanten: het is het enige eerlijke antwoord op een veld waarvan de ruwe vorm
///     niet te meten viel, en de tekenreeksvorm levert een weergavenaam zonder unieke naam op — dus
///     nooit een e-mailadres dat we niet als zodanig hebben herkend.
///   </description></item>
///   <item><description>
///     <strong>Ook niet gemeten:</strong> het aanroepbudget. De MCP-server geeft geen responsheaders door,
///     dus de <c>X-RateLimit-*</c>-headers en de <c>Retry-After</c> die de documentatie belooft zijn
///     nooit gezien. Ze worden gelezen als ze er zijn en er wordt niet op gepland; zie
///     <see cref="SprintOptions"/> voor wat dat voor de standaardwaarden betekent.
///   </description></item>
/// </list>
///
/// <para><strong>Er wordt nooit iets geschreven.</strong> Deze klasse doet twee <c>GET</c>'s en één
/// <c>POST</c>, en die POST is de veldenbatch — een leesaanroep die een lijst nummers in het lichaam heeft
/// omdat een URL daar te kort voor is. Er is geen methode hier die een work item aanraakt, en er hoort er
/// geen te komen: DevOps is leidend en het portaal schrijft nooit terug (§3.4).</para>
/// </remarks>
internal sealed class DevOpsSprintClient(
    IHttpClientFactory clients,
    TokenCredential credential,
    IOptions<SprintOptions> options,
    TimeProvider timeProvider,
    ILogger<DevOpsSprintClient> logger) : IDevOpsSprintClient
{
    /// <summary>De naam van de <see cref="HttpClient"/> in de fabriek.</summary>
    /// <remarks>
    /// Een fabriek en geen geïnjecteerde <see cref="HttpClient"/>, om dezelfde reden als bij
    /// <c>AzureCostClient</c>: deze klasse hangt aan een achtergronddienst en leeft zolang het portaal
    /// draait, dus een vastgehouden handler volgt een DNS-wijziging van <c>dev.azure.com</c> niet meer.
    /// </remarks>
    internal const string HttpClientName = "devops-sprint";

    /// <summary>
    /// De tokenscope van Azure DevOps.
    /// </summary>
    /// <remarks>
    /// <para><c>499b84ac-1321-427f-aa17-267ca6975798</c> is de vaste resource-id van Azure DevOps, gelijk in
    /// elke tenant. Dat getal staat hier als constante en niet in configuratie: het is geen instelling maar
    /// een eigenschap van het platform, en een instelbare tokenscope is een instelling waarmee iemand per
    /// ongeluk een token voor een ander publiek kan aanvragen.</para>
    ///
    /// <para><strong>Dit is de hele authenticatie.</strong> Geen PAT, geen geheim in configuratie, geen
    /// tweede credential. Wat er nog moet gebeuren is een rolverlening buiten deze code; zie de
    /// toelichting bij deze klasse.</para>
    /// </remarks>
    private static readonly string[] TokenScope =
        ["499b84ac-1321-427f-aa17-267ca6975798/.default"];

    /// <summary>
    /// De velden die van elk work item worden gevraagd (§3.4).
    /// </summary>
    /// <remarks>
    /// <para>Bij naam en niet met <c>$expand</c>. Gemeten kunnen die twee niet samen, en een expand zou
    /// bovendien élk veld van élk item ophalen — inclusief <c>System.Description</c>, dat een lap HTML kan
    /// zijn die niemand op dit scherm gebruikt en die wel in onze opslag zou belanden.</para>
    ///
    /// <para><strong>Gemeten: een veld dat een werkitemsoort niet heeft, geeft geen fout maar
    /// ontbreekt.</strong> <c>Microsoft.VSTS.Scheduling.StoryPoints</c> is bij een <c>Task</c> opgevraagd en
    /// het antwoord had die sleutel gewoon niet. Er is dus geen lijst per soort nodig.</para>
    ///
    /// <para>En er wordt géén <c>errorPolicy: Omit</c> meegestuurd. De standaard is <c>Fail</c>, en dat is
    /// hier de goede kant: <c>Omit</c> laat items die niet op te halen zijn stil weg, en dan is de
    /// statistiek te laag — de fout die onzichtbaar is.</para>
    /// </remarks>
    private static readonly string[] Fields =
    [
        "System.Id",
        "System.WorkItemType",
        "System.Title",
        "System.State",
        "System.AssignedTo",
        "System.Tags",
        "System.CreatedBy",
        "Microsoft.VSTS.Scheduling.RemainingWork",
        "Microsoft.VSTS.Scheduling.CompletedWork",
        "Microsoft.VSTS.Scheduling.StoryPoints",
    ];

    /// <summary>Hoeveel work item-nummers er per veldenbatch mee mogen.</summary>
    /// <remarks>
    /// Tweehonderd, uit de documentatie van <c>workitemsbatch</c>. <strong>Niet gemeten</strong> — de
    /// grootste gemeten iteratie had zestien items, dus er is nooit een tweede batch geweest. Zie
    /// <see cref="SprintOptions.MaxWorkItems"/>.
    /// </remarks>
    private const int BatchSize = 200;

    private readonly SprintOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<SprintAnswer> ReadAsync(
        DevOpsScope scope,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var calls = 0;

        var iterations = await GetAsync<IterationList>(
                Url(scope.Path, "_apis/work/teamsettings/iterations"),
                cancellationToken)
            .ConfigureAwait(false);

        calls += iterations.Calls;

        if (iterations.Failure is { } iterationFailure)
        {
            return iterationFailure with { Calls = calls };
        }

        var choice = SprintSelection.Choose(
            [.. (iterations.Value?.Value ?? []).Select(row => row.ToIteration())],
            today);

        if (choice.Current is not { } sprint)
        {
            // Geen huidige sprint is een geslaagde lezing en geen mislukking. Er wordt niet naar work
            // items gevraagd, want er is geen sprint om ze bij te vragen — en de reden staat in
            // choice.State, met vijf vormen die vijf verschillende handelingen vragen.
            return new SprintAnswer(SprintAnswerKind.Answered, choice, [], Reason: null, calls);
        }

        var ids = await GetAsync<IterationWorkItems>(
                Url(scope.Path, $"_apis/work/teamsettings/iterations/{sprint.Id}/workitems"),
                cancellationToken)
            .ConfigureAwait(false);

        calls += ids.Calls;

        if (ids.Failure is { } idFailure)
        {
            return idFailure with { Calls = calls };
        }

        var numbers = (ids.Value?.WorkItemRelations ?? [])
            .Select(relation => relation.Target?.Id)
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .Order()
            .ToArray();

        if (numbers.Length > _options.MaxWorkItems)
        {
            // Geen halve lijst. Zie SprintOptions.MaxWorkItems: een aantal dat te laag is, is even
            // onzichtbaar als een subtotaal dat te laag is.
            return Unreadable(
                $"De sprint '{sprint.Name}' heeft {numbers.Length} work items en de grens staat op "
                + $"{_options.MaxWorkItems}. Een deel ervan tonen zou statistieken opleveren die te laag "
                + "zijn.",
                choice,
                calls);
        }

        var raw = new List<BatchItem>();

        foreach (var chunk in numbers.Chunk(BatchSize))
        {
            var batch = await PostAsync<BatchList>(
                    Url(scope.OrganizationPath, "_apis/wit/workitemsbatch"),
                    new { ids = chunk, fields = Fields },
                    cancellationToken)
                .ConfigureAwait(false);

            calls += batch.Calls;

            if (batch.Failure is { } batchFailure)
            {
                return batchFailure with { Calls = calls };
            }

            raw.AddRange(batch.Value?.Value ?? []);
        }

        if (raw.Count != numbers.Length)
        {
            // De batch gaf een ander aantal terug dan er is gevraagd. Dat is geen toestand maar een
            // defect: het aantal work items van de sprint zou er te laag uit komen en dat is de fout die
            // niet te zien is. Zelfde vorm als de vierde regel van punt 39.
            return Unreadable(
                $"Er is naar {numbers.Length} work items gevraagd en er zijn er {raw.Count} "
                + "teruggekomen. Het aantal en de statistieken zouden daarmee te laag zijn.",
                choice,
                calls);
        }

        var types = raw
            .Select(item => item.Field<string>("System.WorkItemType"))
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (types.Length > _options.MaxWorkItemTypes)
        {
            return Unreadable(
                $"De sprint '{sprint.Name}' heeft {types.Length} werkitemsoorten en de grens staat op "
                + $"{_options.MaxWorkItemTypes}. Zonder de categorie van elke state is niet te zeggen "
                + "welke items afgerond zijn.",
                choice,
                calls);
        }

        var stages = new Dictionary<string, WorkItemStage>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in types)
        {
            var metadata = await GetAsync<WorkItemTypeMetadata>(
                    Url(scope.ProjectPath, $"_apis/wit/workitemtypes/{Escape(type)}"),
                    cancellationToken)
                .ConfigureAwait(false);

            calls += metadata.Calls;

            if (metadata.Failure is { } metadataFailure)
            {
                return metadataFailure with { Calls = calls };
            }

            foreach (var state in metadata.Value?.States ?? [])
            {
                if (state.Name is { Length: > 0 } name)
                {
                    // De sleutel is soort + state. Twee soorten kunnen een state met dezelfde naam en een
                    // andere categorie hebben, en één woordenboek op alleen de statenaam zou dan de
                    // categorie van de ene soort aan de andere geven.
                    stages[$"{type}{name}"] = Stage(state.Category);
                }
            }
        }

        var items = new List<SprintWorkItem>(raw.Count);

        foreach (var item in raw)
        {
            if (Read(item, stages) is { } gelezen)
            {
                items.Add(gelezen);
                continue;
            }

            return Unreadable(
                $"Work item {item.Id} miste een veld dat elk item hoort te hebben (soort, titel of "
                + "state), of zijn state was niet in een categorie te plaatsen. Er is dan niet te zeggen "
                + "of hij afgerond is.",
                choice,
                calls);
        }

        return new SprintAnswer(SprintAnswerKind.Answered, choice, items, Reason: null, calls);
    }

    /// <summary>Een lezing die er wél was en niet te gebruiken viel.</summary>
    /// <param name="reason">Waarom.</param>
    /// <param name="choice">De keuze die tot hier was gemaakt.</param>
    /// <param name="calls">Hoeveel responsen er zijn opgehaald.</param>
    /// <returns>Het antwoord.</returns>
    /// <remarks>
    /// De keuze gaat mee en wordt niet weggegooid, want de iteratielijst was leesbaar — maar het
    /// antwoord is <see cref="SprintAnswerKind.Unreadable"/>, en de collector schrijft dan
    /// <see cref="SprintState.Unknown"/> weg. De keuze zit erin zodat de logregel kan zeggen welke sprint
    /// het was.
    /// </remarks>
    private static SprintAnswer Unreadable(string reason, SprintChoice choice, int calls) =>
        new(SprintAnswerKind.Unreadable, choice, [], reason, calls);

    /// <summary>
    /// Zet één rij uit de veldenbatch om in een work item, of levert <c>null</c> als dat niet kan.
    /// </summary>
    /// <param name="item">De rij.</param>
    /// <param name="stages">De categorieën per soort en state.</param>
    /// <returns>Het work item, of <c>null</c>.</returns>
    /// <remarks>
    /// <para><strong>Elk optioneel veld wordt <c>null</c> als de sleutel ontbreekt, en nooit nul.</strong>
    /// Dat is gemeten: in het antwoord van <c>workitemsbatch</c> staat een leeg veld niet in het
    /// woordenboek. Van de zestien gemeten items had géén enkel item <c>RemainingWork</c>,
    /// <c>CompletedWork</c>, <c>StoryPoints</c> of <c>System.Tags</c>. Een lezer die daar nul van maakt zet
    /// "openstaande uren: 0" op een scherm waar "geen uren ingevuld" hoort te staan.</para>
    ///
    /// <para><c>null</c> teruggeven maakt de hele lezing onleesbaar en niet dit ene item onzichtbaar. Dat
    /// is met opzet: een item dat wegvalt maakt het aantal te laag, en dat is de fout die niemand ziet.
    /// </para>
    /// </remarks>
    private static SprintWorkItem? Read(BatchItem item, IReadOnlyDictionary<string, WorkItemStage> stages)
    {
        var type = item.Field<string>("System.WorkItemType");
        var title = item.Field<string>("System.Title");
        var state = item.Field<string>("System.State");

        if (string.IsNullOrWhiteSpace(type)
            || string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(state)
            || !stages.TryGetValue($"{type}{state}", out var stage)
            || stage == WorkItemStage.Unknown)
        {
            return null;
        }

        var (createdName, createdUnique) = ReadIdentity(item, "System.CreatedBy");
        var (assignedName, assignedUnique) = ReadIdentity(item, "System.AssignedTo");

        return new SprintWorkItem
        {
            Id = item.Id,
            Type = type,
            Title = title,
            State = state,
            Stage = stage,
            Tags = Tags(item.Field<string>("System.Tags")),
            CreatedByName = createdName,
            CreatedByUniqueName = createdUnique,
            AssignedToName = assignedName,
            AssignedToUniqueName = assignedUnique,
            RemainingWork = item.Number("Microsoft.VSTS.Scheduling.RemainingWork"),
            CompletedWork = item.Number("Microsoft.VSTS.Scheduling.CompletedWork"),
            StoryPoints = item.Number("Microsoft.VSTS.Scheduling.StoryPoints"),
        };
    }

    /// <summary>
    /// Splitst het tagveld van DevOps.
    /// </summary>
    /// <param name="raw">De ruwe waarde, of <c>null</c>.</param>
    /// <returns>De tags, zonder lege.</returns>
    /// <remarks>
    /// DevOps levert ze als één tekenreeks met <c>"; "</c> ertussen. Er wordt op <c>;</c> gesplitst en niet
    /// op <c>"; "</c>: de scheiding is het puntkomma en de spatie is opmaak, en een tag met een spatie erin
    /// bestaat.
    /// </remarks>
    private static IReadOnlyList<string> Tags(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : [.. raw.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>
    /// Leest een identiteitsveld, in beide vormen die het kan hebben.
    /// </summary>
    /// <param name="item">De rij.</param>
    /// <param name="field">De veldnaam, bijvoorbeeld <c>System.CreatedBy</c>.</param>
    /// <returns>De weergavenaam en de unieke naam, beide mogelijk <c>null</c>.</returns>
    /// <remarks>
    /// <para><strong>Twee vormen, en dat is geen gok naar twee kanten.</strong> De REST-API geeft volgens de
    /// documentatie een object met <c>displayName</c> en <c>uniqueName</c>; wat er bij mij aankwam was de
    /// tekenreeks <c>"Dennis Verhamme &lt;dennis@soratus.com&gt;"</c>, want de MCP-server waarmee is gemeten
    /// bewerkt het antwoord. De ruwe vorm was dus niet te meten, en dan is een lezer die beide vormen
    /// aankan het enige eerlijke antwoord.</para>
    ///
    /// <para><strong>Uit de tekenreeksvorm wordt géén e-mailadres gepeuterd.</strong> Er staat er wel een
    /// in, tussen punthaken — en het niet lezen is de veilige kant: die vorm is niet gegarandeerd, en een
    /// ontleedregel op een weergavetekst is precies de fout waar <see cref="DevOpsScope"/> tegen bestaat.
    /// Wat het kost is dat de herkomst dan op de weergavenaam vergelijkt in plaats van op het adres, en dat
    /// staat opgeschreven bij <see cref="SprintJudgement.Origin"/>. Wat het oplevert is dat er nooit een
    /// adres op een scherm staat dat wij niet als adres hebben herkend.</para>
    /// </remarks>
    private static (string? Name, string? UniqueName) ReadIdentity(BatchItem item, string field)
    {
        if (item.Fields is null || !item.Fields.TryGetValue(field, out var element))
        {
            return (null, null);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var tekst = element.GetString();
            return (string.IsNullOrWhiteSpace(tekst) ? null : tekst, null);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        return (Text(element, "displayName"), Text(element, "uniqueName"));
    }

    /// <summary>De tekstwaarde van een eigenschap van een JSON-object, of <c>null</c>.</summary>
    /// <param name="element">Het object.</param>
    /// <param name="name">De naam van de eigenschap.</param>
    /// <returns>De tekst, of <c>null</c>.</returns>
    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
        && property.GetString() is { Length: > 0 } value
            ? value
            : null;

    /// <summary>
    /// Zet de categorie van een state om in een <see cref="WorkItemStage"/>.
    /// </summary>
    /// <param name="category">De categorie zoals DevOps hem noemt, of <c>null</c>.</param>
    /// <returns>De fase, of <see cref="WorkItemStage.Unknown"/> bij een categorie die we niet kennen.</returns>
    /// <remarks>
    /// <para>De vier gemeten waarden zijn <c>Proposed</c>, <c>InProgress</c>, <c>Completed</c> en
    /// <c>Removed</c>; <c>Resolved</c> staat erbij uit de documentatie van het Agile-proces en komt op dit
    /// bord niet voor.</para>
    ///
    /// <para><strong>Een onbekende categorie wordt <see cref="WorkItemStage.Unknown"/> en dat maakt de
    /// lezing onleesbaar</strong> — niet stil "niet afgerond". Zou DevOps ooit een vijfde categorie
    /// invoeren, dan hoort dat een zichtbaar defect te zijn en geen statistiek die te laag is.</para>
    /// </remarks>
    private static WorkItemStage Stage(string? category) => category switch
    {
        "Proposed" => WorkItemStage.Proposed,
        "InProgress" => WorkItemStage.InProgress,
        "Resolved" => WorkItemStage.Resolved,
        "Completed" => WorkItemStage.Completed,
        "Removed" => WorkItemStage.Removed,
        _ => WorkItemStage.Unknown,
    };

    /// <summary>Het volledige adres van een aanroep.</summary>
    /// <param name="prefix">Het voorvoegsel uit de scope, met schuine strepen en zonder escaping.</param>
    /// <param name="path">Het deel achter het voorvoegsel.</param>
    /// <returns>De URL.</returns>
    /// <remarks>
    /// <para><strong>Elk segment van het voorvoegsel wordt hier ge-escaped en niet in
    /// <see cref="DevOpsScope"/>.</strong> Die scope is de <em>waarde</em> — hij komt als "bevraagd: …" op
    /// het scherm en in het log, en daar hoort <c>MBVApp4 MAUI</c> te staan en niet
    /// <c>MBVApp4%20MAUI</c>. De codering is mechanisch en hoort dus bij de aanroep. Dat is geen tweede
    /// waarheid: het is één waarde in twee coderingen, en de omzetting staat op één plek.</para>
    ///
    /// <para>De tekens die een URL zouden kunnen breken zijn bovendien al in de validatie verboden
    /// (<c>?</c>, <c>#</c>, <c>%</c>, <c>&amp;</c>), dus wat hier wordt ge-escaped is in de praktijk de
    /// spatie.</para>
    /// </remarks>
    private string Url(string prefix, string path)
    {
        var segments = string.Join(
            '/',
            prefix.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Escape));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_options.Endpoint.TrimEnd('/')}/{segments}/{path}?api-version={_options.ApiVersion}");
    }

    /// <summary>Escapet één padsegment.</summary>
    /// <param name="segment">Het segment.</param>
    /// <returns>Het segment, geschikt voor een URL-pad.</returns>
    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    /// <summary>Wat er van één aanroep terugkwam.</summary>
    /// <typeparam name="T">Het type van het antwoord.</typeparam>
    /// <param name="Value">Het antwoord, of <c>null</c>.</param>
    /// <param name="Failure">Waarom er geen antwoord is, of <c>null</c>.</param>
    /// <param name="Calls">Hoeveel responsen deze aanroep heeft gekost.</param>
    private readonly record struct Response<T>(T? Value, SprintAnswer? Failure, int Calls);

    /// <summary>Doet een GET met de pogingen en de backoff van deze klasse.</summary>
    /// <typeparam name="T">Het type van het antwoord.</typeparam>
    /// <param name="url">Het adres.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het antwoord, of de reden dat er geen is.</returns>
    private Task<Response<T>> GetAsync<T>(string url, CancellationToken cancellationToken) =>
        SendAsync<T>(url, body: null, cancellationToken);

    /// <summary>Doet een POST met de pogingen en de backoff van deze klasse.</summary>
    /// <typeparam name="T">Het type van het antwoord.</typeparam>
    /// <param name="url">Het adres.</param>
    /// <param name="body">Het lichaam.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het antwoord, of de reden dat er geen is.</returns>
    private Task<Response<T>> PostAsync<T>(
        string url,
        object body,
        CancellationToken cancellationToken) =>
        SendAsync<T>(url, body, cancellationToken);

    /// <summary>
    /// Doet één aanroep, met de pogingen en de backoff van deze klasse.
    /// </summary>
    /// <typeparam name="T">Het type van het antwoord.</typeparam>
    /// <param name="url">Het adres.</param>
    /// <param name="body">Het lichaam, of <c>null</c> voor een GET.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het antwoord, of de reden dat er geen is.</returns>
    /// <remarks>
    /// <para><strong>Wachten gebeurt aan het eind van de poging en niet aan het begin van de lus</strong>,
    /// en dat is de reparatie van gat 2 uit punt 41: stond het aan het begin, dan werd er na een geweigerd
    /// verzoek twee keer gewacht, en omdat beide op dezelfde waarde uitkwamen bleef een test groen als de
    /// vloer wegviel.</para>
    ///
    /// <para><strong>Een 401, 403 of 404 wordt niet herhaald.</strong> Die gaan niet over van zichzelf: de
    /// eerste twee zijn een ontbrekende rolverlening en de derde is een bord dat niet bestaat — en DevOps
    /// geeft ook een 404 op een project waar de aanroeper geen recht op heeft, zodat het bestaan ervan niet
    /// lekt. Herhalen kost een aanroep en verandert niets. Dat is een andere keuze dan bij Cost Management,
    /// waar de 404 gemeten "probeer opnieuw" bleek te betekenen; die meting geldt daar en niet hier.</para>
    /// </remarks>
    private async Task<Response<T>> SendAsync<T>(
        string url,
        object? body,
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

                using var request = new HttpRequestMessage(
                    body is null ? HttpMethod.Get : HttpMethod.Post,
                    url);

                if (body is not null)
                {
                    request.Content = JsonContent.Create(body);
                }

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var http = clients.CreateClient(HttpClientName);

                response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                last = "Azure DevOps was niet te bereiken.";
                logger.LogWarning(
                    exception,
                    "api.retry — poging {Attempt} van {Max} aan Azure DevOps is niet aangekomen.",
                    attempt,
                    _options.MaxAttempts);

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
                    try
                    {
                        var payload = await response.Content
                            .ReadFromJsonAsync<T>(cancellationToken)
                            .ConfigureAwait(false);

                        return payload is null
                            ? new Response<T>(
                                default,
                                Unreadable("Azure DevOps gaf een leeg antwoord.", default, calls),
                                calls)
                            : new Response<T>(payload, null, calls);
                    }
                    catch (Exception exception)
                        when (exception is JsonException or NotSupportedException)
                    {
                        // Een antwoord dat er wél was en niet te lezen viel. Unreadable en niet
                        // NotAvailable: er ís geantwoord, en dat ons antwoord niet meer bij de API past
                        // hoort op het scherm te komen in plaats van de vorige lezing te laten staan.
                        return new Response<T>(
                            default,
                            Unreadable(exception.Message, default, calls),
                            calls);
                    }
                }

                last = Refusal(response.StatusCode);

                var retryable = response.StatusCode
                    is HttpStatusCode.TooManyRequests
                    or HttpStatusCode.RequestTimeout
                    or HttpStatusCode.InternalServerError
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout;

                logger.LogWarning(
                    "api.retry — Azure DevOps gaf {Status} op poging {Attempt} van {Max}. "
                    + "Wachthint: {RetryAfter}.",
                    (int)response.StatusCode,
                    attempt,
                    _options.MaxAttempts,
                    Hint(response) ?? "geen");

                if (!retryable)
                {
                    break;
                }

                if (attempt < _options.MaxAttempts)
                {
                    await Task
                        .Delay(Wait(response), timeProvider, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        return new Response<T>(
            default,
            new SprintAnswer(SprintAnswerKind.NotAvailable, default, [], last, calls),
            calls);
    }

    /// <summary>
    /// Hoe lang er na een geweigerd verzoek gewacht wordt.
    /// </summary>
    /// <param name="response">De respons met de hint.</param>
    /// <returns>De wachttijd.</returns>
    /// <remarks>
    /// De <c>Retry-After</c>-hint als hij er is, met <see cref="SprintOptions.BackoffSeconds"/> als vloer.
    /// Die vloer is er om dezelfde reden als bij de kosten, waar gemeten is dat de hint te kort kan zijn —
    /// <strong>hier is dat niet gemeten</strong>, want de MCP-server gaf geen responsheaders door. De vloer
    /// staat er dan op grond van de andere lane en niet op grond van een meting hier, en dat is het
    /// opschrijven waard.
    /// </remarks>
    private TimeSpan Wait(HttpResponseMessage response)
    {
        var hint = Hint(response) is { } text
            && double.TryParse(text, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0
                ? seconds
                : 0;

        return hint > _options.Backoff.TotalSeconds
            ? TimeSpan.FromSeconds(hint)
            : _options.Backoff;
    }

    /// <summary>De ruwe waarde van <c>Retry-After</c>, of <c>null</c>.</summary>
    /// <param name="response">De respons.</param>
    /// <returns>De waarde, of <c>null</c>.</returns>
    /// <remarks>
    /// Alleen de vorm in seconden wordt gelezen en niet de HTTP-datumvorm die de specificatie ook toestaat.
    /// Die tweede is hier niet gemeten en niet gezien, en een parser voor een vorm die je nooit hebt gezien
    /// is een parser die je niet kunt testen; de vloer eronder vangt hem op.
    /// </remarks>
    private static string? Hint(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Retry-After", out var values)
            ? values.FirstOrDefault()
            : null;

    /// <summary>De melding bij een geweigerd verzoek, in taal voor een operator.</summary>
    /// <param name="status">De statuscode.</param>
    /// <returns>De melding.</returns>
    /// <remarks>
    /// Geen statuscode en geen uitzonderingstekst; zie <see cref="SprintDocument.Failure"/>. De technische
    /// vorm staat in de logregel ernaast, met <c>api.retry</c> ervoor.
    /// </remarks>
    private static string Refusal(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.NonAuthoritativeInformation =>
            "Azure DevOps kent de identiteit van het portaal niet. Die moet als service principal lid "
            + "van de organisatie zijn.",
        HttpStatusCode.Forbidden =>
            "Het portaal mag dit bord niet lezen. De identiteit heeft leesrecht op het project nodig.",
        HttpStatusCode.NotFound =>
            "Dit DevOps-bord bestaat niet, of het portaal mag niet zien dat het bestaat. Controleer de "
            + "organisatie, het project en het team.",
        HttpStatusCode.TooManyRequests => "Azure DevOps liet ons niet door.",
        _ => "Azure DevOps gaf geen bruikbaar antwoord.",
    };

    /// <summary>De iteratielijst van een team.</summary>
    /// <remarks>
    /// Het omhulsel <c>{ "count": n, "value": [ … ] }</c> komt uit de documentatie en is niet gemeten: de
    /// MCP-server waarmee is gemeten pakte het uit. Wat er wél is gemeten zijn de veldnamen erin.
    /// </remarks>
    private sealed record IterationList
    {
        /// <summary>De iteraties.</summary>
        [JsonPropertyName("value")]
        public IReadOnlyList<IterationRow> Value { get; init; } = [];
    }

    /// <summary>Eén iteratie zoals DevOps hem geeft.</summary>
    private sealed record IterationRow
    {
        /// <summary>De guid.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        /// <summary>De naam.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        /// <summary>Het boardpad.</summary>
        [JsonPropertyName("path")]
        public string? Path { get; init; }

        /// <summary>De datums.</summary>
        [JsonPropertyName("attributes")]
        public IterationAttributes? Attributes { get; init; }

        /// <summary>Zet deze rij om in het interne type.</summary>
        /// <returns>De iteratie.</returns>
        /// <remarks>
        /// <para><strong>Een ontbrekende naam of guid maakt de iteratie niet ongeldig maar leeg</strong>, en
        /// een iteratie met een lege guid komt nooit als huidige sprint uit
        /// <see cref="SprintSelection"/> — want zonder datums is hij niet gedateerd, en met datums zou hij
        /// een aanroep opleveren die 404 geeft. Dat is de veilige kant, en het is niet nodig hier te
        /// werpen: een lijst die één rare rij bevat hoort niet de hele lezing te kosten.</para>
        ///
        /// <para><strong>De datums worden op de dag afgekapt en niet omgerekend.</strong> Gemeten komt er
        /// <c>2026-08-31T00:00:00Z</c> terug op een verzoek waarin <c>31 augustus 23:59:59</c> stond: DevOps
        /// laat de tijd vallen. Een omrekening naar een lokale zone zou van die middernacht 31 augustus
        /// 02:00 maken, of — een uur de andere kant op — 30 augustus 23:00, en dan mist de sprint een dag.
        /// </para>
        /// </remarks>
        public DevOpsIteration ToIteration() => new()
        {
            Id = Id ?? string.Empty,
            Name = Name ?? string.Empty,
            Path = Path ?? string.Empty,
            Start = Day(Attributes?.StartDate),
            Finish = Day(Attributes?.FinishDate),
        };

        /// <summary>De dag van een iteratiedatum, of <c>null</c>.</summary>
        /// <param name="moment">Het moment zoals DevOps het gaf.</param>
        /// <returns>De dag.</returns>
        private static DateOnly? Day(DateTimeOffset? moment) =>
            moment is { } value ? DateOnly.FromDateTime(value.UtcDateTime) : null;
    }

    /// <summary>De datums van een iteratie.</summary>
    /// <remarks>
    /// <c>timeFrame</c> staat er met opzet niet op. Gemeten stond dat veld op <c>2</c> (future) voor zowel
    /// de iteraties in de toekomst als de drie iteraties zónder datums, dus het kan die twee niet
    /// onderscheiden — en dat is precies het onderscheid waar deze lane om draait. Een veld dat je niet
    /// gebruikt hoort niet in een DTO te staan, want dan kan de volgende lezer denken dat het klopt.
    /// </remarks>
    private sealed record IterationAttributes
    {
        /// <summary>De begindatum, of <c>null</c>.</summary>
        [JsonPropertyName("startDate")]
        public DateTimeOffset? StartDate { get; init; }

        /// <summary>De einddatum, of <c>null</c>. Inclusief.</summary>
        [JsonPropertyName("finishDate")]
        public DateTimeOffset? FinishDate { get; init; }
    }

    /// <summary>De work items van één iteratie, als relaties.</summary>
    /// <remarks>
    /// Gemeten vorm: <c>workItemRelations</c> met per rij een <c>target.id</c>, waarbij de bovenste rijen
    /// <c>rel: null</c> hebben en de rest een hiërarchierelatie. Alleen de doelnummers worden gelezen — de
    /// hiërarchie zelf niet, want §3.4 vraagt een lijst en geen boom.
    /// </remarks>
    private sealed record IterationWorkItems
    {
        /// <summary>De relaties.</summary>
        [JsonPropertyName("workItemRelations")]
        public IReadOnlyList<WorkItemRelation> WorkItemRelations { get; init; } = [];
    }

    /// <summary>Eén relatie uit de iteratielijst.</summary>
    private sealed record WorkItemRelation
    {
        /// <summary>Het work item waar deze relatie naar wijst.</summary>
        [JsonPropertyName("target")]
        public WorkItemReference? Target { get; init; }
    }

    /// <summary>Een verwijzing naar een work item.</summary>
    private sealed record WorkItemReference
    {
        /// <summary>Het nummer.</summary>
        [JsonPropertyName("id")]
        public int? Id { get; init; }
    }

    /// <summary>Het antwoord van de veldenbatch.</summary>
    private sealed record BatchList
    {
        /// <summary>De work items.</summary>
        [JsonPropertyName("value")]
        public IReadOnlyList<BatchItem> Value { get; init; } = [];
    }

    /// <summary>Eén work item uit de veldenbatch.</summary>
    /// <remarks>
    /// De velden staan in een woordenboek van <see cref="JsonElement"/> en niet in getypeerde
    /// eigenschappen. Dat is met opzet: de sleutels zijn veldnamen van DevOps met punten erin, hun
    /// aanwezigheid is niet gegarandeerd, en één van de waarden — het identiteitsveld — heeft een vorm die
    /// niet te meten was. Een DTO met vaste eigenschappen zou dat verschil wegpoetsen.
    /// </remarks>
    private sealed record BatchItem
    {
        /// <summary>Het nummer.</summary>
        [JsonPropertyName("id")]
        public int Id { get; init; }

        /// <summary>De gevraagde velden, voor zover ze een waarde hadden.</summary>
        [JsonPropertyName("fields")]
        public IReadOnlyDictionary<string, JsonElement>? Fields { get; init; }

        /// <summary>De waarde van een veld, of <c>default</c> als het ontbreekt.</summary>
        /// <typeparam name="T">Het type.</typeparam>
        /// <param name="name">De veldnaam.</param>
        /// <returns>De waarde, of <c>default</c>.</returns>
        public T? Field<T>(string name) =>
            Fields is not null && Fields.TryGetValue(name, out var element)
                ? element.Deserialize<T>()
                : default;

        /// <summary>
        /// De getalwaarde van een veld, of <c>null</c> als het ontbreekt of geen getal is.
        /// </summary>
        /// <param name="name">De veldnaam.</param>
        /// <returns>Het getal, of <c>null</c>.</returns>
        /// <remarks>
        /// <strong><c>null</c> en nooit nul.</strong> Dat is de invariant van dit hele onderdeel: een veld
        /// dat niet is ingevuld ontbreekt in dit woordenboek, en nul zou betekenen dat iemand nul heeft
        /// ingevuld. Zie <see cref="SprintTally"/> voor wat er verder mee gebeurt.
        /// </remarks>
        public decimal? Number(string name) =>
            Fields is not null
            && Fields.TryGetValue(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDecimal(out var value)
                ? value
                : null;
    }

    /// <summary>De metadata van één werkitemsoort. Alleen de states worden gelezen.</summary>
    /// <remarks>
    /// Dit antwoord is groot — het bevat het volledige formulier als XML — en er worden twee velden per
    /// state uit gehaald. Dat is een bewuste ruil: dit is het endpoint waarvan de <em>vorm is gemeten</em>,
    /// en de smallere variant (<c>…/states</c>) is dat niet. Een kleiner antwoord waarvan je de vorm niet
    /// hebt gezien is duurder dan een groot antwoord waarvan je hem wél hebt gezien, en dat is precies wat
    /// dertien valse metingen in dit project hebben gekost.
    /// </remarks>
    private sealed record WorkItemTypeMetadata
    {
        /// <summary>De states van deze soort.</summary>
        [JsonPropertyName("states")]
        public IReadOnlyList<WorkItemTypeState> States { get; init; } = [];
    }

    /// <summary>Eén state van een werkitemsoort.</summary>
    private sealed record WorkItemTypeState
    {
        /// <summary>De naam, bijvoorbeeld <c>Active</c>.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        /// <summary>De categorie, bijvoorbeeld <c>InProgress</c>.</summary>
        [JsonPropertyName("category")]
        public string? Category { get; init; }
    }
}
