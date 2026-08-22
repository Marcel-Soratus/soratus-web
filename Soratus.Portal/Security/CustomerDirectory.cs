using System.Security.Claims;
using Microsoft.Extensions.Options;
using Soratus.Portal.Data;
using Soratus.Portal.Platform;

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
    private readonly PlatformTelemetryOptions _platform;
    private Snapshot _snapshot;

    /// <summary>
    /// Bouwt de lijst uit de configuratie. Dat is de momentopname tot de opslag is gelezen.
    /// </summary>
    /// <param name="customers">De sectie <c>Portal</c> met de klantenlijst.</param>
    /// <param name="telemetry">De sectie <c>Telemetry</c>, voor de standaard opslaglocatie.</param>
    /// <param name="platform">
    /// De sectie <c>PlatformTelemetry</c>, voor de opslaglocatie van de interne beheerklant.
    /// </param>
    /// <remarks>
    /// Dat de klantenlijst uit Cosmos komt zit bewust niet in de constructor: deze klasse moet kunnen
    /// antwoorden voordat er ook maar iets is gelezen.
    /// </remarks>
    public CustomerDirectory(
        IOptions<PortalCustomerOptions> customers,
        IOptions<PortalTelemetryOptions> telemetry,
        IOptions<PlatformTelemetryOptions> platform)
    {
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(platform);

        _telemetryDefaults = telemetry.Value;
        _platform = platform.Value;

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

            customer.Telemetry = ResolveLocation(customer, _telemetryDefaults, _platform);

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
    /// <returns>
    /// <c>true</c> als de momentopname is vervangen, <c>false</c> als de lezing niets opleverde
    /// terwijl er nog een lijst stond. Zie de opmerkingen.
    /// </returns>
    /// <remarks>
    /// <para>Eén veldtoewijzing, dus geen enkele lezer ziet een halve lijst. Wie <see cref="All"/>
    /// leest terwijl deze methode loopt, krijgt de oude of de nieuwe momentopname en nooit een
    /// mengeling.</para>
    ///
    /// <para><strong>Een lezing die niets oplevert vervangt niets.</strong> Nul klanten is niet
    /// hetzelfde als een lijst zonder klanten: het is wat je krijgt bij een verse container waarin
    /// de migratie nog niet heeft gelopen, of niet kón lopen omdat het schrijfrecht nog niet stond.
    /// Zou de lege uitkomst de lijst vervangen, dan kent het portaal daarna niemand meer — ook niet
    /// de operator die het zou moeten repareren — en dat is precies de toestand die de terugval op de
    /// configuratielijst hoort te voorkomen. De asymmetrie is bewust: van <em>n</em> naar nul is geen
    /// beheeractie die dit portaal kent (een klant verwijderen bestaat niet), terwijl een lege lezing
    /// een alledaagse inrichtingstoestand is. Van <em>n</em> naar <em>m</em> gaat gewoon door, ook
    /// omlaag.</para>
    ///
    /// <para><c>internal</c> en niet op <see cref="ICustomerDirectory"/>: die interface is publiek,
    /// en een publieke methode waarmee je de autorisatiebron kunt vervangen is een achterdeur.
    /// </para>
    /// </remarks>
    internal bool Replace(
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

            record.Telemetry = ResolveLocation(record, _telemetryDefaults, _platform);

            records.Add(record);
        }

        if (records.Count == 0 && _snapshot.All.Count > 0)
        {
            return false;
        }

        _snapshot = Snapshot.From(records, fromStore: true);
        return true;
    }

    /// <summary>
    /// Waar de telemetrie van deze klant staat: zijn eigen endpoint, of anders de standaard.
    /// </summary>
    /// <param name="record">De klant.</param>
    /// <param name="defaults">De sectie <c>Telemetry</c>.</param>
    /// <param name="platform">De sectie <c>PlatformTelemetry</c>.</param>
    /// <returns>De locatie, of <c>null</c> als er geen endpoint bekend is.</returns>
    /// <remarks>
    /// <para><strong>De interne beheerklant valt terug op de platformtelemetrie en niet op de
    /// standaard, en dat is de leeskant van fase 6.</strong> Het portaal publiceert zijn eigen
    /// beheeragents naar een eigen database — het heeft op de klanttelemetrie met opzet alleen
    /// leesrecht, zodat een gecompromitteerd portaal geen telemetrie van een klant kan verzinnen. Wie
    /// die agents wil zien moet dus in die database kijken, en de interne klant ís het platform.</para>
    ///
    /// <para>Dat die terugval hier staat en niet als waarde op het klantdocument, is opzet: dan
    /// bestaat de toestand "het portaal publiceert netjes in de ene database en het scherm kijkt in de
    /// andere", en die toestand levert geen fout op maar een leeg overzicht. Eén sectie voedt beide
    /// kanten. Zie <see cref="PlatformTelemetryOptions"/>.</para>
    ///
    /// <para>Een waarde die uitdrukkelijk op de klant zelf staat wint nog steeds — ook bij de interne
    /// klant. Zodra het platform een eigen account krijgt, is dat de plek waar dat komt te staan, en
    /// dan hoort de configuratiesectie niet stil te overrulen wat iemand heeft vastgelegd.</para>
    ///
    /// <para><strong>Wat dit kost, en het is zichtbaar:</strong> wat er vandaag in <c>telemetry</c>
    /// onder klant <c>soratus</c> staat — de vijf geseede beheeragents en de echte registratie van
    /// <c>heartbeat-demo</c> — verdwijnt hiermee van <c>/klant/soratus/agents</c>. Dat is de bedoeling
    /// voor de eerste vijf (dat is demodata die anders door de échte registraties zou worden
    /// overschreven: zelfde id, zelfde partitiesleutel) en het is een echt gemis voor
    /// <c>heartbeat-demo</c>. Die agent hoort in de nieuwe database thuis — hij is van ons en niet van
    /// een klant — en dat is één regel in zijn eigen configuratie
    /// (<c>SORATUS_TELEMETRY__DATABASE</c>).</para>
    /// </remarks>
    private static TelemetryLocation? ResolveLocation(
        CustomerRecord record,
        PortalTelemetryOptions defaults,
        PlatformTelemetryOptions platform)
    {
        // IsConfigured en niet alleen IsInternal, en dat sluit de tussenstand af. De sleutel
        // PlatformTelemetry__AccountEndpoint komt uit dezelfde uitrol die de database aanmaakt, dus
        // zolang die er niet is bestaat de database ook niet — en dan zou de interne klant naar een
        // database wijzen die 404 geeft en als "status onbekend" op het overzicht komen. Eén schakelaar
        // voor de leeskant en de schrijfkant: is de platformtelemetrie ingericht, dan schrijft het
        // portaal er zijn agents heen en leest de interne klant ze daar; is hij dat niet, dan verandert
        // er niets ten opzichte van vóór fase 6.
        var useplatform = record.IsInternal && platform.IsConfigured;
        var internalEndpoint = useplatform ? platform.AccountEndpoint : null;
        var internalDatabase = useplatform ? platform.Database : null;

        var resolvedEndpoint = Coalesce(record.TelemetryEndpoint, internalEndpoint, defaults.AccountEndpoint);
        var resolvedDatabase = Coalesce(record.TelemetryDatabase, internalDatabase, defaults.Database);

        return resolvedEndpoint is null || resolvedDatabase is null
            ? null
            : new TelemetryLocation(resolvedEndpoint, resolvedDatabase);
    }

    /// <summary>De eerste waarde die er is, of <c>null</c>.</summary>
    /// <param name="candidates">De kandidaten, in volgorde van voorkeur.</param>
    /// <returns>De eerste niet-lege waarde, getrimd.</returns>
    private static string? Coalesce(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return null;
    }

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
