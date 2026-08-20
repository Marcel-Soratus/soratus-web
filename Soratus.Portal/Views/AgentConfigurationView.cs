using Soratus.Agents.Contracts;

namespace Soratus.Portal.Views;

/// <summary>
/// De teksten van het tabblad Configuratie die geen gegeven zijn maar een mededeling.
/// </summary>
/// <remarks>
/// Eén plek, en de viewmodels dragen de tekst mee in plaats van dat het scherm hem verzint. Dat is
/// geen smaakkwestie: "ingrijpen kan niet" is een bewering over wat het portaal in deze fase kán,
/// en die bewering hoort te veranderen op het moment dat het portaal verandert — niet op het moment
/// dat iemand een Razor-bestand herschrijft.
/// </remarks>
public static class AgentConfigurationNotice
{
    /// <summary>
    /// Waarom er op dit tabblad geen knoppen staan.
    /// </summary>
    /// <remarks>
    /// De spec is expliciet (§3.3, en §7 onder "Later, buiten scope"): pauzeren, herstarten en
    /// limieten wijzigen zitten niet in dit ontwerp. Er staat dus een melding en geen uitgegrijsde
    /// knop — een knop die niets doet belooft dat het wél kan en laat de lezer zoeken naar het
    /// recht dat hij mist.
    /// </remarks>
    public const string ReadOnly =
        "Read-only. Configuratie wordt beheerd via de deployment-pipeline van Soratus; ingrijpen " +
        "vanuit het portaal (pauzeren, herstarten, limieten wijzigen) kan in deze fase niet.";

    /// <summary>
    /// Waarom de identity van deze agent niet op het tabblad staat, en waar hij wél staat.
    /// </summary>
    /// <remarks>
    /// <para>Alleen over identity, en alleen voor de operator. §3.3 noemt vier velden die
    /// <see cref="AgentRegistration"/> niet publiceert, en die vier zijn niet hetzelfde geval — zie
    /// <c>docs/agent-portal/fase-0-afwijkingen.md</c> §11:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     Resource-limieten en image <em>bestaan niet</em> op dit platform. Wij draaien op App
    ///     Service, niet op Container Apps: een agent heeft geen eigen image en limieten hangen aan
    ///     het plan. Daarover valt niets te melden, ook niet dat het er nog niet is.
    ///   </description></item>
    ///   <item><description>
    ///     Resource group staat er wél, in <see cref="OperatorAgentConfigurationView.EnvironmentDetail"/>.
    ///   </description></item>
    ///   <item><description>
    ///     Identity is een echt gat, en de enige die een melding verdient. Hij is Azure-metadata en
    ///     geen telemetrie; een agent die zijn eigen identity publiceert publiceert iets wat hij van
    ///     buiten zichzelf zou moeten opvragen.
    ///   </description></item>
    /// </list>
    ///
    /// <para>Een eerdere versie van deze constante noemde alle vier de velden in één zin en beloofde
    /// dat ze erbij zouden komen "zodra het agentcontract ze meestuurt". Dat was voor drie van de
    /// vier onwaar, en een melding die drie dingen belooft die nooit komen is erger dan geen
    /// melding.</para>
    /// </remarks>
    public const string IdentityElsewhere =
        "De managed identity van deze agent staat er niet bij: dat is Azure-metadata en geen " +
        "telemetrie. Hij is te vinden in Azure, onder de resource group hierboven.";
}

