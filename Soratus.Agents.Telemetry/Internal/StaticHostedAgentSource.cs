using Soratus.Agents.Telemetry.HostedAgents;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>Eén met de hand aangekondigde geherbergde agent.</summary>
/// <remarks>
/// Het eenvoudige geval, voor een host waarin de agents niet uit iets bestaands af te lezen zijn.
/// Zie <see cref="SoratusAgentBuilderExtensions.AddSoratusHostedAgent"/>.
/// </remarks>
internal sealed class StaticHostedAgentSource(HostedAgentDeclaration declaration) : IHostedAgentSource
{
    private readonly HostedAgentDeclaration[] _agents = [declaration];

    public IReadOnlyList<HostedAgentDeclaration> GetAgents() => _agents;
}
