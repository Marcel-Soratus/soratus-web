using System.Reflection;
using Microsoft.Extensions.Options;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Bouwt een echte <see cref="ICustomerScopeResolver"/> op een klantenlijst uit configuratie.
/// </summary>
/// <remarks>
/// <para><c>ConfigurationCustomerDirectory</c> en <c>CustomerScopeResolver</c> zijn
/// <c>internal</c>, en dat hoort zo: buiten het portaal bestaan ze niet. Het testproject bereikt
/// ze daarom via reflectie in plaats van via een <c>InternalsVisibleTo</c> in
/// <c>Soratus.Portal</c> — een testproject hoort de zichtbaarheidsgrenzen van de code die het test
/// niet op te rekken.</para>
///
/// <para>De omweg via de configuratieklantenlijst is bewust: <c>CustomerRecord.Telemetry</c> heeft
/// een <c>internal</c> setter, dus alleen die klasse kan een klant een opslaglocatie geven. Zo
/// testen we de echte keten — configuratie → klantenlijst → resolver — en niet een nagebouwde
/// versie ervan.</para>
/// </remarks>
internal static class Autorisatiebron
{
    /// <summary>De endpoint die de testklanten delen. In fase 0 deelt iedereen er één.</summary>
    public const string StandaardEndpoint = "https://cosmos-test.documents.azure.com:443/";

    private static readonly Assembly Portaal = typeof(CustomerScope).Assembly;

    /// <summary>
    /// Bouwt een resolver op een klantenlijst met een ingerichte opslag.
    /// </summary>
    /// <param name="klanten">De klanten, of leeg voor <see cref="Standaard"/>.</param>
    /// <returns>De resolver.</returns>
    public static ICustomerScopeResolver Resolver(params CustomerRecord[] klanten) =>
        Resolver(klanten.Length == 0 ? Standaard() : klanten, StandaardEndpoint);

    /// <summary>
    /// Bouwt een resolver op klanten zonder ingerichte opslag: geen endpoint, dus geen scope.
    /// </summary>
    /// <param name="klanten">De klanten.</param>
    /// <returns>De resolver.</returns>
    public static ICustomerScopeResolver ResolverZonderOpslag(params CustomerRecord[] klanten) =>
        Resolver(klanten, standaardEndpoint: null);

    /// <summary>
    /// Bouwt een resolver op een klantenlijst met een gekozen standaardendpoint.
    /// </summary>
    /// <param name="klanten">De klanten.</param>
    /// <param name="standaardEndpoint">De standaardendpoint, of <c>null</c> voor geen opslag.</param>
    /// <returns>De resolver.</returns>
    public static ICustomerScopeResolver Resolver(
        IEnumerable<CustomerRecord> klanten,
        string? standaardEndpoint)
    {
        var directory = Klantenlijst(klanten, standaardEndpoint);
        var type = Portaal.GetType("Soratus.Portal.Security.CustomerScopeResolver", throwOnError: true)!;

        return (ICustomerScopeResolver)Maak(type, directory);
    }

    /// <summary>
    /// Bouwt de echte klantenlijst uit configuratie, inclusief het uitrekenen van de opslag.
    /// </summary>
    /// <param name="klanten">De klanten.</param>
    /// <param name="standaardEndpoint">De standaardendpoint, of <c>null</c> voor geen opslag.</param>
    /// <returns>De klantenlijst.</returns>
    public static ICustomerDirectory Klantenlijst(
        IEnumerable<CustomerRecord> klanten,
        string? standaardEndpoint = StandaardEndpoint)
    {
        var opties = Options.Create(new PortalCustomerOptions { Customers = [.. klanten] });
        var telemetrie = Options.Create(new PortalTelemetryOptions
        {
            AccountEndpoint = standaardEndpoint,
            Database = "telemetry",
        });

        var type = Portaal.GetType(
            "Soratus.Portal.Security.ConfigurationCustomerDirectory", throwOnError: true)!;

        return (ICustomerDirectory)Maak(type, opties, telemetrie);
    }

    /// <summary>
    /// De standaardlijst: twee klanten, elk met één klantgebruiker.
    /// </summary>
    /// <remarks>
    /// De klantgebruiker uit <see cref="Testprincipals.Klant"/> staat alleen bij
    /// <c>acme-logistiek</c>. <c>bakker-bv</c> is de vreemde klant waar hij niet bij mag.
    /// </remarks>
    /// <returns>De klanten.</returns>
    public static CustomerRecord[] Standaard() =>
    [
        new CustomerRecord
        {
            Id = "acme-logistiek",
            Name = "Acme Logistiek",
            Environment = "West-Europa",
            EnvironmentDetail = "sub-soratus-acme · rg-acme-prod",
            Access = [new CustomerAccessRecord { Email = Testprincipals.KlantEmail, Role = "Lezer" }],
        },
        new CustomerRecord
        {
            Id = "bakker-bv",
            Name = "Bakker B.V.",
            Environment = "West-Europa",
            EnvironmentDetail = "sub-soratus-bakker · rg-bakker-prod",
            Access = [new CustomerAccessRecord { Email = "beheer@bakker.nl", Role = "Beheerder" }],
        },
    ];

    /// <summary>
    /// Twee klanten waar dezelfde klantgebruiker toegang toe heeft.
    /// </summary>
    /// <remarks>
    /// Voor de landingsroute: bij meer dan één omgeving hoort er een keuze te komen in plaats van
    /// een gok.
    /// </remarks>
    /// <returns>De klanten.</returns>
    public static CustomerRecord[] TweeOmgevingenVoorDezelfdeGebruiker() =>
    [
        new CustomerRecord
        {
            Id = "acme-logistiek",
            Name = "Acme Logistiek",
            Environment = "West-Europa",
            Access = [new CustomerAccessRecord { Email = Testprincipals.KlantEmail }],
        },
        new CustomerRecord
        {
            Id = "acme-retail",
            Name = "Acme Retail",
            Environment = "Noord-Europa",
            Access = [new CustomerAccessRecord { Email = Testprincipals.KlantEmail }],
        },
    ];

    /// <summary>
    /// Een klantenlijst waarin de testklantgebruiker nergens op staat.
    /// </summary>
    /// <returns>De klanten.</returns>
    public static CustomerRecord[] ZonderToegangVoorDeTestgebruiker() =>
    [
        new CustomerRecord
        {
            Id = "bakker-bv",
            Name = "Bakker B.V.",
            Access = [new CustomerAccessRecord { Email = "beheer@bakker.nl" }],
        },
    ];

    /// <summary>Een klant die in de registratie staat maar geen opslag heeft.</summary>
    /// <returns>De klantrecord.</returns>
    public static CustomerRecord ZonderOpslag() => new()
    {
        Id = "cordaan-zorg",
        Name = "Cordaan Zorg",
        Access = [new CustomerAccessRecord { Email = Testprincipals.KlantEmail }],
    };

    private static object Maak(Type type, params object?[] argumenten) =>
        Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: argumenten,
            culture: null)
        ?? throw new InvalidOperationException(
            $"{type.FullName} kon niet worden gemaakt. Is de constructor gewijzigd?");
}
