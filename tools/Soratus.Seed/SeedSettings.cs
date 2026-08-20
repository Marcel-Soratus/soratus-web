using System.Text.Json;
using Soratus.Agents.Contracts;

namespace Soratus.Seed;

/// <summary>
/// Waar dit gereedschap naartoe schrijft en wat het moet doen.
/// </summary>
/// <remarks>
/// Er staat geen sleutel en geen connection string in, en die kan er ook niet in: op
/// <c>cosmos-soratus-prod</c> is local auth uitgeschakeld. De verbinding loopt via het endpoint en
/// <c>DefaultAzureCredential</c>.
///
/// Volgorde van winnen: argument boven omgevingsvariabele boven <c>appsettings.json</c> boven de
/// ingebouwde standaardwaarde.
/// </remarks>
internal sealed record SeedSettings
{
    /// <summary>De Cosmos-endpoint.</summary>
    public string Endpoint { get; init; } = "https://cosmos-soratus-prod.documents.azure.com:443/";

    /// <summary>De database.</summary>
    public string Database { get; init; } = "telemetry";

    /// <summary>De container met één registratie per agent.</summary>
    public string AgentsContainer { get; init; } = "agents";

    /// <summary>De container met de runs.</summary>
    public string RunsContainer { get; init; } = "runs";

    /// <summary>De container met de logregels.</summary>
    public string LogsContainer { get; init; } = "logs";

    /// <summary>Het pad naar <c>telemetry.json</c>.</summary>
    public string ManifestPath { get; init; } = string.Empty;

    /// <summary>Tonen wat er zou gebeuren, zonder iets te schrijven of te verwijderen.</summary>
    public bool DryRun { get; init; }

    /// <summary>De geseede documenten opruimen in plaats van schrijven.</summary>
    public bool Clean { get; init; }

    /// <summary>Alleen tellen wat er staat.</summary>
    public bool VerifyOnly { get; init; }

    /// <summary>
    /// Na het seeden in een lus blijven draaien en alleen de hartslag van de registraties
    /// verversen, zodat de demodata niet binnen twee minuten op <c>degraded</c> valt.
    /// </summary>
    public bool KeepFresh { get; init; }

    /// <summary>Hoe vaak <see cref="KeepFresh"/> een ronde doet.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>De hulptekst tonen en verder niets doen.</summary>
    public bool Help { get; init; }
}

/// <summary>Leest de instellingen uit standaardwaarden, bestand, omgeving en argumenten.</summary>
internal static class SeedSettingsReader
{
    private const string EnvironmentPrefix = "SORATUS_SEED_";

    /// <summary>Bouwt de instellingen op.</summary>
    /// <param name="args">De argumenten van de opdrachtregel.</param>
    /// <returns>De samengestelde instellingen.</returns>
    /// <exception cref="SeedException">Bij een onbekend of onvolledig argument.</exception>
    internal static SeedSettings Read(string[] args)
    {
        var settings = FromFile(new SeedSettings());
        settings = FromEnvironment(settings);
        settings = FromArguments(settings, args);

        if (string.IsNullOrWhiteSpace(settings.ManifestPath))
        {
            settings = settings with { ManifestPath = LocateManifest() };
        }

        return settings with { ManifestPath = Path.GetFullPath(settings.ManifestPath) };
    }

    private static SeedSettings FromFile(SeedSettings settings)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        if (!File.Exists(path))
        {
            return settings;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty("Seed", out var seed))
        {
            return settings;
        }

        return settings with
        {
            Endpoint = Text(seed, "Endpoint") ?? settings.Endpoint,
            Database = Text(seed, "Database") ?? settings.Database,
            AgentsContainer = Text(seed, "AgentsContainer") ?? settings.AgentsContainer,
            RunsContainer = Text(seed, "RunsContainer") ?? settings.RunsContainer,
            LogsContainer = Text(seed, "LogsContainer") ?? settings.LogsContainer,
            ManifestPath = Text(seed, "ManifestPath") ?? settings.ManifestPath,
        };

        static string? Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static SeedSettings FromEnvironment(SeedSettings settings) =>
        settings with
        {
            Endpoint = Variable("ENDPOINT") ?? settings.Endpoint,
            Database = Variable("DATABASE") ?? settings.Database,
            ManifestPath = Variable("FILE") ?? settings.ManifestPath,
        };

    private static string? Variable(string name)
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentPrefix + name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static SeedSettings FromArguments(SeedSettings settings, string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            switch (argument)
            {
                case "--dry-run":
                    settings = settings with { DryRun = true };
                    break;
                case "--clean":
                    settings = settings with { Clean = true };
                    break;
                case "--verify":
                    settings = settings with { VerifyOnly = true };
                    break;
                case "--keep-fresh":
                    settings = settings with { KeepFresh = true };
                    break;
                case "--interval":
                    settings = settings with { Interval = Interval(Value(args, ref index)) };
                    break;
                case "--help" or "-h" or "-?":
                    settings = settings with { Help = true };
                    break;
                case "--endpoint":
                    settings = settings with { Endpoint = Value(args, ref index) };
                    break;
                case "--database":
                    settings = settings with { Database = Value(args, ref index) };
                    break;
                case "--file":
                    settings = settings with { ManifestPath = Value(args, ref index) };
                    break;
                default:
                    throw new SeedException(
                        $"Onbekend argument '{argument}'. Draai met --help voor de mogelijkheden.");
            }
        }

        return settings;
    }

    /// <summary>Leest het interval van <c>--keep-fresh</c> in seconden.</summary>
    /// <remarks>
    /// Boven de degraded-drempel heeft vershouden geen zin: dan valt de agent tussen twee rondes
    /// door alsnog om. Dat weigeren we in plaats van het stil te laten mislukken.
    /// </remarks>
    private static TimeSpan Interval(string text)
    {
        if (!int.TryParse(text, out var seconds) || seconds <= 0)
        {
            throw new SeedException($"--interval verwacht een aantal seconden, niet '{text}'.");
        }

        var interval = TimeSpan.FromSeconds(seconds);

        if (interval >= AgentStatusThresholds.Degraded)
        {
            throw new SeedException(
                $"--interval van {seconds} s ligt op of boven de degraded-drempel van " +
                $"{AgentStatusThresholds.Degraded.TotalSeconds:0} s. Dan valt elke agent tussen twee rondes " +
                "door alsnog om. Kies iets ruim daaronder; 30 s is de standaard.");
        }

        return interval;
    }

    private static string Value(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new SeedException($"Argument '{args[index]}' heeft een waarde nodig.");
        }

        return args[++index];
    }

    /// <summary>
    /// Zoekt <c>tools/seed/telemetry.json</c> door vanaf de uitvoermap omhoog te lopen.
    /// </summary>
    /// <remarks>
    /// Zo werkt <c>dotnet run</c> vanuit elke map in de repository zonder <c>--file</c>. Wordt het
    /// bestand niet gevonden, dan valt hij terug op het pad ten opzichte van de huidige map en
    /// meldt de foutafhandeling straks netjes wat er ontbreekt.
    /// </remarks>
    private static string LocateManifest()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "seed", "telemetry.json");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.Combine("tools", "seed", "telemetry.json");
    }
}
