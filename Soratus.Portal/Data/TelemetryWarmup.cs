using Soratus.Portal.Security;

namespace Soratus.Portal.Data;

/// <summary>
/// Legt na het opstarten alvast de verbinding met elke klantopslag aan.
/// </summary>
/// <remarks>
/// <para>Gemeten tegen de echte opslag: het overzicht opbouwen kost warm ongeveer 200 ms en koud
/// bijna acht seconden. Dat verschil zit niet in de query's maar in het eenmalige werk eromheen —
/// een token ophalen, de <c>CosmosClient</c> zijn routering laten opbouwen, en per container één
/// keer controleren of hij bestaat. Zonder deze klasse betaalt de eerste operator die 's ochtends
/// het portaal opent die rekening, en precies dan wil je binnen twee seconden zien of er iets stuk
/// is.</para>
///
/// <para>Het draait als achtergrondtaak en niet als startupcontrole. Dat is bewust: een klantopslag
/// die hapert mag het opstarten niet tegenhouden, want dan gaat <c>/healthz</c> niet omhoog en rolt
/// de uitrolpijplijn de vorige versie terug om een storing bij één klant. Een fout hier wordt
/// gelogd en verder genegeerd — het overzicht toont die klant later gewoon als "status onbekend"
/// met de reden erbij.</para>
///
/// <para>Bijvangst die het waard is: een verkeerd ingerichte container of een ontbrekende
/// roltoewijzing staat nu bij het opstarten in de log, in plaats van pas bij de eerste keer dat
/// iemand het scherm opent.</para>
/// </remarks>
internal sealed class TelemetryWarmup(
    ICustomerDirectory directory,
    CosmosContainerProvider containers,
    ILogger<TelemetryWarmup> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Klanten delen in fase 0 één account; die willen we niet zeven keer opwarmen.
        var locations = directory.All
            .Select(customer => customer.Telemetry)
            .OfType<TelemetryLocation>()
            .DistinctBy(location => location.CacheKey)
            .ToArray();

        if (locations.Length == 0)
        {
            logger.LogWarning(
                "Er is geen enkele klantopslag ingericht. Controleer de secties Telemetry en " +
                "Portal:Customers in de configuratie.");
            return;
        }

        foreach (var location in locations)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.WhenAll(
                    containers.AgentsAsync(location, stoppingToken),
                    containers.RunsAsync(location, stoppingToken),
                    containers.LogsAsync(location, stoppingToken)).ConfigureAwait(false);

                logger.LogInformation(
                    "Telemetrie-opslag {Endpoint}/{Database} is bereikbaar.",
                    location.AccountEndpoint,
                    location.Database);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Bewust niet doorgooien: het portaal hoort te blijven draaien en de storing op het
                // scherm te tonen, niet om te vallen.
                logger.LogError(
                    exception,
                    "Telemetrie-opslag {Endpoint}/{Database} is bij het opstarten niet bereikbaar.",
                    location.AccountEndpoint,
                    location.Database);
            }
        }
    }
}
