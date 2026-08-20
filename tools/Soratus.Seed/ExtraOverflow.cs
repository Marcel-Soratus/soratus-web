using System.Text.Json;
using System.Text.Json.Nodes;
using Soratus.Agents.Contracts;

namespace Soratus.Seed;

/// <summary>
/// Zet de overloop van een geknipt logbericht in <see cref="LogRecord.Extra"/>.
/// </summary>
/// <remarks>
/// <para><strong>De knipregel zelf staat hier niet.</strong> Die staat in
/// <see cref="MessageTruncation"/> in <c>Soratus.Agents.Contracts</c> en wordt hier alleen
/// aangeroepen. Dit gereedschap had die regel korte tijd zelf staan — nagebouwd op de vorm die met
/// de telemetriebibliotheek was afgesproken, en op alle constanten en op de newline-regel gelijk —
/// en toch verschilde hij. Bij een dubbele knip plakte deze kopie twee helften met een <c>\n</c>
/// aan elkaar, terwijl het contract één aaneengesloten slice neemt; stond er in het origineel al
/// een regelovergang op die plek, dan kwam er dus één te veel. Drie plekken met dezelfde regel
/// blijven niet gelijk, ook niet als de schrijvers elkaars werk hadden gelezen. Vandaar één
/// definitie en hier alleen een aanroep.</para>
///
/// <para>Wat wél hier hoort, is dit: het contract geeft de overloop apart terug en zet hem
/// nergens neer, juist omdat de twee aanroepers er verschillende dingen mee moeten — het klantpad
/// in het portaal gooit hem weg, de schrijfkant bergt hem op. Deze klasse is de schrijfkant van
/// het seed-gereedschap en niets meer.</para>
/// </remarks>
internal static class ExtraOverflow
{
    /// <summary>Voegt de overloop toe aan de bestaande context.</summary>
    /// <param name="extra">De context uit <c>telemetry.json</c>, of <c>null</c>.</param>
    /// <param name="overflow">De overloop, zoals <see cref="MessageTruncation.Cut"/> hem teruggaf.</param>
    /// <param name="where">Waar we zijn, alleen voor de foutmelding.</param>
    /// <returns>De aangevulde context.</returns>
    /// <exception cref="SeedManifestException">Als <c>extra</c> bestaat maar geen object is.</exception>
    /// <remarks>
    /// Een bestaande sleutel met deze naam wordt overschreven, zoals het contract voorschrijft:
    /// <see cref="MessageTruncation.OverflowKey"/> is gereserveerd.
    /// </remarks>
    internal static JsonElement Merge(JsonElement? extra, string overflow, string where)
    {
        JsonObject target;

        if (extra is null || extra.Value.ValueKind == JsonValueKind.Null)
        {
            target = [];
        }
        else if (extra.Value.ValueKind == JsonValueKind.Object)
        {
            target = JsonNode.Parse(extra.Value.GetRawText())!.AsObject();
        }
        else
        {
            throw new SeedManifestException(
                $"{where}: extra moet een object zijn, want de overloop van msg wordt er als sleutel " +
                $"'{MessageTruncation.OverflowKey}' bij gezet. Gevonden: {extra.Value.ValueKind}.");
        }

        target[MessageTruncation.OverflowKey] = overflow;

        return JsonSerializer.SerializeToElement(target);
    }
}
