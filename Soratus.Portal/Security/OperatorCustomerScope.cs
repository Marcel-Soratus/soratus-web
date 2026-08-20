namespace Soratus.Portal.Security;

/// <summary>
/// Het bewijs dat de huidige gebruiker operator is <em>en</em> naar één specifieke klant kijkt.
/// </summary>
/// <remarks>
/// Nodig omdat de operator volgens §1 doorklikt naar "exact dezelfde klantweergave, met
/// beheerfuncties erbovenop". Die weergave toont meer dan de klantweergave — alle omgevingen in
/// plaats van alleen productie, en de volledige omgevingsaanduiding — en dat verschil hoort in het
/// typesysteem te zitten en niet in een <c>@if (isOperator)</c>.
///
/// Dit type erft niet van <see cref="CustomerScope"/>. Bewust: overerving zou betekenen dat elke
/// methode die een <see cref="CustomerScope"/> aanneemt stilzwijgend ook de operatorvariant
/// accepteert, en dan is niet meer aan de signatuur te zien wat een methode nodig heeft. In plaats
/// daarvan draagt hij de klantscope in <see cref="Customer"/> mee: wie een leesaanroep wil doen
/// geeft die door, en dat staat er dan ook.
/// </remarks>
public sealed class OperatorCustomerScope
{
    /// <summary>
    /// Alleen <see cref="CustomerScopeResolver"/> mag scopes maken.
    /// </summary>
    internal OperatorCustomerScope(
        OperatorScope operatorScope,
        CustomerScope customer,
        string? environmentDetail)
    {
        Operator = operatorScope;
        Customer = customer;
        EnvironmentDetail = environmentDetail;
    }

    /// <summary>Het bewijs van de operatorrol.</summary>
    public OperatorScope Operator { get; }

    /// <summary>
    /// Het leesrecht op deze klant. Geef dit door aan elke methode die een
    /// <see cref="CustomerScope"/> vraagt.
    /// </summary>
    public CustomerScope Customer { get; }

    /// <summary>De slug van de klant.</summary>
    public string CustomerId => Customer.CustomerId;

    /// <summary>De klantnaam.</summary>
    public string DisplayName => Customer.DisplayName;

    /// <summary>Korte omgevingsaanduiding.</summary>
    public string? Environment => Customer.Environment;

    /// <summary>
    /// De volledige omgeving, bijvoorbeeld <c>sub-soratus-acme · rg-acme-prod</c>. Operator-only;
    /// dit veld bestaat niet op <see cref="CustomerScope"/>.
    /// </summary>
    public string? EnvironmentDetail { get; }

    /// <summary>Of dit de interne beheerklant is.</summary>
    public bool IsInternal => Customer.IsInternal;
}
