using System.Globalization;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Views;

/// <summary>
/// Bouwt de viewmodels van het contractscherm op uit de portaaleigen opslag.
/// </summary>
/// <remarks>
/// <para>De klantvelden komen uit het klantdocument als dat er is, en anders uit de klantenlijst.
/// Die terugval is er voor de klant die nog niet is gemigreerd: het contractscherm hoort dan gewoon
/// te werken en te zeggen dat er nog geen document is, in plaats van een lege kaart te tonen of te
/// werpen.</para>
///
/// <para>De klok komt uit <see cref="TimeProvider"/> en wordt één keer per opbouw uitgelezen,
/// dezelfde afspraak als in <see cref="PortalViews"/>.</para>
/// </remarks>
internal sealed class ContractViews(
    IPortalDataStore store,
    ICustomerDirectory directory,
    TimeProvider timeProvider,
    ILogger<ContractViews> logger) : IContractViews
{
    /// <inheritdoc />
    public async Task<CustomerContractView> BuildContractAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var contract = await store.GetContractAsync(scope, cancellationToken).ConfigureAwait(false);
        var access = await store.GetAccessAsync(scope, cancellationToken).ConfigureAwait(false);

        return new CustomerContractView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            GeneratedAt = timeProvider.GetUtcNow(),
            IsInternal = scope.IsInternal,
            Environment = scope.Environment,
            HasContract = contract is not null,
            Number = contract?.Number,
            Type = contract?.Type,
            StartsOn = StartsOn(contract),
            Term = contract?.Term,
            NoticePeriod = contract?.NoticePeriod,
            Sla = contract?.Sla,
            BundledHours = contract?.BundledHours ?? 0m,
            HourlyRate = contract?.HourlyRate ?? 0m,
            Indexation = contract?.Indexation,
            Contact = contract?.Contact,
            ManagedBy = contract?.ManagedBy,
            Access =
            [
                .. access.Select(entry => new CustomerAccessRow
                {
                    Email = entry.Email,
                    Name = entry.Name,
                    Role = entry.Role,

                    // Onbekend, en niet "niet uitgenodigd". Het portaal heeft geen leesrecht op
                    // Entra; zodra dat er is, komt hier een lezing te staan en geen veld uit het
                    // document.
                    EntraState = AccessEntraState.Unknown,
                }),
            ],
            ReadOnlyNotice = ContractNotice.ReadOnly,
            AccessStateNotice = ContractNotice.EntraStateUnknown,
        };
    }

    /// <inheritdoc />
    public async Task<OperatorContractView> BuildContractAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var customer = await store.GetCustomerAsync(scope, cancellationToken).ConfigureAwait(false);
        var contract = await store.GetContractAsync(scope, cancellationToken).ConfigureAwait(false);
        var access = await store.GetAccessAsync(scope, cancellationToken).ConfigureAwait(false);

        // De klantenlijst als terugval, niet als tweede bron: hij wordt alleen gelezen als er geen
        // document is, en dan staat er ook op het scherm dat dat zo is.
        var record = customer is null ? directory.Find(scope.CustomerId) : null;

        return new OperatorContractView
        {
            CustomerId = scope.CustomerId,
            DisplayName = customer?.Name ?? scope.DisplayName,
            GeneratedAt = timeProvider.GetUtcNow(),
            IsInternal = customer?.IsInternal ?? record?.IsInternal ?? false,
            HasContract = contract is not null,
            Number = contract?.Number,
            Type = contract?.Type,
            StartsOn = StartsOn(contract),
            Term = contract?.Term,
            NoticePeriod = contract?.NoticePeriod,
            Sla = contract?.Sla,
            BundledHours = contract?.BundledHours ?? 0m,
            HourlyRate = contract?.HourlyRate ?? 0m,
            Indexation = contract?.Indexation,
            Contact = contract?.Contact,
            ManagedBy = contract?.ManagedBy,
            AzureSurchargePercentage = contract?.AzureSurchargePercentage ?? 0m,
            ChangedAt = contract?.ChangedAt,
            ChangedBy = contract?.ChangedBy,
            ContractETag = contract?.ETag,
            Environment = customer?.Environment ?? record?.Environment,
            EnvironmentDetail = customer?.EnvironmentDetail ?? record?.EnvironmentDetail,
            CustomerETag = customer?.ETag,
            IsFromConfigurationOnly = customer is null,
            Access =
            [
                .. access.Select(entry => new OperatorAccessRow
                {
                    Email = entry.Email,
                    Name = entry.Name,
                    Role = entry.Role,
                    GrantedAt = entry.GrantedAt,
                    GrantedBy = entry.GrantedBy,
                    EntraState = AccessEntraState.Unknown,
                    ETag = entry.ETag,
                }),
            ],
            RoleNotice = ContractNotice.RolesAreReadOnly,
            AccessStateNotice = ContractNotice.EntraStateUnknown,
        };
    }

    /// <summary>
    /// De ingangsdatum als datum, of <c>null</c> als er niets of iets onleesbaars staat.
    /// </summary>
    /// <remarks>
    /// De schrijfkant laat alleen <c>yyyy-MM-dd</c> door, dus dit hoort niet voor te komen. Komt het
    /// toch voor — een met de hand aangepast document, of een vorm van vóór deze regel — dan wordt
    /// het gelogd in plaats van stil weggelaten. Een datum die van het scherm verdwijnt zonder dat
    /// iemand het merkt is erger dan een lege cel met een regel in de log.
    /// </remarks>
    private DateOnly? StartsOn(ContractDocument? contract)
    {
        if (string.IsNullOrWhiteSpace(contract?.StartsOn))
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                contract.StartsOn.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        logger.LogWarning(
            "De ingangsdatum '{StartsOn}' van het contract van klant {CustomerId} is niet " +
            "jjjj-mm-dd en wordt niet getoond.",
            contract.StartsOn,
            contract.CustomerId);

        return null;
    }
}
