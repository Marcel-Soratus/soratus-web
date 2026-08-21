using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Soratus.Agents.AspNetCore.Internal;
using Soratus.Agents.Telemetry;
using Soratus.Agents.Telemetry.HostedAgents;

namespace Soratus.Agents.AspNetCore;

/// <summary>
/// Sluit een bestaande ASP.NET Core-webapplicatie aan op het Soratus-agentcontract.
/// </summary>
/// <remarks>
/// <para>Het geval waarvoor dit bestaat: een klant zonder achtergrondagents, wiens "agents"
/// diensten zijn binnen zijn webapplicatie — een chat, een overzicht, een inlezing — die alleen
/// draaien wanneer iemand ze aanroept. Er is geen schema, geen eigen proces, geen eigen lus. Wat er
/// wél is, is een webproces dat continu geladen blijft, en dáár komt de hartslag vandaan.</para>
///
/// <para>Wat de aanroeper ervoor moet doen, en niet meer dan dat:</para>
/// <code>
/// builder.AddSoratusWebAgents();                       // eenmaal, bij het opzetten
///
/// app.UseRouting();
/// app.UseSoratusAgentRuns();                           // eenmaal, ná UseRouting
///
/// var chat = app.MapPost("/api/chat", …).WithSoratusAgent("boekhoud-chat", "Chat");
/// var overzicht = app.MapGet("/api/financieel", …).WithSoratusAgent("financieel-overzicht", "Rapportage");
/// var import = app.MapPost("/api/declaraties", …).WithSoratusAgent("declaraties-import", "Document-intake");
/// </code>
///
/// <para>De bestaande handlers worden niet aangeraakt. Wil een handler melden hoeveel regels hij
/// verwerkte, dan kost dat één regel: <c>context.SoratusAgentRun()?.Processed(regels)</c>.</para>
/// </remarks>
public static class SoratusWebAgentsExtensions
{
    /// <summary>
    /// Registreert de telemetrie voor een webapplicatie die agents herbergt.
    /// </summary>
    /// <param name="builder">De hostbouwer.</param>
    /// <param name="configure">Optionele bijstelling van de knoppen.</param>
    /// <returns>Dezelfde bouwer.</returns>
    /// <exception cref="ArgumentNullException">Als <paramref name="builder"/> <c>null</c> is.</exception>
    /// <exception cref="InvalidOperationException">
    /// Als verplichte configuratie ontbreekt. Zie
    /// <see cref="SoratusAgentBuilderExtensions.AddSoratusHostedAgents"/>.
    /// </exception>
    /// <remarks>
    /// Welke agents deze applicatie herbergt staat hier niet: dat leest
    /// <see cref="Internal.EndpointHostedAgentSource"/> uit de metadata op de endpoints. Eén lijst,
    /// op de plek waar het werk staat.
    /// </remarks>
    public static IHostApplicationBuilder AddSoratusWebAgents(
        this IHostApplicationBuilder builder,
        Action<SoratusTelemetryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddSoratusHostedAgents(configure);

        builder.Services.TryAddSingleton<RunMiddlewareMarker>();
        builder.Services.TryAddSingleton<EndpointHostedAgentSource>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedAgentSource, EndpointHostedAgentSource>(
                static sp => sp.GetRequiredService<EndpointHostedAgentSource>()));
        builder.Services.AddSingleton<IHostedService, EndpointWiringCheck>();

