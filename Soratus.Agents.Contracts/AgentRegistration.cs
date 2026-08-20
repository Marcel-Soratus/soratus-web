using System.Text.Json.Serialization;

namespace Soratus.Agents.Contracts;

/// <summary>
/// Wat een agent over zichzelf publiceert. Eén document per agent, telkens overschreven.
/// </summary>
/// <remarks>
/// Let op wat hier <em>niet</em> in staat: geen status, geen uptime, geen "aantal runs in de
/// laatste 24 uur". Dat zijn allemaal afleidingen, en die worden in het portaal berekend uit
/// deze feiten en uit <see cref="RunRecord"/>. Een agent mag feiten melden over zichzelf,
/// geen oordelen.
///
/// De agentbouwer schrijft dit document niet met de hand. Dat doet
/// <c>Soratus.Agents.Telemetry</c>, die de meeste velden zelf afleidt uit het assembly en de
/// configuratie.
/// </remarks>
public sealed record AgentRegistration
{
    /// <summary>Documentsleutel voor Cosmos. Gelijk aan <see cref="AgentName"/>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Partitiesleutel. Gelijk aan <see cref="AgentName"/>.</summary>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>De klant waar deze agent voor draait, als slug.</summary>
    [JsonPropertyName("customerId")]
    public required string CustomerId { get; init; }

    /// <summary>
    /// Technische naam, kleine letters met koppelstreepjes, bijvoorbeeld
    /// <c>factuur-intake</c>. Stabiel over uitrollen heen — dit is waar alles op aansluit.
    /// </summary>
    [JsonPropertyName("agentName")]
    public required string AgentName { get; init; }

    /// <summary>
    /// Vrij te kiezen typeaanduiding voor in de typekolom, bijvoorbeeld
    /// <c>Document-intake</c>. Alleen presentatie.
    /// </summary>
    [JsonPropertyName("displayType")]
    public required string DisplayType { get; init; }

    /// <summary>Informational assembly version, door de pijplijn gestempeld.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>Wanneer dit proces startte.</summary>
    [JsonPropertyName("startedAt")]
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// De laatste hartslag. Dit veld draagt in zijn eentje het verschil tussen live en
    /// degraded, dus het wordt door de bibliotheek geschreven en nooit door de agent zelf.
    /// </summary>
    [JsonPropertyName("lastHeartbeatAt")]
    public required DateTimeOffset LastHeartbeatAt { get; init; }

    /// <summary>Wat de agent over zijn eigen levenscyclus meldt.</summary>
    [JsonPropertyName("lifecycle")]
    public required AgentLifecycle Lifecycle { get; init; }

    /// <summary>
    /// Cron-expressie waarop deze agent plant, of <c>null</c> als hij alleen op een trigger
    /// draait. Dit is de expressie waarmee de bibliotheek <em>daadwerkelijk</em> plant, niet
    /// een losse beschrijving die uit de pas kan lopen met de werkelijkheid.
    /// </summary>
    [JsonPropertyName("schedule")]
    public string? Schedule { get; init; }

    /// <summary>Waardoor deze agent aan het werk gaat.</summary>
    [JsonPropertyName("triggerKind")]
    public required TriggerKind TriggerKind { get; init; }

    /// <summary>
    /// Toelichting op de trigger voor op het scherm, bijvoorbeeld
    /// <c>Blob-drop (inbox-facturen)</c>.
    /// </summary>
    [JsonPropertyName("triggerDetail")]
    public string? TriggerDetail { get; init; }

    /// <summary>
    /// De eerstvolgende geplande run, berekend uit <see cref="Schedule"/>. <c>null</c> bij
    /// een agent die alleen op een trigger draait — dan toont het scherm de trigger in
    /// plaats van een tijdstip, en niet een verzonnen moment.
    /// </summary>
    [JsonPropertyName("nextRunAt")]
    public DateTimeOffset? NextRunAt { get; init; }

    /// <summary>Productie, acceptatie of ontwikkeling.</summary>
    [JsonPropertyName("environment")]
    public required AgentEnvironment Environment { get; init; }

    /// <summary>
    /// Versie van dit contract. Loopt op zodra een veld van betekenis verandert, zodat het
    /// portaal een agent kan herkennen die op een oude vorm is blijven staan.
    /// </summary>
    [JsonPropertyName("contractVersion")]
    public int ContractVersion { get; init; } = CurrentContractVersion;

    /// <summary>De contractversie die deze bibliotheek schrijft en leest.</summary>
    public const int CurrentContractVersion = 1;
}
