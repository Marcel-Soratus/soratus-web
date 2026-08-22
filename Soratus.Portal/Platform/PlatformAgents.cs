using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry;
using Soratus.Agents.Telemetry.HostedAgents;
using Soratus.Portal.Alerts;
using Soratus.Portal.Data;

namespace Soratus.Portal.Platform;

/// <summary>
/// De namen van de beheeragents van Soratus (§4), zoals ze in het portaal staan.
/// </summary>
/// <remarks>
/// Als constanten, omdat ze op drie plekken hetzelfde moeten zijn: in de aankondiging, in de lus die
/// de run opent, en in de opslag waar het registratiedocument op deze naam staat. Een naam die op één
/// van die drie plekken verschuift levert een agent op die aanroepen verwerkt zonder in het portaal
/// te staan, of een rij in het portaal waar niets achter zit — dezelfde fout die
/// <c>EndpointHostedAgentSource</c> voor de webkant dichtzet.
/// </remarks>
internal static class PlatformAgentNames
{
    /// <summary>De dagelijkse kostenmeting per klant (§4, <c>kosten-collector</c>).</summary>
    internal const string Costs = "kosten-collector";

    /// <summary>De storingsmelder die elke minuut kijkt (§4, <c>storingsmelder</c>).</summary>
    internal const string Alerts = "storingsmelder";
}

/// <summary>
/// Wat er van het aansluiten van de beheeragents terecht is gekomen, om te loggen.
/// </summary>
/// <param name="Published">Of het portaal zijn eigen agents publiceert.</param>
/// <param name="Level">Het niveau waarop <paramref name="Explanation"/> hoort te worden gelogd.</param>
/// <param name="Explanation">Wat er is gebeurd, in één regel, met de reden erbij.</param>
/// <remarks>
/// Een uitkomst en geen logregel, omdat het aansluiten in de opstartcode gebeurt en er dan nog geen
/// logger is: die bestaat pas ná <c>Build()</c>. De uitkomst wordt daar gelogd. Dat het een
/// <em>uitkomst</em> is en niet stilte, is het punt — een portaal dat zijn eigen agents niet
/// publiceert hoort dat te zeggen, want anders is een leeg beheeroverzicht niet van een kapotte
/// inrichting te onderscheiden.
/// </remarks>
internal sealed record PlatformAgentsWiring(
    bool Published,
    Microsoft.Extensions.Logging.LogLevel Level,
    string Explanation);

/// <summary>
/// Laat het portaal zich als agent-host melden, met zijn eigen beheeragents erin (§4, fase 6).
/// </summary>
/// <remarks>
/// <para><strong>Het portaal publiceert twee agents en meldt zichzelf niet als agent.</strong> Er is
/// geen derde rij "het portaal": zijn hartslag zou per constructie gelijk zijn aan die van de twee,
/// dus die rij voegt een regel toe zonder een feit toe te voegen. Dezelfde afweging als bij de
/// webhost van punt 42.</para>
///
/// <para><strong>Waarom de vorm voor meerdere agents en niet die voor één agent met een
/// cron.</strong> <c>AddSoratusAgent&lt;TAgent&gt;</c> bestaat voor precies één agent per proces: hij
/// zet één <c>AgentIdentity</c>, één <c>AgentSchedule</c> en één <c>AgentLifecycleState</c> als
/// singleton neer, leest één <c>SORATUS_AGENT__SCHEDULE</c>, en een tweede aanroep doet niets. Het
/// portaal is één proces met twee agents en straks meer, en die tweede zou dus stil verdwijnen. En de
/// betekenis zou verschuiven: dan is het portaal de agent, terwijl het de host is waarin agents
/// wonen.</para>
///
/// <para><strong>En waarom de klok van het portaal blijft.</strong> De bibliotheek kan de klok
/// overnemen — dat doet ze bij <c>IScheduledAgent</c> — maar dan draait de kostencollector niet meer
/// als de telemetrie niet is ingericht. Telemetrie mag het werk nooit omleggen, en werk dat zonder
/// telemetrie helemaal niet meer gebeurt is de scherpste vorm daarvan. De collector en de melder
/// houden dus hun eigen lus; wat de bibliotheek erbij doet is de run vastleggen en het plan
/// publiceren. Dat het gepubliceerde plan niet uit de pas kan lopen met de lus komt van
/// <see cref="PlatformAgentPlans"/>: één <see cref="SoratusSchedule"/> per agent, aangekondigd én
/// gebruikt om op te wachten.</para>
///
/// <para><strong>Deze code werpt niet, en dat is de belangrijkste eigenschap ervan.</strong> Zie
/// <see cref="AddSoratusPlatformAgents"/>.</para>
/// </remarks>
internal static class PlatformAgents
{
    /// <summary>
    /// De aankondiging van de kostencollector.
    /// </summary>
    /// <param name="costs">De instellingen van de collector, voor het draaimoment.</param>
    /// <returns>De aankondiging.</returns>
    internal static HostedAgentDeclaration CostsDeclaration(AzureCostOptions costs) => new()
    {
        AgentName = PlatformAgentNames.Costs,
        DisplayType = "Cost Management",
        Trigger = TriggerKind.Timer,
        Schedule = PlatformAgentPlans.Costs(costs.RunHourUtc),
        TriggerDetail = $"dagelijks {Math.Clamp(costs.RunHourUtc, 0, 23):D2}:00 UTC",
    };

