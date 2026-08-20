using System.Text.Json.Serialization;

namespace Soratus.Agents.Contracts;

/// <summary>
/// Eén run van één agent. Tweemaal geschreven: bij het starten en bij het afronden.
/// </summary>
/// <remarks>
/// Retentie is 400 dagen, ruimer dan de 30 dagen voor <see cref="LogRecord"/>. Reden: bij een
/// factuurdiscussie of de vraag "wat is er in mei gebeurd" wil je de runs nog hebben, ook al
/// zijn de logregels dan allang opgeruimd. Retentie is dus geen enkel getal.
/// </remarks>
public sealed record RunRecord
{
    /// <summary>Documentsleutel. Gelijk aan de runId, bijvoorbeeld <c>r-8f3c</c>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Partitiesleutel, <c>{agentName}|{yyyy-MM-dd}</c>. Zie
    /// <see cref="BuildPartitionKey"/> voor waarom deze vorm.
    /// </summary>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>De klant waar deze run voor draaide, als slug.</summary>
    [JsonPropertyName("customerId")]
    public required string CustomerId { get; init; }

    /// <summary>De agent die deze run draaide.</summary>
    [JsonPropertyName("agentName")]
    public required string AgentName { get; init; }

    /// <summary>Wanneer de run begon.</summary>
    [JsonPropertyName("startedAt")]
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary><c>null</c> zolang de run loopt.</summary>
    [JsonPropertyName("finishedAt")]
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary><c>null</c> zolang de run loopt.</summary>
    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; init; }

    /// <summary>De afloop. <see cref="RunResult.Running"/> zolang de run bezig is.</summary>
    [JsonPropertyName("result")]
    public required RunResult Result { get; init; }

    /// <summary>
    /// Hoeveel items deze run heeft verwerkt. Wat een item is weet alleen de agent, dus dit
    /// zet de bouwer zelf.
    /// </summary>
    [JsonPropertyName("itemsProcessed")]
    public int ItemsProcessed { get; init; }

    /// <summary>Hoeveel items zijn afgekeurd of mislukt.</summary>
    [JsonPropertyName("itemsFailed")]
    public int ItemsFailed { get; init; }

    /// <summary>
    /// Of de transactie is teruggedraaid. Het foutscherm vertelt de klant dat er geen halve
    /// stand is weggeschreven; die bewering moet waar zijn en wordt daarom gemeld, niet
    /// geraden.
    /// </summary>
    [JsonPropertyName("rolledBack")]
    public bool RolledBack { get; init; }

    /// <summary>Waardoor deze run startte.</summary>
    [JsonPropertyName("trigger")]
    public required TriggerKind Trigger { get; init; }

    /// <summary>Het .NET-type van de uitzondering, als de run mislukte.</summary>
    [JsonPropertyName("errorType")]
    public string? ErrorType { get; init; }

    /// <summary>De foutmelding, als de run mislukte. Eén zin, leesbaar op het scherm.</summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    /// <summary>De agentversie die deze run draaide.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>
    /// Bouwt de partitiesleutel <c>{agentName}|{yyyy-MM-dd}</c>.
    /// </summary>
    /// <remarks>
    /// Partitioneren op alleen de agentnaam laat één partitie eindeloos groeien; partitioneren
    /// op de runId maakt "alle runs van deze agent vandaag" een query over alle partities.
    /// De combinatie van naam en dag begrenst de partitie én houdt precies de vraag die het
    /// scherm stelt binnen één partitie.
    /// </remarks>
    public static string BuildPartitionKey(string agentName, DateTimeOffset startedAt) =>
        $"{agentName}|{startedAt.UtcDateTime:yyyy-MM-dd}";
}
