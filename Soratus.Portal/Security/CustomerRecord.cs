using Soratus.Portal.Data;

namespace Soratus.Portal.Security;

/// <summary>
/// Wat het portaal over één klant weet buiten de telemetrie om: zijn naam, zijn omgeving, waar zijn
/// opslag staat en wie er namens hem mag inloggen.
/// </summary>
/// <remarks>
/// <para><strong>Dit is de vorm waarin de klantenlijst in het geheugen staat, en niet de vorm waarin
/// hij wordt opgeslagen.</strong> Opgeslagen wordt <see cref="CustomerDocument"/> in de container
/// <c>customers</c>; <see cref="CustomerDirectory"/> zet die documenten om naar dit type. Dat de twee
/// gescheiden zijn, is bewust: hier hangt de uitgerekende <see cref="Telemetry"/> aan, daar hangt de
/// <c>_etag</c> aan, en een type dat beide draagt zou een opslagdetail tot in de autorisatie
/// meenemen.</para>
///
/// <para>De vorm volgt §6 van de spec (<c>Customer</c> en <c>Access</c>). Tot fase 1 kwam dit uit
/// configuratie; die sectie is nu de terugval en de inhoud van de eenmalige migratie — zie
/// <see cref="PortalCustomerOptions"/>.</para>
///
/// <para>Deze registratie is meer dan presentatie: hij bepaalt <em>waar</em> de gegevens van een
/// klant staan. Zodra elke klant zijn eigen Cosmos-account heeft, staat het verschil tussen twee
/// klanten hier en nergens anders.</para>
///
/// <para>Er staat geen geheim in. Een endpoint, een resource-groepnaam en een e-mailadres zijn geen
/// credentials; op de accounts staat local auth uit, dus er is geen sleutel om per ongeluk naast te
/// zetten.</para>
/// </remarks>
public sealed class CustomerRecord
{
    /// <summary>
    /// De slug waarmee deze klant overal wordt aangeduid, bijvoorbeeld <c>acme-logistiek</c>.
    /// Gelijk aan <c>customerId</c> in de telemetriedocumenten en aan het pad in de URL.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>De naam zoals hij op het scherm hoort te staan.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Of dit de interne klant "Soratus — intern beheer" is. Die verschijnt gewoon in het overzicht
    /// (§4), maar loopt op een beheercontract en wordt niet gefactureerd.
    /// </summary>
    public bool IsInternal { get; set; }

    /// <summary>
    /// Korte omgevingsaanduiding voor de kop van de klantweergave, bijvoorbeeld
    /// <c>West-Europa</c>. Dit is het enige omgevingsveld dat een klant te zien krijgt.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// De volledige omgeving, bijvoorbeeld <c>sub-soratus-acme · rg-acme-prod</c>.
    /// </summary>
    /// <remarks>
    /// Operator-only. Dit veld is de reden dat <see cref="CustomerScope"/> het niet draagt en
    /// <see cref="OperatorCustomerScope"/> wel: een klantpagina kan het dan niet renderen, ook niet
    /// per ongeluk.
    /// </remarks>
    public string? EnvironmentDetail { get; set; }

    /// <summary>
    /// De Cosmos-endpoint van déze klant, of leeg om terug te vallen op
    /// <c>Telemetry:AccountEndpoint</c>.
    /// </summary>
    /// <remarks>
    /// In fase 0 is dit leeg voor iedereen en wijst alles naar het gedeelde account. Zodra een
    /// klant zijn eigen account krijgt, komt hier zijn endpoint te staan en verandert er verder
    /// niets.
    /// </remarks>
    public string? TelemetryEndpoint { get; set; }

    /// <summary>
    /// De databasenaam bij <see cref="TelemetryEndpoint"/>, of leeg voor <c>Telemetry:Database</c>.
    /// </summary>
    public string? TelemetryDatabase { get; set; }

    /// <summary>Wie er namens deze klant mag inloggen.</summary>
    public IList<CustomerAccessRecord> Access { get; set; } = [];

    /// <summary>
    /// De uitgerekende opslaglocatie, of <c>null</c> als er geen endpoint bekend is.
    /// </summary>
    /// <remarks>
    /// Wordt door <see cref="CustomerDirectory"/> gezet en niet uit configuratie
    /// gebonden. <c>null</c> betekent: deze klant staat in de registratie maar zijn opslag is niet
    /// ingericht. Hij verdwijnt daarmee niet van het overzicht — hij komt erop als "status
    /// onbekend" — maar er valt niets voor hem te lezen.
    /// </remarks>
    internal TelemetryLocation? Telemetry { get; set; }
}

/// <summary>
/// Eén portaaltoegang van een klant: een e-mailadres met een naam en een rol.
/// </summary>
/// <remarks>
/// De rol hier is de rol <em>binnen</em> de klant (Beheerder klant / Lezer, §3.5) en niet de
/// app-rol uit Entra. In fase 0 doet die rol nog niets — de klantweergave is voor iedereen
/// read-only — maar het veld staat er zodat fase 2 hem kan gaan gebruiken zonder de vorm te
/// veranderen.
/// </remarks>
public sealed class CustomerAccessRecord
{
    /// <summary>Het e-mailadres waarmee deze persoon in Entra bekend is.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>De naam, voor het toegangsoverzicht.</summary>
    public string? Name { get; set; }

    /// <summary>De rol binnen de klant: <c>Beheerder</c> of <c>Lezer</c>.</summary>
    public string? Role { get; set; }
}
