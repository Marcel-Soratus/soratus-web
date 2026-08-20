using System.Text;
using System.Text.Json;
using Azure.Identity;
using Soratus.Agents.Contracts;
using Soratus.Seed;

// ─────────────────────────────────────────────────────────────────────────────
//  Soratus.Seed — zet de demodata van het Agent Portal in de echte Cosmos.
//
//  Dit gereedschap bestaat omdat het portaal géén blijvende mocklaag krijgt. In
//  plaats van een tweede, nagemaakte bron naast de echte, schrijven we demodata
//  in precies dezelfde documentvorm die Soratus.Agents.Telemetry zou schrijven.
//  Het portaal weet daardoor niet dat het om demodata gaat en kan dat ook niet
//  weten: het leest gewoon zijn normale bron. In fase 1 valt er dus niets te
//  vervangen — dan stoppen we met seeden en verdwijnt dit project.
// ─────────────────────────────────────────────────────────────────────────────

Console.OutputEncoding = Encoding.UTF8;

try
{
    var settings = SeedSettingsReader.Read(args);

    if (settings.Help)
    {
        ShowHelp();
        return 0;
    }

    // Bewaakt dat dit gereedschap tijden nog steeds precies zo wegschrijft als de bibliotheek.
    SeedJson.AssertMatchesTelemetryLibrary();

    // En dat de gedeelde knipregel op msg nog doet wat hij belooft. Die staat in het contract en
    // wordt hier alleen aangeroepen; deze assertie is er zodat een verbouwing daar hier opvalt
    // vóórdat er documenten met een stacktrace in msg de database in gaan.
    MessageTruncation.AssertContract();

    var now = DateTimeOffset.UtcNow;

    Console.WriteLine("Soratus.Seed — demodata voor het Agent Portal");
    Console.WriteLine($"  endpoint : {settings.Endpoint}");
    Console.WriteLine($"  database : {settings.Database}");
    Console.WriteLine($"  bestand  : {settings.ManifestPath}");
    Console.WriteLine($"  moment   : {now.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z (alle relatieve tijden worden hierop gerekend)");
    Console.WriteLine($"  modus    : {Mode(settings)}");
    Console.WriteLine();

    var manifest = ReadManifest(settings);
    var plan = SeedPlanner.Build(manifest, now);
    Report(plan);

    using var seeder = new CosmosSeeder(settings, new DefaultAzureCredential());

    if (settings.VerifyOnly)
    {
        await seeder.VerifyAsync(CancellationToken.None);
        return 0;
    }

    if (settings.Clean)
    {
        await seeder.CleanAsync(plan.AgentNames, settings.DryRun, CancellationToken.None);
    }
    else
    {
        await seeder.SeedAsync(plan, settings.DryRun, CancellationToken.None);
    }

    Console.WriteLine();

    if (settings.DryRun)
    {
        Console.WriteLine("Proefdraai afgerond. Er is niets geschreven en niets verwijderd.");

        if (settings.KeepFresh)
        {
            Console.WriteLine("--keep-fresh doet bij een proefdraai niets; er valt niets vers te houden.");
        }

        return 0;
    }

    await seeder.VerifyAsync(CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine(settings.Clean
        ? "Opruimen afgerond. De documenten van de bibliotheek staan er nog."
        : "Seeden afgerond. Het portaal leest dit als gewone telemetrie.");

    if (settings.KeepFresh && !settings.Clean)
    {
        await KeepFreshAsync(seeder, manifest, settings);
    }

    return 0;
}
catch (SeedManifestException exception)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Het bestand telemetry.json klopt niet:");
    Console.Error.WriteLine("  " + exception.Message);
    return 2;
}
catch (SeedException exception)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Kon niet seeden:");
    Console.Error.WriteLine("  " + exception.Message);
    return 3;
}
catch (Exception exception)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Onverwachte fout:");
    Console.Error.WriteLine("  " + exception);
    return 4;
}

static string Mode(SeedSettings settings) => (settings.Clean, settings.DryRun, settings.VerifyOnly) switch
{
    (_, _, true) => "alleen tellen wat er staat",
    (true, true, _) => "proefdraai van het opruimen — er wordt niets verwijderd",
    (true, false, _) => "opruimen",
    (false, true, _) => "proefdraai — er wordt niets geschreven",
    _ => settings.KeepFresh
        ? $"schrijven, daarna vershouden (simulatie) elke {settings.Interval.TotalSeconds:0} s"
        : "schrijven",
};

