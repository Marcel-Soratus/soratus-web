using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Core;
using Microsoft.Azure.Cosmos;

namespace Soratus.Portal.Data;

/// <summary>
/// Eén <see cref="CosmosClient"/> per account-endpoint, hergebruikt zolang de app draait.
/// </summary>
/// <remarks>
/// <para>Een <see cref="CosmosClient"/> is duur om op te zetten — hij bouwt een verbindingenpool
/// op en houdt routeringsinformatie bij — en is expliciet bedoeld om te delen. Er per verzoek
/// eentje maken kost meer dan de query zelf en put uiteindelijk de poorten van de machine uit.
/// </para>
///
/// <para>De cache is per endpoint en niet per klant: klanten die (nu nog) hetzelfde account delen,
/// delen dus ook de client. Zodra elke klant zijn eigen account heeft, groeit dit vanzelf mee naar
/// één client per klant, zonder wijziging.</para>
///
/// <para>De <c>TokenCredential</c> wordt gedeeld. <c>DefaultAzureCredential</c> cachet zijn tokens
/// intern en is thread-safe; per client een nieuwe maken zou bij elke nieuwe klant een nieuwe
/// tokenaanvraag betekenen.</para>
/// </remarks>
internal sealed class CosmosClientCache(TokenCredential credential) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, CosmosClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// De client voor dit account-endpoint, aangemaakt bij de eerste aanvraag.
    /// </summary>
    /// <param name="accountEndpoint">De endpoint van het Cosmos-account.</param>
    /// <returns>De gedeelde client.</returns>
    public CosmosClient For(string accountEndpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountEndpoint);

        return _clients.GetOrAdd(accountEndpoint, endpoint => new CosmosClient(
            endpoint,
            credential,
            new CosmosClientOptions
            {
                ApplicationName = "soratus-portal",

                // Zonder deze regel serialiseert de SDK met Newtonsoft en negeert hij de
                // [JsonPropertyName]-namen uit het contract. Dan leest het portaal andere velden
                // dan de telemetriebibliotheek schrijft. Dezelfde instelling staat aan de
                // schrijfkant in Soratus.Agents.Telemetry.
                UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),

                // Het portaal leest alleen. Gateway-modus houdt het aantal uitgaande verbindingen
                // klein, wat telt zodra er per klant een account bij komt.
                ConnectionMode = ConnectionMode.Gateway,
            }));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _clients.Clear();
        return ValueTask.CompletedTask;
    }
}
