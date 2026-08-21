using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.Internal;

namespace Soratus.Agents.AspNetCore.Tests.Hulpmiddelen;

/// <summary>
/// Vangt op wat de bibliotheek zou wegschrijven, in plaats van het naar Cosmos te sturen.
/// </summary>
/// <remarks>
/// Anders dan de variant in <c>Soratus.Agents.Telemetry.Tests</c> houdt deze de registraties wél
/// bij: bij een geherbergde agent zit de hele vraag juist in dat document — hoeveel er zijn, wat
/// hun levensfase is, en of <c>schedule</c> en <c>nextRunAt</c> leeg blijven.
/// </remarks>
internal sealed class OpvangendeSink : ITelemetrySink
{
    private readonly List<AgentRegistration> _registrations = [];
    private readonly List<RunRecord> _runs = [];
    private readonly List<LogRecord> _logs = [];
    private readonly Lock _slot = new();

    internal IReadOnlyList<AgentRegistration> Registrations => Kopie(_registrations);

    internal IReadOnlyList<RunRecord> Runs => Kopie(_runs);

    internal IReadOnlyList<LogRecord> Logs => Kopie(_logs);

    /// <summary>De laatste registratie per agent, zoals het portaal die zou lezen.</summary>
    internal IReadOnlyDictionary<string, AgentRegistration> LaatsteRegistraties =>
        Registrations
            .GroupBy(registration => registration.AgentName, StringComparer.Ordinal)
            .ToDictionary(groep => groep.Key, groep => groep.Last(), StringComparer.Ordinal);

    /// <summary>De afgeronde runs van één agent, in de volgorde waarin ze zijn weggeschreven.</summary>
    internal IReadOnlyList<RunRecord> AfgerondeRunsVan(string agentNaam) =>
        [.. Runs.Where(run => run.AgentName == agentNaam && run.Result != RunResult.Running)];

    public Task UpsertRegistrationAsync(AgentRegistration registration, CancellationToken cancellationToken)
    {
        lock (_slot)
        {
            _registrations.Add(registration);
        }

        return Task.CompletedTask;
    }

    public Task UpsertRunAsync(RunRecord run, CancellationToken cancellationToken)
    {
        lock (_slot)
        {
            _runs.Add(run);
        }

        return Task.CompletedTask;
    }

    public Task WriteLogsAsync(IReadOnlyList<LogRecord> logs, CancellationToken cancellationToken)
    {
        lock (_slot)
        {
            _logs.AddRange(logs);
        }

        return Task.CompletedTask;
    }

    private IReadOnlyList<T> Kopie<T>(List<T> bron)
    {
        lock (_slot)
        {
            return [.. bron];
        }
    }
}
