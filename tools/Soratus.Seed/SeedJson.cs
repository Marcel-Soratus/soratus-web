using System.Text.Json;
using Soratus.Agents.Contracts;

namespace Soratus.Seed;

/// <summary>
/// De serialisatievorm waarin dit gereedschap naar Cosmos schrijft.
/// </summary>
/// <remarks>
/// Hier stond een bewuste kopie van de converter uit de telemetriebibliotheek, met in de eigen
/// documentatie de toegift dat de assertie erop niet kon zien of de twee nog gelijk wáren — alleen
/// dat deze kopie niet verschoof. De reden voor die kopie was dat de bibliotheekversie
/// <c>internal</c> was en dat hoorde te blijven: de telemetriebibliotheek is voor agents, niet voor
/// gereedschap.
///
/// Die reden is weg. De normalisatie staat sinds de reparatie van de schrijfkant van het portaal in
/// <see cref="TimestampNormalization"/>, in het contractproject waar dit gereedschap al naar
/// verwijst — het schrijft immers de contracttypen zelf. Daarmee is de kopie niet langer nodig en is
/// "nog gelijk zijn" geen meting meer maar een eigenschap van de code: er is één implementatie.
/// </remarks>
internal static class SeedJson
{
    private static readonly Lazy<JsonSerializerOptions> LazyWriteOptions = new(() =>
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
        };

        TimestampNormalization.Register(options);
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
    /// De proef zelf staat in <see cref="TimestampNormalization.AssertCanonical"/> en wordt hier
    /// uitgeoefend op precies de opties waarmee dit gereedschap schrijft. Dat is het verschil met de
    /// vorige versie van deze methode: die toetste een eigen kopie tegen een letterlijke
    /// verwachting in deze file, en bleef dus groen als de bibliotheek naar een ander formaat
    /// verhuisde. Nu is er geen tweede formaat om van af te wijken.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Als een tijd niet als UTC wordt geschreven.</exception>
    internal static void AssertCanonicalUtc() => TimestampNormalization.AssertCanonical(SerializerOptions);
}
