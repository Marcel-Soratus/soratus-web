using System.Text.Json.Serialization;

namespace Soratus.Agents.Contracts;

/// <summary>Waardoor een agent aan het werk gaat.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TriggerKind>))]
public enum TriggerKind
{
    /// <summary>Op schema. Dan is <see cref="AgentRegistration.Schedule"/> gevuld.</summary>
    [JsonStringEnumMemberName("timer")]
    Timer,

    /// <summary>Op een bericht in een wachtrij.</summary>
    [JsonStringEnumMemberName("queue")]
    Queue,

    /// <summary>Op een binnenkomend verzoek. Een dienst, geen geplande agent.</summary>
    [JsonStringEnumMemberName("http")]
    Http,

    /// <summary>Op een aanroep van buiten, bijvoorbeeld door SnelStart of DevOps.</summary>
    [JsonStringEnumMemberName("webhook")]
    Webhook,

    /// <summary>Op een bestand dat in opslag wordt neergezet.</summary>
    [JsonStringEnumMemberName("blob")]
    Blob,

    /// <summary>Alleen met de hand gestart.</summary>
    [JsonStringEnumMemberName("manual")]
    Manual,
}
