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
                ErrorMessage = ErrorMessageOf(run, where),
                Version = agent.Version,
            };
        }
    }

    /// <summary>
    /// De foutmelding van een run, met dezelfde eis erop als op een logbericht: één regel.
    /// </summary>
    /// <remarks>
    /// <para><see cref="RunRecord.ErrorMessage"/> is klantzichtbaar — het portaal draagt hem op de
    /// runrij en er is geen operator/klant-splitsing op runs zoals er wel één op logregels is. Er is
    /// dus geen vangnet: wat hier in gaat, leest de klant. De bibliotheek knipt daarom ook dit veld
    /// en niet alleen <c>msg</c>, en dit gereedschap schrijft runs zelf, dus zonder deze regel zou
    /// de seed opnieuw het ene document met een halve pagina diagnostiek erin zijn.</para>
    ///
    /// <para><strong>En hier weigeren we in plaats van te knippen.</strong> Dat is een ander besluit
    /// dan bij <c>msg</c>, met een reden. Bij de bibliotheek moet de knip zacht landen: een agent in
    /// productie mag niet omvallen over de vorm van een foutmelding, en de volledige tekst blijft
    /// daar bewaard in de bijbehorende <c>run.failed</c>-logregel onder <c>extra</c>. Hier is geen van
    /// beide waar. Een <see cref="RunRecord"/> heeft geen veld voor vrije JSON, dus knippen zou de
    /// rest wég gooien in plaats van verplaatsen — en de bron is een bestand dat een mens onderhoudt,
    /// dus de goedkoopste plek om het op te lossen is dat bestand. Stil afkappen zou de auteur
    /// nooit vertellen dat zijn tekst half in de database staat.</para>
    /// </remarks>
    private static string? ErrorMessageOf(SeedRun run, string where)
    {
        if (string.IsNullOrEmpty(run.ErrorMessage))
        {
            return run.ErrorMessage;
        }

        var (message, overflow) = MessageTruncation.Cut(run.ErrorMessage);

        if (overflow is not null)
        {
            throw new SeedManifestException(
                $"{where}: errorMessage bestaat uit meer dan één regel. Dat veld is klantzichtbaar en " +
                "een RunRecord heeft geen extra om de rest in te bewaren, dus er valt hier niets te " +
                "verplaatsen. Kort de melding in tot één zin en zet de techniek in de extra van de " +
                "bijbehorende run.failed-logregel.");
        }

        return message;
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

            if (log.Message.AsSpan().IndexOfAny('\n', '\r') == 0)
            {
                throw new SeedManifestException(
                    $"{where}: msg begint met een regelovergang, dus er blijft geen zin over om te tonen.");
            }

            // Het contract wil één zin in msg. Alles vanaf de eerste regelovergang gaat naar extra.
            // De regel zelf staat in Soratus.Agents.Contracts en wordt door de telemetriebibliotheek
            // en door het portaal ook gebruikt — één definitie, want de seed hoort in de database
            // niet van een echt document te verschillen.
            var (message, overflow) = MessageTruncation.Cut(log.Message);

            yield return new LogRecord
            {
                Id = SeedUlid.Create(timestamp, $"{agent.AgentName}|{index}|{log.Event}"),
                PartitionKey = LogRecord.BuildPartitionKey(agent.AgentName, timestamp),
                Timestamp = timestamp,
                Level = log.Level,
                Event = log.Event,
                Message = message,
                RunId = string.IsNullOrWhiteSpace(log.RunId) ? null : log.RunId,
                Extra = overflow is null ? log.Extra : ExtraOverflow.Merge(log.Extra, overflow, where),
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

    /// <summary>Hoeveel berichten op hun eerste regelovergang zijn geknipt.</summary>
    public int CutMessages { get; } =
        Logs.Count(log => log.Message.EndsWith(MessageTruncation.Marker, StringComparison.Ordinal));

    /// <summary>
    /// Het langste bericht dat weggeschreven wordt, met de agent en de gebeurtenis erbij.
    /// </summary>
    /// <remarks>
    /// Staat in de uitvoer omdat de lengte van <c>msg</c> een meetbaar feit hoort te zijn en geen
    /// aanname. Een lang bericht is toegestaan zolang het één regel is; wat niet mag is een
    /// regelovergang, en dat is wat <see cref="MultiLineMessages"/> bewaakt.
    /// </remarks>
    public (int Length, string AgentName, string Event) LongestMessage { get; } = Logs.Count == 0
        ? (0, "—", "—")
        : Logs
            .Select(log => (Length: log.Message.Length, log.AgentName, log.Event))
            .OrderByDescending(entry => entry.Length)
            .First();

    /// <summary>
    /// Hoeveel berichten er ná de knip nog een regelovergang bevatten. Hoort nul te zijn; is dat
    /// niet zo, dan klopt <see cref="MessageTruncation"/> niet meer.
    /// </summary>
    public int MultiLineMessages { get; } =
        Logs.Count(log => log.Message.AsSpan().IndexOfAny('\n', '\r') >= 0);
}

/// <summary>Wat er voor één klant in het bestand staat.</summary>
/// <param name="Id">De slug.</param>
/// <param name="Name">De naam.</param>
/// <param name="Agents">Het aantal agents; nul is een geldige en betekenisvolle waarde.</param>
/// <param name="Runs">Het aantal runs.</param>
/// <param name="Logs">Het aantal logregels.</param>
internal sealed record SeedCustomerTally(string Id, string Name, int Agents, int Runs, int Logs);
