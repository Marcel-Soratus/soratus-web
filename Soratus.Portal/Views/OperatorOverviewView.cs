using Soratus.Agents.Contracts;
using Soratus.Portal.Data;

namespace Soratus.Portal.Views;

/// <summary>
/// Het Soratus-overzicht over alle klanten (§3.1).
/// </summary>
/// <remarks>
/// Bestaat alleen in de operatorvariant. Er is geen klantversie van dit type en die hoort er ook
/// niet te komen: een klantgebruiker kan geen <c>OperatorScope</c> in handen krijgen, dus hij kan
/// deze weergave niet laten opbouwen.
/// </remarks>
public sealed record OperatorOverviewView
{
    /// <summary>Wanneer dit overzicht is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>De KPI-rij bovenaan, afgeleid uit <see cref="Customers"/>.</summary>
    public required OperatorOverviewKpis Kpis { get; init; }

    /// <summary>
    /// De klanten, gesorteerd op ernst en dan op recentheid: failed(4) &gt; degraded(3) &gt;
    /// live(2) &gt; idle(1) &gt; geen agents(0). Idle tilt een klant dus nooit naar boven.
    /// </summary>
    /// <remarks>
    /// De sortering komt uit <see cref="CustomerSeverity.Sort{T}"/> in de contractbibliotheek en
    /// niet uit een <c>OrderBy</c> hier, zodat de tests van het contract en dit scherm dezelfde
    /// volgorde opleveren. De sortering is stabiel: klanten die op ernst en recentheid gelijk
    /// uitkomen houden hun volgorde en springen niet van plek bij het verversen.
    ///
    /// Klanten waarvan de opslag niet antwoordde staan hier gewoon in, met
    /// <see cref="OperatorCustomerRow.Unavailable"/> gevuld en status <c>Unknown</c>. Ze zakken
    /// daarmee naar onderen — rang 0 — maar ze verdwijnen niet.
    /// </remarks>
    public required IReadOnlyList<OperatorCustomerRow> Customers { get; init; }

    /// <summary>
    /// Of er klanten zijn waarvan de opslag niet te lezen was. Het scherm hoort dat te melden en
    /// niet stilzwijgend een lager totaal te tonen.
    /// </summary>
    public bool HasUnavailableCustomers => Kpis.UnavailableCount > 0;
}

/// <summary>
/// De KPI-rij van het overzicht.
/// </summary>
/// <remarks>
/// <strong>Alles hier is afgeleid, niets is gekopieerd.</strong> <see cref="Statuses"/> is de som
/// van de statusverdelingen van precies de klantrijen die eronder staan, dus "13 live, 1 degraded"
/// en de lijst zijn dezelfde telling; ze kunnen niet uit elkaar lopen zonder dat de lijst zelf
/// verandert. Hetzelfde geldt voor de runtellingen: die komen uit dezelfde leesactie per klant, en
/// het foutpercentage komt uit dezelfde telling als het runaantal en niet uit een tweede query.
/// </remarks>
public sealed record OperatorOverviewKpis
{
    /// <summary>Het aantal klanten in de lijst.</summary>
    public required int CustomerCount { get; init; }

    /// <summary>
    /// Het aantal klanten dat helemaal nog geen agents heeft. Die zijn ingericht maar nog niet
    /// uitgerold (§3.9); dat is geen storing.
    /// </summary>
    /// <remarks>
    /// Telt uitsluitend klanten met nul agents in élke omgeving. Een klant die alleen op acceptatie
    /// draait is niet in onboarding — die is bezig — en staat in
    /// <see cref="NonProductionOnlyCount"/>.
    /// </remarks>
    public required int OnboardingCount { get; init; }

    /// <summary>
    /// Het aantal klanten met agents, maar geen enkele in productie.
    /// </summary>
    public required int NonProductionOnlyCount { get; init; }

