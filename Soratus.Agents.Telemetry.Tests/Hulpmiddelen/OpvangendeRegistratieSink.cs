using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.Internal;

namespace Soratus.Agents.Telemetry.Tests.Hulpmiddelen;

/// <summary>
/// Als <see cref="OpvangendeSink"/>, en houdt de registraties ook bij.
/// </summary>
/// <remarks>
/// De bestaande sink gooit registraties weg, want voor de knip op <c>msg</c> en de tijdvorm doen ze
/// niet mee. Bij een host met meerdere geherbergde agents zit de vraag juist in dát document:
/// hoeveel er zijn, wat hun levensfase is, en of <c>schedule</c> en <c>nextRunAt</c> leeg blijven.
/// Een eigen type in plaats van de bestaande uitbreiden, zodat de bestaande tests niet meeveranderen
/// met een reden die niet de hunne is.
/// </remarks>
internal sealed class OpvangendeRegistratieSink : ITelemetrySink
{
    private readonly List<AgentRegistration> _registrations = [];
    private readonly List<RunRecord> _runs = [];
    private readonly List<LogRecord> _logs = [];
    private readonly Lock _slot = new();

    internal IReadOnlyList<AgentRegistration> Registrations => Kopie(_registrations);

    internal IReadOnlyList<RunRecord> Runs => Kopie(_runs);

    internal IReadOnlyList<LogRecord> Logs => Kopie(_logs);

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
