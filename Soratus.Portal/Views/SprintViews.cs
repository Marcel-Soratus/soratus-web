using System.Globalization;
using Microsoft.Extensions.Options;
using Soratus.Portal.Data;
using Soratus.Portal.Security;
using Soratus.Portal.Sprints;

namespace Soratus.Portal.Views;

/// <summary>
/// De enige implementatie van <see cref="ISprintViews"/>: de projectie van het sprintdocument naar de twee
/// rolvormen.
/// </summary>
/// <remarks>
/// <para><strong>Eén puntlezing per weergave, en verder rekent deze klasse alleen.</strong> De
/// statistieken komen uit <see cref="SprintTally.Of"/> en de twee oordelen uit
/// <see cref="SprintJudgement"/> — beide puur, beide met een eigen test. Deze klasse voegt de rolgrens toe
/// en de teksten, en niets anders.</para>
///
/// <para><strong>De operatorkant leest één document extra, voor precies één veld.</strong> Het
/// klantdocument, voor het vastgelegde bord — een puntlezing van ongeveer één RU, en hij koopt het
/// onderscheid dat de lege pagina zelf niet kan maken: "niet ingericht" tegenover "nog niet opgehaald".
/// Zonder hem wacht een operator op een ophaling die nooit komt. Exact de constructie die
/// <see cref="BillingViews"/> voor de Azure-scope heeft.</para>
///
/// <para><strong>En de klantkant leest dat document níet.</strong> Dat is geen zuinigheid maar de
/// rolgrens: <see cref="IPortalDataStore.GetCustomerAsync"/> neemt alleen een
/// <see cref="CustomerWriteScope"/>, en de klant hoort het bord ook niet te zien. Wat de klant in plaats
/// daarvan krijgt is <see cref="SprintNotice.CustomerUnknown"/> — dezelfde waarheid ("wij hebben hier nog
/// niets gelezen") zonder de reden, want die reden is de koppeling.</para>
///
/// <para><strong>De volgorde van de items is die van DevOps en wordt hier niet gewijzigd.</strong> Dat is
/// een keuze: sorteren op state of op uren zou een rangorde suggereren die het bord niet heeft, en de
/// volgorde van de iteratie-aanroep is de volgorde waarin de items op het bord staan. Wie een andere
/// ordening wil, sorteert in de tabel — en dan staat er op het scherm waarop.</para>
/// </remarks>
internal sealed class SprintViews(
    IPortalSprintStore sprints,
    IPortalDataStore store,
    IOptions<SprintOptions> options,
    TimeProvider timeProvider,
    ILogger<SprintViews> logger) : ISprintViews
{
    private readonly SprintOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<CustomerSprintView> BuildSprintAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var document = await sprints.GetSprintAsync(scope, cancellationToken).ConfigureAwait(false);
        var state = State(document);
        var items = document?.Items ?? [];

        logger.LogDebug(
            "Sprintweergave (klant) van {CustomerId}: {State}, {Items} item(s).",
            scope.CustomerId,
            state,
            items.Count);

        return new CustomerSprintView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            GeneratedAt = timeProvider.GetUtcNow(),
            State = state,
            StateNotice = CustomerNotice(state),
            SprintName = document?.SprintName,
            Start = Day(document?.Start),
            Finish = Day(document?.Finish),
            BoardPath = document?.BoardPath,
            ReadAt = document?.ReadAt,
            Tally = SprintTally.Of(items, _options.BlockedMarker),
            Items = [.. items.Select(CustomerRow)],
            UndatedCount = document?.Undated.Count ?? 0,
            UndatedNotice = document?.Undated.Count > 0 ? SprintNotice.Undated : null,
            ReadOnlyNotice = SprintNotice.ReadOnly,
            SnapshotNotice = SprintNotice.Snapshot,
            HoursNotice = SprintNotice.HoursUnknown,
        };
    }

    /// <inheritdoc />
    public async Task<OperatorSprintView> BuildSprintAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var document = await sprints.GetSprintAsync(scope, cancellationToken).ConfigureAwait(false);

        // Het klantdocument, voor precies één veld: het vastgelegde bord. Uit het document en niet uit de
        // klantenlijst — die is een momentopname die bij een koude start nog uit de configuratie kan
        // komen, en daar staat geen bord in. Zie ContractViews, dat om dezelfde reden geen terugval op
        // het configuratierecord heeft voor dit veld.
        var customer = await store.GetCustomerAsync(scope, cancellationToken).ConfigureAwait(false);

        var state = State(document);
        var items = document?.Items ?? [];

        logger.LogDebug(
            "Sprintweergave (operator) van {CustomerId}: {State}, {Items} item(s), bord {Board}.",
            scope.CustomerId,
            state,
            items.Count,
            customer?.DevOpsScope ?? "geen");

        return new OperatorSprintView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            GeneratedAt = timeProvider.GetUtcNow(),
            State = state,
            StateNotice = OperatorNotice(state),
            SprintName = document?.SprintName,
            Start = Day(document?.Start),
            Finish = Day(document?.Finish),
            BoardPath = document?.BoardPath,
            ReadAt = document?.ReadAt,
            Tally = SprintTally.Of(items, _options.BlockedMarker),
            Items = [.. items.Select(item => OperatorRow(item))],
            DevOpsScope = customer?.DevOpsScope,
            QueriedScope = document?.Scope,
            ScopeNotice = ScopeNotice(customer?.DevOpsScope),
            Failure = document?.Failure,
            Undated = document?.Undated ?? [],
            UndatedNotice = document?.Undated.Count > 0 ? SprintNotice.Undated : null,
            Overlapping = document?.Overlapping ?? [],
            DatedCount = document?.DatedCount ?? 0,
            ReadOnlyNotice = SprintNotice.ReadOnly,
            SnapshotNotice = SprintNotice.Snapshot,
            HoursNotice = SprintNotice.HoursUnknown,
        };
    }

    /// <summary>
    /// De toestand van een document, of <see cref="SprintState.Unknown"/> als er geen document is.
    /// </summary>
    /// <param name="document">Het document, of <c>null</c>.</param>
    /// <returns>De toestand.</returns>
    /// <remarks>
    /// <strong>Dit is de enige plek waar de afwezigheid van een document een toestand wordt.</strong>
    /// Dezelfde regel en dezelfde enige-plek-eis als bij <c>AzureCostReading.From</c>: "geen document
    /// betekent geen sprint" (punt 2), en zou die omzetting op twee plekken staan, dan zou de ene op een
    /// dag "leeg" zeggen waar de andere "onbekend" zegt.
    /// </remarks>
    private static SprintState State(SprintDocument? document) =>
        document?.State ?? SprintState.Unknown;

    /// <summary>De klanttekst bij een toestand, of <c>null</c> als er niets uit te leggen is.</summary>
    /// <param name="state">De toestand.</param>
    /// <returns>De tekst, of <c>null</c>.</returns>
    /// <remarks>
    /// Vijf van de zes toestanden krijgen een zin; <see cref="SprintState.Current"/> krijgt er geen, want
    /// dan staat de sprint er gewoon. Een <c>switch</c> zonder <c>default</c>-tak die iets verzint: een
    /// nieuwe waarde in de enum levert hier een compileerwaarschuwing op in plaats van een stille lege
    /// tekst.
    /// </remarks>
    private static string? CustomerNotice(SprintState state) => state switch
    {
        SprintState.Unknown => SprintNotice.CustomerUnknown,
        SprintState.NoIterations => SprintNotice.NoIterations,
        SprintState.NoDatedIterations => SprintNotice.NoDatedIterations,
        SprintState.NoCurrentSprint => SprintNotice.NoCurrentSprint,
        SprintState.Ambiguous => SprintNotice.CustomerAmbiguous,
        _ => null,
    };

    /// <summary>De operatortekst bij een toestand, of <c>null</c>.</summary>
    /// <param name="state">De toestand.</param>
    /// <returns>De tekst, of <c>null</c>.</returns>
    /// <remarks>
    /// Twee van de vijf teksten wijken af van de klantvariant, en dat is geen opmaak: de operatorvarianten
    /// noemen wat er in DevOps moet gebeuren. De drie die gelijk zijn, zijn dat omdat de mededeling
    /// dezelfde is en de handeling voor beide rollen buiten het portaal ligt.
    /// </remarks>
    private static string? OperatorNotice(SprintState state) => state switch
    {
        SprintState.Unknown => SprintNotice.OperatorUnknown,
        SprintState.NoIterations => SprintNotice.NoIterations,
        SprintState.NoDatedIterations => SprintNotice.NoDatedIterations,
        SprintState.NoCurrentSprint => SprintNotice.NoCurrentSprint,
        SprintState.Ambiguous => SprintNotice.OperatorAmbiguous,
        _ => null,
    };

    /// <summary>
    /// Waarom er voor deze klant niets wordt opgehaald, of <c>null</c> als er wél wordt opgehaald.
    /// </summary>
    /// <param name="scope">Het vastgelegde bord, of <c>null</c>.</param>
    /// <returns>De tekst, of <c>null</c>.</returns>
    /// <remarks>
    /// <para><strong>Drie gevallen en niet twee, en dat is dezelfde keten als bij de kosten.</strong> Er is
    /// een bruikbaar bord (geen melding), er is er geen (niet ingericht), of er staat er een die niet werkt
    /// (een fout). De laatste twee leveren beide een lege pagina op en vragen een verschillende handeling:
    /// iets invullen tegenover iets corrigeren.</para>
    ///
    /// <para><strong>Dezelfde functie die de schrijfkant en de collector gebruiken.</strong> Zou hier een
    /// eigen controle staan, dan is er een pad waarop het scherm een bord goedkeurt dat de collector
    /// weigert — en dan staat er "wordt opgehaald" bij een klant die niet wordt opgehaald. Dat is gat 1 uit
    /// punt 41 en het is daar met een mutatie gevonden.</para>
    /// </remarks>
    private static string? ScopeNotice(string? scope) =>
        string.IsNullOrWhiteSpace(scope) ? SprintNotice.NoScopeConfigured
        : DevOpsScope.TryParse(scope, out _) ? null
        : SprintNotice.ScopeUnusable;

    /// <summary>De klantvariant van één work item.</summary>
    /// <param name="item">Het item uit het document.</param>
    /// <returns>De rij.</returns>
    /// <remarks>
    /// <strong>Geen aanmaker en geen adressen.</strong> Zie <see cref="CustomerSprintRow"/>: die velden
    /// bestaan op dat type niet, dus dit is geen filter maar een projectie op een smaller type. De herkomst
    /// gaat wél mee — dat is de vraag die §3.4 stelt, en die is te beantwoorden zonder een naam te noemen.
    /// </remarks>
    private CustomerSprintRow CustomerRow(SprintWorkItem item) => new()
    {
        Id = item.Id,
        Type = item.Type,
        Title = item.Title,
        State = item.State,
        Stage = item.Stage,
        Tags = item.Tags,
        Origin = SprintJudgement.Origin(item, _options.AgentIdentities),
        IsBlocked = SprintJudgement.IsBlocked(item, _options.BlockedMarker),
        AssignedTo = item.AssignedToName,
        OpenHours = item.RemainingWork,
        DoneHours = item.CompletedWork,
        StoryPoints = item.StoryPoints,
    };

    /// <summary>De operatorvariant van één work item.</summary>
    /// <param name="item">Het item uit het document.</param>
    /// <returns>De rij.</returns>
    private OperatorSprintRow OperatorRow(SprintWorkItem item) => new()
    {
        Id = item.Id,
        Type = item.Type,
        Title = item.Title,
        State = item.State,
        Stage = item.Stage,
        Tags = item.Tags,
        Origin = SprintJudgement.Origin(item, _options.AgentIdentities),
        IsBlocked = SprintJudgement.IsBlocked(item, _options.BlockedMarker),
        AssignedTo = item.AssignedToName,
        AssignedToAddress = item.AssignedToUniqueName,
        CreatedBy = item.CreatedByName,
        CreatedByAddress = item.CreatedByUniqueName,
        OpenHours = item.RemainingWork,
        DoneHours = item.CompletedWork,
        StoryPoints = item.StoryPoints,
    };

    /// <summary>
    /// De dag uit de opslagvorm, of <c>null</c>.
    /// </summary>
    /// <param name="text">De dag als <c>jjjj-MM-dd</c>, of <c>null</c>.</param>
    /// <returns>De dag, of <c>null</c> als er niets staat of het niet te lezen is.</returns>
    /// <remarks>
    /// <para><strong>Onleesbaar wordt <c>null</c> en niet een verzonnen dag.</strong> Een datum die niet te
    /// lezen is betekent dat de periode onbekend is, en een periode die we niet kennen hoort niet als
    /// vandaag of als 1 januari op het scherm te komen. Dat kan alleen bij een document uit een oudere vorm
    /// of een handmatige wijziging; de schrijfkant schrijft altijd <c>jjjj-MM-dd</c>.</para>
    ///
    /// <para><c>DateOnly.TryParseExact</c> en niet <c>TryParse</c>: dat laatste leest ook
    /// <c>08-31-2026</c> en <c>31/08/2026</c>, en welke van de twee er uitkomt hangt dan van de cultuur van
    /// de server af. Dezelfde regel als bij de tijdvorm van punt 7.</para>
    /// </remarks>
    private static DateOnly? Day(string? text) =>
        DateOnly.TryParseExact(
            text,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var day)
            ? day
            : null;
}
