using System.Text.Json.Serialization;

namespace Soratus.Agents.Contracts;

/// <summary>In welke omgeving een agent draait.</summary>
/// <remarks>
/// De klantweergave toont uitsluitend <see cref="Production"/>. Een acceptatie-agent die
/// omvalt is geen storing voor de klant.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AgentEnvironment>))]
public enum AgentEnvironment
{
    /// <summary>Productie. Het enige dat de klant te zien krijgt.</summary>
    [JsonStringEnumMemberName("prod")]
    Production,

    /// <summary>Acceptatie. Zichtbaar voor de operator, niet voor de klant.</summary>
    [JsonStringEnumMemberName("acc")]
    Acceptance,

    /// <summary>Ontwikkeling, meestal lokaal.</summary>
    [JsonStringEnumMemberName("dev")]
    Development,
}
