using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soratus.Seed;

/// <summary>
/// De serialisatievorm waarin dit gereedschap naar Cosmos schrijft.
/// </summary>
/// <remarks>
/// Dit is een bewuste kopie van <c>Soratus.Agents.Telemetry.Internal.TelemetryJson</c>. Die klasse
/// is <c>internal</c> en hoort dat te blijven — de telemetriebibliotheek is voor agents, niet voor
/// gereedschap. De kopie mag dus bestaan, maar hij moet exact hetzelfde doen: elke tijd als UTC,
/// vaste breedte, zeven decimalen. Cosmos vergelijkt <c>DateTimeOffset</c>-velden als string, en de
/// logtabel en de runlijst in het portaal sorteren daar rechtstreeks op. Eén seed-document met een
/// offset van <c>+02:00</c> zou tussen de echte documenten verkeerd gaan sorteren, en dan is de
/// demodata niet langer stil.
///
/// <see cref="AssertCanonicalUtc"/> pint deze vorm vast. Let op wat die assertie <em>niet</em>
/// doet: hij vergelijkt niets met de telemetriebibliotheek. Dat kán hij ook niet — die klasse is
/// <c>internal</c> — dus hij toetst deze kopie tegen een letterlijke verwachting in zijn eigen
/// code. Zou de bibliotheek morgen op een ander formaat overgaan, dan blijft deze assertie
/// vrolijk groen. Ze bewaakt dus dat wij niet verschuiven, niet dat wij nog gelijk zijn.
///
/// Dat "nog gelijk zijn" is een meting en geen assertie: leg een geseed document en een document
/// van <c>heartbeat-demo</c> naast elkaar in de database en vergelijk de velden en de tijdvorm.
/// Dat is gedaan en het klopte. Wordt het formaat aan één van beide kanten verbouwd, dan moet die
/// vergelijking opnieuw — en dan zegt een groene start hier niets.
/// </remarks>
internal static class SeedJson
{
    /// <summary>Vaste breedte, altijd UTC, altijd zeven decimalen.</summary>
    internal const string UtcFormat = "yyyy-MM-ddTHH:mm:ss.fffffff'Z'";

    private static readonly Lazy<JsonSerializerOptions> LazyWriteOptions = new(() =>
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
        };

        options.Converters.Add(new UtcDateTimeOffsetConverter());
        options.Converters.Add(new UtcDateTimeConverter());
        // Bevriezen zodat niemand er later ongemerkt een converter uit haalt. De parameter is
        // nodig: het parameterloze MakeReadOnly() werpt op .NET 10 zolang er nog geen
        // TypeInfoResolver is gezet. Met de reflectie-resolver erbij is de uitvoer ongewijzigd.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    });

    private static readonly Lazy<JsonSerializerOptions> LazyManifestOptions = new(() =>
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        // Bevriezen zodat niemand er later ongemerkt een converter uit haalt. De parameter is
        // nodig: het parameterloze MakeReadOnly() werpt op .NET 10 zolang er nog geen
        // TypeInfoResolver is gezet. Met de reflectie-resolver erbij is de uitvoer ongewijzigd.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    });

    /// <summary>De opties waarmee documenten naar Cosmos gaan.</summary>
    internal static JsonSerializerOptions SerializerOptions => LazyWriteOptions.Value;

    /// <summary>De opties waarmee <c>telemetry.json</c> wordt gelezen.</summary>
    internal static JsonSerializerOptions ManifestOptions => LazyManifestOptions.Value;

    /// <summary>
    /// Controleert bij het opstarten dat de normalisatie naar UTC er nog op zit.
    /// </summary>
    /// <remarks>
    /// Dezelfde soort proef als in de telemetriebibliotheek: twee momenten die gelijk zijn maar in
    /// verschillende offsets zijn uitgedrukt, plus een waarde zonder decimalen. Komen die er niet
    /// alle drie identiek uit, dan sorteren de seed-documenten anders dan de echte en stopt dit
    /// gereedschap voordat het iets wegschrijft.
    ///
    /// De verwachting staat hier als letterlijke tekst, niet als verwijzing naar de bibliotheek.
    /// Deze assertie toetst dus <em>deze</em> kopie tegen een vastgelegde vorm; hij bewijst niet dat
    /// die vorm nog dezelfde is als die van de bibliotheek. Zie de opmerking bij
    /// <see cref="SeedJson"/> voor hoe dat wél gemeten wordt. Vandaar ook de naam: hij zegt wat
    /// er gecontroleerd wordt en niet meer.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Als een tijd niet als UTC wordt geschreven.</exception>
    internal static void AssertCanonicalUtc()
    {
        var probe = new TimeProbe(
            new DateTimeOffset(2026, 8, 19, 17, 14, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 8, 19, 15, 14, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 15, 13, 19, 944, TimeSpan.Zero).AddTicks(9045));

        string json = JsonSerializer.Serialize(probe, SerializerOptions);

        if (json != """{"a":"2026-08-19T15:14:00.0000000Z","b":"2026-08-19T15:14:00.0000000Z","c":"2026-08-19T15:13:19.9449045Z"}""")
        {
            throw new InvalidOperationException(
                "De seed-serialisatie wijkt af van die van de telemetriebibliotheek. Daarmee zouden de " +
                "seed-documenten anders sorteren dan de echte documenten. Gevonden: " + json);
        }
    }

    private sealed record TimeProbe(DateTimeOffset A, DateTimeOffset? B, DateTimeOffset C);

    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(
                reader.GetString() ?? string.Empty,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture));
    }

    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.Parse(
                reader.GetString() ?? string.Empty,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture));
    }
}
