using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Beveiliging;

/// <summary>
/// Er is precies één implementatie van de telemetriestore.
/// </summary>
/// <remarks>
/// Geen seed-variant, geen in-memory variant, geen tweede DI-registratie. Seed-data wordt door een
/// apart consoleproject in dezelfde Cosmos gezet, in dezelfde documentvorm; het portaal weet niet
/// dat het seed is en hoort dat ook niet te kunnen weten.
///
/// Deze test is drie regels en maakt "even een mock erin" onmogelijk zonder dat iemand hem
/// expliciet ongedaan maakt. Dat is de bedoeling: een mocklaag die blijft hangen wordt vanzelf de
/// plek waar het verschil tussen demo en werkelijkheid gaat zitten, en dan toont het portaal iets
/// dat niet waar is.
/// </remarks>
public class StoreImplementatieTests
{
    [Fact]
    public void ErIsPreciesEenImplementatieVanDeTelemetriestore()
    {
        var implementaties = typeof(IAgentTelemetryStore).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(typeof(IAgentTelemetryStore).IsAssignableFrom)
            .Select(t => t.FullName!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            implementaties.Length == 1,
            $"Er zijn {implementaties.Length} implementaties van IAgentTelemetryStore in " +
            $"Soratus.Portal: {string.Join(", ", implementaties)}.\n\n" +
            "Er hoort er precies één te zijn. Een tweede implementatie — een mock, een " +
            "seed-store, een in-memory variant \"even voor de demo\" — wordt vanzelf de plek waar " +
            "het verschil tussen demo en werkelijkheid gaat zitten. Het portaal toont dan iets " +
            "dat niet uit de opslag komt, en dat is precies de onwaarheid die dit scherm niet " +
            "hoort te maken.\n\n" +
            "Testgegevens nodig? Zet ze met het seed-project in dezelfde Cosmos, in dezelfde " +
            "documentvorm. Dan kan het portaal het verschil niet zien, en dat is het punt.");

        Assert.Equal("Soratus.Portal.Data.CosmosAgentTelemetryStore", implementaties[0]);
    }

    [Fact]
    public void DeStoreVraagtOveralEenScopeEnNooitEenLosseKlantSlug()
    {
        // Geen enkele methode neemt een string customerId aan. Wie geen scope heeft kan hier
        // niets, en wie er wel een heeft, heeft hem met een oordeel erachter gekregen.
        var eerste = typeof(IAgentTelemetryStore)
            .GetMethods()
            .Select(m => (m.Name, Parameter: m.GetParameters().FirstOrDefault()))
            .ToArray();

        Assert.NotEmpty(eerste);

        foreach (var (naam, parameter) in eerste)
        {
            Assert.NotNull(parameter);
            Assert.True(
                parameter.ParameterType.Name.EndsWith("Scope", StringComparison.Ordinal),
                $"IAgentTelemetryStore.{naam} begint niet met een scope maar met " +
                $"{parameter.ParameterType.Name}. Elke methode hoort met een scope te beginnen: " +
                "dat is wat autorisatie hier tot een eigenschap van het typesysteem maakt in " +
                "plaats van tot een vergeten if.");
        }
    }
}