        return builder;
    }

    /// <summary>
    /// Zet de aanroeplaag in de verzoekpijplijn: elke aanroep van een endpoint met
    /// <see cref="SoratusAgentMetadata"/> wordt één run.
    /// </summary>
    /// <param name="app">De pijplijn.</param>
    /// <returns>Dezelfde pijplijn.</returns>
    /// <exception cref="ArgumentNullException">Als <paramref name="app"/> <c>null</c> is.</exception>
    /// <remarks>
    /// <para>Hoort ná <c>UseRouting</c>, want vóór dat punt weet niemand welk endpoint geraakt wordt
    /// en dus ook niet welke agent aan het werk gaat. Ná <c>UseAuthentication</c> en
    /// <c>UseAuthorization</c> is ook goed en meestal beter: een verzoek dat op de deur wordt
    /// tegengehouden heeft de dienst nooit bereikt, en dan is er geen run om vast te leggen.</para>
    ///
    /// <para>Deze regel is niet weg te automatiseren, en dat is gemeten en niet aangenomen. Een
    /// <c>IStartupFilter</c> kan middleware alleen vóór of ná de hele gebruikerspijplijn hangen: vóór
    /// <c>UseRouting</c> is het endpoint nog onbekend, en ná de endpointlaag komt hij nooit meer aan
    /// de beurt. Vergeten wordt daarom niet stil: zie <see cref="Internal.EndpointWiringCheck"/>.</para>
    /// </remarks>
    public static IApplicationBuilder UseSoratusAgentRuns(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.ApplicationServices.GetRequiredService<RunMiddlewareMarker>().MarkInstalled();
        return app.UseMiddleware<SoratusAgentRunMiddleware>();
    }

    /// <summary>
    /// Kondigt aan dat dit endpoint een Soratus-agent is.
    /// </summary>
    /// <typeparam name="TBuilder">Het soort endpointbouwer.</typeparam>
    /// <param name="builder">Het endpoint.</param>
    /// <param name="agentName">
    /// Technische naam, kleine letters met koppelstreepjes, bijvoorbeeld <c>declaraties-import</c>.
    /// </param>
    /// <param name="displayType">Typeaanduiding voor de typekolom, bijvoorbeeld <c>Document-intake</c>.</param>
    /// <param name="triggerDetail">
    /// Toelichting op de trigger voor op het scherm, bijvoorbeeld <c>POST /api/declaraties</c>. Vrije
    /// tekst die de klant leest.
    /// </param>
    /// <param name="trigger">
    /// Waardoor de aanroep binnenkomt; standaard <see cref="Contracts.TriggerKind.Http"/>.
    /// </param>
    /// <returns>Hetzelfde endpoint.</returns>
    /// <exception cref="ArgumentNullException">Als <paramref name="builder"/> <c>null</c> is.</exception>
    /// <remarks>
    /// Twee endpoints mogen dezelfde agentnaam aankondigen — één dienst met een lees- en een
    /// schrijfroute is één agent — mits met dezelfde typeaanduiding en trigger.
    /// </remarks>
    public static TBuilder WithSoratusAgent<TBuilder>(
        this TBuilder builder,
        string agentName,
        string? displayType = null,
        string? triggerDetail = null,
        Contracts.TriggerKind trigger = Contracts.TriggerKind.Http)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithMetadata(new SoratusAgentMetadata(agentName, displayType, triggerDetail, trigger));
        return builder;
    }

    /// <summary>
    /// De run van deze aanroep, of <c>null</c> als dit endpoint geen Soratus-agent is.
    /// </summary>
    /// <param name="context">Het verzoek.</param>
    /// <returns>De lopende run, of <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException">Als <paramref name="context"/> <c>null</c> is.</exception>
    /// <remarks>
    /// <para>Hiermee meldt een handler wat hij verwerkte: <c>run.Processed(regels)</c>,
    /// <c>run.FailedItems(afgekeurd)</c>, <c>run.MarkRolledBack()</c>. Dat is het enige dat de
    /// bibliotheek niet zelf kan weten, want wat een item is weet alleen de dienst.</para>
    ///
    /// <para><c>null</c> teruggeven en niet werpen, en dat is opzet: telemetrie mag geen enkele
    /// handler kunnen omleggen, ook niet die van iemand die de metadata vergeet.</para>
    /// </remarks>
    public static IAgentRun? SoratusAgentRun(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Features.Get<IAgentRun>();
    }
}
