using Soratus.Agents.Contracts;

namespace Soratus.Seed;

/// <summary>
/// Zet het manifest om naar contractdocumenten en controleert onderweg of het bestand klopt.
/// </summary>
/// <remarks>
/// Alles wat hier uit komt is een <see cref="AgentRegistration"/>, een <see cref="RunRecord"/> of
/// een <see cref="LogRecord"/>. Er wordt nergens JSON met de hand samengesteld: zou dat wel
/// gebeuren, dan kan de demodata ongemerkt uit de pas lopen met het contract, en dan bewijst een
/// scherm dat op deze data werkt niets meer over het echte geval.
///
/// Er wordt ook geen <c>ttl</c> gezet. Retentie is een eigenschap van de container.
/// </remarks>
internal static class SeedPlanner
{
    /// <summary>
    /// Agents die dit gereedschap nooit aanraakt, hoe het bestand er ook uitziet.
    /// </summary>
    /// <remarks>
    /// <c>heartbeat-demo</c> is de referentie-agent die zijn documenten zelf via de
    /// telemetriebibliotheek schrijft. Dat document is het bewijs dat het portaal op echte
    /// telemetrie werkt; het overschrijven of opruimen ervan zou precies dat bewijs weggooien.
    /// De uitsluiting staat hier expliciet en niet als toevallig gevolg van wat er in het bestand
    /// staat, zodat een tikfout in <c>telemetry.json</c> hem niet alsnog kan raken.
    /// </remarks>
    internal static readonly string[] ProtectedAgents = ["heartbeat-demo"];

    /// <summary>Bouwt het volledige plan uit het manifest.</summary>
    /// <param name="manifest">Het gelezen bestand.</param>
    /// <param name="now">Het moment van seeden, in UTC.</param>
    /// <returns>De documenten plus de telling per klant.</returns>
    /// <exception cref="SeedManifestException">Als het manifest niet klopt.</exception>
    internal static SeedPlan Build(SeedManifest manifest, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var registrations = new List<AgentRegistration>();
        var runs = new List<RunRecord>();
        var logs = new List<LogRecord>();
        var customers = new List<SeedCustomerTally>();
        var seenAgents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var customer in manifest.Customers)
        {
            if (string.IsNullOrWhiteSpace(customer.Id))
            {
                throw new SeedManifestException("Er staat een klant zonder id in het bestand.");
            }

            var runsBefore = runs.Count;
            var logsBefore = logs.Count;

            foreach (var agent in customer.Agents)
            {
                Validate(customer, agent, seenAgents);

                registrations.Add(BuildRegistration(customer, agent, now));
                runs.AddRange(BuildRuns(customer, agent, now));
                logs.AddRange(BuildLogs(customer, agent, now));
            }

            customers.Add(new SeedCustomerTally(
                customer.Id,
                customer.Name ?? customer.Id,
                customer.Agents.Count,
                runs.Count - runsBefore,
                logs.Count - logsBefore));
        }

