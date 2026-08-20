using System.Security.Claims;
using Microsoft.Identity.Web;

namespace Soratus.Portal.Security;

/// <summary>
/// Beoordeelt of een gebruiker een scope mag krijgen, en maakt hem dan.
/// </summary>
/// <remarks>
/// Dit is de enige klasse in het portaal die de constructors van <see cref="CustomerScope"/>,
/// <see cref="OperatorScope"/> en <see cref="OperatorCustomerScope"/> aanroept. Voeg die aanroep
/// nergens anders toe: zodra een tweede plek een scope kan maken, is het bewijs dat het type
/// levert niet meer waard dan de zorgvuldigheid van die tweede plek.
///
/// Een klant zonder ingerichte opslag levert geen scope op. Er valt dan niets te lezen, en een
/// scope zonder verbinding zou een bewijs zijn van iets dat niet kan. Dat de klant desondanks
/// zichtbaar blijft op het overzicht regelt de weergave, uit <see cref="ICustomerDirectory"/>.
///
/// De methoden zijn <c>async</c> zonder iets te awaiten. Dat is bewust: vanaf fase 2 komt de
/// toegangslijst uit een beheerd model in plaats van uit configuratie, en dan wordt dit echt I/O.
/// De aanroepers hoeven daar dan niet voor te veranderen.
/// </remarks>
internal sealed class CustomerScopeResolver(ICustomerDirectory directory) : ICustomerScopeResolver
{
    /// <inheritdoc />
    public Task<CustomerScope?> ResolveAsync(
        ClaimsPrincipal? user,
        string? customerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSignedIn(user))
        {
            return Task.FromResult<CustomerScope?>(null);
        }

        var record = directory.Find(customerId);
        if (record is null)
        {
            // Bestaat niet en mag niet zijn hier hetzelfde antwoord. Zie de opmerking bij
            // ICustomerScopeResolver: het onderscheid verklappen is zelf een lek.
            return Task.FromResult<CustomerScope?>(null);
        }

        // Een operator mag bij elke klant. Dat is de rol.
        if (user!.IsInRole(PortalRoles.Operator))
        {
            return Task.FromResult(ToScope(record));
        }

        // Een klantgebruiker mag alleen bij de klanten waar hij op staat. De rolcontrole staat
        // erbij omdat een token zonder een van beide rollen niets hoort te kunnen; met
        // appRoleAssignmentRequired zou dat niet mogen voorkomen, maar een autorisatiepad dat op
        // "kan niet voorkomen" leunt is er geen.
        if (!user.IsInRole(PortalRoles.Customer))
        {
            return Task.FromResult<CustomerScope?>(null);
        }

        var allowed = directory.ForUser(user).Any(c =>
            string.Equals(c.Id, record.Id, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(allowed ? ToScope(record) : null);
    }

    /// <inheritdoc />
    public Task<OperatorScope?> ResolveOperatorAsync(
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSignedIn(user) || !user!.IsInRole(PortalRoles.Operator))
        {
            return Task.FromResult<OperatorScope?>(null);
        }

        var subject = user.GetObjectId()
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.Identity!.Name
            ?? string.Empty;

        // Een operator mag bij elke klant, dus krijgt hij een leesrecht per klant mee. Dat is wat
        // het overzicht nodig heeft om over de klantopslagen heen te kunnen fan-outen zonder dat
        // er buiten deze klasse een scope ontstaat.
        IReadOnlyList<CustomerScope> customers =
        [
            .. directory.All.Select(ToScope).OfType<CustomerScope>()
        ];

        return Task.FromResult<OperatorScope?>(new OperatorScope(subject, user.Identity!.Name, customers));
    }

    /// <inheritdoc />
    public async Task<OperatorCustomerScope?> ResolveOperatorAsync(
        ClaimsPrincipal? user,
        string? customerId,
        CancellationToken cancellationToken = default)
    {
        var operatorScope = await ResolveOperatorAsync(user, cancellationToken).ConfigureAwait(false);
        if (operatorScope is null)
        {
            return null;
        }

        var record = directory.Find(customerId);
        if (record is null || ToScope(record) is not { } customer)
        {
            return null;
        }

        return new OperatorCustomerScope(operatorScope, customer, record.EnvironmentDetail);
    }

    /// <inheritdoc />
    public async Task<PortalWriteScope?> ResolveWriteAsync(
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        // Het schrijfrecht volgt uit de operatorrol en uit niets anders. Er is bewust geen tweede
        // voorwaarde: zou schrijven ook nog van een instelling of een vlag afhangen, dan is de
        // rolmatrix niet meer de plek waar staat wie wat mag.
        var operatorScope = await ResolveOperatorAsync(user, cancellationToken).ConfigureAwait(false);

        return operatorScope is null ? null : new PortalWriteScope(operatorScope);
    }

    /// <inheritdoc />
    public async Task<CustomerWriteScope?> ResolveWriteAsync(
        ClaimsPrincipal? user,
        string? customerId,
        CancellationToken cancellationToken = default)
    {
        var write = await ResolveWriteAsync(user, cancellationToken).ConfigureAwait(false);
        if (write is null)
        {
            return null;
        }

        // Wel de klant opzoeken, en niet de slug uit de URL vertrouwen: anders legt een getypte
        // slug een klant aan de partitiesleutel vast die niemand ooit heeft ingericht, en staat er
        // een contract in een partitie die geen klant is.
        var record = directory.Find(customerId);
        if (record is null)
        {
            return null;
        }

        // Let op wat er hier níet staat: een controle op record.Telemetry. Een klant zonder
        // ingerichte opslag levert geen CustomerScope op — er valt niets te lezen — maar zijn
        // contract en zijn toegangen zijn er wel. Dat is precies de klant in onboarding.
        return new CustomerWriteScope(write, record.Id, record.Name);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CustomerScope>> ResolveOwnAsync(
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSignedIn(user) || !user!.IsInRole(PortalRoles.Customer))
        {
            return Task.FromResult<IReadOnlyList<CustomerScope>>([]);
        }

        IReadOnlyList<CustomerScope> scopes =
        [
            .. directory.ForUser(user).Select(ToScope).OfType<CustomerScope>()
        ];

        return Task.FromResult(scopes);
    }

    private static bool IsSignedIn(ClaimsPrincipal? user) => user?.Identity?.IsAuthenticated == true;

    /// <summary>
    /// Maakt de scope, of <c>null</c> als er geen opslag voor deze klant is ingericht.
    /// </summary>
    private static CustomerScope? ToScope(CustomerRecord record) =>
        record.Telemetry is null
            ? null
            : new CustomerScope(
                record.Id,
                record.Name,
                record.Environment,
                record.IsInternal,
                record.Telemetry);
}
