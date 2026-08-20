using System.Text.Json;
using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// De enige serialisatievorm waarin deze bibliotheek naar Cosmos schrijft.
/// </summary>
/// <remarks>
/// Alles staat hier op één plek omdat één afwijking al genoeg is. Cosmos bewaart een
/// <c>DateTimeOffset</c> als string en <c>ORDER BY</c> vergelijkt die lexicografisch. Twee
/// documenten met dezelfde momenten maar verschillende offsets — <c>15:13:19+00:00</c> naast
/// <c>17:14:00+02:00</c> — sorteren dan verkeerd, en de logtabel en runlijst in het portaal
/// sorteren precies op die velden. Lokaal valt dat niet op, want in C# vergelijkt
/// <c>DateTimeOffset</c> wél correct.
///
/// Daarom: elke tijd gaat als UTC de deur uit, met een vast aantal decimalen, in een vorm van
/// vaste lengte. Dan is lexicografisch sorteren gelijk aan chronologisch sorteren. Dit wordt
/// niet per aanroepplek geregeld maar in de serializer, want een <c>.ToUniversalTime()</c> op
/// twintig plekken is er negentien te veel om te vergeten.
///
/// De converter en het formaat staan sinds de reparatie van de schrijfkant van het portaal in
/// <see cref="TimestampNormalization"/>, in het contractproject. Ze golden namelijk al voor drie
/// schrijvers — deze bibliotheek, het seed-gereedschap en het portaal — en van die drie had het
/// portaal ze niet en had het seed-gereedschap een eigen kopie. Dezelfde afweging als bij
/// <see cref="MessageTruncation"/>: een regel die op meer dan één plek moet gelden hoort één
/// implementatie te hebben.
/// </remarks>
internal static class TelemetryJson
{
    private static readonly Lazy<JsonSerializerOptions> LazyOptions = new(() =>
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
        };

        TimestampNormalization.Register(options);
        return options;
    });

    /// <summary>De serialisatie-opties voor alles wat naar de opslag gaat.</summary>
    internal static JsonSerializerOptions SerializerOptions => LazyOptions.Value;

    /// <summary>
    /// Controleert bij het opstarten dat de normalisatie er echt op zit.
    /// </summary>
    /// <remarks>
    /// Een assertie en geen test, omdat een test in een ander project staat en dus overgeslagen
    /// kan worden. Dit loopt bij elke start van elke agent, kost een handvol serialisaties, en werpt
    /// meteen als iemand ooit een eigen <c>JsonSerializerOptions</c> doorgeeft of deze converter
    /// weghaalt. Zo kan de sorteerfout niet terugkomen zonder dat het opvalt.
    ///
    /// De vormcontrole zelf staat in <see cref="TimestampNormalization.AssertCanonical"/> en geldt
    /// voor alle schrijvers. Wat hier bovenop komt is de proef op het echte contracttype, en dat is
    /// geen dubbelop: een <c>[JsonConverter]</c> op een property gaat vóór een converter in de
    /// opties, en een tijdveld dat als <c>string</c> in het contract belandt gaat langs de
    /// normalisatie heen. Geen van beide is aan de opties te zien.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Als een tijd niet als UTC wordt geschreven.</exception>
    internal static void AssertCanonicalUtc()
    {
        // Eerst de gedeelde vormcontrole op precies de opties waarmee geschreven wordt.
        TimestampNormalization.AssertCanonical(SerializerOptions);

        // Bewust het echte contracttype en bewust precies de vormen uit het foute document: een
        // hartslag in UTC met zeven decimalen naast een nextRunAt in +02:00 zonder decimalen.
        var probe = new AgentRegistration
        {
            Id = "probe",
            PartitionKey = "probe",
            CustomerId = "probe",
            AgentName = "probe",
            DisplayType = "probe",
            Version = "0.0.0",
            StartedAt = new DateTimeOffset(2026, 8, 19, 15, 0, 0, TimeSpan.Zero),
            LastHeartbeatAt = new DateTimeOffset(2026, 8, 19, 15, 13, 19, 944, TimeSpan.Zero).AddTicks(9045),
            NextRunAt = new DateTimeOffset(2026, 8, 19, 17, 14, 0, TimeSpan.FromHours(2)),
            Lifecycle = AgentLifecycle.Running,
            TriggerKind = TriggerKind.Timer,
            Environment = AgentEnvironment.Production,
        };

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(probe, SerializerOptions));

        foreach (string field in (string[])["startedAt", "lastHeartbeatAt", "nextRunAt"])
        {
            string value = document.RootElement.GetProperty(field).GetString() ?? string.Empty;

            // Vaste lengte én afsluitende Z: alleen dan is lexicografisch sorteren in Cosmos
            // hetzelfde als chronologisch sorteren.
            if (value.Length != TimestampNormalization.Width
                || !value.EndsWith('Z')
                || value[10] != 'T'
                || value[19] != '.')
            {
                throw new InvalidOperationException(
                    "De telemetrieserialisatie normaliseert tijden niet meer naar UTC met vaste precisie. " +
                    $"Daarmee sorteren de logtabel en de runlijst in het portaal stil verkeerd. Veld '{field}' " +
                    $"werd '{value}', verwacht was de vorm '{TimestampNormalization.UtcFormat}'.");
            }
        }

        // En het moment zelf mag niet verschoven zijn: 17:14+02:00 is 15:14 UTC.
        if (document.RootElement.GetProperty("nextRunAt").GetString() != "2026-08-19T15:14:00.0000000Z")
        {
            throw new InvalidOperationException(
                "De telemetrieserialisatie rekent een tijd met offset verkeerd om naar UTC.");
        }
    }
}
