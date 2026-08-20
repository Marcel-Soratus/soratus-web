using System.Reflection;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Vindt de broncode van <c>Soratus.Portal</c> vanaf de testassembly.
/// </summary>
/// <remarks>
/// Het pad wordt relatief aan de testassembly bepaald en niet hard ingetypt, zodat de tests ook op
/// de build-agent draaien — daar staat de repository ergens anders dan op een werkplek. De
/// zoektocht loopt van de map van de assembly omhoog tot de map met <c>Soratus.slnx</c>.
/// </remarks>
internal static class Broncode
{
    /// <summary>De map van het project <c>Soratus.Portal</c>.</summary>
    public static DirectoryInfo Portaalproject { get; } = ZoekPortaalproject();

    /// <summary>
    /// Alle <c>.cs</c>- en <c>.razor</c>-bestanden van het portaalproject, zonder <c>bin</c> en
    /// <c>obj</c>.
    /// </summary>
    /// <returns>De bestanden.</returns>
    public static IEnumerable<FileInfo> Portaalbestanden() =>
        Portaalproject
            .EnumerateFiles("*.*", SearchOption.AllDirectories)
            .Where(f => f.Extension is ".cs" or ".razor")
            .Where(f => !IsGegenereerd(f))
            .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Het pad van een bestand ten opzichte van het projectpad, met forward slashes.</summary>
    /// <param name="bestand">Het bestand.</param>
    /// <returns>Bijvoorbeeld <c>Security/CustomerScopeResolver.cs</c>.</returns>
    public static string RelatiefPad(FileInfo bestand) =>
        Path.GetRelativePath(Portaalproject.FullName, bestand.FullName).Replace('\\', '/');

    private static bool IsGegenereerd(FileInfo bestand)
    {
        var pad = bestand.FullName.Replace('\\', '/');

        return pad.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || pad.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static DirectoryInfo ZoekPortaalproject()
    {
        var start = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? Directory.GetCurrentDirectory());

        for (var map = start; map is not null; map = map.Parent)
        {
            var kandidaat = Path.Combine(map.FullName, "Soratus.Portal", "Soratus.Portal.csproj");
            if (File.Exists(kandidaat))
            {
                return new DirectoryInfo(Path.Combine(map.FullName, "Soratus.Portal"));
            }
        }

        throw new DirectoryNotFoundException(
            $"Het project Soratus.Portal is niet gevonden vanaf '{start.FullName}'. De broncodetests " +
            "zoeken vanaf de testassembly omhoog naar de map met Soratus.Portal/Soratus.Portal.csproj; " +
            "verhuist de mappenstructuur, dan hoort deze zoektocht mee te verhuizen.");
    }
}
