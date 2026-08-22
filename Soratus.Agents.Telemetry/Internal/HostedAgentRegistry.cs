using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.HostedAgents;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>De implementatie van <see cref="ISoratusHostedAgents"/>.</summary>
/// <remarks>
/// De identiteit van een geherbergde agent is voor een deel die van de host: klant, versie,
/// omgeving en het moment waarop dit proces startte. Alleen naam, typeaanduiding en trigger zijn
/// van de agent zelf. Dat is geen bezuiniging maar de waarheid van dit geval — drie diensten in
/// één webapplicatie zijn één uitrol, één versie en één proces, en ze zouden er los van elkaar
/// nooit anders uit kunnen zien.
/// </remarks>
internal sealed class HostedAgentRegistry(
    AgentIdentity host,
    IOptions<SoratusTelemetryOptions> options,
    IEnumerable<IHostedAgentSource> sources,
    TelemetryWriter writer,
    TimeProvider clock,
    ILogger<HostedAgentRegistry> logger) : ISoratusHostedAgents
{
    private readonly ConcurrentDictionary<string, HostedAgent> _agents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _reportedConflicts = new(StringComparer.Ordinal);

    /// <summary>
    /// De host zelf: waar klant, versie en omgeving van elke geherbergde agent uit komen.
    /// </summary>
    /// <remarks>
    /// Deze identiteit wordt <em>niet</em> als registratiedocument gepubliceerd. Er is bewust geen
    /// vierde rij "de webhost" in het overzicht van een klant met drie diensten: zijn hartslag is
    /// per constructie dezelfde als die van de drie, dus die rij zou een regel toevoegen zonder
    /// een feit toe te voegen. Wat de host wél in de gegevens achterlaat is
    /// <see cref="AgentIdentity.StartedAt"/> op elk van de drie documenten — zie
    /// <see cref="HostedAgentsRegistrationService"/> voor waarom dat het interessante veld is.
    /// </remarks>
    internal AgentIdentity Host => host;

    public IReadOnlyList<ISoratusHostedAgent> All =>
        [.. _agents.Values.OrderBy(static agent => agent.Identity.AgentName, StringComparer.Ordinal)];

    public ISoratusHostedAgent? Find(string agentName) =>
        agentName is not null && _agents.TryGetValue(agentName, out HostedAgent? agent) ? agent : null;

    public ISoratusHostedAgent GetOrAdd(HostedAgentDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        declaration.Validate();

        HostedAgent agent = _agents.GetOrAdd(
            declaration.AgentName,
            static (_, state) => state.Registry.Build(state.Declaration),
            (Registry: this, Declaration: declaration));

        ReportConflict(agent, declaration);
        return agent;
    }

    /// <summary>
    /// Vraagt alle bronnen opnieuw wat deze host herbergt en levert het geheel op.
    /// </summary>
    /// <remarks>
    /// Bij elke hartslag, en niet één keer bij het opstarten. Zie
    /// <see cref="IHostedAgentSource.GetAgents"/> voor waarom: een bron die zijn antwoord pas kent
    /// nadat de host zijn verzoekpijplijn heeft gebouwd, levert bij één keer vragen een lege lijst,
    /// en een agent die dáárdoor ontbreekt is in het portaal niet zichtbaar als fout maar als
    /// afwezigheid.
    /// </remarks>
    internal IReadOnlyList<HostedAgent> Refresh()
    {
        foreach (IHostedAgentSource source in sources)
        {
            foreach (HostedAgentDeclaration declaration in source.GetAgents())
            {
                GetOrAdd(declaration);
            }
        }

        return [.. _agents.Values.OrderBy(static agent => agent.Identity.AgentName, StringComparer.Ordinal)];
    }

    private HostedAgent Build(HostedAgentDeclaration declaration)
    {
        var identity = new AgentIdentity
        {
            CustomerId = host.CustomerId,
            AgentName = declaration.AgentName,
            DisplayType = string.IsNullOrWhiteSpace(declaration.DisplayType)
                ? SoratusAgentBuilderExtensions.Humanise(declaration.AgentName)
                : declaration.DisplayType,
            Version = host.Version,
            Environment = host.Environment,
            TriggerKind = declaration.Trigger,
            TriggerDetail = declaration.TriggerDetail,

            // Het plan van deze agent, of niets bij een dienst die op een aanroep draait. Bij een
            // dienst op aanvraag is dat geen weglating maar de vorm van het geval; bij een agent op
            // een klok in deze host is het plan de maat waaraan stilte wordt afgelezen. Zie
            // HostedAgentDeclaration.Schedule.
            Schedule = declaration.Schedule?.Expression,
            ScheduleTimeZone = declaration.Schedule?.TimeZone ?? host.ScheduleTimeZone,

            // Het moment waarop dit proces startte, en niet het moment waarop deze agent voor het
            // eerst werd aangeroepen. 'Draait sinds' gaat over de host, want die draagt de hartslag.
            StartedAt = host.StartedAt,
        };

        return new HostedAgent(
            identity,
            declaration,
            writer,
            new LogRecordFactory(identity, options),
            clock);
    }

    /// <summary>
    /// Meldt één keer per naam dat twee aankondigingen van dezelfde agent niet gelijk zijn.
    /// </summary>
    /// <remarks>
    /// Op de gewone logger van de host en niet in het portaal: dit is een inrichtingsfout van de
    /// bouwer, geen gebeurtenis in het werk van de klant. Eén keer per naam, want de aanroepkant
    /// komt hier bij elk verzoek langs en een melding per verzoek is geen melding meer.
    /// </remarks>
    private void ReportConflict(HostedAgent existing, HostedAgentDeclaration declaration)
    {
        if (existing.Declaration == declaration || !_reportedConflicts.TryAdd(declaration.AgentName, 0))
        {
            return;
        }

        logger.LogWarning(
            "Agent '{AgentName}' is twee keer aangekondigd met verschillende gegevens. De eerste " +
            "aankondiging blijft staan (type '{DisplayType}', trigger {Trigger}); de tweede is " +
            "genegeerd. Twee endpoints mogen dezelfde agent aankondigen, maar dan met dezelfde " +
            "typeaanduiding en trigger.",
            declaration.AgentName,
            existing.Identity.DisplayType,
            existing.Identity.TriggerKind);
    }
}
