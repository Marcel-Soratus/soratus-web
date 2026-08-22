using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soratus.Portal.Security;
using Soratus.Portal.Sprints;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// De weergavelaag van het sprintscherm voor de tests: de échte <c>SprintViews</c> op een
/// <see cref="Vasteportaalopslag"/>.
/// </summary>
/// <remarks>
/// <para>Bewust géén eigen implementatie van <see cref="ISprintViews"/> die de viewmodellen met de hand
/// vult. Dezelfde afweging als bij <see cref="VasteFactuurweergaven"/> en
/// <see cref="VasteUrenweergaven"/>, en hier met een eigen scherpe kant: de gevoelige inhoud van dit
/// scherm is de kaartjestekst en de adressen uit een DevOps-project. Een fixture die het klantpad zelf
/// armer vult, laat elke zichtbaarheidstest groen staan zonder hem te meten — en dan is er niets dat
/// bewijst dat de <em>projectie</em> die adressen weglaat.</para>
///
/// <para><strong>Er zijn in deze lane twee naden, en dit is de tweede.</strong> De eerste is
/// <see cref="IDevOpsSprintClient"/> — DevOps naar document — en die heeft zijn eigen dubbel in
/// <see cref="Vastesprintbron"/>, met <c>SprintSelection</c>, de clientprojectie en de collector als
/// echte code ertussen. Deze naad is document naar viewmodel, met <c>SprintViews</c> en de rolsplitsing
/// als echte code ertussen. Bij beide loopt de productieprojectie mee; nergens vult een fixture een
/// viewmodel.</para>
///
/// <para><strong>Er is geen bouwmethode met opties.</strong> Wat er te variëren valt zit in de opslag —
/// welke sprint er is gelezen, in welke toestand, of er iteraties zonder datums zijn, of er een bord is
/// vastgelegd — en dat is de kant waar het hoort: het viewmodel is een projectie en geen bron.</para>
/// </remarks>
internal static class VasteSprintweergaven
{
    /// <summary>
    /// De instellingen waarmee de weergavelaag in de tests rekent.
    /// </summary>
    /// <remarks>
    /// <para><strong>Met een agentidentiteit erin, en dat is een keuze met een prijs.</strong> In productie
    /// is die lijst vandaag leeg en komt élk item dus op <see cref="WorkItemOrigin.Unknown"/> uit — dat is
    /// de eerlijke stand zolang er geen agent is die items aanmaakt. Een lege lijst in de fixture zou
    /// betekenen dat er geen test bestaat die het verschil tussen de drie herkomsten meet, en dan is de
    /// enige eigenschap die §3.4 van dit veld vraagt niet gedekt. Vandaar de identiteit hier, en een eigen
    /// test op de lege lijst.</para>
    ///
    /// <para>De blokkademarkering staat op de standaardwaarde. Die staat er niet uit gemak: de gezaaide
    /// sprint draagt hem als tag, en een fixture met een eigen markering zou meten of de <em>fixture</em>
    /// consistent is in plaats van of de standaard werkt.</para>
    /// </remarks>
    public static SprintOptions Instellingen() => new()
    {
        AgentIdentities = [Vasteportaalopslag.Agentidentiteit],
    };

    /// <summary>
    /// Bouwt de weergavelaag van het sprintscherm op deze opslag.
    /// </summary>
    /// <param name="opslag">De opslag met de sprintlezing en het klantdocument.</param>
    /// <param name="klanten">De klantenlijst, of <c>null</c> voor <see cref="Autorisatiebron.Standaard"/>.</param>
    /// <returns>De echte <c>SprintViews</c>.</returns>
    /// <remarks>
    /// <para>Het retourtype is de <em>interface</em> en niet de implementatie. Dat is geen stijl: de
    /// registratie in de testbasis leidt het type af uit deze expressie, en met het concrete type als
    /// retourtype zou daar <c>SprintViews</c> in de container staan in plaats van
    /// <see cref="ISprintViews"/> — en dan valt de pagina om op een ontbrekende dienst met een melding die
    /// eruitziet alsof de registratie er niet is.</para>
    ///
    /// <para><paramref name="klanten"/> wordt niet gebruikt en staat er toch, en dat is eerlijker dan hem
    /// weglaten: elke andere <c>Vaste…weergaven.Bouw</c> in deze map neemt hem, en een afwijkende signatuur
    /// zou de aanroeper laten denken dat deze laag iets anders doet met de klantenlijst. Hij doet er niets
    /// mee omdat hij hem niet nodig heeft — de klantnaam en de slug zitten aan de scope vast, en er is op
    /// dit scherm geen veld dat uit de configuratie komt.</para>
    /// </remarks>
    public static ISprintViews Bouw(
        Vasteportaalopslag opslag,
        IEnumerable<CustomerRecord>? klanten = null)
    {
        ArgumentNullException.ThrowIfNull(opslag);

        _ = klanten;

        return new SprintViews(
            opslag,
            opslag,
            Options.Create(Instellingen()),
            Weergavelaag.Klok,
            NullLogger<SprintViews>.Instance);
    }
}
