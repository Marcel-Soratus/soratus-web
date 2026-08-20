using System.Text.Json.Serialization;

namespace Soratus.Agents.Contracts;

/// <summary>
/// Het niveau van een logregel. Bewust drie waarden, niet zes.
/// </summary>
/// <remarks>
/// Debug en trace horen niet in dit contract. Die zijn voor de ontwikkelaar en horen in
/// Application Insights; deze regels zijn voor het portaal en worden door een operator
/// gelezen die weten wil of er iets mis is. Vijfhonderd debugregels per run maken dat
/// moeilijker, niet makkelijker.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<LogLevel>))]
public enum LogLevel
{
    /// <summary>Normaal verloop. Wat er gebeurde, zonder dat er iets aan de hand is.</summary>
    [JsonStringEnumMemberName("info")]
    Info,

    /// <summary>Iets ging net niet goed, maar het werk liep door. Een retry bijvoorbeeld.</summary>
    [JsonStringEnumMemberName("warn")]
    Warn,

    /// <summary>Er ging iets mis dat aandacht nodig heeft.</summary>
    [JsonStringEnumMemberName("error")]
    Error,
}
