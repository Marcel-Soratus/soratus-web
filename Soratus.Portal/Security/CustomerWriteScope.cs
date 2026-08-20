namespace Soratus.Portal.Security;

/// <summary>
/// Het bewijs dat de huidige gebruiker de portaalgegevens van één specifieke klant mag wijzigen.
/// </summary>
/// <remarks>
/// <para>De tegenhanger van <see cref="OperatorCustomerScope"/> aan de schrijfkant: operator zijn
/// én naar één klant kijken. Elke schrijfmethode op één klant neemt dit type, en geen enkele neemt
/// een losse <c>string customerId</c>. Dat is dezelfde regel die de leeskant al volgt, en om
/// dezelfde reden: met een string erbij is de vraag "mag deze gebruiker bij deze klant" weer iets
/// dat de aanroeper hoort te stellen, en dan kan hij het vergeten.</para>
///
/// <para><strong>Dit type draagt geen <see cref="CustomerScope"/>, en dat is een bewuste
/// afwijking.</strong> Een <see cref="CustomerScope"/> bestaat alleen voor een klant met een
/// ingerichte telemetrie-opslag — zonder opslag valt er niets te lezen en is de scope dus onwaar.
/// Maar juist de klant die nog géén opslag heeft is degene wiens contract je invult: dat is de
/// klant in onboarding. Zou het schrijfrecht op een leesrecht leunen, dan was het contract van een
/// nieuwe klant niet vast te leggen tot zijn Azure-omgeving stond, en dat is precies de omgekeerde
/// volgorde van hoe onboarding gaat.</para>
///
/// <para>Constructor <c>internal</c>, en alleen <see cref="CustomerScopeResolver"/> roept hem aan.
/// </para>
/// </remarks>
public sealed class CustomerWriteScope
{
    /// <summary>
    /// Alleen <see cref="CustomerScopeResolver"/> mag scopes maken.
    /// </summary>
    internal CustomerWriteScope(PortalWriteScope portal, string customerId, string displayName)
    {
        Portal = portal;
        CustomerId = customerId;
        DisplayName = displayName;
    }

    /// <summary>Het schrijfrecht waar dit uit volgt.</summary>
    public PortalWriteScope Portal { get; }

    /// <summary>
    /// De klantslug. Dit is de partitiesleutel waarin geschreven wordt, en daarmee de grens.
    /// </summary>
    public string CustomerId { get; }

    /// <summary>De klantnaam, voor meldingen op het scherm.</summary>
    public string DisplayName { get; }

    /// <summary>Wie de wijziging op zijn naam krijgt. Zie <see cref="PortalWriteScope.Actor"/>.</summary>
    public string Actor => Portal.Actor;
}
