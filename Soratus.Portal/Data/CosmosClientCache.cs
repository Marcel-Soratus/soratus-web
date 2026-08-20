using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Core;
using Microsoft.Azure.Cosmos;
using Soratus.Agents.Contracts;

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
    /// De serialisatie-opties waarmee dit portaal leest én schrijft.
    /// </summary>
    /// <remarks>
    /// <para>Hier stond alleen <c>new JsonSerializerOptions(JsonSerializerDefaults.Web)</c>, en dat
    /// was genoeg zolang het portaal alleen las. Sinds fase 2 schrijft het, en toen bleek dat een
    /// tijdstempel er als <c>2026-08-20T15:04:05.678+00:00</c> uitging: een offset in plaats van een
    /// <c>Z</c>, en een variabel aantal decimalen. Cosmos bewaart die velden als tekst en
    /// <c>ORDER BY</c> vergelijkt ze lexicografisch, dus die vorm sorteert stil verkeerd — zie punt
    /// 7 van de fase-0-afwijkingen en <see cref="TimestampNormalization"/>.</para>
    ///
    /// <para>Statisch en niet per client: het zijn de opties van dít proces, niet van dít endpoint.
    /// Bevroren nadat ze zijn samengesteld, zodat er later niet ongemerkt een converter uit gehaald
    /// kan worden; de assertie loopt vóór het bevriezen en vóór de eerste schrijfactie.</para>
    ///
    /// <para><strong>Waarom <c>internal</c> en niet <c>private</c>.</strong> Een test die zijn eigen
    /// opties opbouwt "net zoals het portaal het doet" bewijst iets over die kopie en niets over wat
    /// de SDK meekrijgt — precies de fout die deze reparatie ongedaan maakt. Het testproject ziet
    /// dit veld via de <c>InternalsVisibleTo</c> in <c>Soratus.Portal.csproj</c> en serialiseert er
    /// een echt portaaldocument mee. Er valt niets mee te slopen: het veld is <c>readonly</c> en het
    /// object is bevroren, dus een lezer kan er geen converter uit halen.</para>
    /// </remarks>
    internal static readonly JsonSerializerOptions SerializerOptions = BuildSerializerOptions();

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
                UseSystemTextJsonSerializerWithOptions = SerializerOptions,

                // Gateway-modus houdt het aantal uitgaande verbindingen klein, wat telt zodra er
                // per klant een account bij komt.
                ConnectionMode = ConnectionMode.Gateway,
            }));
    }

    /// <summary>
    /// Stelt de opties samen en bewijst daarna dat tijdstempels er canoniek uit komen.
    /// </summary>
    /// <remarks>
    /// <para>De assertie staat hier en niet in <c>Program.cs</c>, want dit is de plek waar de opties
    /// gemaakt worden. Wie hier een converter uit haalt, krijgt de fout bij de eerste aanraking van
    /// deze klasse — niet drie fasen later bij de eerste lijst die verkeerd sorteert.</para>
    ///
    /// <para>Preciezer dan "bij het opstarten", want dat is het niet: dit is een statische
    /// veldinitialisatie en die loopt lui, bij het eerste gebruik van deze klasse. Dat is vóór de
    /// eerste lees- of schrijfactie — geen enkel Cosmos-verkeer gaat hier langs heen — maar niet
    /// vóór het eerste verzoek, en de fout komt dan verpakt in een
    /// <c>TypeInitializationException</c>. Bewust zo gelaten: eerder afgaan vraagt een aanroep in
    /// <c>Program.cs</c>, en dan staat de controle níet meer op de plek waar de opties gemaakt
    /// worden. Zie punt 25 van de fase-0-afwijkingen.</para>
    /// </remarks>
    private static JsonSerializerOptions BuildSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        TimestampNormalization.Register(options);
        TimestampNormalization.AssertCanonical(options);

        // De parameter is nodig: het parameterloze MakeReadOnly() werpt zolang er nog geen
        // TypeInfoResolver is gezet. Zelfde afweging als in het seed-gereedschap.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
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
