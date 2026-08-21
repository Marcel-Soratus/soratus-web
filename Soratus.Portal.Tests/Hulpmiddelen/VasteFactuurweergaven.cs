using Microsoft.Extensions.Logging.Abstractions;
using Soratus.Portal.Security;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// De weergavelaag van het facturatiescherm voor de tests: de échte <c>BillingViews</c> op een
/// <see cref="Vasteportaalopslag"/>, met een stilstaande klok.
/// </summary>
/// <remarks>
/// <para>Bewust géén eigen implementatie van <see cref="IBillingViews"/> die de viewmodellen met de
/// hand vult. Dezelfde afweging als bij <see cref="VasteUrenweergaven"/>, en hier nog scherper: de hele
/// opgave van dit onderdeel is dat "we weten het niet" onderweg geen nul wordt. Een fixture die de
/// viewmodellen zelf vult, vult ze met de bedragen die de testschrijver in gedachten had — en dan is
/// elke test over een ontbrekend bedrag groen omdat de fixture het zo heeft neergezet, en niet omdat
/// de projectie het verschil bewaart.</para>
///
/// <para>Datzelfde geldt voor de rolgrens. De klantprojectie levert minder dan de operatorprojectie,
/// en dat is precies wat er te meten valt. Een fixture die het klantpad armer vult, laat elke
/// zichtbaarheidstest groen staan zonder hem te meten.</para>
///
/// <para>Er is geen bouwmethode met opties. Wat er te variëren valt zit in de opslag — welke maanden
/// er gemeten zijn, in welke toestand, of er een contract is, of er een opslagpercentage is
/// afgesproken — en dat is de kant waar het hoort: het viewmodel is een projectie en geen bron.</para>
/// </remarks>
internal static class VasteFactuurweergaven
{
    /// <summary>
    /// Bouwt de weergavelaag van het facturatiescherm op deze opslag.
    /// </summary>
    /// <param name="opslag">De opslag met de metingen, het contract en de urenregels.</param>
    /// <param name="klanten">De klantenlijst, of <c>null</c> voor <see cref="Autorisatiebron.Standaard"/>.</param>
    /// <returns>De echte <c>BillingViews</c>.</returns>
    /// <remarks>
    /// Eén opslag voor alle drie de bronnen die deze laag leest. Dat is geen gemak: het maandbedrag
    /// combineert het gemeten verbruik met de bundel uit het contract en de gefiatteerde uren, en drie
    /// fixtures zouden drie werkelijkheden zijn — dan is "Azure en uren op één totaal" niet te meten.
    /// </remarks>
    public static IBillingViews Bouw(
        Vasteportaalopslag opslag,
        IEnumerable<CustomerRecord>? klanten = null)
    {
        ArgumentNullException.ThrowIfNull(opslag);

        return new BillingViews(
            opslag,
            opslag,
            opslag,
            Autorisatiebron.Klantenlijst(klanten ?? Autorisatiebron.Standaard()),
            Weergavelaag.Klok,
            NullLogger<BillingViews>.Instance);
    }
}
