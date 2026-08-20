using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.Internal;

namespace Soratus.Agents.Telemetry.Tests.Hulpmiddelen;

/// <summary>
/// Vangt op wat de bibliotheek zou wegschrijven, in plaats van het naar Cosmos te sturen.
/// </summary>
/// <remarks>
/// Hiermee is het hele schrijfpad te testen — logger, provider, bufferlaag — zonder database en
/// zonder netwerk. Dat is precies de bedoeling: de knip op <c>msg</c> gebeurt onderweg, en een
/// test die alleen de knipfunctie aanroept bewijst niet dat hij ook op de echte route langskomt.
/// </remarks>
internal sealed class OpvangendeSink : ITelemetrySink
{
    private readonly List<LogRecord> _logs = [];
    private readonly List<RunRecord> _runs = [];
    private readonly Lock _slot = new();

    internal IReadOnlyList<LogRecord> Logs
    {
        get
        {
            lock (_slot)
            {
                return [.. _logs];
            }
        }
    }

    internal IReadOnlyList<RunRecord> Runs
    {
        get
        {
            lock (_slot)
            {
                return [.. _runs];
            }
        }
    }

    public Task UpsertRegistrationAsync(AgentRegistration registration, CancellationToken cancellationToken) =>
        Task.CompletedTask;

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
}
