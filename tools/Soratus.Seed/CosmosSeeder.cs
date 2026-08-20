using System.Net;
using Azure.Core;
using Microsoft.Azure.Cosmos;
using Soratus.Agents.Contracts;

namespace Soratus.Seed;

/// <summary>
/// Schrijft, controleert en ruimt de demodata op in de echte Cosmos.
/// </summary>
/// <remarks>
/// <para><strong>Waarom hier geen markering in de documenten staat.</strong> Het zou makkelijk zijn
/// om elk seed-document een veldje <c>seed: true</c> mee te geven; <c>--clean</c> zou dan simpelweg
/// daarop kunnen filteren. Dat is bewust niet gedaan. De hele opzet van dit gereedschap is dat het
/// portaal niet kan zien dat het naar demodata kijkt: het leest zijn gewone bron in de gewone vorm.
/// Een extra veld zou dat onderscheid weer invoeren, en daarmee een mocklaag door de achterdeur.</para>
///
/// <para><strong>Waar <c>--clean</c> zich dan wél op baseert.</strong> Op de agentnaam. De namen in
/// <c>telemetry.json</c> zijn de namen van agents die niet bestaan; er draait geen proces dat onder
/// die naam telemetrie schrijft. Alles in de drie containers met zo'n naam is dus door dit
/// gereedschap gezet. Documenten van een agent die niet in het bestand staat worden nooit
/// aangeraakt — ook niet als hij bij dezelfde klant hoort. Bovenop die regel ligt een tweede,
/// onafhankelijke grendel: namen uit <see cref="SeedPlanner.ProtectedAgents"/> worden altijd
/// overgeslagen, bij zowel schrijven als verwijderen.</para>
///
/// <para><strong>De keerzijde, expliciet.</strong> Hernoem je een agent in het bestand, dan valt de
/// oude naam buiten het bereik en blijven zijn documenten staan. Draai dan eerst <c>--clean</c> met
/// het oude bestand. Dat is de prijs voor niet-markeren en hij is bewust betaald.</para>
/// </remarks>
internal sealed class CosmosSeeder(SeedSettings settings, TokenCredential credential) : IDisposable
{
    /// <summary>Hoeveel schrijf- of verwijderacties er tegelijk lopen.</summary>
    private const int MaxParallelism = 16;

    private readonly CosmosClient _client = new(
        settings.Endpoint,
        credential,
        new CosmosClientOptions
        {
            ApplicationName = "soratus-seed",
            // Dezelfde serializer als de telemetriebibliotheek. Zonder deze regel schrijft de SDK
            // met Newtonsoft, negeert hij de [JsonPropertyName]-namen uit het contract en zet hij
            // tijden in een andere vorm weg. Dan staan er andere documenten in de database dan het
            // portaal verwacht.
            UseSystemTextJsonSerializerWithOptions = SeedJson.SerializerOptions,
            AllowBulkExecution = false,
        });

    private Container Agents => _client.GetContainer(settings.Database, settings.AgentsContainer);

    private Container Runs => _client.GetContainer(settings.Database, settings.RunsContainer);

    private Container Logs => _client.GetContainer(settings.Database, settings.LogsContainer);

