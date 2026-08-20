using Microsoft.Extensions.Logging.Abstractions;
using Soratus.Portal.Security;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// De weergavelaag van het urenscherm voor de tests: de échte <c>HourViews</c> op een
/// <see cref="Vasteportaalopslag"/>, met een stilstaande klok.
/// </summary>
/// <remarks>
/// <para>Bewust géén eigen implementatie van <see cref="IHourViews"/> die de viewmodellen met de hand
/// vult. Dat is dezelfde afweging als bij <see cref="VasteContractweergaven"/> en
/// <see cref="Weergavelaag"/>, en hier is hij scherper dan daar: de acceptatie van fase 3 gaat er
/// juist over dat de klantprojectie minder oplevert dan de operatorprojectie. Een fixture die het
/// klantpad zelf armer vult, laat elke zichtbaarheidstest groen staan omdat de fixture al filterde
/// en niet omdat de scheiding werkt. Dat is precies de valse groene meting die dit project al twee
/// keer heeft gekost.</para>
///
/// <para>Er is geen bouwmethode met opties. Wat er te variëren valt zit in de opslag — welke regels
/// er staan, of er een contract is, of er een bundel is afgesproken — en dat is de kant waar het
/// hoort: het viewmodel is een projectie en geen bron.</para>
/// </remarks>
internal static class VasteUrenweergaven
{
    /// <summary>
    /// Bouwt de weergavelaag van het urenscherm op deze opslag.
    /// </summary>
    /// <param name="opslag">De opslag met de urenregels en het contract.</param>
    /// <param name="klanten">De klantenlijst, of <c>null</c> voor <see cref="Autorisatiebron.Standaard"/>.</param>
    /// <returns>De echte <c>HourViews</c>.</returns>
    /// <remarks>
    /// Dezelfde instantie voor beide overloads van <see cref="IHourViews.BuildHoursAsync(CustomerScope, Soratus.Portal.Data.HoursQuery, string?, CancellationToken)"/>,
    /// want dat is er in productie ook één. Twee instanties zouden twee lezingen zijn, en dan is
    /// "de operator ziet hetzelfde maandtotaal als de klant" niet meer te bewijzen op één opslag.
    /// </remarks>
    public static IHourViews Bouw(
        Vasteportaalopslag opslag,
        IEnumerable<CustomerRecord>? klanten = null)
    {
        ArgumentNullException.ThrowIfNull(opslag);

        return new HourViews(
            opslag,
            opslag,
            Autorisatiebron.Klantenlijst(klanten ?? Autorisatiebron.Standaard()),
            Weergavelaag.Klok,
            NullLogger<HourViews>.Instance);
    }
}
