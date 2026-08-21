using System.Reflection;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.HostedAgents;
using Soratus.Agents.Telemetry.Internal;
using Soratus.Agents.Telemetry.Logging;
using Soratus.Agents.Telemetry.Scheduling;

namespace Soratus.Agents.Telemetry;

/// <summary>
/// Eén aanroep om aan het Soratus-agentcontract te voldoen.
/// </summary>
/// <remarks>
/// Dit is het hele punt van de bibliotheek. Wie drie velden met de hand moet invullen, vergeet
/// er ooit één, en dan liegt het scherm. Daarom leidt deze methode alles af wat af te leiden is
/// en werpt hij meteen bij wat ontbreekt: misconfiguratie is een programmeerfout, geen
/// bedrijfsstoring, en hoort bij het opstarten zichtbaar te worden en niet pas als een operator
/// zich afvraagt waarom een agent nooit in het overzicht kwam.
/// </remarks>
public static class SoratusAgentBuilderExtensions
{
    private const string AgentNameKey = "SORATUS_AGENT__NAME";
    private const string AgentDisplayTypeKey = "SORATUS_AGENT__DISPLAY_TYPE";
    private const string AgentScheduleKey = "SORATUS_AGENT__SCHEDULE";
    private const string AgentTimeZoneKey = "SORATUS_AGENT__TIMEZONE";
    private const string AgentTriggerKey = "SORATUS_AGENT__TRIGGER";
    private const string AgentTriggerDetailKey = "SORATUS_AGENT__TRIGGER_DETAIL";
    private const string AgentEnvironmentKey = "SORATUS_AGENT__ENVIRONMENT";
    private const string CustomerIdKey = "SORATUS_CUSTOMER__ID";
    private const string EndpointKey = "SORATUS_TELEMETRY__ENDPOINT";
    private const string DatabaseKey = "SORATUS_TELEMETRY__DATABASE";
    private const string AgentsContainerKey = "SORATUS_TELEMETRY__AGENTS_CONTAINER";
    private const string RunsContainerKey = "SORATUS_TELEMETRY__RUNS_CONTAINER";
    private const string LogsContainerKey = "SORATUS_TELEMETRY__LOGS_CONTAINER";

    /// <summary>
    /// Sluit deze host aan op het Soratus-agentcontract: registratie, hartslag, runs en logs.
    /// </summary>
    /// <param name="builder">De hostbouwer.</param>
    /// <param name="configure">Optionele bijstelling van de knoppen, ná de configuratie.</param>
    /// <returns>Dezelfde bouwer, zodat je door kunt ketenen.</returns>
    /// <exception cref="InvalidOperationException">
    /// Als verplichte configuratie ontbreekt of niet klopt.
    /// </exception>
    public static IHostApplicationBuilder AddSoratusAgent(
        this IHostApplicationBuilder builder,
        Action<SoratusTelemetryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (IsRegistered<HostedAgentRegistry>(builder.Services))
        {
            throw new InvalidOperationException(
                $"{nameof(AddSoratusHostedAgents)} is al aangeroepen op deze host. De twee vormen " +
                "sluiten elkaar uit: de eerste publiceert één agent met een levensfase die de agent " +
                "zelf meldt, de tweede publiceert er meerdere met een levensfase die uit de lopende " +
                "aanroepen volgt. Samen zouden ze twee hartslagen met verschillende betekenis over " +
                "hetzelfde proces schrijven.");
        }

        if (builder.Services.Any(static descriptor => descriptor.ServiceType == typeof(AgentIdentity)))
        {
            return builder;
        }

        // Vóór alles: de twee eigenschappen bewijzen die stil fout kunnen gaan. Tijden moeten als
        // UTC met vaste precisie de deur uit, anders sorteert het portaal verkeerd; en msg moet op
        // de eerste regelovergang geknipt worden, anders leest een klant onze stacktraces.
        TelemetryJson.AssertCanonicalUtc();
        MessageTruncation.AssertContract();

        AgentIdentity identity = ResolveIdentity(builder.Configuration, builder.Environment);
        SoratusTelemetryOptions options = ResolveOptions(builder.Configuration, configure);

        builder.Services.AddSingleton(identity);
        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddSingleton(new AgentSchedule(identity.Schedule, identity.ScheduleTimeZone));
        builder.Services.AddSingleton<AgentLifecycleState>();
        builder.Services.AddSingleton<LogRecordFactory>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<TokenCredential>(static _ => new DefaultAzureCredential());
        builder.Services.AddSingleton<ITelemetrySink, CosmosTelemetrySink>();
        builder.Services.AddSingleton<TelemetryWriter>();
        builder.Services.AddSingleton<ISoratusAgent, SoratusAgent>();

        // Volgorde telt. Diensten stoppen in omgekeerde volgorde van registratie, dus de
        // schrijver staat vooraan zodat hij als laatste stopt en het afsluitende
        // registratiedocument nog kan wegschrijven.
        builder.Services.AddSingleton<IHostedService>(static sp => sp.GetRequiredService<TelemetryWriter>());
        builder.Services.AddSingleton<AgentRegistrationService>();
        builder.Services.AddSingleton<IHostedService>(static sp => sp.GetRequiredService<AgentRegistrationService>());

        builder.Services.AddSingleton<ILoggerProvider, SoratusLoggerProvider>();

        // De host mag zijn eigen minimumniveau kiezen; voor deze provider ligt de ondergrens
        // vast op Information, want daaronder begint het contract niet.
        builder.Logging.AddFilter<SoratusLoggerProvider>(null, Microsoft.Extensions.Logging.LogLevel.Information);

        return builder;
    }

