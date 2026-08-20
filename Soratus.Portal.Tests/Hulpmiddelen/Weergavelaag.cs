using Soratus.Portal.Data;
using Soratus.Portal.Security;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Zet de echte <c>PortalViews</c> op een <see cref="Vastetelemetriestore"/>, met een stilstaande
/// klok en echte scopes.
/// </summary>
/// <remarks>
/// <para>Dit is het tegendeel van <see cref="VastePortaalweergaven"/>. Die fixture vervangt de
/// weergavelaag om een pagina te kunnen renderen; deze houdt hem juist heel en vervangt alleen de
/// opslag, zodat er iets valt te zeggen over de rekenregels erin: de zichtbaarheidscontrole, de
/// tellingen per niveau, de cursor van de live tail.</para>
///
/// <para>De scopes komen uit de echte resolver via <see cref="Autorisatiebron"/> en worden niet
/// nagebouwd. Ze zijn <c>internal</c> te maken maar niet van buiten te construeren, en dat hoort
/// zo: een scope zonder oordeel erachter bestaat niet.</para>
/// </remarks>
internal static class Weergavelaag
{
    /// <summary>De stilstaande klok van de tests.</summary>
    public static TimeProvider Klok { get; } = new StilstaandeKlok(Testgegevens.Nu);

    /// <summary>
    /// Bouwt de weergavelaag met de tabbladinterface erbij.
    /// </summary>
    /// <param name="store">De opslag.</param>
    /// <param name="klanten">De klantenlijst, of <c>null</c> voor <see cref="Autorisatiebron.Standaard"/>.</param>
    /// <returns>De weergavelaag; dezelfde instantie bedient beide interfaces.</returns>
    public static IAgentDetailViews Tabbladen(
        Vastetelemetriestore store,
        IEnumerable<CustomerRecord>? klanten = null) =>
        (IAgentDetailViews)Bouw(store, klanten);

    /// <summary>
    /// Bouwt de weergavelaag als paginabouwer: dezelfde instantie, andere interface.
    /// </summary>
    /// <param name="store">De opslag.</param>
    /// <param name="klanten">De klantenlijst, of <c>null</c> voor de standaardlijst.</param>
    /// <returns>De weergavelaag.</returns>
    public static IPortalViews Paginas(
        Vastetelemetriestore store,
        IEnumerable<CustomerRecord>? klanten = null) =>
        (IPortalViews)Bouw(store, klanten);

    /// <summary>
    /// Bouwt de weergavelaag één keer en geeft hem als beide interfaces terug.
    /// </summary>
    /// <param name="store">De opslag.</param>
    /// <param name="klanten">De klantenlijst, of <c>null</c> voor de standaardlijst.</param>
    /// <returns>Dezelfde instantie, twee keer.</returns>
    /// <remarks>
    /// Eén instantie en niet twee. De zichtbaarheidscontrole van het detail en die van de
    /// tabbladen horen op dezelfde opslag en dezelfde klok te staan, anders test je twee
    /// verschillende werelden die per ongeluk hetzelfde antwoord geven.
    /// </remarks>
    public static (IPortalViews Paginas, IAgentDetailViews Tabbladen) Beide(
        Vastetelemetriestore store,
        IEnumerable<CustomerRecord>? klanten = null)
    {
        var laag = Bouw(store, klanten);

        return ((IPortalViews)laag, (IAgentDetailViews)laag);
    }

    /// <summary>De klantscope van de testklantgebruiker op <c>acme-logistiek</c>.</summary>
    /// <param name="klanten">De klantenlijst, of <c>null</c> voor de standaardlijst.</param>
    /// <returns>De scope.</returns>
    public static async Task<CustomerScope> Klantscope(IEnumerable<CustomerRecord>? klanten = null)
    {
        var resolver = Autorisatiebron.Resolver(
            klanten ?? Autorisatiebron.Standaard(),
            Autorisatiebron.StandaardEndpoint);

        return await resolver.ResolveAsync(Testprincipals.Klant(), "acme-logistiek")
            ?? throw new InvalidOperationException(
                "De testklantgebruiker kreeg geen scope op acme-logistiek. Zonder scope valt er " +
                "niets te vragen aan de weergavelaag; controleer Autorisatiebron.Standaard.");
    }

    /// <summary>De operatorscope op <c>acme-logistiek</c>.</summary>
    /// <param name="klanten">De klantenlijst, of <c>null</c> voor de standaardlijst.</param>
    /// <returns>De scope.</returns>
    public static async Task<OperatorCustomerScope> Operatorscope(
        IEnumerable<CustomerRecord>? klanten = null)
    {
        var resolver = Autorisatiebron.Resolver(
            klanten ?? Autorisatiebron.Standaard(),
            Autorisatiebron.StandaardEndpoint);

        return await resolver.ResolveOperatorAsync(Testprincipals.Operator(), "acme-logistiek")
            ?? throw new InvalidOperationException(
                "De testoperator kreeg geen scope op acme-logistiek. Controleer of het rolclaim " +
                "in Testprincipals nog klopt.");
    }

    private static object Bouw(Vastetelemetriestore store, IEnumerable<CustomerRecord>? klanten)
    {
        ArgumentNullException.ThrowIfNull(store);

        var lijst = klanten ?? Autorisatiebron.Standaard();

        return new PortalViews(
            store,
            Autorisatiebron.Klantenlijst(lijst),
            Klok);
    }

    /// <summary>Een klok die niet loopt.</summary>
    /// <remarks>
    /// Het portaal leest de klok één keer per opbouw. Staat hij stil, dan is elk getal op een
    /// gerenderde pagina reproduceerbaar en gaat geen enkele test 's nachts rood.
    /// </remarks>
    private sealed class StilstaandeKlok(DateTimeOffset moment) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => moment;
    }
}