    /// <summary>
    /// De aankondiging van de storingsmelder.
    /// </summary>
    /// <param name="alerts">De instellingen van de melder, voor het interval.</param>
    /// <returns>De aankondiging.</returns>
    internal static HostedAgentDeclaration AlertsDeclaration(AgentAlertOptions alerts) => new()
    {
        AgentName = PlatformAgentNames.Alerts,
        DisplayType = "Monitoring",
        Trigger = TriggerKind.Timer,
        Schedule = PlatformAgentPlans.Alerts(alerts.IntervalSeconds),
        TriggerDetail = Interval(PlatformAgentPlans.PlannedInterval(alerts.IntervalSeconds)),
    };

    /// <summary>
    /// Sluit het portaal aan als agent-host en kondigt de twee beheeragents aan.
    /// </summary>
    /// <param name="builder">De hostbouwer.</param>
    /// <returns>Wat er van het aansluiten terecht is gekomen, om te loggen ná <c>Build()</c>.</returns>
    /// <remarks>
    /// <para><strong>Deze methode werpt niet, voor geen enkele configuratie.</strong> Dat is een
    /// bewuste afwijking van wat de bibliotheek zelf wil: <c>AddSoratusHostedAgents</c> wérpt bij een
    /// ontbrekende endpoint, en dat is voor een agent het juiste gedrag — daar is de telemetrie de
    /// hele opdracht, en een agent die niets meldt bestaat niet. Hier is het andersom. Dit is een
    /// portaal waar operators en klanten op inloggen; zijn agent-zijn is bijzaak. Een ontbrekende
    /// sleutel, een verkeerde endpoint of een rol die nog niet verleend is mogen <c>/</c> niet raken,
    /// en een uitzondering in de opstartcode is de hardste manier om dat wél te doen: dan start de
    /// app niet, geeft <c>/healthz</c> geen 200, en rolt de pijplijn terug om iets dat het portaal
    /// had kunnen overleven.</para>
    ///
    /// <para><strong>Een mislukte aansluiting laat de container onaangeraakt.</strong> Dat is geen
    /// aanname over de bibliotheek maar een eigenschap ervan: <c>AddSoratusHostedAgents</c> doet al
    /// zijn controles — de twee contractasserties, de schemasleutel, de identiteit en de opties —
    /// vóór de eerste <c>AddSingleton</c>. Er staat een test op dat een mislukte aansluiting geen
    /// halve registratie achterlaat, want de halve toestand is wat een <c>try</c> om een reeks
    /// registraties gevaarlijk zou maken.</para>
    ///
    /// <para><strong>De configuratie gaat langs de sleutels van de bibliotheek zelf.</strong> Die
    /// leest <c>SORATUS_TELEMETRY__*</c> en <c>SORATUS_CUSTOMER__ID</c>, en wij vullen die uit
    /// <see cref="PlatformTelemetryOptions"/> — één sectie die zegt waar de telemetrie van het
    /// platform staat, voor zowel de schrijf- als de leeskant. Deze bron staat áchter de gewone
    /// configuratie, dus hij overschrijft een met de hand gezette <c>SORATUS_*</c>-sleutel. Dat is
    /// bedoeld: twee plekken die hetzelfde zeggen is één plek die kan afwijken, en de fout die daaruit
    /// komt is een portaal dat netjes publiceert in een database waar het scherm niet kijkt.</para>
    /// </remarks>
    internal static PlatformAgentsWiring AddSoratusPlatformAgents(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new PlatformTelemetryOptions();
        builder.Configuration.GetSection(PlatformTelemetryOptions.SectionName).Bind(options);

        if (!options.Enabled)
        {
            return new PlatformAgentsWiring(
                false,
                Microsoft.Extensions.Logging.LogLevel.Warning,
                "PlatformTelemetry:Enabled staat uit. Het portaal publiceert zijn eigen "
                + "beheeragents niet; op /klant/soratus/agents staat dan niets, en dat de "
                + "kostencollector of de storingsmelder is stilgevallen is alleen in het log te zien.");
        }

        if (!options.IsConfigured)
        {
            return new PlatformAgentsWiring(
                false,
                Microsoft.Extensions.Logging.LogLevel.Warning,
                "PlatformTelemetry:AccountEndpoint is leeg. Het portaal publiceert zijn eigen "
                + "beheeragents niet; op /klant/soratus/agents staat dan niets, en dat de "
                + "kostencollector of de storingsmelder is stilgevallen is alleen in het log te zien. "
                + "De agents zelf draaien gewoon door.");
        }

        // De sleutels die Soratus.Agents.Telemetry zelf leest, gevuld uit één portaalsectie. Als
        // laatste bron toegevoegd, dus deze waarden winnen — zie de opmerkingen hierboven.
        builder.Configuration.AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>("SORATUS_TELEMETRY:ENDPOINT", options.AccountEndpoint),
            new KeyValuePair<string, string?>("SORATUS_TELEMETRY:DATABASE", options.Database),
            new KeyValuePair<string, string?>("SORATUS_CUSTOMER:ID", options.CustomerId),

            // De naam van de host. Hij wordt niet als agent gepubliceerd — er is geen derde rij "het
            // portaal" — maar hij staat als ApplicationName op de Cosmos-verbinding en in de
            // diagnostiek van de bibliotheek, en daar is "soratus.portal" uit de assemblynaam
            // minder leesbaar dan dit.
            new KeyValuePair<string, string?>("SORATUS_AGENT:NAME", "soratus-portal"),
        ]);

        var costs = Bind<AzureCostOptions>(builder, AzureCostOptions.SectionName);
        var alerts = Bind<AgentAlertOptions>(builder, AgentAlertOptions.SectionName);

        try
        {
            builder.AddSoratusHostedAgents();
            builder.Services.AddSoratusHostedAgent(CostsDeclaration(costs));
            builder.Services.AddSoratusHostedAgent(AlertsDeclaration(alerts));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return new PlatformAgentsWiring(
                false,
                Microsoft.Extensions.Logging.LogLevel.Error,
                "Het portaal kon zich niet als agent-host aansluiten en publiceert zijn eigen "
                + "beheeragents dus niet. De agents zelf draaien gewoon door en het portaal ook. "
                + $"Reden: {exception.Message}");
        }

        return new PlatformAgentsWiring(
            true,
            Microsoft.Extensions.Logging.LogLevel.Information,
            $"Het portaal publiceert {PlatformAgentNames.Costs} en {PlatformAgentNames.Alerts} als "
            + $"agents van klant '{options.CustomerId}' naar database '{options.Database}' op "
            + $"{options.AccountEndpoint}.");
    }

    /// <summary>
    /// Leest een optiesectie los van de container.
    /// </summary>
    /// <typeparam name="TOptions">De optieklasse.</typeparam>
    /// <param name="builder">De hostbouwer.</param>
    /// <param name="section">De naam van de sectie.</param>
    /// <returns>De gebonden opties.</returns>
    /// <remarks>
    /// Los en niet uit de container, want de container bestaat hier nog niet. Bewust zonder
    /// validatie: het plan wordt uit deze waarden opgebouwd door
    /// <see cref="PlatformAgentPlans"/>, en die klemt in plaats van te werpen.
    /// </remarks>
    private static TOptions Bind<TOptions>(WebApplicationBuilder builder, string section)
        where TOptions : new()
    {
        var options = new TOptions();
        builder.Configuration.GetSection(section).Bind(options);
        return options;
    }

    /// <summary>Een interval in leesbare woorden, voor de toelichting op de trigger.</summary>
    /// <param name="interval">Het interval.</param>
    /// <returns>Bijvoorbeeld <c>elke minuut</c>.</returns>
    private static string Interval(TimeSpan interval) => interval switch
    {
        { TotalMinutes: 1 } => "elke minuut",
        { TotalMinutes: < 60 } => $"elke {(int)interval.TotalMinutes} minuten",
        _ => "elk uur",
    };
}