        return new SeedPlan(registrations, runs, logs, customers);
    }

    private static void Validate(SeedCustomer customer, SeedAgent agent, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(agent.AgentName))
        {
            throw new SeedManifestException($"Klant '{customer.Id}' heeft een agent zonder naam.");
        }

        if (ProtectedAgents.Contains(agent.AgentName, StringComparer.OrdinalIgnoreCase))
        {
            throw new SeedManifestException(
                $"'{agent.AgentName}' is een beschermde agent: die schrijft zijn eigen telemetrie via de " +
                "bibliotheek en wordt door dit gereedschap nooit aangeraakt. Haal hem uit telemetry.json.");
        }

        if (!seen.Add(agent.AgentName))
        {
            throw new SeedManifestException(
                $"De agentnaam '{agent.AgentName}' komt twee keer voor. In de container 'agents' is de naam " +
                "tegelijk documentsleutel en partitiesleutel, dus hij moet accountbreed uniek zijn.");
        }
    }

    private static AgentRegistration BuildRegistration(SeedCustomer customer, SeedAgent agent, DateTimeOffset now)
    {
        var where = $"{customer.Id}/{agent.AgentName}";

        return new AgentRegistration
        {
            Id = agent.AgentName,
            PartitionKey = agent.AgentName,
            CustomerId = customer.Id,
            AgentName = agent.AgentName,
            DisplayType = agent.DisplayType,
            Version = agent.Version,
            StartedAt = RelativeMoment.Resolve(agent.StartedAt, now, $"{where} startedAt"),
            LastHeartbeatAt = RelativeMoment.Resolve(agent.LastHeartbeatAt, now, $"{where} lastHeartbeatAt"),
            Lifecycle = agent.Lifecycle,
            Schedule = agent.Schedule,
            TriggerKind = agent.TriggerKind,
            TriggerDetail = agent.TriggerDetail,
            NextRunAt = RelativeMoment.ResolveOptional(agent.NextRunAt, now, $"{where} nextRunAt"),
            Environment = agent.Environment,
        };
    }

    private static IEnumerable<RunRecord> BuildRuns(SeedCustomer customer, SeedAgent agent, DateTimeOffset now)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var run in agent.Runs)
        {
            var where = $"{customer.Id}/{agent.AgentName} run '{run.RunId}'";

            if (string.IsNullOrWhiteSpace(run.RunId))
            {
                throw new SeedManifestException($"{customer.Id}/{agent.AgentName}: er staat een run zonder runId.");
            }

            var startedAt = RelativeMoment.Resolve(run.StartedAt, now, $"{where} startedAt");
            var partitionKey = RunRecord.BuildPartitionKey(agent.AgentName, startedAt);

            if (!seen.Add($"{partitionKey}|{run.RunId}"))
            {
                throw new SeedManifestException(
                    $"{where}: deze runId komt twee keer voor op dezelfde dag. Binnen een partitie moet hij uniek zijn.");
            }

            var running = run.Result == RunResult.Running;

            if (!running && run.DurationMs is null)
            {
                throw new SeedManifestException($"{where}: een afgeronde run heeft een durationMs nodig.");
            }

            if (run.Result == RunResult.Failed && string.IsNullOrWhiteSpace(run.ErrorMessage))
            {
                throw new SeedManifestException(
                    $"{where}: een mislukte run zonder errorMessage. Het foutscherm heeft die zin nodig.");
            }

            yield return new RunRecord
            {
                Id = run.RunId,
                PartitionKey = partitionKey,
                CustomerId = customer.Id,
                AgentName = agent.AgentName,
                StartedAt = startedAt,
                FinishedAt = running ? null : startedAt.AddMilliseconds(run.DurationMs!.Value),
                DurationMs = running ? null : run.DurationMs,
                Result = run.Result,
                ItemsProcessed = run.ItemsProcessed,
                ItemsFailed = run.ItemsFailed,
                RolledBack = run.RolledBack,
                Trigger = run.Trigger,
                ErrorType = run.ErrorType,
                ErrorMessage = run.ErrorMessage,
                Version = agent.Version,
            };
        }
    }

    private static IEnumerable<LogRecord> BuildLogs(SeedCustomer customer, SeedAgent agent, DateTimeOffset now)
    {
        var index = 0;

        foreach (var log in agent.Logs)
        {
            var where = $"{customer.Id}/{agent.AgentName} logregel {index + 1}";
            var timestamp = RelativeMoment.Resolve(log.At, now, $"{where} at");

            if (string.IsNullOrWhiteSpace(log.Event) || string.IsNullOrWhiteSpace(log.Message))
            {
                throw new SeedManifestException($"{where}: event en msg zijn allebei verplicht.");
            }

            yield return new LogRecord
            {
                Id = SeedUlid.Create(timestamp, $"{agent.AgentName}|{index}|{log.Event}"),
                PartitionKey = LogRecord.BuildPartitionKey(agent.AgentName, timestamp),
                Timestamp = timestamp,
                Level = log.Level,
                Event = log.Event,
                Message = log.Message,
                RunId = string.IsNullOrWhiteSpace(log.RunId) ? null : log.RunId,
                Extra = log.Extra,
                CustomerId = customer.Id,
                AgentName = agent.AgentName,
            };

            index++;
        }
    }
}

/// <summary>Alles wat er geschreven moet worden, plus de telling per klant.</summary>
/// <param name="Agents">De registratiedocumenten.</param>
/// <param name="Runs">De runs.</param>
/// <param name="Logs">De logregels.</param>
/// <param name="Customers">De telling per klant, in bestandsvolgorde.</param>
internal sealed record SeedPlan(
    IReadOnlyList<AgentRegistration> Agents,
    IReadOnlyList<RunRecord> Runs,
    IReadOnlyList<LogRecord> Logs,
    IReadOnlyList<SeedCustomerTally> Customers)
{
    /// <summary>
    /// De agentnamen waar dit gereedschap zich eigenaar van voelt. Dit is het enige waarop
    /// <c>--clean</c> zich baseert; zie <see cref="CosmosSeeder"/>.
    /// </summary>
    public IReadOnlyList<string> AgentNames { get; } = [.. Agents.Select(agent => agent.AgentName)];
}

/// <summary>Wat er voor één klant in het bestand staat.</summary>
/// <param name="Id">De slug.</param>
/// <param name="Name">De naam.</param>
/// <param name="Agents">Het aantal agents; nul is een geldige en betekenisvolle waarde.</param>
/// <param name="Runs">Het aantal runs.</param>
/// <param name="Logs">Het aantal logregels.</param>
internal sealed record SeedCustomerTally(string Id, string Name, int Agents, int Runs, int Logs);
