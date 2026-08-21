using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.HostedAgents;

/// <summary>
/// Eén agent die door deze host wordt geherbergd. De aankondiging, niet de agent zelf.
/// </summary>
/// <remarks>
/// <para>Dit type bestaat voor het geval dat niet in <see cref="Scheduling.IScheduledAgent"/>
/// past: een agent die geen eigen proces en geen eigen lus heeft, maar een dienst is binnen een
/// grotere host — een endpoint in een webapplicatie, een handler op een wachtrij. Zulke agents
/// komen met meer dan één per proces, dus ze kunnen niet ieder een
/// <see cref="AgentIdentity"/>-singleton zijn.</para>
///
/// <para>Wat hier <em>niet</em> in staat is een schema. Dat is geen weglating: een geherbergde
/// agent draait wanneer hij wordt aangeroepen, dus er is geen volgende run om te voorspellen.
/// <see cref="AgentRegistration.Schedule"/> blijft leeg en
/// <see cref="AgentRegistration.NextRunAt"/> blijft <c>null</c>, en het scherm toont dan de
/// trigger in plaats van een verzonnen tijdstip.</para>
/// </remarks>
public sealed record HostedAgentDeclaration
{
    /// <summary>
    /// Technische naam, kleine letters met koppelstreepjes, bijvoorbeeld
    /// <c>declaraties-import</c>. Stabiel over uitrollen heen — hier sluit alles op aan.
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// Waardoor deze agent aan het werk gaat. Verplicht, en nooit
    /// <see cref="TriggerKind.Timer"/>.
    /// </summary>
    /// <remarks>
    /// Er is geen standaardwaarde, en dat is opzet. De aanroeper is de enige die weet waar de
    /// aanroep vandaan komt, en een gegokte trigger staat straks als feit op het scherm van de
    /// klant.
    /// </remarks>
    public required TriggerKind Trigger { get; init; }

    /// <summary>
    /// Typeaanduiding voor de typekolom, bijvoorbeeld <c>Document-intake</c>. Alleen
    /// presentatie; leeg laten levert een leesbare vorm van <see cref="AgentName"/> op.
    /// </summary>
    public string? DisplayType { get; init; }

    /// <summary>
    /// Toelichting op de trigger voor op het scherm, bijvoorbeeld
    /// <c>POST /api/declaraties</c>.
    /// </summary>
    /// <remarks>
    /// Dit veld is vrije tekst en komt op het scherm van de klant terecht — dezelfde categorie
    /// als <c>msg</c> en <c>errorMessage</c>, met dezelfde eis: geen bestandspaden, geen
    /// klasse- of methodenamen, geen resource groups. Een routepatroon mag; een
    /// controllernaam niet.
    /// </remarks>
    public string? TriggerDetail { get; init; }

    /// <summary>
    /// Controleert de eigenschappen die een geherbergde agent per definitie heeft.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Als de naam leeg is of als de trigger <see cref="TriggerKind.Timer"/> is.
    /// </exception>
    /// <remarks>
    /// <see cref="TriggerKind.Timer"/> is hier verboden en niet stil gecorrigeerd. De
    /// documentatie van <see cref="AgentRegistration.Schedule"/> belooft dat bij een
    /// timer-agent een cron-expressie staat, en een geherbergde agent heeft die niet. Zou het
    /// hier mogen, dan staat er in het portaal een agent op schema zonder schema en met
    /// <c>nextRunAt</c> leeg — een tegenspraak die de lezer moet oplossen in plaats van de
    /// bouwer.
    /// </remarks>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AgentName))
        {
            throw new InvalidOperationException(
                "Een geherbergde agent moet een naam hebben; die naam is de sleutel waarop het " +
                "portaal, de runs en de logregels aansluiten.");
        }

        if (Trigger == TriggerKind.Timer)
        {
            throw new InvalidOperationException(
                $"Geherbergde agent '{AgentName}' meldt trigger '{TriggerKind.Timer}', maar een " +
                "geherbergde agent heeft geen schema: hij draait wanneer hij wordt aangeroepen. " +
                "Kies de trigger die de aanroep beschrijft (http, queue, webhook, blob of manual).");
        }
    }
}
