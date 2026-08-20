using System.Security.Claims;
using Microsoft.Extensions.Options;
using Soratus.Portal.Data;

namespace Soratus.Portal.Security;

/// <summary>
/// De klantenlijst waar het portaal zijn autorisatie uit haalt: één momentopname in het geheugen.
/// </summary>
/// <remarks>
/// <para><strong>Waarom dit synchroon is en in het geheugen staat.</strong> Deze lijst wordt bij
/// elke pagina en elke autorisatievraag geraadpleegd — vaak meer dan eens per verzoek. Zou elke
/// vraag een query worden, dan hangt het aanmelden van een klant aan de bereikbaarheid van Cosmos
/// en betaalt elk verzoek een leesactie voor gegevens die per week een keer veranderen.
/// <see cref="ICustomerDirectory"/> blijft daarom synchroon; wat verandert is waar de momentopname
/// vandaan komt.</para>
///
/// <para><strong>De omschakeling van fase 0 naar fase 2 zit in deze klasse.</strong> Bij het
/// opstarten staat hier de lijst uit <c>Portal:Customers</c>: het portaal kent zijn zeven klanten
/// dus vóórdat er ook maar één query is gelopen. Zodra <see cref="Data.PortalDirectoryRefresh"/> de
/// opslag heeft gelezen, wordt die momentopname in één keer vervángen. Er is daarmee geen moment
/// waarop het portaal geen klanten kent — niet bij een koude start, niet als Cosmos traag is, en
/// niet als de opslag onbereikbaar is. Dat laatste levert een verouderde lijst op met een
/// waarschuwing in de log, en dat is beter dan een portaal dat niemand meer binnenlaat.</para>
///
/// <para><strong>Vervangen en niet samenvoegen.</strong> Zodra de opslag is gelezen is die de
/// waarheid; de configuratielijst doet dan niets meer. Zouden de twee worden samengevoegd, dan komt
/// een klant die iemand bewust heeft verwijderd bij elke herstart terug, en zou de configuratie een
/// stille tweede bron blijven — precies wat fase 2 weghaalt.</para>
///
/// <para>Klanten met een lege <c>Id</c> worden overgeslagen: één verkeerd geplakte regel mag het
/// portaal niet omleggen. Een klant zónder bruikbare endpoint blijft wél in de lijst staan, met
/// <see cref="CustomerRecord.Telemetry"/> op <c>null</c>. Hij is dan niet te lezen maar niet
/// verdwenen — het overzicht toont hem als "status onbekend", en dat is eerlijker dan een klant die
/// stilletjes wegvalt.</para>
/// </remarks>
internal sealed class CustomerDirectory : ICustomerDirectory
{
    private readonly PortalTelemetryOptions _telemetryDefaults;
    private Snapshot _snapshot;

    /// <summary>
    /// Bouwt de lijst uit de configuratie. Dat is de momentopname tot de opslag is gelezen.
    /// </summary>
    /// <param name="customers">De sectie <c>Portal</c> met de klantenlijst.</param>
    /// <param name="telemetry">De sectie <c>Telemetry</c>, voor de standaard opslaglocatie.</param>
    /// <remarks>
    /// De constructorvorm is gelijk aan die van zijn voorganger, zodat de omschakeling in het
    /// testproject één typenaam is en geen verbouwing. Dat de klantenlijst uit Cosmos komt zit
    /// bewust niet in de constructor: deze klasse moet kunnen antwoorden voordat er ook maar iets
    /// is gelezen.
    /// </remarks>
    public CustomerDirectory(
        IOptions<PortalCustomerOptions> customers,
        IOptions<PortalTelemetryOptions> telemetry)
    {
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(telemetry);

        _telemetryDefaults = telemetry.Value;

        var configured = new List<CustomerRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var customer in customers.Value.Customers)
        {
            if (string.IsNullOrWhiteSpace(customer.Id) || !seen.Add(customer.Id))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(customer.Name))
            {
                customer.Name = customer.Id;
            }