/// <summary>
/// Het tabblad Configuratie zoals de klant het ziet (§3.3). Read-only.
/// </summary>
/// <remarks>
/// <para>Alles op dit type komt uit het registratiedocument dat de agent over zichzelf publiceert,
/// plus de twee bewaartermijnen uit het contract. Er wordt niets uit Azure opgehaald en niets
/// geraden.</para>
///
/// <para>Wat er níet op staat, staat er niet als leeg veld: geen omgeving, geen contractversie, geen
/// opslaglocatie, geen levenscyclus. Die zijn operator-only en staan op
/// <see cref="OperatorAgentConfigurationView"/>. Dezelfde regel als bij
/// <see cref="CustomerAgentsView"/>: een ontbrekende property kan niet lekken, ook niet als iemand
/// er over een half jaar een rij bij zet en het <c>@if</c> vergeet.</para>
///
/// <para><strong>De spec vraagt hier meer dan de klant mag zien.</strong> §3.3 noemt ook
/// resource-limieten, image, resource group en identity. Die staan er niet, en om drie
/// verschillende redenen: de eerste twee bestaan niet op App Service, de derde is operator-only en
/// de vierde is Azure-metadata en geen telemetrie. Zie
/// <c>docs/agent-portal/fase-0-afwijkingen.md</c> §11. Er staat dus ook geen lege rij en geen
/// melding — voor een klant is dit tabblad simpelweg korter.</para>
/// </remarks>
public sealed record CustomerAgentConfigurationView
{
    /// <summary>De technische naam van de agent.</summary>
    public required string AgentName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>De versie die nu draait.</summary>
    public required string Version { get; init; }

    /// <summary>
    /// De cron-expressie waarop deze agent plant, of <c>null</c> bij een agent die alleen op een
    /// trigger draait.
    /// </summary>
    public string? Schedule { get; init; }

    /// <summary>Waardoor deze agent aan het werk gaat.</summary>
    public required TriggerKind TriggerKind { get; init; }

    /// <summary>Toelichting op de trigger, bijvoorbeeld <c>Timer + webhook (WMS)</c>.</summary>
    public string? TriggerDetail { get; init; }

    /// <summary>
    /// De eerstvolgende geplande run, of <c>null</c> bij een agent die alleen op een trigger
    /// draait. Toon dan de trigger en niet een verzonnen tijdstip.
    /// </summary>
    public DateTimeOffset? NextRunAt { get; init; }

    /// <summary>Sinds wanneer dit proces loopt.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>De laatste hartslag.</summary>
    public required DateTimeOffset LastHeartbeatAt { get; init; }

    /// <summary>
    /// Hoe lang deze agent al zwijgt, of <c>null</c> als er geen registratie is.
    /// </summary>
    /// <remarks>
    /// Staat hier zodat de laatste hartslag op dit tabblad niet los van zijn betekenis in beeld
    /// komt: "19 sec geleden" en "4 uur geleden" zijn twee verschillende mededelingen, en het is
    /// dezelfde waarde als in de kop van het scherm — niet een tweede berekening.
    /// </remarks>
    public required TimeSpan? Silence { get; init; }

    /// <summary>Hoe vaak de telemetriebibliotheek een hartslag wegschrijft.</summary>
    public required TimeSpan HeartbeatInterval { get; init; }

    /// <summary>Hoe lang logregels bewaard blijven.</summary>
    public required TimeSpan LogRetention { get; init; }

    /// <summary>Hoe lang runs bewaard blijven.</summary>
    /// <remarks>
    /// Ruimer dan <see cref="LogRetention"/>, en dat is geen slordigheid: bij een factuurdiscussie
    /// wil je de runs nog hebben als de logregels allang zijn opgeruimd. Retentie is dus geen enkel
    /// getal, en dit tabblad hoort er ook geen enkel getal van te maken.
    /// </remarks>
    public required TimeSpan RunRetention { get; init; }

    /// <summary>
    /// Waarom er op dit tabblad geen knoppen staan. Zie <see cref="AgentConfigurationNotice"/>.
    /// </summary>
    public required string ReadOnlyNotice { get; init; }

}

/// <summary>
/// Het tabblad Configuratie zoals de operator het ziet.
/// </summary>
/// <remarks>
/// Een apart type en niet een vlag op het klanttype, om dezelfde reden als bij
/// <see cref="OperatorAgentDetailView"/>: het verschil tussen de rollen is een verschil tussen
/// typen. Wat er hier bovenop komt is precies wat §2 als operator-only aanmerkt.
/// </remarks>
public sealed record OperatorAgentConfigurationView
{
    /// <summary>De technische naam van de agent.</summary>
    public required string AgentName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>De versie die nu draait.</summary>
    public required string Version { get; init; }

