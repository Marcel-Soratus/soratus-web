using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.HostedAgents;
using ContractLogLevel = Soratus.Agents.Contracts.LogLevel;

namespace Soratus.Agents.AspNetCore.Internal;

/// <summary>
/// Meldt bij het opstarten als er endpoints een agent aankondigen terwijl de aanroeplaag niet in de
/// verzoekpijplijn staat.
/// </summary>
/// <remarks>
/// <para><strong>De fout die dit dicht.</strong> <c>WithSoratusAgent</c> op de endpoints en
/// <c>UseSoratusAgentRuns</c> vergeten levert drie agents op met een verse hartslag en nul runs.
/// In het portaal staat dat als <see cref="AgentStatus.Idle"/> — de gezonde stand — en er is niets
/// aan te zien. Dat is de duurste fout die dit contract kan maken: hij lijkt op orde.</para>
///
/// <para><strong>Waarom het geen uitzondering is.</strong> De verleiding is om bij het opstarten om
/// te vallen, zoals de bibliotheek bij ontbrekende configuratie doet. Dat mag hier niet: dit loopt
/// in de webapplicatie van een klant, en dan zou onze telemetrie zijn app neerhalen. De belofte is
/// dat telemetrie een agent nooit omlegt, en die geldt hier het sterkst. Dus komt de melding er als
/// <c>error</c>-logregel op naam van elke betrokken agent: rood in het portaal, op de plek waar
/// iemand hem zoekt, met de app in de lucht.</para>
/// </remarks>
internal sealed class EndpointWiringCheck(
    ISoratusHostedAgents agents,
    EndpointHostedAgentSource endpoints,
    RunMiddlewareMarker marker,
    IHostApplicationLifetime lifetime,
    ILogger<EndpointWiringCheck> logger) : IHostedService
{
    /// <summary>De gebeurtenisnaam van de melding.</summary>
    internal const string Event = "host.aanroeplaag.ontbreekt";

    /// <summary>De melding, één zin en leesbaar voor wie de code niet kent.</summary>
    internal const string Message =
        "Deze dienst legt geen aanroepen vast; de koppeling in de webapplicatie is niet volledig ingericht.";

    /// <inheritdoc />
    /// <remarks>
    /// De controle hangt aan <c>ApplicationStarted</c> en niet aan het starten van deze dienst zelf.
    /// Dat is geen omslachtigheid maar de reparatie van een valse melding: de verzoekpijplijn wordt
    /// gebouwd door een ándere achtergronddienst, en welke van de twee eerder aan de beurt is, is
    /// een detail van de host. Op <c>ApplicationStarted</c> staat de pijplijn er zeker, dus is
    /// "de aanroeplaag ontbreekt" op dat moment een feit en geen wedloop.
    /// </remarks>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(Check);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Check()
    {
        if (marker.Installed)
        {
            return;
        }

        IReadOnlyList<HostedAgentDeclaration> declared = endpoints.GetAgents();
        if (declared.Count == 0)
        {
            // Geen endpoint kondigt een agent aan, dus er valt ook niets vast te leggen. Dat is een
            // host die de bibliotheek registreerde en nog niets gebruikt; geen fout.
            return;
        }

        logger.LogError(
            "Er zijn {Count} endpoints met een Soratus-agent, maar UseSoratusAgentRuns staat niet in " +
            "de verzoekpijplijn. Deze agents kloppen wel en leggen geen enkele aanroep vast. Zet " +
            "app.UseSoratusAgentRuns() na app.UseRouting().",
            declared.Count);

        foreach (HostedAgentDeclaration declaration in declared)
        {
            agents.GetOrAdd(declaration).ReportEvent(
                ContractLogLevel.Error,
                Event,
                Message,
                new { endpoints = declared.Count });
        }
    }
}