    /// <summary>
    /// Als <see cref="AddSoratusAgent"/>, en laat daarnaast <typeparamref name="TAgent"/> draaien
    /// op de cron-expressie uit <c>SORATUS_AGENT__SCHEDULE</c>.
    /// </summary>
    /// <typeparam name="TAgent">De agent die per run wordt aangeroepen.</typeparam>
    /// <param name="builder">De hostbouwer.</param>
    /// <param name="configure">Optionele bijstelling van de knoppen.</param>
    /// <returns>Dezelfde bouwer.</returns>
    /// <remarks>
    /// <typeparamref name="TAgent"/> wordt scoped geregistreerd en per run uit een verse scope
    /// gehaald, zodat scoped afhankelijkheden gewoon werken.
    /// </remarks>
    public static IHostApplicationBuilder AddSoratusAgent<TAgent>(
        this IHostApplicationBuilder builder,
        Action<SoratusTelemetryOptions>? configure = null)
        where TAgent : class, IScheduledAgent
    {
        AddSoratusAgent(builder, configure);

        builder.Services.AddScoped<TAgent>();
        builder.Services.AddScoped<IScheduledAgent>(static sp => sp.GetRequiredService<TAgent>());
        builder.Services.AddSingleton<IHostedService, ScheduledAgentService>();

        return builder;
    }

