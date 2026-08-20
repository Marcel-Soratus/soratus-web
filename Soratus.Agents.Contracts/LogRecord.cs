using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soratus.Agents.Contracts;

/// <summary>
/// Eén logregel. Retentie 30 dagen, voor operator en klant gelijk.
/// </summary>
/// <remarks>
/// Deze vorm is bewust plat en klein. Wat je hier schrijft leest een mens die wil weten of
/// er iets mis is, niet een debugger.
/// </remarks>
public sealed record LogRecord
{
    /// <summary>
    /// Documentsleutel, een ULID. Stabiel en oplopend in tijd.
    /// </summary>
    /// <remarks>
    /// Het portaal heeft deze sleutel nodig om logregels in de lijst te kunnen bijhouden.
    /// Zonder een stabiele sleutel bouwt de live tail de hele tabel opnieuw op zodra er een
    /// regel bovenaan bij komt.
    /// </remarks>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Partitiesleutel, <c>{agentName}|{yyyy-MM-dd}</c>.</summary>
    [JsonPropertyName("pk")]
    public required string PartitionKey { get; init; }

    /// <summary>Wanneer de regel is geschreven.</summary>
    [JsonPropertyName("ts")]
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Info, warn of error. Bepaalt de kleur van de rij en de filtertelling.</summary>
    [JsonPropertyName("level")]
    public required LogLevel Level { get; init; }

    /// <summary>
    /// Puntgescheiden gebeurtenisnaam, bijvoorbeeld <c>document.processed</c> of
    /// <c>api.retry</c>. Dit is het enige veld waar de bouwer echt over moet nadenken:
    /// alleen hij weet wat er gebeurde.
    /// </summary>
    [JsonPropertyName("event")]
    public required string Event { get; init; }

    /// <summary>Eén zin, in het Nederlands, leesbaar voor wie de code niet kent.</summary>
    [JsonPropertyName("msg")]
    public required string Message { get; init; }

    /// <summary>
    /// De run waarbinnen deze regel viel. Wordt automatisch meegevoerd; de bouwer hoeft hem
    /// nergens door te geven. <c>null</c> voor regels buiten een run, zoals bij het starten.
    /// </summary>
    [JsonPropertyName("runId")]
    public string? RunId { get; init; }

    /// <summary>
    /// Vrije context, uitklapbaar op het scherm. Hier komt de structured-logging-state van
    /// <c>ILogger</c> terecht, en hier hoort een stacktrace.
    /// </summary>
    [JsonPropertyName("extra")]
    public JsonElement? Extra { get; init; }

    /// <summary>De klant waar deze regel bij hoort, als slug.</summary>
    [JsonPropertyName("customerId")]
    public required string CustomerId { get; init; }

    /// <summary>De agent die deze regel schreef.</summary>
    [JsonPropertyName("agentName")]
    public required string AgentName { get; init; }

    /// <summary>Bouwt de partitiesleutel, gelijk aan die van <see cref="RunRecord"/>.</summary>
    public static string BuildPartitionKey(string agentName, DateTimeOffset timestamp) =>
        $"{agentName}|{timestamp.UtcDateTime:yyyy-MM-dd}";
}