            customer.Telemetry = ResolveLocation(
                customer.TelemetryEndpoint,
                customer.TelemetryDatabase,
                _telemetryDefaults);

            configured.Add(customer);
        }

        Configured = configured;
        _snapshot = Snapshot.From(configured, fromStore: false);
    }

    /// <summary>
    /// De klanten uit de configuratie, in de vorm waarin ze naar de opslag mogen worden gemigreerd.
    /// </summary>
    /// <remarks>
    /// Alleen voor de eenmalige migratie in <see cref="Data.PortalDirectoryRefresh"/>. Dit is niet
    /// de lijst waar het portaal mee werkt; dat is <see cref="All"/>.
    /// </remarks>
    internal IReadOnlyList<CustomerRecord> Configured { get; }

    /// <summary>
    /// Of de huidige momentopname uit de opslag komt. <c>false</c> betekent: nog de
    /// configuratielijst.
    /// </summary>
    internal bool LoadedFromStore => _snapshot.FromStore;

    /// <inheritdoc />
    public IReadOnlyList<CustomerRecord> All => _snapshot.All;

    /// <inheritdoc />
    public CustomerRecord? Find(string? customerId) =>
        string.IsNullOrWhiteSpace(customerId) ? null : _snapshot.BySlug.GetValueOrDefault(customerId);

    /// <inheritdoc />
    public IReadOnlyList<CustomerRecord> ForUser(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return [];
        }

        // Eén momentopname voor de hele vraag. Zou hij tussen twee opzoekingen worden vervangen,
        // dan kan dezelfde vraag twee antwoorden mengen.
        var snapshot = _snapshot;
        var matches = new List<CustomerRecord>();

        foreach (var address in EmailAddresses(user))
        {
            if (!snapshot.ByEmail.TryGetValue(address, out var records))
            {
                continue;
            }

            foreach (var record in records)
            {
                if (!matches.Contains(record))
                {
                    matches.Add(record);
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Vervangt de momentopname door wat er in de opslag staat.
    /// </summary>
    /// <param name="customers">De klantdocumenten.</param>
    /// <param name="access">Alle toegangsdocumenten, van alle klanten.</param>
    /// <remarks>
    /// <para>Eén veldtoewijzing, dus geen enkele lezer ziet een halve lijst. Wie <see cref="All"/>
    /// leest terwijl deze methode loopt, krijgt de oude of de nieuwe momentopname en nooit een
    /// mengeling.</para>
    ///
    /// <para><c>internal</c> en niet op <see cref="ICustomerDirectory"/>: die interface is publiek,
    /// en een publieke methode waarmee je de autorisatiebron kunt vervangen is een achterdeur.
    /// </para>
    /// </remarks>
    internal void Replace(
        IReadOnlyList<CustomerDocument> customers,
        IReadOnlyList<AccessDocument> access)
    {
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(access);

        var byCustomer = access
            .Where(document => !string.IsNullOrWhiteSpace(document.Email))
            .GroupBy(document => document.CustomerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var records = new List<CustomerRecord>(customers.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in customers)
        {
            if (string.IsNullOrWhiteSpace(document.CustomerId) || !seen.Add(document.CustomerId))
            {
                continue;
            }

            var record = new CustomerRecord
            {
                Id = document.CustomerId,
                Name = string.IsNullOrWhiteSpace(document.Name) ? document.CustomerId : document.Name,
                IsInternal = document.IsInternal,
                Environment = document.Environment,
                EnvironmentDetail = document.EnvironmentDetail,
                TelemetryEndpoint = document.TelemetryEndpoint,
                TelemetryDatabase = document.TelemetryDatabase,
                Access =
                [
                    .. (byCustomer.GetValueOrDefault(document.CustomerId) ?? [])
                        .Select(entry => new CustomerAccessRecord
                        {
                            Email = entry.Email,
                            Name = entry.Name,
                            Role = entry.Role,
                        }),
                ],
            };

            record.Telemetry = ResolveLocation(
                document.TelemetryEndpoint,
                document.TelemetryDatabase,
                _telemetryDefaults);

            records.Add(record);
        }

        _snapshot = Snapshot.From(records, fromStore: true);
    }

    /// <summary>
    /// Waar de telemetrie van deze klant staat: zijn eigen endpoint, of anders de standaard.
    /// </summary>
    private static TelemetryLocation? ResolveLocation(
        string? endpoint,
        string? database,
        PortalTelemetryOptions defaults)
    {
        var resolvedEndpoint = Coalesce(endpoint, defaults.AccountEndpoint);
        var resolvedDatabase = Coalesce(database, defaults.Database);

        return resolvedEndpoint is null || resolvedDatabase is null
            ? null
            : new TelemetryLocation(resolvedEndpoint, resolvedDatabase);
    }

    private static string? Coalesce(string? preferred, string? fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred.Trim()
        : !string.IsNullOrWhiteSpace(fallback) ? fallback.Trim()
        : null;

    /// <summary>
    /// De e-mailadressen die in het token kunnen staan.
    /// </summary>
    /// <remarks>
    /// Entra levert het adres afhankelijk van het accounttype in een ander claim. Voor een
    /// werkaccount staat het in <c>preferred_username</c>, voor een gastaccount vaak in
    /// <c>email</c>, en <c>upn</c> komt er ook voor. We kijken naar alle drie in plaats van er
    /// eentje te kiezen en dan te ontdekken dat een gast niet binnenkomt.
    /// </remarks>
    private static IEnumerable<string> EmailAddresses(ClaimsPrincipal user)
    {
        string[] claimTypes = ["preferred_username", ClaimTypes.Email, "email", ClaimTypes.Upn, "upn"];

        foreach (var claimType in claimTypes)
        {
            foreach (var claim in user.FindAll(claimType))
            {
                if (!string.IsNullOrWhiteSpace(claim.Value))
                {
                    yield return claim.Value.Trim();
                }
            }
        }
    }

    /// <summary>
    /// Eén onveranderlijke momentopname: de lijst en de twee opzoektabellen erop.
    /// </summary>
    /// <remarks>
    /// De drie horen bij elkaar en worden daarom samen vervangen. Zouden ze drie velden zijn, dan
    /// bestaat er een moment waarop de lijst nieuw is en de opzoektabel oud — en dan geeft
    /// <see cref="Find"/> een klant die niet in <see cref="All"/> staat.
    /// </remarks>
    private sealed class Snapshot
    {
        private Snapshot(
            IReadOnlyList<CustomerRecord> all,
            Dictionary<string, CustomerRecord> bySlug,
            Dictionary<string, List<CustomerRecord>> byEmail,
            bool fromStore)
        {
            All = all;
            BySlug = bySlug;
            ByEmail = byEmail;
            FromStore = fromStore;
        }

        public IReadOnlyList<CustomerRecord> All { get; }

        public Dictionary<string, CustomerRecord> BySlug { get; }

        public Dictionary<string, List<CustomerRecord>> ByEmail { get; }

        public bool FromStore { get; }

        public static Snapshot From(IReadOnlyList<CustomerRecord> records, bool fromStore)
        {
            var bySlug = new Dictionary<string, CustomerRecord>(StringComparer.OrdinalIgnoreCase);
            var byEmail = new Dictionary<string, List<CustomerRecord>>(StringComparer.OrdinalIgnoreCase);

            foreach (var record in records)
            {
                bySlug[record.Id] = record;

                foreach (var access in record.Access)
                {
                    if (string.IsNullOrWhiteSpace(access.Email))
                    {
                        continue;
                    }

                    if (!byEmail.TryGetValue(access.Email, out var list))
                    {
                        list = [];
                        byEmail[access.Email] = list;
                    }

                    list.Add(record);
                }
            }

            return new Snapshot(records, bySlug, byEmail, fromStore);
        }
    }
}
