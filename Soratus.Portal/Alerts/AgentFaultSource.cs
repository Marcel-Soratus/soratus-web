using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Alerts;

/// <summary>
/// Wat er van één klant is gelezen: zijn agents, of de reden dat ze niet te lezen waren.
/// </summary>
/// <param name="CustomerId">De klantslug.</param>
/// <param name="DisplayName">De klantnaam, voor de onderwerpregel van de melding.</param>
/// <param name="Agents">De agents met hun laatste afgeronde run. Leeg als er niets te lezen was.</param>
/// <param name="Unavailable">
/// Waarom de opslag van deze klant niet te lezen was, of <c>null</c>. Zie de opmerkingen.
/// </param>
/// <remarks>
/// <para><strong>Een klant die niet te lezen is verdwijnt niet, en er wordt ook niet over
/// gemaild.</strong> Dezelfde vorm als <c>CustomerTelemetry.Unavailable</c> op het overzicht, met een
/// andere uitkomst: daar toont het scherm "status onbekend", hier gebeurt er niets. Dat is met opzet
/// die kant op. <c>AgentStatusCalculator</c> maakt van een ontbrekende registratie
/// <c>Unknown</c> en <c>ShouldAlert</c> meldt daar niet over — "wij konden niet lezen" is geen storing
/// van de agent, en een melder die daarover mailt mailt bij elke hapering van Cosmos over élke agent
/// van die klant tegelijk.</para>
///
/// <para>Wat er dan wél gebeurt: de melder logt het als <c>warning</c>. Dat is de eerlijke plek — een
/// storing in ons eigen leespad hoort niet als storing van de klant te worden gemeld.</para>
/// </remarks>
internal sealed record CustomerAgentScan(
    string CustomerId,
    string DisplayName,
    IReadOnlyList<AgentSnapshot> Agents,
    string? Unavailable);

/// <summary>
/// Waar de storingsmelder zijn agents vandaan haalt.
/// </summary>
/// <remarks>
/// <para><strong>Een eigen naad en niet <see cref="IAgentTelemetryStore"/>, en dat is de rolgrens en
/// geen netheid.</strong> Elke methode van die interface neemt een <see cref="CustomerScope"/>: het
/// bewijs dat er een mens naar een klant kijkt en dat hij dat mag. De melder heeft geen mens en dus
/// geen scope, en zou er een moeten verzinnen om daar langs te komen — een operatorbewijs zonder
/// operator, en dat is precies de constructie waarmee een autorisatiegrens ophoudt iets te betekenen.
/// Dezelfde afweging en dezelfde uitkomst als bij <see cref="IAzureCostCollectorStore"/> (punt 39).
/// </para>
///
/// <para><strong>De naad is er ook voor de kosten.</strong> Eén ronde is vandaag per klant één query
/// plus één per agent. Wordt dat te duur, dan komt daar een goedkopere lezing achter zonder dat de
/// melder verandert — en dat is de tweede reden dat het scannen niet ín de melder staat.</para>
///
/// <para>Er is precies één implementatie en die is <c>internal</c>. De naad is er voor de test en voor
/// de rolgrens, niet voor een tweede opslag.</para>
/// </remarks>
internal interface IAgentFaultSource
{
    /// <summary>
    /// Leest van elke klant met een ingerichte opslag zijn agents.
    /// </summary>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Eén resultaat per klant. Werpt niet: een klant die niet te lezen is komt terug met
    /// <see cref="CustomerAgentScan.Unavailable"/> gevuld.</returns>
    Task<IReadOnlyList<CustomerAgentScan>> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// De enige implementatie: een fan-out over de klantenlijst naar de scopevrije lezing van de
/// telemetriestore.
/// </summary>
/// <remarks>
/// <para><strong>Uit <see cref="ICustomerDirectory"/> en niet uit de documenten.</strong> Dat is de
/// omgekeerde keuze van <see cref="IAzureCostCollectorStore.TargetsAsync"/>, en de reden is het
/// verschil in wat er wordt gelezen: de kostencollector heeft een veld nodig dat alleen op het
/// klantdocument staat (de Azure-scope), en de melder heeft de telemetrielocatie nodig — precies wat
/// deze lijst uitrekent en het klantdocument niet kant-en-klaar draagt. De lijst wordt door
/// <c>PortalDirectoryRefresh</c> uit de opslag bijgehouden, dus hij is niet ouder dan dat interval.
/// </para>
///
/// <para><strong>Een klant zonder ingerichte opslag komt er niet in.</strong> Er valt dan niets te
/// lezen, en dat is de klant in onboarding — geen storing.</para>
///
/// <para><strong>Serieel en niet parallel.</strong> Dit draait elke minuut in het portaal naast
/// verkeer van operators, en de lezing per klant is zelf al een parallelle fan-out over zijn agents.
/// Een tweede laag parallelliteit erbovenop zou de minuut die er is niet korter maken en wel een piek
/// op de opslag zetten op het moment dat er iets stuk is — precies wanneer een operator het scherm
/// opent.</para>
/// </remarks>
internal sealed class TelemetryAgentFaultSource(
    ICustomerDirectory directory,
    CosmosAgentTelemetryStore telemetry,
    ILogger<TelemetryAgentFaultSource> logger) : IAgentFaultSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerAgentScan>> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<CustomerAgentScan>();

        foreach (var record in directory.All)
        {
            if (record.Telemetry is not { } location)
            {
                continue;
            }

            try
            {
                var agents = await telemetry
                    .ScanAsync(new AgentScanTarget(location, record.Id), cancellationToken)
                    .ConfigureAwait(false);

                results.Add(new CustomerAgentScan(record.Id, record.Name, agents, Unavailable: null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Warning en niet error, en er gaat geen mail uit. "Wij konden niet lezen" is geen
                // storing van de agent; zie CustomerAgentScan.
                logger.LogWarning(
                    exception,
                    "De storingsmelder kon de agents van klant {CustomerId} niet lezen. Er wordt over "
                    + "deze klant niets gemeld; de vorige melding blijft staan zoals hij stond.",
                    record.Id);

                results.Add(new CustomerAgentScan(
                    record.Id,
                    record.Name,
                    Agents: [],
                    Unavailable: exception.GetType().Name));
            }
        }

        return results;
    }
}
