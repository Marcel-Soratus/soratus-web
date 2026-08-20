using System.Text.Json;
using System.Text.Json.Serialization;
using Soratus.Agents.Contracts;

namespace Soratus.Seed;

/// <summary>
/// De vorm van <c>tools/seed/telemetry.json</c>.
/// </summary>
/// <remarks>
/// Dit is bewust <em>niet</em> hetzelfde als de contracttypen. Het bestand is voor een mens: hij
/// groepeert per klant, hij zet logregels bij hun agent, en hij noteert tijden relatief. De
/// omzetting naar <see cref="AgentRegistration"/>, <see cref="RunRecord"/> en
/// <see cref="LogRecord"/> gebeurt in <see cref="SeedPlanner"/>, zodat wat er uiteindelijk in
/// Cosmos belandt altijd een contracttype is en nooit met de hand gebouwde JSON.
/// </remarks>
internal sealed record SeedManifest
{
    /// <summary>De klanten, in de volgorde waarin ze in het bestand staan.</summary>
    [JsonPropertyName("customers")]
    public IReadOnlyList<SeedCustomer> Customers { get; init; } = [];
}

/// <summary>Eén klant met zijn agents. Een klant zonder agents is toegestaan en betekenisvol.</summary>
internal sealed record SeedCustomer
{
    /// <summary>De slug, gelijk aan <c>customerId</c> in de telemetriedocumenten.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// De naam op het scherm. Wordt niet naar Cosmos geschreven — het portaal haalt klantnamen
    /// uit configuratie — maar staat hier zodat het bestand leesbaar is en als bron kan dienen
    /// voor de sectie <c>Customers</c> van het portaal.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Of dit de interne klant is. Alleen toelichting.</summary>
    [JsonPropertyName("internal")]
    public bool Internal { get; init; }

    /// <summary>Korte omgevingsaanduiding. Alleen toelichting.</summary>
    [JsonPropertyName("environment")]
    public string? Environment { get; init; }

    /// <summary>Volledige omgevingsaanduiding. Alleen toelichting.</summary>
    [JsonPropertyName("environmentDetail")]
    public string? EnvironmentDetail { get; init; }

    /// <summary>De agents van deze klant.</summary>
    [JsonPropertyName("agents")]
    public IReadOnlyList<SeedAgent> Agents { get; init; } = [];
}

/// <summary>Eén agent met zijn runs en logregels.</summary>
internal sealed record SeedAgent
{
    /// <summary>
    /// De technische naam. Dit is tegelijk de documentsleutel én de partitiesleutel in de
    /// container <c>agents</c>, en daarmee accountbreed uniek.
    /// </summary>
    [JsonPropertyName("agentName")]
    public string AgentName { get; init; } = string.Empty;

    /// <summary>De typeaanduiding voor de typekolom.</summary>
    [JsonPropertyName("displayType")]
    public string DisplayType { get; init; } = string.Empty;

    /// <summary>De versie.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>Productie, acceptatie of ontwikkeling. Standaard productie.</summary>
    [JsonPropertyName("environment")]
    public AgentEnvironment Environment { get; init; } = AgentEnvironment.Production;

    /// <summary>Wat de agent over zijn eigen levenscyclus meldt.</summary>
    [JsonPropertyName("lifecycle")]
    public AgentLifecycle Lifecycle { get; init; } = AgentLifecycle.Running;

    /// <summary>Wanneer het proces startte, relatief.</summary>
    [JsonPropertyName("startedAt")]
    public string StartedAt { get; init; } = string.Empty;

    /// <summary>De laatste hartslag, relatief. Ouder dan twee minuten betekent degraded.</summary>
    [JsonPropertyName("lastHeartbeatAt")]
    public string LastHeartbeatAt { get; init; } = string.Empty;

    /// <summary>De cron-expressie, of <c>null</c> bij een agent die alleen op een trigger draait.</summary>
    [JsonPropertyName("schedule")]
    public string? Schedule { get; init; }

    /// <summary>Waardoor deze agent aan het werk gaat.</summary>
    [JsonPropertyName("triggerKind")]
    public TriggerKind TriggerKind { get; init; }

    /// <summary>Toelichting op de trigger.</summary>
    [JsonPropertyName("triggerDetail")]
    public string? TriggerDetail { get; init; }

    /// <summary>De eerstvolgende geplande run, relatief, of <c>null</c>.</summary>
    [JsonPropertyName("nextRunAt")]
    public string? NextRunAt { get; init; }

    /// <summary>De runs, jongste eerst of oudste eerst — de volgorde doet er niet toe.</summary>
    [JsonPropertyName("runs")]
    public IReadOnlyList<SeedRun> Runs { get; init; } = [];

    /// <summary>De logregels.</summary>
    [JsonPropertyName("logs")]
    public IReadOnlyList<SeedLog> Logs { get; init; } = [];
}

/// <summary>Eén run.</summary>
internal sealed record SeedRun
{
    /// <summary>De runId, tevens documentsleutel.</summary>
    [JsonPropertyName("runId")]
    public string RunId { get; init; } = string.Empty;

    /// <summary>Wanneer de run begon, relatief.</summary>
    [JsonPropertyName("startedAt")]
    public string StartedAt { get; init; } = string.Empty;

    /// <summary>
    /// De duur. Samen met <see cref="StartedAt"/> bepaalt dit het eindmoment; een lopende run
    /// laat dit veld weg.
    /// </summary>
    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; init; }

    /// <summary>De afloop.</summary>
    [JsonPropertyName("result")]
    public RunResult Result { get; init; }

    /// <summary>Hoeveel items zijn verwerkt.</summary>
    [JsonPropertyName("itemsProcessed")]
    public int ItemsProcessed { get; init; }

    /// <summary>Hoeveel items zijn afgekeurd.</summary>
    [JsonPropertyName("itemsFailed")]
    public int ItemsFailed { get; init; }

    /// <summary>Of de transactie is teruggedraaid.</summary>
    [JsonPropertyName("rolledBack")]
    public bool RolledBack { get; init; }

    /// <summary>Waardoor deze run startte.</summary>
    [JsonPropertyName("trigger")]
    public TriggerKind Trigger { get; init; }

    /// <summary>Het type van de uitzondering bij een mislukte run.</summary>
    [JsonPropertyName("errorType")]
    public string? ErrorType { get; init; }

    /// <summary>De foutmelding bij een mislukte run.</summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }
}

/// <summary>Eén logregel.</summary>
internal sealed record SeedLog
{
    /// <summary>Wanneer de regel is geschreven, relatief.</summary>
    [JsonPropertyName("at")]
    public string At { get; init; } = string.Empty;

    /// <summary>Info, warn of error.</summary>
    [JsonPropertyName("level")]
    public LogLevel Level { get; init; }

    /// <summary>De puntgescheiden gebeurtenisnaam.</summary>
    [JsonPropertyName("event")]
    public string Event { get; init; } = string.Empty;

    /// <summary>De regel zelf, in het Nederlands.</summary>
    [JsonPropertyName("msg")]
    public string Message { get; init; } = string.Empty;

    /// <summary>De run waarbinnen de regel viel, of <c>null</c> voor regels buiten een run.</summary>
    [JsonPropertyName("runId")]
    public string? RunId { get; init; }

    /// <summary>Vrije context; hier hoort een stacktrace.</summary>
    [JsonPropertyName("extra")]
    public JsonElement? Extra { get; init; }
}

/// <summary>Wordt geworpen als <c>telemetry.json</c> niet klopt.</summary>
internal sealed class SeedManifestException(string message) : Exception(message);
