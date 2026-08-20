using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry;

/// <summary>
/// Alles wat deze agent over zichzelf weet, één keer afgeleid bij het opstarten.
/// </summary>
/// <remarks>
/// Deze feiten worden niet met de hand ingevuld. <see cref="SoratusAgentBuilderExtensions.AddSoratusAgent"/>
/// leidt ze af uit het assembly en de configuratie en werpt als er iets ontbreekt. Zo kan er
/// nooit een agent draaien die zich onder een halve naam meldt.
/// </remarks>
public sealed record AgentIdentity
{
    /// <summary>De klant waar deze agent voor draait, als slug.</summary>
    public required string CustomerId { get; init; }

    /// <summary>Technische naam, kleine letters met koppelstreepjes. Stabiel over uitrollen heen.</summary>
    public required string AgentName { get; init; }

    /// <summary>Typeaanduiding voor de typekolom in het portaal. Alleen presentatie.</summary>
    public required string DisplayType { get; init; }

    /// <summary>Informational assembly version van het entry-assembly.</summary>
    public required string Version { get; init; }

    /// <summary>Productie, acceptatie of ontwikkeling.</summary>
    public required AgentEnvironment Environment { get; init; }

    /// <summary>Waardoor deze agent aan het werk gaat.</summary>
    public required TriggerKind TriggerKind { get; init; }

    /// <summary>Toelichting op de trigger voor op het scherm, of <c>null</c>.</summary>
    public string? TriggerDetail { get; init; }

    /// <summary>
    /// De cron-expressie waarop deze agent plant, of <c>null</c> bij een agent die alleen op
    /// een trigger draait. Dit is de expressie waarmee de bibliotheek daadwerkelijk plant.
    /// </summary>
    public string? Schedule { get; init; }

    /// <summary>De tijdzone waarin <see cref="Schedule"/> wordt uitgelegd.</summary>
    public required TimeZoneInfo ScheduleTimeZone { get; init; }

    /// <summary>Wanneer dit proces startte.</summary>
    public required DateTimeOffset StartedAt { get; init; }
}