    /// <summary>
    /// Sluit een host aan die <em>meerdere</em> agents herbergt die geen eigen proces en geen eigen
    /// lus hebben: diensten die draaien wanneer ze worden aangeroepen.
    /// </summary>
    /// <param name="builder">De hostbouwer.</param>
    /// <param name="configure">Optionele bijstelling van de knoppen, ná de configuratie.</param>
    /// <returns>Dezelfde bouwer, zodat je door kunt ketenen.</returns>
    /// <exception cref="InvalidOperationException">
    /// Als verplichte configuratie ontbreekt, als er een schema is gezet, of als
    /// <see cref="AddSoratusAgent(IHostApplicationBuilder, Action{SoratusTelemetryOptions})"/> al is
    /// aangeroepen.
    /// </exception>
    /// <remarks>
    /// <para><strong>Wat er anders is dan bij de gewone vorm.</strong> Er is geen enkelvoudige agent
    /// en dus geen <see cref="ISoratusAgent"/> in de container; er is
    /// <see cref="ISoratusHostedAgents"/>, en daaruit haal je per aanroep de agent op wiens naam het
    /// werk gebeurt. De levensfase meldt de bouwer niet: die volgt uit het aantal lopende aanroepen,
    /// want de bibliotheek opent en sluit ze zelf en weet het daarmee beter.</para>
    ///
    /// <para><strong>Welke agents het zijn, wordt niet hier opgegeven.</strong> Dat komt uit de
    /// geregistreerde <see cref="IHostedAgentSource"/>-bronnen — in een webapplicatie is dat de
    /// metadata op de endpoints zelf. Eén lijst op één plek, en die plek is de plek waar het werk
    /// staat: dan kan er geen tweede lijst zijn die ermee uit de pas loopt.</para>
    ///
    /// <para><strong>Waarom een schema hier een fout is en niet een instelling.</strong> Een
    /// geherbergde agent draait op een aanroep. Zou <c>SORATUS_AGENT__SCHEDULE</c> hier iets mogen
    /// zeggen, dan staat er een cron-expressie in de configuratie die niets plant, en zulke
    /// expressies worden geloofd.</para>
    /// </remarks>
    public static IHostApplicationBuilder AddSoratusHostedAgents(
        this IHostApplicationBuilder builder,
        Action<SoratusTelemetryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (IsRegistered<HostedAgentRegistry>(builder.Services))
        {
            return builder;
        }

        if (IsRegistered<AgentLifecycleState>(builder.Services))
        {
            throw new InvalidOperationException(
                $"{nameof(AddSoratusAgent)} is al aangeroepen op deze host. De twee vormen sluiten " +
                "elkaar uit: de eerste publiceert één agent met een levensfase die de agent zelf " +
                "meldt, de tweede publiceert er meerdere met een levensfase die uit de lopende " +
                "aanroepen volgt. Samen zouden ze twee hartslagen met verschillende betekenis over " +
                "hetzelfde proces schrijven.");
        }

        // Dezelfde twee asserties als bij de gewone vorm, en om dezelfde reden: dit zijn de twee
        // eigenschappen die stil fout gaan.
        TelemetryJson.AssertCanonicalUtc();
        MessageTruncation.AssertContract();

        if (Read(builder.Configuration, AgentScheduleKey) is { } schedule)
        {
            throw new InvalidOperationException(
                $"{AgentScheduleKey} staat op '{schedule}', maar deze host herbergt agents die op een " +
                "aanroep draaien en niet op een schema. Er is niets dat deze expressie uitvoert, dus " +
                "hij zou als feit op het scherm staan zonder er een te zijn. Haal hem weg.");
        }

        // De identiteit van de host zelf. Hij wordt niet als registratiedocument gepubliceerd — er is
        // geen vierde rij "de webhost" — maar hij levert de klant, de versie, de omgeving en het
        // moment waarop dit proces startte aan elke geherbergde agent.
        AgentIdentity host = ResolveIdentity(builder.Configuration, builder.Environment);
        SoratusTelemetryOptions options = ResolveOptions(builder.Configuration, configure);

        builder.Services.AddSingleton(host);
        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<TokenCredential>(static _ => new DefaultAzureCredential());
        builder.Services.AddSingleton<ITelemetrySink, CosmosTelemetrySink>();
        builder.Services.AddSingleton<TelemetryWriter>();
        builder.Services.AddSingleton<HostedAgentRegistry>();
        builder.Services.AddSingleton<ISoratusHostedAgents>(
            static sp => sp.GetRequiredService<HostedAgentRegistry>());

        // Volgorde telt, net als bij de gewone vorm: diensten stoppen in omgekeerde volgorde van
        // registratie, dus de schrijver staat vooraan zodat hij als laatste stopt en de afsluitende
        // registraties nog kan wegschrijven.
        builder.Services.AddSingleton<IHostedService>(static sp => sp.GetRequiredService<TelemetryWriter>());
        builder.Services.AddSingleton<HostedAgentsRegistrationService>();
        builder.Services.AddSingleton<IHostedService>(
            static sp => sp.GetRequiredService<HostedAgentsRegistrationService>());

        // Geen LogRecordFactory in de container: er is geen enkelvoudige agentnaam om hem op te
        // bouwen. De provider ziet dat en haalt de fabriek van de lopende aanroep. Zie
        // SoratusLoggerProvider.ResolveFactory.
        builder.Services.AddSingleton<ILoggerProvider, SoratusLoggerProvider>();
        builder.Logging.AddFilter<SoratusLoggerProvider>(null, Microsoft.Extensions.Logging.LogLevel.Information);

        return builder;
    }

