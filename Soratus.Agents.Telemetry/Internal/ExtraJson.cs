using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// Bouwt het <c>extra</c>-veld van een <c>LogRecord</c>.
/// </summary>
/// <remarks>
/// Sleutels van de bouwer staan op het hoogste niveau, want daar kijkt de operator naar.
/// Alles wat de bibliotheek zelf toevoegt begint met een liggend streepje (<c>_category</c>,
/// <c>_exception</c>, <c>_scopes</c>), zodat het onderin het uitklappaneel staat en niet met de
/// namen van de bouwer botst.
/// </remarks>
internal static class ExtraJson
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    /// <summary>
    /// Dezelfde opties als de rest van de bibliotheek, zodat een tijdstempel in <c>extra</c>
    /// dezelfde vorm heeft als een tijdstempel in een contractveld.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = TelemetryJson.SerializerOptions;

    /// <summary>
    /// Zet de structured-logging-state, een eigen payload, scopes en een uitzondering om naar
    /// één JSON-object. Werpt nooit: een onserialiseerbare waarde mag geen logregel kosten.
    /// </summary>
    internal static JsonElement? Build(
        IEnumerable<KeyValuePair<string, object?>>? state,
        object? payload,
        Exception? exception,
        string? category,
        EventId eventId,
        IExternalScopeProvider? scopeProvider,
        int maxLength)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);

        AppendPayload(fields, payload);
        AppendState(fields, state);
        AppendScopes(fields, scopeProvider);

        if (!string.IsNullOrEmpty(category))
        {
            fields["_category"] = category;
        }

        if (eventId.Id != 0)
        {
            fields["_eventId"] = eventId.Id;
        }

        if (exception is not null)
        {
            fields["_exception"] = Describe(exception, depth: 0);
        }

        if (fields.Count == 0)
        {
            return null;
        }

        return Serialize(fields, maxLength);
    }

    /// <summary>
    /// Geeft <paramref name="extra"/> terug met <paramref name="name"/> erin gezet.
    /// </summary>
    /// <remarks>
    /// Een bestaande sleutel met dezelfde naam wordt overschreven. Dat is de enige consistente
    /// uitkomst voor een gereserveerde naam: een tweede sleutel ernaast zou betekenen dat het
    /// portaal twee vormen moet kennen.
    /// </remarks>
    internal static JsonElement WithField(JsonElement? extra, string name, string value)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            switch (extra)
            {
                case { ValueKind: JsonValueKind.Object } existing:
                    foreach (JsonProperty property in existing.EnumerateObject())
                    {
                        if (!property.NameEquals(name))
                        {
                            property.WriteTo(writer);
                        }
                    }

                    break;

                // extra was geen object — dat komt hier niet voor, maar weggooien van context is
                // nooit het juiste antwoord.
                case { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } other:
                    writer.WritePropertyName("_payload");
                    other.WriteTo(writer);
                    break;
            }

            writer.WriteString(name, value);
            writer.WriteEndObject();
        }

        return JsonSerializer.Deserialize<JsonElement>(buffer.WrittenSpan);
    }

    private static void AppendPayload(Dictionary<string, object?> fields, object? payload)
    {
        if (payload is null)
        {
            return;
        }

        try
        {
            JsonElement element = JsonSerializer.SerializeToElement(payload, SerializerOptions);
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    fields[property.Name] = property.Value.Clone();
                }
            }
            else
            {
                fields["_payload"] = element.Clone();
            }
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            fields["_payload"] = payload.ToString();
        }
    }

    private static void AppendState(
        Dictionary<string, object?> fields,
        IEnumerable<KeyValuePair<string, object?>>? state)
    {
        if (state is null)
        {
            return;
        }

        foreach (KeyValuePair<string, object?> entry in state)
        {
            if (string.IsNullOrEmpty(entry.Key))
            {
                continue;
            }

            if (entry.Key == OriginalFormatKey)
            {
                fields["_template"] = entry.Value?.ToString();
                continue;
            }

            fields[entry.Key] = Flatten(entry.Value);
        }
    }

    private static void AppendScopes(Dictionary<string, object?> fields, IExternalScopeProvider? scopeProvider)
    {
        if (scopeProvider is null)
        {
            return;
        }

        var scopes = new List<string>();
        scopeProvider.ForEachScope(
            static (scope, list) =>
            {
                string? text = scope?.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    list.Add(text);
                }
            },
            scopes);

        if (scopes.Count > 0)
        {
            fields["_scopes"] = scopes;
        }
    }

    /// <summary>
    /// Zet een willekeurige state-waarde om naar iets dat zeker serialiseert. State-waarden
    /// komen uit vreemde code; een domeinobject met een lus erin mag geen logregel kosten.
    /// </summary>
    private static object? Flatten(object? value) => value switch
    {
        null => null,
        string or bool or byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal or DateTime or DateTimeOffset or TimeSpan or Guid => value,
        Enum => value.ToString(),
        _ => value.ToString(),
    };

    private static Dictionary<string, object?> Describe(Exception exception, int depth)
    {
        var description = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = exception.GetType().FullName,
            ["message"] = exception.Message,
            ["stackTrace"] = exception.StackTrace,
        };

        if (exception.InnerException is not null && depth < 3)
        {
            description["inner"] = Describe(exception.InnerException, depth + 1);
        }

        return description;
    }

    private static JsonElement? Serialize(Dictionary<string, object?> fields, int maxLength)
    {
        try
        {
            string json = JsonSerializer.Serialize(fields, SerializerOptions);
            if (json.Length <= maxLength)
            {
                return JsonSerializer.Deserialize<JsonElement>(json);
            }

            // Te groot voor één document. Liever een eerlijke melding dan een schrijfactie die
            // door Cosmos wordt afgewezen en de hele batch meesleept.
            var truncated = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["_truncated"] = true,
                ["_originalLength"] = json.Length,
                // Ook hier op een grafeemgrens: een losse surrogaat zou de serializer dwingen hem
                // door een vervangingsteken te vervangen, en dan staat er iets anders dan er stond.
                ["_preview"] = MessageTruncation.Shorten(json, Math.Max(64, maxLength - 200)),
            };

            return JsonSerializer.SerializeToElement(truncated, SerializerOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return JsonSerializer.SerializeToElement(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["_serializationError"] = exception.Message,
                },
                SerializerOptions);
        }
    }
}
