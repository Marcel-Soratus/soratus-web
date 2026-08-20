using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Cosmos;

namespace Soratus.Portal.Data;

/// <summary>
/// Levert de drie containers van één klantopslag en meldt het duidelijk als er iets ontbreekt.
/// </summary>
/// <remarks>
/// Het portaal maakt database noch containers aan. Een ontbrekende container is een
/// inrichtingsfout, en een inrichtingsfout die stilletjes wordt gerepareerd komt terug als een
/// container met de verkeerde partitiesleutel of de verkeerde bewaartermijn. Dus: zichtbaar falen,
/// met een foutmelding waar iemand iets aan heeft.
///
/// De controle gebeurt lui en één keer per container per opslag. Eager bij het opstarten zou
/// betekenen dat één onbereikbare klantopslag het opstarten van de app tegenhoudt — en daarmee ook
/// <c>/healthz</c>, waar de uitrolpijplijn op wacht. Een portaal dat draait en de storing toont is
/// beter dan een portaal dat niet start.
/// </remarks>
internal sealed class CosmosContainerProvider(CosmosClientCache clients)
{
    private readonly ConcurrentDictionary<string, bool> _verified = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>De container met de agentregistraties.</summary>
    public Task<Container> AgentsAsync(TelemetryLocation location, CancellationToken cancellationToken) =>
        ResolveAsync(location, CosmosContainerNames.Agents, cancellationToken);

    /// <summary>De container met de runs.</summary>
    public Task<Container> RunsAsync(TelemetryLocation location, CancellationToken cancellationToken) =>
        ResolveAsync(location, CosmosContainerNames.Runs, cancellationToken);

    /// <summary>De container met de logregels.</summary>
    public Task<Container> LogsAsync(TelemetryLocation location, CancellationToken cancellationToken) =>
        ResolveAsync(location, CosmosContainerNames.Logs, cancellationToken);

    /// <summary>
    /// De container met de portaaleigen gegevens: klanten, contracten en toegang.
    /// </summary>
    /// <param name="location">De portaalopslag.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De container.</returns>
    /// <remarks>
    /// Staat naast de drie telemetriecontainers en niet ertussen, omdat het een andere opslag met
    /// een ander rechtenmodel is: hier wordt geschreven. De foutmeldingen noemen daarom een andere
    /// roltoewijzing dan die van de leeskant — een Data Reader op deze database geeft een 403 op de
    /// eerste schrijfpoging en niet eerder, en dat is een fout waar je zonder deze tekst lang naar
    /// zoekt.
    /// </remarks>
    public async Task<Container> CustomersAsync(
        PortalDataLocation location,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);

        var container = clients.For(location.AccountEndpoint)
            .GetContainer(location.Database, PortalContainerNames.Customers);

        var key = $"{location.CacheKey}|{PortalContainerNames.Customers}";

        if (_verified.ContainsKey(key))
        {
            return container;
        }

        try
        {
            await container.ReadContainerAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new PortalDataNotProvisionedException(
                $"Container '{PortalContainerNames.Customers}' bestaat niet in database " +
                $"'{location.Database}' op {location.AccountEndpoint}. Het portaal maakt containers " +
                "niet aan. Maak hem aan met partitiesleutelpad /pk en zonder defaultTtl — een TTL " +
                "op deze container verwijdert klanten en contracten.",
                exception);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new PortalDataNotProvisionedException(
                $"Geen rechten op container '{PortalContainerNames.Customers}' in database " +
                $"'{location.Database}' op {location.AccountEndpoint}. Het portaal schrijft hier, " +
                "dus de managed identity heeft 'Cosmos DB Built-in Data Contributor' nodig op " +
                $"dbs/{location.Database}. Een rol op het control plane geeft geen recht op " +
                "documenten, en Data Reader levert pas bij de eerste schrijfpoging een 403 op.",
                exception);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new PortalDataNotProvisionedException(
                $"De aanmelding bij {location.AccountEndpoint} is geweigerd. Controleer of " +
                "AZURE_CLIENT_ID naar de user-assigned managed identity van de app wijst. Op dit " +
                "account staat local auth uit, dus er is geen sleutel om op terug te vallen.",
                exception);
        }

