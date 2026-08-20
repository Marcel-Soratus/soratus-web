using System.Text.Json.Serialization;

namespace Soratus.Agents.Contracts;

/// <summary>De afloop van één run.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RunResult>))]
public enum RunResult
{
    /// <summary>
    /// Nog bezig. Wordt bij het starten weggeschreven en bij het afronden overschreven.
    /// Een run die op deze waarde blijft staan terwijl de hartslag doorloopt is zelf een
    /// signaal: het proces leeft, maar de run is nooit afgerond.
    /// </summary>
    [JsonStringEnumMemberName("running")]
    Running,

    /// <summary>Afgerond zoals bedoeld.</summary>
    [JsonStringEnumMemberName("ok")]
    Ok,

    /// <summary>Afgebroken door een fout. <c>errorType</c> en <c>errorMessage</c> zijn gevuld.</summary>
    [JsonStringEnumMemberName("failed")]
    Failed,

    /// <summary>Niets te doen gehad. Geen fout.</summary>
    [JsonStringEnumMemberName("skipped")]
    Skipped,
}