    /// <summary>De cron-expressie, of <c>null</c> bij een trigger-agent.</summary>
    public string? Schedule { get; init; }

    /// <summary>Waardoor deze agent aan het werk gaat.</summary>
    public required TriggerKind TriggerKind { get; init; }

    /// <summary>Toelichting op de trigger.</summary>
    public string? TriggerDetail { get; init; }

    /// <summary>De eerstvolgende geplande run, of <c>null</c>.</summary>
    public DateTimeOffset? NextRunAt { get; init; }

    /// <summary>Sinds wanneer dit proces loopt.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>De laatste hartslag.</summary>
    public required DateTimeOffset LastHeartbeatAt { get; init; }

    /// <summary>Hoe lang deze agent al zwijgt.</summary>
    public required TimeSpan? Silence { get; init; }

    /// <summary>Hoe vaak de telemetriebibliotheek een hartslag wegschrijft.</summary>
    public required TimeSpan HeartbeatInterval { get; init; }

    /// <summary>Hoe lang logregels bewaard blijven.</summary>
    public required TimeSpan LogRetention { get; init; }

    /// <summary>Hoe lang runs bewaard blijven.</summary>
    public required TimeSpan RunRetention { get; init; }

    /// <summary>Waarom er op dit tabblad geen knoppen staan.</summary>
    public required string ReadOnlyNotice { get; init; }

    /// <summary>
    /// Waarom de identity niet op dit tabblad staat. Zie
    /// <see cref="AgentConfigurationNotice.IdentityElsewhere"/>.
    /// </summary>
    /// <remarks>
    /// Alleen op het operatortype. Een klant hoort niets over managed identities te lezen — dat is
    /// infrastructuur, en §2 maakt die operator-only. Zwijgen is daar dus geen omissie maar het
    /// besluit.
    /// </remarks>
    public required string IdentityNotice { get; init; }

    /// <summary>Productie, acceptatie of ontwikkeling. Operator-only.</summary>
    public required AgentEnvironment AgentEnvironment { get; init; }

    /// <summary>Wat de agent over zijn eigen levenscyclus meldt. Operator-only.</summary>
    public required AgentLifecycle Lifecycle { get; init; }

    /// <summary>
    /// De contractversie die deze agent schrijft. Operator-only.
    /// </summary>
    public required int ContractVersion { get; init; }

    /// <summary>
    /// De contractversie die dit portaal verwacht.
    /// </summary>
    /// <remarks>
    /// Staat er naast <see cref="ContractVersion"/> in plaats van dat het scherm de vergelijking
    /// maakt met een getal dat het zelf kent. Zie <see cref="IsContractOutdated"/>.
    /// </remarks>
    public required int ExpectedContractVersion { get; init; }

    /// <summary>
    /// De volledige omgeving van de klant, bijvoorbeeld <c>sub-77b2e0 · rg-soratus-bakker</c>.
    /// Operator-only.
    /// </summary>
    public string? EnvironmentDetail { get; init; }

    /// <summary>
    /// Waar de telemetrie van deze klant staat: endpoint en database. Operator-only.
    /// </summary>
    /// <remarks>
    /// Alleen de endpoint en de database; er is geen sleutel om te tonen, want op de accounts staat
    /// local auth uit.
    /// </remarks>
    public required string TelemetryLocation { get; init; }

    /// <summary>
    /// Of deze agent op een oudere contractvorm is blijven staan.
    /// </summary>
    /// <remarks>
    /// Dit is de reden dat <see cref="AgentRegistration.ContractVersion"/> bestaat: een agent die
    /// niet meer wordt uitgerold levert stilletjes velden aan die het portaal anders leest dan
    /// bedoeld. Dat is een uitrolvraag en geen storing, dus het hoort op dit tabblad en niet in de
    /// statusmelding.
    /// </remarks>
    public bool IsContractOutdated => ContractVersion < ExpectedContractVersion;
}