    /// <summary>
    /// Het aantal klanten waarvan de opslag niet antwoordde. Hun agents zitten niet in
    /// <see cref="Statuses"/> en hun runs niet in de tellingen — de getallen hieronder gaan dus
    /// over de klanten die we wél konden lezen, en het scherm hoort dat erbij te zetten.
    /// </summary>
    public required int UnavailableCount { get; init; }

    /// <summary>
    /// De statusverdeling over de <em>productie</em>-agents van alle leesbare klanten.
    /// </summary>
    /// <remarks>
    /// Dit is de KPI die de vraag "is er ergens iets mis" beantwoordt. Alleen productie, om
    /// dezelfde reden als bij <see cref="OperatorCustomerRow.Severity"/>.
    /// </remarks>
    public required AgentStatusBreakdown Statuses { get; init; }

    /// <summary>
    /// De statusverdeling over de agents buiten productie.
    /// </summary>
    /// <remarks>
    /// Hoort op het scherm als een rustige teller — "n agents buiten productie, waarvan m met
    /// problemen" — en niet in een statuskleur. Het is informatie, geen alarm; §8 reserveert
    /// groen, amber en rood voor status en dit is er geen.
    /// </remarks>
    public required AgentStatusBreakdown NonProductionStatuses { get; init; }

    /// <summary>Het aantal productie-agents. Gelijk aan <c>Statuses.Total</c>.</summary>
    public int AgentCount => Statuses.Total;

    /// <summary>Het aantal agents buiten productie.</summary>
    public int NonProductionAgentCount => NonProductionStatuses.Total;

    /// <summary>
    /// Hoeveel agents buiten productie aandacht vragen: failed plus degraded.
    /// </summary>
    public int NonProductionAttention => NonProductionStatuses.Attention;

    /// <summary>
    /// Het begin van de dag waarover <see cref="RunsToday"/> gaat, in de tijdzone van het scherm.
    /// </summary>
    /// <remarks>
    /// Staat erbij zodat de kop eerlijk kan zijn over wat "vandaag" betekent. Een KPI "runs
    /// vandaag" zonder te zeggen vanaf welk moment is een getal dat om 00:30 iets anders betekent
    /// dan om 23:30.
    /// </remarks>
    public required DateTimeOffset TodayStartedAt { get; init; }

    /// <summary>De runs die vandaag zijn gestart, uitgesplitst naar afloop.</summary>
    public required RunTally Today { get; init; }

    /// <summary>De runs van de laatste 24 uur, uitgesplitst naar afloop.</summary>
    public required RunTally Last24Hours { get; init; }

    /// <summary>Hoeveel runs er vandaag zijn gestart.</summary>
    public int RunsToday => Today.Total;

    /// <summary>Hoeveel daarvan mislukten.</summary>
    public int RunsFailedToday => Today.Failed;

    /// <summary>
    /// Het foutpercentage over 24 uur, of <c>null</c> als er in die 24 uur niets is afgerond.
    /// </summary>
    /// <remarks>
    /// <c>null</c> en niet nul. Nul procent fout suggereert dat het goed ging; er ging niets.
    /// </remarks>
    public double? ErrorRate24Hours => Last24Hours.ErrorRate;
}

/// <summary>
/// Eén klant op het overzicht.
/// </summary>
public sealed record OperatorCustomerRow
{
    /// <summary>De slug, ook het pad om door te klikken.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Of dit de interne beheerklant is (§4).</summary>
    public required bool IsInternal { get; init; }

    /// <summary>Korte omgevingsaanduiding.</summary>
    public string? Environment { get; init; }

    /// <summary>
    /// De volledige omgeving, bijvoorbeeld <c>sub-soratus-acme · rg-acme-prod</c>. Operator-only —
    /// dit veld bestaat niet op enig klanttype.
    /// </summary>
    public string? EnvironmentDetail { get; init; }