    /// <summary>
    /// Kondigt één geherbergde agent aan die niet uit een andere bron volgt.
    /// </summary>
    /// <param name="services">De container.</param>
    /// <param name="declaration">De aankondiging.</param>
    /// <returns>Dezelfde container.</returns>
    /// <exception cref="ArgumentNullException">Als een van beide <c>null</c> is.</exception>
    /// <exception cref="InvalidOperationException">
    /// Als de aankondiging niet door <see cref="HostedAgentDeclaration.Validate"/> komt.
    /// </exception>
    /// <remarks>
    /// Voor een host waar de agents niet uit iets bestaands af te lezen zijn. Waar dat wél kan — de
    /// endpoints van een webapplicatie — is een <see cref="IHostedAgentSource"/> beter: dan bestaat
    /// er geen tweede lijst die met de eerste uit de pas kan lopen.
    /// </remarks>
    public static IServiceCollection AddSoratusHostedAgent(
        this IServiceCollection services,
        HostedAgentDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(declaration);
        declaration.Validate();

        services.AddSingleton<IHostedAgentSource>(new StaticHostedAgentSource(declaration));
        return services;
    }

    private static bool IsRegistered<T>(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ServiceType == typeof(T));

    private static AgentIdentity ResolveIdentity(IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        var missing = new List<string>();

        Assembly? entry = Assembly.GetEntryAssembly();

        string agentName = Read(configuration, AgentNameKey)
            ?? entry?.GetName().Name?.ToLowerInvariant()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(agentName))
        {
            missing.Add($"{AgentNameKey} (en er is geen assemblynaam om op terug te vallen)");
        }

        string? customerId = Read(configuration, CustomerIdKey);
        if (string.IsNullOrWhiteSpace(customerId))
        {
            missing.Add(CustomerIdKey);
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Soratus-agent kan niet opstarten. Ontbrekende configuratie: " + string.Join(", ", missing) + ".");
        }

        string? schedule = Read(configuration, AgentScheduleKey);
        TimeZoneInfo timeZone = ResolveTimeZone(Read(configuration, AgentTimeZoneKey));

        // Meteen parseren, zodat een typefout in de cron-expressie hier omvalt en niet pas over
        // een uur, wanneer blijkt dat de agent nooit is gaan draaien.
        _ = new AgentSchedule(schedule, timeZone);

