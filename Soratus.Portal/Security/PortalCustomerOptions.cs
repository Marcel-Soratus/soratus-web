namespace Soratus.Portal.Security;

/// <summary>
/// De configuratiesectie <c>Portal</c>, met daarin de klantenlijst.
/// </summary>
/// <remarks>
/// <para><strong>Dit is sinds fase 2 niet meer de bron van de klantenlijst.</strong> Die staat in de
/// portaalopslag (zie <see cref="Data.PortalDataOptions"/>), want een klant toevoegen mag geen uitrol
/// vragen. Wat deze sectie nog doet is twee dingen, en het zijn beide vangnetten:</para>
/// <list type="number">
///   <item><description>
///     Hij is de momentopname waarmee <see cref="CustomerDirectory"/> begint. Het portaal kent zijn
///     klanten dus vóórdat er één query is gelopen, en blijft ze kennen als de opslag niet
///     antwoordt.
///   </description></item>
///   <item><description>
///     Hij is de inhoud van de eenmalige migratie: deze lijst is precies wat er één keer naar de
///     opslag wordt geschreven. Daarna wordt hij nooit meer geschreven — zie
///     <see cref="Data.PortalDataOptions.Bootstrap"/>.
///   </description></item>
/// </list>
///
/// <para>Zodra de migratie is gelopen doet wat hier staat niets meer voor een draaiend portaal. Hij
/// blijft staan omdat de terugval echt is: een portaal dat niemand binnenlaat omdat Cosmos twee
/// seconden hapert, is een slechtere ruil dan een lijst die even oud is. Verwijder deze sectie dus
/// niet "omdat hij toch niets doet" — hij doet iets op precies het moment dat het ertoe doet.</para>
///
/// <para>Bevat geen geheimen: klantnamen, slugs, endpoints, resource-groepnamen en e-mailadressen.
/// Een client secret hoort hier niet en komt later uit Key Vault.</para>
/// </remarks>
public sealed class PortalCustomerOptions
{
    /// <summary>De naam van de configuratiesectie.</summary>
    public const string SectionName = "Portal";

    /// <summary>De ingerichte klanten.</summary>
    public IList<CustomerRecord> Customers { get; set; } = [];
}