    /// <summary>
    /// De statusverdeling van de <em>productie</em>-agents van deze klant.
    /// </summary>
    /// <remarks>
    /// Alleen productie, net als <see cref="Severity"/>. De balk en de rangkolom in dezelfde rij
    /// moeten hetzelfde verhaal vertellen; zou de balk alle omgevingen tellen en de rang alleen
    /// productie, dan spreken twee cellen naast elkaar elkaar tegen. Zie
    /// <see cref="NonProductionStatuses"/> voor de rest.
    /// </remarks>
    public required AgentStatusBreakdown Statuses { get; init; }

    /// <summary>
    /// De statusverdeling van de agents buiten productie: acceptatie en ontwikkeling.
    /// </summary>
    /// <remarks>
    /// Telt niet mee in <see cref="Severity"/> en niet in de sortering. Staat er wél, want een
    /// operator hoort te kunnen zien dat er buiten productie iets stuk is — het is alleen geen
    /// alarm, en het scherm hoort het dus ook niet als alarm te tonen.
    /// </remarks>
    public required AgentStatusBreakdown NonProductionStatuses { get; init; }

    /// <summary>
    /// Het samengevatte klantbeeld: ernstigste status, laatste activiteit en aantal agents. Komt
    /// uit <see cref="CustomerSeverity.FromAgents"/> over <em>uitsluitend de productie-agents</em>.
    /// </summary>
    /// <remarks>
    /// Dit is de rang waarop het overzicht sorteert, en de reden dat hij alleen over productie gaat
    /// staat in <c>docs/agent-portal/fase-0-afwijkingen.md</c> §9. Kort: het overzicht beantwoordt
    /// de vraag "is er ergens iets mis bij een klant", en een uitgezette acceptatie-agent is dat
    /// niet. Zou een kapotte dev-agent een klant naar boven tillen, dan verliest de sortering
    /// precies de betekenis waarvoor hij bestaat en gaat een operator de bovenste rijen wegkijken.
    /// </remarks>
    public required CustomerSeverity Severity { get; init; }

    /// <summary>De runs van deze klant vandaag.</summary>
    public required RunTally Today { get; init; }

    /// <summary>De runs van deze klant in de laatste 24 uur.</summary>
    public required RunTally Last24Hours { get; init; }

    /// <summary>
    /// Waarom deze klant niet te lezen was, of <c>null</c> als dat wel lukte.
    /// </summary>
    /// <remarks>
    /// Is dit gevuld, dan zegt de rij niets over de agents van deze klant — er staat geen "0
    /// agents" maar "onbekend". Het scherm hoort <see cref="TelemetryUnavailable.Reason"/> te tonen
    /// in plaats van een lege statusbalk.
    /// </remarks>
    public TelemetryUnavailable? Unavailable { get; init; }

    /// <summary>Of de opslag van deze klant antwoordde.</summary>
    public bool IsAvailable => Unavailable is null;

    /// <summary>Het aantal productie-agents.</summary>
    public int AgentCount => Statuses.Total;

    /// <summary>Het aantal agents buiten productie.</summary>
    public int NonProductionAgentCount => NonProductionStatuses.Total;

    /// <summary>
    /// Deze klant heeft wél agents, maar geen enkele in productie.
    /// </summary>
    /// <remarks>
    /// Bestaat zodat het scherm niet hoeft te liegen. Zo'n klant komt op rang 0 uit — hetzelfde als
    /// een klant zonder agents — maar "geen agents" is dan onwaar. De eerlijke formulering is
    /// "geen agents in productie", met het aantal buiten productie erachter. Zonder dit veld zou de
    /// pagina die twee gevallen niet uit elkaar kunnen houden.
    /// </remarks>
    public bool HasOnlyNonProductionAgents => AgentCount == 0 && NonProductionAgentCount > 0;

    /// <summary>De ernstigste status binnen deze klant.</summary>
    public AgentStatus Status => Severity.Status;

    /// <summary>Het jongste moment waarop een agent van deze klant iets deed.</summary>
    public DateTimeOffset? LastActivityAt => Severity.LastActivityAt;
}
