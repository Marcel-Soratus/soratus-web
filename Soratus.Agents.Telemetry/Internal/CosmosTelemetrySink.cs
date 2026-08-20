using System.Net;
using System.Text.Json;
using Azure.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// Schrijft de contractdocumenten naar Cosmos DB, met managed identity en zonder sleutels.
/// </summary>
/// <remarks>
/// De <c>CosmosClient</c> wordt hier alleen gemaakt, niet gecontroleerd. Dat is opzet: het
/// opzetten van een verbinding raakt het netwerk niet, dus een onbereikbare database kan het
/// opstarten van een agent niet tegenhouden.
/// </remarks>
internal sealed class CosmosTelemetrySink(
    IOptions<SoratusTelemetryOptions> options,
    TokenCredential credential,
    AgentIdentity identity) : ITelemetrySink, IDisposable
{
    /// <summary>De harde grens van een transactional batch in Cosmos.</summary>
    private const int MaxBatchOperations = 100;

    private readonly SoratusTelemetryOptions _options = options.Value;

    private readonly CosmosClient _client = new(
        options.Value.Endpoint,
        credential,
        new CosmosClientOptions
        {
            ApplicationName = identity.AgentName,
            // Zonder deze regel serialiseert de SDK met Newtonsoft en negeert hij de
            // [JsonPropertyName]-namen uit het contract. Dan staan er andere velden in de
            // database dan het portaal leest. Deze opties normaliseren tegelijk elke tijd naar
            // UTC met vaste precisie — zie TelemetryJson voor waarom dat hier moet gebeuren en
            // niet op de aanroepplekken.
            UseSystemTextJsonSerializerWithOptions = TelemetryJson.SerializerOptions,
            AllowBulkExecution = false,
        });

    private Container Agents => _client.GetContainer(_options.Database, _options.AgentsContainer);

    private Container Runs => _client.GetContainer(_options.Database, _options.RunsContainer);

    private Container Logs => _client.GetContainer(_options.Database, _options.LogsContainer);

    public Task UpsertRegistrationAsync(AgentRegistration registration, CancellationToken cancellationToken) =>
        GuardAsync(
            _options.AgentsContainer,
            () => Agents.UpsertItemAsync(
                registration,
                new PartitionKey(registration.PartitionKey),
                cancellationToken: cancellationToken));

    public Task UpsertRunAsync(RunRecord run, CancellationToken cancellationToken) =>
        GuardAsync(
            _options.RunsContainer,
            () => Runs.UpsertItemAsync(
                run,
                new PartitionKey(run.PartitionKey),
                cancellationToken: cancellationToken));

    public Task WriteLogsAsync(IReadOnlyList<LogRecord> logs, CancellationToken cancellationToken) =>
        GuardAsync(_options.LogsContainer, () => WriteLogBatchesAsync(logs, cancellationToken));

    private async Task WriteLogBatchesAsync(IReadOnlyList<LogRecord> logs, CancellationToken cancellationToken)
    {
        // Logregels van dezelfde agent en dezelfde dag delen hun partitiesleutel, dus in de
        // praktijk is dit één groep en één batch.
        foreach (IGrouping<string, LogRecord> group in logs.GroupBy(static log => log.PartitionKey))
        {
            var partition = new PartitionKey(group.Key);

            foreach (LogRecord[] chunk in group.Chunk(MaxBatchOperations))
            {
                if (chunk.Length == 1)
                {
                    await Logs.UpsertItemAsync(chunk[0], partition, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                TransactionalBatch batch = Logs.CreateTransactionalBatch(partition);
                foreach (LogRecord log in chunk)
                {
                    batch.UpsertItem(log);
                }

                using TransactionalBatchResponse response =
                    await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw new CosmosException(
                        $"Batch van {chunk.Length} logregels mislukte: {response.ErrorMessage}",
                        response.StatusCode,
                        subStatusCode: 0,
                        activityId: response.ActivityId,
                        requestCharge: response.RequestCharge);
                }
            }
        }
    }

    /// <summary>
    /// Vertaalt de twee fouten die nooit vanzelf overgaan naar een duidelijke melding: de
    /// container bestaat niet, of deze identiteit mag er niet in schrijven.
    /// </summary>
    private async Task GuardAsync(string container, Func<Task> write)
    {
        try
        {
            await write().ConfigureAwait(false);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new TelemetryConfigurationException(
                $"Database '{_options.Database}' of container '{container}' bestaat niet op {_options.Endpoint}. " +
                "De telemetriebibliotheek maakt die bewust niet aan: retentie en partitiesleutel horen bij de " +
                "inrichting van de omgeving, niet bij een agent.",
                exception);
        }
        catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new TelemetryConfigurationException(
                $"Deze identiteit mag niet schrijven in container '{container}' op {_options.Endpoint}. " +
                "Controleer de roltoewijzing van de managed identity op het Cosmos-account.",
                exception);
        }
    }

    public void Dispose() => _client.Dispose();
}
