using System.Text.Json.Serialization;

namespace Soratus.Agents.Contracts;

/// <summary>
/// Wat de agent zelf over zijn eigen levenscyclus meldt.
/// </summary>
/// <remarks>
/// Dit is geen status. Het is één van de feiten waaruit
/// <see cref="AgentStatusCalculator"/> status afleidt. Een agent mag melden dat hij wacht;
/// hij mag niet melden dat het goed met hem gaat.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AgentLifecycle>))]
public enum AgentLifecycle
{
    /// <summary>Draait en werkt.</summary>
    [JsonStringEnumMemberName("running")]
    Running,

    /// <summary>
    /// Draait, maar wacht bewust op werk. De bibliotheek kan dit niet raden — een leeg
    /// wachtinterval ziet er van buiten hetzelfde uit als een vastgelopen lus — dus de
    /// agent zet dit zelf.
    /// </summary>
    [JsonStringEnumMemberName("idleWaiting")]
    IdleWaiting,

    /// <summary>Netjes afgesloten, bijvoorbeeld tijdens een uitrol.</summary>
    [JsonStringEnumMemberName("stoppedCleanly")]
    StoppedCleanly,
}
