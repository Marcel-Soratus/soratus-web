using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.HostedAgents;

namespace Soratus.Agents.AspNetCore;

/// <summary>
/// De metadata die van een endpoint een Soratus-agent maakt.
/// </summary>
/// <remarks>
/// <para>Dit is het enige dat een aanroeper per endpoint kwijt moet: één regel achter de
/// <c>Map…</c>-aanroep. De rest — registreren, kloppen, een run per aanroep openen en sluiten —
/// volgt hieruit.</para>
///
/// <para>Deze metadata is tegelijk de <em>enige</em> lijst van agents in het proces. De hartslag
/// leest dezelfde endpoints (<see cref="Internal.EndpointHostedAgentSource"/>) als de aanroeplaag,
/// dus een agent kan niet in het portaal staan zonder endpoint, en geen endpoint kan werk
/// verzetten zonder in het portaal te staan. Een tweede lijst in de opstartcode zou precies dat
/// paar kunnen breken.</para>
/// </remarks>
public sealed class SoratusAgentMetadata
{
    /// <summary>
    /// Maakt de metadata voor één endpoint.
    /// </summary>
    /// <param name="agentName">
    /// Technische naam, kleine letters met koppelstreepjes, bijvoorbeeld <c>declaraties-import</c>.
    /// Stabiel over uitrollen heen — hier sluit alles op aan.
    /// </param>
    /// <param name="displayType">
    /// Typeaanduiding voor de typekolom, bijvoorbeeld <c>Document-intake</c>. Leeg laten levert een
    /// leesbare vorm van <paramref name="agentName"/> op.
    /// </param>
    /// <param name="triggerDetail">
    /// Toelichting op de trigger voor op het scherm, bijvoorbeeld <c>POST /api/declaraties</c>.
    /// Vrije tekst die de klant leest: geen klassenamen, geen paden.
    /// </param>
    /// <param name="trigger">
    /// Waardoor de aanroep binnenkomt. Standaard <see cref="TriggerKind.Http"/>; zet
    /// <see cref="TriggerKind.Webhook"/> als het endpoint door een externe partij wordt aangeroepen,
    /// want dat is voor een operator een ander soort afhankelijkheid.
    /// </param>
    /// <exception cref="ArgumentException">Als <paramref name="agentName"/> leeg is.</exception>
    /// <exception cref="InvalidOperationException">Als <paramref name="trigger"/> een timer is.</exception>
    public SoratusAgentMetadata(
        string agentName,
        string? displayType = null,
        string? triggerDetail = null,
        TriggerKind trigger = TriggerKind.Http)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        Declaration = new HostedAgentDeclaration
        {
            AgentName = agentName.Trim(),
            DisplayType = displayType,
            TriggerDetail = triggerDetail,
            Trigger = trigger,
        };

        Declaration.Validate();
    }

    /// <summary>De aankondiging die uit dit endpoint volgt.</summary>
    public HostedAgentDeclaration Declaration { get; }

    /// <inheritdoc />
    public override string ToString() =>
        $"Soratus-agent '{Declaration.AgentName}' ({Declaration.Trigger})";
}
