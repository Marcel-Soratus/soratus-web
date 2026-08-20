using Microsoft.Extensions.Options;

namespace Soratus.Portal.Data;

/// <summary>
/// Migreert de klantenlijst één keer naar de opslag en houdt de momentopname in het geheugen bij.
/// </summary>
/// <remarks>
/// <para><strong>Dit is de omschakeling.</strong> Tot fase 1 kwam de klantenlijst uit
/// <c>Portal:Customers</c> en was een klant toevoegen een uitrol. Deze klasse zet die lijst één keer
/// in de opslag en leest hem daarna daaruit. Vanaf dat moment is een nieuwe klant een formulier.
/// </para>
///
/// <para><strong>Er is geen moment waarop het portaal geen klanten kent.</strong> Dat is met opzet in
/// deze volgorde gebouwd: <see cref="Security.CustomerDirectory"/> staat bij het opstarten al vol met
/// de configuratielijst, deze achtergrondtaak vervangt die lijst pas als het lezen is gelukt, en
/// mislukt het lezen dan blijft staan wat er stond. Een portaal dat niemand meer binnenlaat omdat
/// Cosmos twee seconden hapert, zou een slechtere ruil zijn dan een lijst die even oud is.</para>
///
/// <para><strong>Achtergrondtaak en geen startupcontrole</strong>, om dezelfde reden als bij
/// <see cref="TelemetryWarmup"/>: een opslag die hapert mag <c>/healthz</c> niet tegenhouden, want
/// dan rolt de uitrolpijplijn de vorige versie terug om een storing die het portaal had kunnen
/// overleven.</para>
///
/// <para>De periodieke verversing is er voor de tweede instantie. Draait de app op meer dan één
/// instantie, dan bestaat een klant die op A wordt aangemaakt voor B pas na een verversing — een
/// schrijfactie ververst alleen de instantie waar hij langskwam. Vandaar dat het interval niet uit
/// staat: zonder hem zou "de nieuwe klant is er soms wel en soms niet" het gedrag zijn, en dat is de
/// vervelendste soort fout om te vinden.</para>
/// </remarks>
internal sealed class PortalDirectoryRefresh(
    CosmosPortalDataStore store,
    Security.CustomerDirectory directory,
    IOptions<PortalDataOptions> options,
    ILogger<PortalDirectoryRefresh> logger) : BackgroundService
{
    private readonly PortalDataOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!store.IsConfigured)
        {
            // Geen endpoint betekent: het portaal werkt op de configuratielijst. Dat werkt, maar
            // niets is dan te beheren, dus dit hoort geen stilte te zijn.
            logger.LogWarning(
                "PortalData:AccountEndpoint is leeg. Het portaal werkt op de klantenlijst uit " +
                "Portal:Customers en kan klanten, contracten en toegang niet beheren.");
            return;
        }

        await MigrateAsync(stoppingToken).ConfigureAwait(false);
        await RefreshAsync(first: true, stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.RefreshSeconds));

        while (await SafeWaitAsync(timer, stoppingToken).ConfigureAwait(false))
        {
            await RefreshAsync(first: false, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        if (!_options.Bootstrap)
        {
            return;
        }

        try
        {
            var migrated = await store
                .BootstrapAsync(directory.Configured, cancellationToken)
                .ConfigureAwait(false);

            if (migrated is null)
            {
                logger.LogInformation(
                    "De klantenlijst is eerder al gemigreerd; de configuratielijst wordt niet meer " +
                    "geschreven.");
            }
            else
            {
                logger.LogInformation(
                    "{Count} klant(en) uit Portal:Customers gemigreerd naar de portaalopslag. " +
                    "Vanaf nu is dat de bron; de configuratielijst is alleen nog de terugval bij " +
                    "het opstarten.",
                    migrated);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Niet doorgooien: zonder migratie werkt het portaal op de configuratielijst, en dat is
            // precies wat het tot nu toe deed. De volgende start probeert het opnieuw — de migratie
            // is herhaalbaar tot de markering staat.
            logger.LogError(
                exception,
                "De eenmalige migratie van de klantenlijst is mislukt. Het portaal werkt door op " +
                "Portal:Customers en probeert het bij de volgende start opnieuw.");
        }
    }

    private async Task RefreshAsync(bool first, CancellationToken cancellationToken)
    {
        try
        {
            await store.ReloadDirectoryAsync(cancellationToken).ConfigureAwait(false);

            if (first)
            {
                logger.LogInformation(
                    "De klantenlijst komt nu uit de portaalopslag: {Count} klant(en).",
                    directory.All.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // De momentopname blijft staan zoals hij was. Bij de eerste poging is dat de
            // configuratielijst, daarna de laatste die wél is gelezen. Een lijst van vijf minuten
            // oud is beter dan geen lijst.
            logger.LogError(
                exception,
                directory.LoadedFromStore
                    ? "De klantenlijst kon niet worden ververst. Het portaal werkt door op de " +
                      "vorige momentopname."
                    : "De klantenlijst kon niet uit de portaalopslag worden gelezen. Het portaal " +
                      "werkt op Portal:Customers.");
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