        _verified[key] = true;
        return container;
    }

    /// <summary>
    /// Zorgt dat de drie containers van deze opslagen zijn opgezocht en gecontroleerd.
    /// </summary>
    /// <param name="locations">De opslagen. Dubbele locaties worden één keer gedaan.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <remarks>
    /// Bestaat omdat het eerste contact met een Cosmos-account duur is: een token ophalen, de
    /// routering opbouwen en per container één keer controleren of hij bestaat. Gemeten kost dat
    /// samen bijna acht seconden, tegen zo'n 200 ms als het eenmaal loopt.
    ///
    /// Het overzicht roept dit aan vóórdat het zijn tijdslimiet per klant start. Anders valt die
    /// eenmalige opstartkost binnen de limiet en meldt het overzicht bij de eerste bezoeker dat
    /// álle klanten onbereikbaar zijn — precies de fout die deze methode voorkomt, en een fout die
    /// je alleen ziet als je het echt koud draait.
    /// </remarks>
    public async Task WarmAsync(IEnumerable<TelemetryLocation> locations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locations);

        var distinct = locations.DistinctBy(location => location.CacheKey).ToArray();

        foreach (var location in distinct)
        {
            if (_verified.ContainsKey($"{location.CacheKey}|{CosmosContainerNames.Logs}"))
            {
                continue;
            }

            try
            {
                await Task.WhenAll(
                    AgentsAsync(location, cancellationToken),
                    RunsAsync(location, cancellationToken),
                    LogsAsync(location, cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Een opslag die niet opwarmt is geen reden om de rest niet te proberen. De
                // aanroeper ontdekt hem zo meteen alsnog, en meldt hem dan per klant.
            }
        }
    }

    private async Task<Container> ResolveAsync(
        TelemetryLocation location,
        string containerName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);

        var container = clients.For(location.AccountEndpoint)
            .GetContainer(location.Database, containerName);

        var key = $"{location.CacheKey}|{containerName}";

        if (_verified.ContainsKey(key))
        {
            return container;
        }

        try
        {
            await container.ReadContainerAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new TelemetryNotProvisionedException(
                $"Container '{containerName}' bestaat niet in database '{location.Database}' op " +
                $"{location.AccountEndpoint}. Het portaal maakt containers niet aan — dit is een " +
                "inrichtingsfout. Maak de container aan met partitiesleutelpad /pk.",
                exception);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new TelemetryNotProvisionedException(
                $"Geen leesrechten op container '{containerName}' in database " +
                $"'{location.Database}' op {location.AccountEndpoint}. De managed identity van de " +
                "app heeft een data-plane roltoewijzing nodig (Cosmos DB Built-in Data Reader) op " +
                "dat account; een rol op het control plane geeft geen leesrecht op documenten.",
                exception);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new TelemetryNotProvisionedException(
                $"De aanmelding bij {location.AccountEndpoint} is geweigerd. Controleer of " +
                "AZURE_CLIENT_ID naar de user-assigned managed identity van de app wijst en of " +
                "die identiteit aan de app is gekoppeld. Op deze accounts staat local auth uit, " +
                "dus er is geen sleutel om op terug te vallen.",
                exception);
        }

        _verified[key] = true;
        return container;
    }
}

/// <summary>
/// De telemetrie-opslag van een klant is niet (goed) ingericht of niet bereikbaar.
/// </summary>
/// <remarks>
/// Een eigen type zodat een scherm dit geval kan onderscheiden van "de query ging mis" en er de
/// eerlijke tekst bij kan zetten in plaats van een lege lijst te tonen. Een leeg scherm zou hier
/// een leugen zijn: er zijn geen nul agents, we kunnen ze alleen niet zien.
/// </remarks>
public sealed class TelemetryNotProvisionedException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

/// <summary>
/// De portaaleigen opslag is niet (goed) ingericht, of het schrijfrecht ontbreekt.
/// </summary>
/// <remarks>
/// <para>Een eigen type naast <see cref="TelemetryNotProvisionedException"/>, want het is een ander
/// gebrek met een ander antwoord. Telemetrie die hapert is één klant die grijs wordt; de
/// portaalopslag die hapert raakt het contract-, toegangs- en klantbeheer van iedereen, en de
/// oorzaak is bijna altijd de roltoewijzing die nog niet is goedgekeurd.</para>
///
/// <para>Dit werpt, waar een verouderde etag of een ongeldig e-mailadres een
/// <see cref="PortalWriteResult{T}"/> oplevert. Het onderscheid is opzet: dit is een
/// inrichtingsfout en hoort luidruchtig te zijn, en een formulier dat "opslaan is mislukt" zegt
/// terwijl de identiteit geen schrijfrecht heeft, laat iemand een uur naar zijn invoer kijken.</para>
/// </remarks>
public sealed class PortalDataNotProvisionedException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