    /// <summary>Schrijft het plan weg en ruimt daarna op wat er niet meer bij hoort.</summary>
    /// <param name="plan">Wat er in Cosmos hoort te staan.</param>
    /// <param name="dryRun">Bij <c>true</c> wordt er niets geschreven of verwijderd.</param>
    /// <param name="cancellationToken">Afbreeksein.</param>
    internal async Task SeedAsync(SeedPlan plan, bool dryRun, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var names = plan.AgentNames;

        Console.WriteLine("Bestaande seed-documenten opzoeken...");
        var existingAgents = await ExistingAsync(Agents, names, cancellationToken).ConfigureAwait(false);
        var existingRuns = await ExistingAsync(Runs, names, cancellationToken).ConfigureAwait(false);
        var existingLogs = await ExistingAsync(Logs, names, cancellationToken).ConfigureAwait(false);

        Console.WriteLine(
            $"  gevonden: {existingAgents.Count} agents, {existingRuns.Count} runs, {existingLogs.Count} logregels " +
            "van een eerdere seed.");
        Console.WriteLine();

        Console.WriteLine(dryRun ? "Zou schrijven:" : "Schrijven...");
        await UpsertAsync(Agents, settings.AgentsContainer, plan.Agents, item => item.PartitionKey, dryRun, cancellationToken)
            .ConfigureAwait(false);
        await UpsertAsync(Runs, settings.RunsContainer, plan.Runs, item => item.PartitionKey, dryRun, cancellationToken)
            .ConfigureAwait(false);
        await UpsertAsync(Logs, settings.LogsContainer, plan.Logs, item => item.PartitionKey, dryRun, cancellationToken)
            .ConfigureAwait(false);

        // Wat er na deze seed hoort te staan. Alles daarbuiten — met een agentnaam uit het bestand —
        // is van een eerdere seed en moet weg, anders stapelen runs en logregels zich op bij elke
        // keer draaien en klopt de eindtoestand niet meer met het bestand.
        var keep = new HashSet<DocumentKey>();
        foreach (var agent in plan.Agents)
        {
            keep.Add(new DocumentKey(settings.AgentsContainer, agent.PartitionKey, agent.Id));
        }

        foreach (var run in plan.Runs)
        {
            keep.Add(new DocumentKey(settings.RunsContainer, run.PartitionKey, run.Id));
        }

        foreach (var log in plan.Logs)
        {
            keep.Add(new DocumentKey(settings.LogsContainer, log.PartitionKey, log.Id));
        }

        var stale = new List<(Container Container, string Name, IReadOnlyList<DocumentRef> Documents)>
        {
            (Agents, settings.AgentsContainer, Filter(existingAgents, settings.AgentsContainer, keep)),
            (Runs, settings.RunsContainer, Filter(existingRuns, settings.RunsContainer, keep)),
            (Logs, settings.LogsContainer, Filter(existingLogs, settings.LogsContainer, keep)),
        };

        if (stale.Sum(entry => entry.Documents.Count) == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Opruimen: niets van een eerdere seed dat nu overbodig is.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine(dryRun ? "Zou opruimen (van een eerdere seed):" : "Opruimen van een eerdere seed...");

        foreach (var (container, name, documents) in stale)
        {
            await DeleteAsync(container, name, documents, dryRun, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Schrijft alleen de registraties opnieuw weg, zodat hun hartslag weer vers is.
    /// </summary>
    /// <param name="registrations">De registraties, opnieuw gerekend op het huidige moment.</param>
    /// <param name="now">Dat moment.</param>
    /// <param name="cancellationToken">Afbreeksein.</param>
    /// <returns>Hoeveel er vers zijn en hoeveel er bewust stil blijven.</returns>
    /// <remarks>
    /// Nadrukkelijk alleen de container <c>agents</c>. Runs en logregels blijven staan en worden
    /// dus ouder — dat hoort ook: een run die tien minuten geleden liep is tien minuten geleden
    /// gelopen, en een demo waarin de laatste run eeuwig "zojuist" is, is geen demo maar een
    /// schilderij.
    ///
    /// Elke agent houdt zijn eigen afstand tot nu, zoals die in <c>telemetry.json</c> staat. Wie
    /// daar <c>-12s</c> heeft blijft twaalf seconden geleden gezien en dus live; wie <c>-8m7s</c>
    /// heeft blijft acht minuten stil en dus degraded. De statusmatrix blijft daarmee precies
    /// staan zoals hij bedoeld is, in plaats van na twee minuten in één kleur te vallen.
    /// </remarks>
    internal async Task<(int Fresh, int Silent)> RefreshHeartbeatsAsync(
        IReadOnlyList<AgentRegistration> registrations,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        await Parallel.ForEachAsync(
            registrations,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = cancellationToken },
            async (registration, token) =>
            {
                await GuardAsync(
                    settings.AgentsContainer,
                    () => Agents.UpsertItemAsync(
                        registration,
                        new PartitionKey(registration.PartitionKey),
                        cancellationToken: token)).ConfigureAwait(false);
            }).ConfigureAwait(false);

        var fresh = registrations.Count(registration => AgentStatusCalculator.IsHeartbeatFresh(registration, now));
        return (fresh, registrations.Count - fresh);
    }

    /// <summary>Verwijdert alle documenten van de agents uit het bestand, en niets anders.</summary>
    /// <param name="names">De agentnamen uit <c>telemetry.json</c>.</param>
    /// <param name="dryRun">Bij <c>true</c> wordt er niets verwijderd.</param>
    /// <param name="cancellationToken">Afbreeksein.</param>
    internal async Task CleanAsync(IReadOnlyList<string> names, bool dryRun, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(names);

        Console.WriteLine(dryRun ? "Zou opruimen:" : "Opruimen...");

        foreach (var (container, name) in new[]
                 {
                     (Agents, settings.AgentsContainer),
                     (Runs, settings.RunsContainer),
                     (Logs, settings.LogsContainer),
                 })
        {
            var documents = await ExistingAsync(container, names, cancellationToken).ConfigureAwait(false);
            await DeleteAsync(container, name, documents, dryRun, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Telt na afloop wat er werkelijk in de database staat en controleert of de beschermde agents
    /// nog heel zijn.
    /// </summary>
    /// <param name="cancellationToken">Afbreeksein.</param>
    internal async Task VerifyAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Controle — wat staat er nu werkelijk in Cosmos:");

        foreach (var (container, name) in new[]
                 {
                     (Agents, settings.AgentsContainer),
                     (Runs, settings.RunsContainer),
                     (Logs, settings.LogsContainer),
                 })
        {
            var buckets = await ReadAllAsync<CustomerBucket>(
                container,
                new QueryDefinition("SELECT c.customerId AS customerId, COUNT(1) AS count FROM c GROUP BY c.customerId"),
                cancellationToken).ConfigureAwait(false);

            var total = buckets.Sum(bucket => bucket.Count);
            Console.WriteLine($"  {name,-7} {total,6} documenten");

            foreach (var bucket in buckets.OrderByDescending(bucket => bucket.Count))
            {
                Console.WriteLine($"          {bucket.CustomerId ?? "(zonder klant)",-10} {bucket.Count,6}");
            }
        }

        Console.WriteLine();

        foreach (var protectedName in SeedPlanner.ProtectedAgents)
        {
            try
            {
                var response = await Agents.ReadItemAsync<AgentRegistration>(
                    protectedName,
                    new PartitionKey(protectedName),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var registration = response.Resource;
                Console.WriteLine(
                    $"  {protectedName}: aanwezig en onaangeraakt — klant '{registration.CustomerId}', " +
                    $"versie {registration.Version}, laatste hartslag {registration.LastHeartbeatAt.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z.");
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine(
                    $"  {protectedName}: niet gevonden. Dit gereedschap heeft hem niet weggehaald — het raakt " +
                    "beschermde agents nooit aan — maar controleer of de referentie-agent nog draait.");
            }
        }
    }

    private static IReadOnlyList<DocumentRef> Filter(
        IReadOnlyList<DocumentRef> existing,
        string container,
        HashSet<DocumentKey> keep) =>
    [
        .. existing.Where(document =>
            !keep.Contains(new DocumentKey(container, document.PartitionKey, document.Id)))
    ];

    private async Task<IReadOnlyList<DocumentRef>> ExistingAsync(
        Container container,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return [];
        }

        var query = new QueryDefinition(
                "SELECT c.id AS id, c.pk AS pk, c.agentName AS agentName FROM c " +
                "WHERE ARRAY_CONTAINS(@names, c.agentName)")
            .WithParameter("@names", names);

        return await ReadAllAsync<DocumentRef>(container, query, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertAsync<T>(
        Container container,
        string name,
        IReadOnlyList<T> items,
        Func<T, string> partitionKey,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            Console.WriteLine($"  {name,-7} {items.Count,6} documenten");
            return;
        }

        var written = 0;

        await Parallel.ForEachAsync(
            items,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = cancellationToken },
            async (item, token) =>
            {
                await GuardAsync(
                    name,
                    () => container.UpsertItemAsync(item, new PartitionKey(partitionKey(item)), cancellationToken: token))
                    .ConfigureAwait(false);

                Interlocked.Increment(ref written);
            }).ConfigureAwait(false);

        Console.WriteLine($"  {name,-7} {written,6} documenten geschreven");
    }

    private async Task DeleteAsync(
        Container container,
        string name,
        IReadOnlyList<DocumentRef> documents,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        // Tweede grendel. De eerste is dat een beschermde naam nooit in het plan komt; deze vangt
        // het geval dat er langs een andere weg toch een beschermd document in de lijst belandt.
        var safe = documents
            .Where(document => !SeedPlanner.ProtectedAgents.Contains(document.AgentName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var skipped = documents.Count - safe.Count;

        if (skipped > 0)
        {
            Console.WriteLine($"  {name,-7} {skipped,6} documenten overgeslagen: beschermde agent.");
        }

        if (dryRun)
        {
            Console.WriteLine($"  {name,-7} {safe.Count,6} documenten");
            return;
        }

        var removed = 0;

        await Parallel.ForEachAsync(
            safe,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = cancellationToken },
            async (document, token) =>
            {
                try
                {
                    await container.DeleteItemAsync<object>(
                        document.Id,
                        new PartitionKey(document.PartitionKey),
                        cancellationToken: token).ConfigureAwait(false);

                    Interlocked.Increment(ref removed);
                }
                catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
                {
                    // Al weg, bijvoorbeeld door de TTL van de container. Geen probleem.
                }
            }).ConfigureAwait(false);

        Console.WriteLine($"  {name,-7} {removed,6} documenten verwijderd");
    }

    private static async Task<List<T>> ReadAllAsync<T>(
        Container container,
        QueryDefinition query,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();
        using var iterator = container.GetItemQueryIterator<T>(query);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            results.AddRange(page);
        }

        return results;
    }

    /// <summary>
    /// Vertaalt de twee fouten die nooit vanzelf overgaan naar een melding waar je iets aan hebt.
    /// </summary>
    private async Task GuardAsync(string container, Func<Task> write)
    {
        try
        {
            await write().ConfigureAwait(false);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SeedException(
                $"Database '{settings.Database}' of container '{container}' bestaat niet op {settings.Endpoint}. " +
                "Dit gereedschap maakt die bewust niet aan: retentie en partitiesleutel horen bij de inrichting " +
                "van de omgeving.",
                exception);
        }
        catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new SeedException(
                $"Deze identiteit mag niet schrijven in container '{container}' op {settings.Endpoint}. " +
                "Local auth staat uit op dit account; controleer of je bent ingelogd met een account dat " +
                "'Cosmos DB Built-in Data Contributor' heeft.",
                exception);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    /// <summary>
    /// Eén document, uniek aangeduid met de container, de partitiesleutel en de id.
    /// </summary>
    /// <param name="Container">De containernaam.</param>
    /// <param name="PartitionKey">De partitiesleutel.</param>
    /// <param name="Id">De documentsleutel.</param>
    /// <remarks>
    /// Bewust een record en geen samengestelde string. De vorige versie plakte de drie delen met een
    /// scheidingsteken aan elkaar, en dan moet je een teken kiezen dat in geen van de drie kan
    /// voorkomen — een spatie kan dat niet garanderen en <c>|</c> zit al ín elke partitiesleutel
    /// (<c>agentnaam|dag</c>). Met drie losse velden bestaat die vraag niet. Een record struct geeft
    /// bovendien gratis de juiste <c>Equals</c> en <c>GetHashCode</c> voor gebruik in een
    /// <see cref="HashSet{T}"/>.
    /// </remarks>
    private readonly record struct DocumentKey(string Container, string PartitionKey, string Id);

    /// <summary>Genoeg van een document om het te kunnen verwijderen.</summary>
    private sealed record DocumentRef(string Id, string Pk, string? AgentName)
    {
        /// <summary>De partitiesleutel.</summary>
        public string PartitionKey => Pk;
    }

    /// <summary>Het resultaat van een telling per klant.</summary>
    private sealed record CustomerBucket(string? CustomerId, int Count);
}

/// <summary>Wordt geworpen als het seeden op een inrichtings- of rechtenprobleem stuit.</summary>
internal sealed class SeedException(string message, Exception? inner = null) : Exception(message, inner);