/// <summary>
/// Houdt de hartslag van de geseede registraties vers tot iemand Ctrl+C drukt.
/// </summary>
/// <remarks>
/// Dit is een demohulpstuk en het presenteert zich ook zo. Status is een afgeleide van de
/// hartslag, en een seed-document klopt niet uit zichzelf door; zonder deze lus staat de hele
/// demodata twee minuten na het seeden op <c>degraded</c> en verdwijnt precies het onderscheid
/// waar hij voor gemaakt is. Wat hier gebeurt is dus simulatie, en dat hoort er met zoveel woorden
/// bij te staan: er draait geen enkele echte agent.
///
/// Alleen de registraties worden herschreven. Runs en logregels blijven staan en worden ouder.
/// </remarks>
static async Task KeepFreshAsync(CosmosSeeder seeder, SeedManifest manifest, SeedSettings settings)
{
    using var stop = new CancellationTokenSource();

    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        stop.Cancel();
    };

    Console.WriteLine();
    Console.WriteLine($"Vershouden — SIMULATIE. Elke {settings.Interval.TotalSeconds:0} seconden wordt alleen");
    Console.WriteLine("  lastHeartbeatAt van de registraties opnieuw weggeschreven, zodat de demodata niet binnen");
    Console.WriteLine("  twee minuten op degraded valt. Runs en logregels blijven staan en worden dus ouder.");
    Console.WriteLine("  Elke agent houdt zijn eigen afstand tot nu: live blijft live, degraded blijft degraded,");
    Console.WriteLine("  failed blijft failed. Er draait geen enkele echte agent. Ctrl+C stopt.");
    Console.WriteLine();

    var rounds = 0;

    try
    {
        while (!stop.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var plan = SeedPlanner.Build(manifest, now);
            var (fresh, silent) = await seeder.RefreshHeartbeatsAsync(plan.Agents, now, stop.Token);

            rounds++;
            Console.WriteLine(
                $"  {now.UtcDateTime:HH:mm:ss}Z  {plan.Agents.Count} registraties bijgewerkt — " +
                $"{fresh} vers, {silent} bewust stil");

            await Task.Delay(settings.Interval, stop.Token);
        }
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C. Geen fout.
    }

    Console.WriteLine();
    Console.WriteLine(
        $"Vershouden gestopt na {rounds} rondes. Vanaf nu veroudert de demodata weer: binnen twee minuten " +
        "staat alles op degraded, precies zoals het hoort bij agents die niet draaien.");
}

static SeedManifest ReadManifest(SeedSettings settings)
{
    if (!File.Exists(settings.ManifestPath))
    {
        throw new SeedException(
            $"Het bestand '{settings.ManifestPath}' bestaat niet. Geef het pad mee met --file of zet " +
            "telemetry.json terug in tools/seed.");
    }

    SeedManifest? manifest;

    try
    {
        manifest = JsonSerializer.Deserialize<SeedManifest>(
            File.ReadAllText(settings.ManifestPath),
            SeedJson.ManifestOptions);
    }
    catch (JsonException exception)
    {
        throw new SeedManifestException($"De JSON is niet te lezen: {exception.Message}");
    }

    if (manifest is null)
    {
        throw new SeedManifestException("Het bestand is leeg.");
    }

    return manifest;
}

static void Report(SeedPlan plan)
{
    Console.WriteLine(
        $"Gelezen: {plan.Customers.Count} klanten, {plan.Agents.Count} agents, {plan.Runs.Count} runs, " +
        $"{plan.Logs.Count} logregels.");

    foreach (var customer in plan.Customers)
    {
        var line = $"  {customer.Id,-9} {customer.Agents,2} agents  {customer.Runs,4} runs  {customer.Logs,4} logregels";
        Console.WriteLine(customer.Agents == 0 ? line + "   ← lege staat" : line);
    }

    Console.WriteLine();

    var (length, agentName, name) = plan.LongestMessage;
    Console.WriteLine(
        $"Berichten: {plan.CutMessages} van de {plan.Logs.Count} geknipt op de eerste regelovergang " +
        $"(overloop naar extra.{MessageTruncation.OverflowKey}).");
    Console.WriteLine($"  langste msg: {length} tekens — {agentName} / {name}");

    if (plan.MultiLineMessages > 0)
    {
        // Kan alleen als de knip zelf stuk is. Dan liever nu stoppen dan een stacktrace in een
        // veld wegschrijven dat een klant ziet.
        throw new SeedManifestException(
            $"{plan.MultiLineMessages} berichten bevatten na de knip nog een regelovergang. " +
            "Er wordt niets weggeschreven; controleer MessageTruncation in Soratus.Agents.Contracts.");
    }

    Console.WriteLine("  geen enkel bericht bevat nog een regelovergang.");
    Console.WriteLine();
}

static void ShowHelp()
{
    Console.WriteLine("""
        Soratus.Seed — zet de demodata van het Agent Portal in Cosmos.

        Gebruik:
          dotnet run --project tools/Soratus.Seed [opties]

        Opties:
          --dry-run              Toon wat er zou gebeuren; schrijf en verwijder niets.
          --clean                Ruim de geseede documenten op. Alleen documenten van de agents
                                 die in telemetry.json staan; nooit die van de bibliotheek.
          --verify               Tel alleen wat er nu in de database staat.
          --keep-fresh           Blijf na het seeden draaien en ververs alleen de hartslag van de
                                 registraties, zodat de demodata niet binnen twee minuten op
                                 degraded valt. SIMULATIE, alleen voor demo's en schermbouw; er
                                 draait geen enkele echte agent. Ctrl+C stopt.
          --interval <seconden>  Hoe vaak --keep-fresh een ronde doet. Standaard 30, en moet ruim
                                 onder de degraded-drempel van 120 seconden blijven.
          --endpoint <url>       Cosmos-endpoint. Standaard cosmos-soratus-prod.
          --database <naam>      Database. Standaard 'telemetry'.
          --file <pad>           Pad naar telemetry.json. Standaard tools/seed/telemetry.json.
          --help                 Deze tekst.

        Ook te zetten via SORATUS_SEED_ENDPOINT, SORATUS_SEED_DATABASE en SORATUS_SEED_FILE, of
        via appsettings.json naast het programma. Een argument wint van een omgevingsvariabele,
        die wint van het bestand.

        Authenticatie gaat uitsluitend via DefaultAzureCredential. Er zijn geen accountsleutels:
        local auth staat uit op het account.
        """);
}
