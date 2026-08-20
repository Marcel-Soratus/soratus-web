using System.Security.Claims;
using Microsoft.Extensions.Options;
using Soratus.Portal.Data;

namespace Soratus.Portal.Security;

/// <summary>
/// De klantenlijst uit de configuratiesectie <c>Portal:Customers</c>.
/// </summary>
/// <remarks>
/// De configuratie wordt één keer bij het opstarten omgezet naar opzoektabellen en naar een
/// opslaglocatie per klant. Dat mag, omdat deze lijst in fase 0 niet verandert zonder herstart;
/// vanaf fase 2 komt er een implementatie die hem beheert.
///
/// Klanten met een lege <c>Id</c> worden overgeslagen: één verkeerd geplakte regel in de
/// app-instellingen mag het portaal niet omleggen. Een klant zónder bruikbare endpoint blijft wél
/// in de lijst staan, met <see cref="CustomerRecord.Telemetry"/> op <c>null</c>. Hij is dan niet te
/// lezen, maar hij is ook niet verdwenen — het overzicht toont hem als "status onbekend", en dat is
/// eerlijker dan een klant die stilletjes wegvalt.
/// </remarks>
internal sealed class ConfigurationCustomerDirectory : ICustomerDirectory
{
    private readonly IReadOnlyList<CustomerRecord> _all;
    private readonly Dictionary<string, CustomerRecord> _bySlug;
    private readonly Dictionary<string, List<CustomerRecord>> _byEmail;

    public ConfigurationCustomerDirectory(
        IOptions<PortalCustomerOptions> customers,
        IOptions<PortalTelemetryOptions> telemetry)
    {
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(telemetry);

        var defaults = telemetry.Value;

        _bySlug = new Dictionary<string, CustomerRecord>(StringComparer.OrdinalIgnoreCase);
        _byEmail = new Dictionary<string, List<CustomerRecord>>(StringComparer.OrdinalIgnoreCase);

        var all = new List<CustomerRecord>();

        foreach (var customer in customers.Value.Customers)
        {
            if (string.IsNullOrWhiteSpace(customer.Id) || !_bySlug.TryAdd(customer.Id, customer))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(customer.Name))
            {
                customer.Name = customer.Id;
            }

            customer.Telemetry = ResolveLocation(customer, defaults);
            all.Add(customer);

            foreach (var access in customer.Access)
            {
                if (string.IsNullOrWhiteSpace(access.Email))
                {
                    continue;
                }

                if (!_byEmail.TryGetValue(access.Email, out var list))
                {
                    list = [];
                    _byEmail[access.Email] = list;
                }

                list.Add(customer);
            }
        }

        _all = all;
    }

    /// <inheritdoc />
    public IReadOnlyList<CustomerRecord> All => _all;

    /// <inheritdoc />
    public CustomerRecord? Find(string? customerId) =>
        string.IsNullOrWhiteSpace(customerId) ? null : _bySlug.GetValueOrDefault(customerId);

    /// <inheritdoc />
    public IReadOnlyList<CustomerRecord> ForUser(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return [];
        }

        var matches = new List<CustomerRecord>();

        foreach (var address in EmailAddresses(user))
        {
            if (!_byEmail.TryGetValue(address, out var records))
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
    /// Waar de opslag van deze klant staat: zijn eigen endpoint, of anders de standaard.
    /// </summary>
    private static TelemetryLocation? ResolveLocation(
        CustomerRecord customer,
        PortalTelemetryOptions defaults)
    {
        var endpoint = Coalesce(customer.TelemetryEndpoint, defaults.AccountEndpoint);
        var database = Coalesce(customer.TelemetryDatabase, defaults.Database);

        return endpoint is null || database is null ? null : new TelemetryLocation(endpoint, database);
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
}

/// <summary>
/// De configuratiesectie <c>Portal</c>, met daarin de klantenlijst.
/// </summary>
/// <remarks>
/// Bevat geen geheimen: klantnamen, slugs, endpoints, resource-groepnamen en e-mailadressen. Een
/// client secret hoort hier niet en komt later uit Key Vault.
/// </remarks>
public sealed class PortalCustomerOptions
{
    /// <summary>De naam van de configuratiesectie.</summary>
    public const string SectionName = "Portal";

    /// <summary>De ingerichte klanten.</summary>
    public IList<CustomerRecord> Customers { get; set; } = [];
}