        return new AgentIdentity
        {
            CustomerId = customerId!.Trim(),
            AgentName = agentName.Trim(),
            DisplayType = Read(configuration, AgentDisplayTypeKey) ?? Humanise(agentName),
            Version = ResolveVersion(entry),
            Environment = ParseEnum(
                Read(configuration, AgentEnvironmentKey),
                AgentEnvironmentKey,
                DefaultEnvironment(hostEnvironment, configuration)),
            TriggerKind = ParseEnum(
                Read(configuration, AgentTriggerKey),
                AgentTriggerKey,
                string.IsNullOrWhiteSpace(schedule) ? TriggerKind.Manual : TriggerKind.Timer),
            TriggerDetail = Read(configuration, AgentTriggerDetailKey),
            Schedule = string.IsNullOrWhiteSpace(schedule) ? null : schedule.Trim(),
            ScheduleTimeZone = timeZone,
            StartedAt = DateTimeOffset.UtcNow,
        };
    }

    private static SoratusTelemetryOptions ResolveOptions(
        IConfiguration configuration,
        Action<SoratusTelemetryOptions>? configure)
    {
        var options = new SoratusTelemetryOptions
        {
            Endpoint = Read(configuration, EndpointKey) ?? string.Empty,
        };

        if (Read(configuration, DatabaseKey) is { } database)
        {
            options.Database = database;
        }

        if (Read(configuration, AgentsContainerKey) is { } agents)
        {
            options.AgentsContainer = agents;
        }

        if (Read(configuration, RunsContainerKey) is { } runs)
        {
            options.RunsContainer = runs;
        }

        if (Read(configuration, LogsContainerKey) is { } logs)
        {
            options.LogsContainer = logs;
        }

        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new InvalidOperationException(
                $"Soratus-agent kan niet opstarten. Ontbrekende configuratie: {EndpointKey}.");
        }

        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out Uri? endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps && !endpoint.IsLoopback))
        {
            throw new InvalidOperationException(
                $"{EndpointKey} moet een absolute https-URL zijn, maar is '{options.Endpoint}'.");
        }

        if (options.MaxMessageLength < MessageTruncation.MinimumLength)
        {
            throw new InvalidOperationException(
                $"MaxMessageLength staat op {options.MaxMessageLength}, en onder " +
                $"{MessageTruncation.MinimumLength} past de markering '{MessageTruncation.Marker}' zelf niet meer. " +
                "Let op dat deze grens alleen hygiëne is; de knip op de eerste regelovergang doet het werk.");
        }

        if (options.Endpoint.Contains("AccountKey", StringComparison.OrdinalIgnoreCase))
        {
            // Een connection string met sleutel hoort hier niet. Meteen omvallen is vriendelijker
            // dan een geheim dat ongemerkt in een omgevingsvariabele blijft staan.
            throw new InvalidOperationException(
                $"{EndpointKey} lijkt een connection string met een sleutel te bevatten. " +
                "Gebruik alleen de endpoint-URL; de verbinding loopt via managed identity.");
        }

        return options;
    }

    /// <summary>
    /// Leest een sleutel in beide vormen: als sectie (<c>SORATUS_AGENT:NAME</c>, waar
    /// omgevingsvariabelen met dubbel liggend streepje op uitkomen) en als platte sleutel, zodat
    /// een <c>appsettings.json</c> met de letterlijke naam ook werkt.
    /// </summary>
    private static string? Read(IConfiguration configuration, string key)
    {
        string sectioned = key.Replace("__", ":", StringComparison.Ordinal);
        string? value = configuration[sectioned] ?? configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static TEnum ParseEnum<TEnum>(string? value, string key, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (value is null)
        {
            return fallback;
        }

        if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"{key} heeft de waarde '{value}', maar dat is geen geldige {typeof(TEnum).Name}. " +
            $"Geldig zijn: {string.Join(", ", Enum.GetNames<TEnum>())}.");
    }

    /// <summary>
    /// Omgevingsvariabelen die alleen bestaan als dit proces in Azure draait.
    /// </summary>
    private static readonly string[] AzureHostMarkers =
    [
        "CONTAINER_APP_NAME",
        "CONTAINER_APP_REVISION",
        "WEBSITE_SITE_NAME",
    ];

    /// <summary>
    /// Bepaalt de omgeving als <c>SORATUS_AGENT__ENVIRONMENT</c> niet is gezet.
    /// </summary>
    /// <remarks>
    /// Terugvallen op <c>dev</c> is lokaal verstandig en in Azure gevaarlijk: de klantweergave
    /// toont uitsluitend <c>prod</c>, dus een productie-agent die stilletjes op <c>dev</c> blijft
    /// staan verdwijnt uit beeld zonder dat iemand een foutmelding ziet. Dat is precies het
    /// tegenovergestelde van wat dit contract moet doen, dus in Azure is het een inrichtingsfout
    /// en geen standaardwaarde.
    /// </remarks>
    private static AgentEnvironment DefaultEnvironment(IHostEnvironment hostEnvironment, IConfiguration configuration)
    {
        if (hostEnvironment.IsProduction())
        {
            return AgentEnvironment.Production;
        }

        if (hostEnvironment.IsStaging())
        {
            return AgentEnvironment.Acceptance;
        }

        string? marker = AzureHostMarkers.FirstOrDefault(
            key => !string.IsNullOrWhiteSpace(configuration[key]));

        if (marker is not null)
        {
            throw new InvalidOperationException(
                $"Deze agent draait in Azure ({marker} is gezet) met DOTNET_ENVIRONMENT " +
                $"'{hostEnvironment.EnvironmentName}', en {AgentEnvironmentKey} is niet gezet. " +
                "Terugvallen op 'dev' zou de agent uit de klantweergave laten verdwijnen zonder " +
                $"melding. Zet {AgentEnvironmentKey} expliciet op prod, acc of dev.");
        }

        return AgentEnvironment.Development;
    }

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"{AgentTimeZoneKey} heeft de waarde '{id}', maar die tijdzone bestaat niet op dit systeem.",
                exception);
        }
    }

    private static string ResolveVersion(Assembly? entry)
    {
        string? informational = entry?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return entry?.GetName().Version?.ToString() ?? "0.0.0";
    }

    /// <summary>Maakt van <c>factuur-intake</c> een leesbare typeaanduiding: <c>Factuur intake</c>.</summary>
    internal static string Humanise(string agentName)
    {
        string spaced = agentName.Replace('-', ' ').Replace('_', ' ');
        return spaced.Length == 0 ? agentName : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
