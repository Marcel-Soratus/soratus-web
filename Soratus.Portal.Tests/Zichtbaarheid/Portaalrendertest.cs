using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Soratus.Portal.Data;
using Soratus.Portal.Security;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// Basis voor tests die een echte portaalpagina renderen met een gekozen rol.
/// </summary>
/// <remarks>
/// <para><strong>Dit is geen beveiliging.</strong> Wat hier wordt getest is een vangnet tegen
/// ongelukken: dat een operator-only blok niet per ongeluk in de klantweergave belandt. De echte
/// grens ligt ergens anders, en dat is bewust zo:</para>
/// <list type="number">
///   <item><description>
///     in de <strong>datalaag</strong>: een klantgebruiker krijgt van
///     <see cref="ICustomerScopeResolver"/> geen <see cref="OperatorScope"/>, en zonder dat
///     argument is de aanroep die alle klanten leest niet eens te schrijven;
///   </description></item>
///   <item><description>
///     in de <strong>viewmodellen</strong>: wat de klant niet mag zien staat niet als
///     <c>null</c>-veld op het type, het staat er helemaal niet;
///   </description></item>
///   <item><description>
///     bij de <strong>autorisatie op de endpoints</strong>: het fallback-beleid in
///     <c>Program.cs</c> eist een aangemelde gebruiker, en de beleiden uit
///     <see cref="PortalPolicies"/> zetten hele pagina's dicht.
///   </description></item>
/// </list>
/// <para>Zou iemand deze tests weghalen, dan is er nog steeds geen weg voor een klant naar
/// operatorgegevens. Zou iemand alleen op deze tests vertrouwen, dan is er dat wél: een
/// zichtbaarheidstest kijkt naar markup, en markup is het laatste station.</para>
/// </remarks>
public abstract class Portaalrendertest : BunitContext
{
    /// <summary>De klant-slug waar de testklantgebruiker recht op heeft.</summary>
    protected const string EigenKlant = "acme-logistiek";

    /// <summary>
    /// De weergavelaag die de pagina's te zien krijgen. Zet hem vóór het aanmelden om een
    /// bijzondere stand te renderen — een klant die alleen buiten productie draait, bijvoorbeeld.
    /// </summary>
    /// <remarks>
    /// Blijft met opzet één vaste weergavelaag en geen mock per test: wat er te zien is hoort uit
    /// dezelfde viewmodellen te komen als in productie. Zie <see cref="VastePortaalweergaven"/>.
    /// </remarks>
    protected IPortalViews Weergaven { get; set; } = new VastePortaalweergaven();

    /// <summary>
    /// De weergavelaag van de tabbladen op het agentdetail: logs, runs en configuratie.
    /// </summary>
    /// <remarks>
    /// <para>Dit is standaard dezelfde instantie als <see cref="Weergaven"/> — één fixture die
    /// beide interfaces implementeert. Dat is geen gemakzucht: op een echt scherm komen de kop en
    /// de tabbladen uit dezelfde gegevens, en twee losse fixtures zouden elkaar kunnen
    /// tegenspreken zonder dat een test dat merkt.</para>
    ///
    /// <para>Zet <see cref="Weergaven"/> op een eigen instantie en deze blijft daaraan gekoppeld
    /// zolang die instantie ook <see cref="IAgentDetailViews"/> is; anders valt hij terug op de
    /// standaardfixture.</para>
    /// </remarks>
    protected IAgentDetailViews Tabbladen
    {
        get => _tabbladen ?? Weergaven as IAgentDetailViews ?? new VastePortaalweergaven();
        set => _tabbladen = value;
    }

    private IAgentDetailViews? _tabbladen;

    /// <summary>
    /// De portaaleigen opslag: klanten, contracten en toegangen. Het contractscherm schrijft
    /// hierin, en na het renderen valt hier af te lezen wat er is weggeschreven.
    /// </summary>
    /// <remarks>
    /// Vervang hem vóór het aanmelden om een bijzondere stand te renderen — een klant zonder
    /// contract, of een klant die alleen in de configuratie staat. Zie
    /// <see cref="Vasteportaalopslag"/>.
    /// </remarks>
    /// <remarks>
    /// <c>internal</c> en niet <c>protected</c>: het type is internal, en een protected lid van een
    /// public klasse zou daarmee toegankelijker zijn dan zijn eigen type (CS0053). De afgeleide
    /// testklassen staan in dezelfde assembly, dus in de praktijk maakt het niets uit.
    /// </remarks>
    internal Vasteportaalopslag Opslag { get; set; } = new();

    /// <summary>
    /// De weergavelaag van het contractscherm. Standaard de échte projectie op
    /// <see cref="Opslag"/>; zet hem om een afwijkende stand te renderen.
    /// </summary>
    /// <remarks>
    /// <para>Standaard <see cref="VasteContractweergaven"/>, en dat is geen lege stub met opzet: een
    /// zichtbaarheidstest op een scherm zonder gegevens bewijst niets. Er staat dus een volledig
    /// contract achter, met een toegangslijst waarin beide aanduidingen voorkomen.</para>
    ///
    /// <para><c>null</c> betekent "bouw de standaard bij het aanmelden", want die heeft de
    /// klantenlijst nodig die aan <see cref="MeldAan"/> is meegegeven.</para>
    /// </remarks>
    protected IContractViews? Contracten { get; set; }

    /// <summary>
    /// De weergavelaag van het urenscherm. Standaard de échte projectie op <see cref="Opslag"/>.
    /// </summary>
    /// <remarks>
    /// <para><c>null</c> betekent "bouw de standaard bij het aanmelden", want die heeft de
    /// klantenlijst nodig die aan <see cref="MeldAan"/> is meegegeven.</para>
    ///
    /// <para>Vervang hem alleen als een test een stand nodig heeft die de projectie niet kan
    /// opleveren. Voor alles wat over zichtbaarheid gaat is de échte projectie het punt: zie
    /// <see cref="VasteUrenweergaven"/> — een fixture die het klantpad zelf armer vult, laat de
    /// scheiding groen staan zonder hem te meten.</para>
    /// </remarks>
    protected IHourViews? Uren { get; set; }

    /// <summary>
    /// De weergavelaag van het facturatiescherm. Standaard de échte projectie op <see cref="Opslag"/>.
    /// </summary>
    /// <remarks>
    /// <para><c>null</c> betekent "bouw de standaard bij het aanmelden", want die heeft de klantenlijst
    /// nodig die aan <see cref="MeldAan"/> is meegegeven.</para>
    ///
    /// <para>Vervang hem alleen als een test een stand nodig heeft die de projectie niet kan opleveren.
    /// Voor alles wat over bedragen gaat is de échte projectie het punt: zie
    /// <see cref="VasteFactuurweergaven"/> — de hele opgave van dit scherm is dat een onbekend bedrag
    /// onderweg geen nul wordt, en een fixture die de viewmodellen zelf vult, vult ze met de bedragen
    /// die de testschrijver in gedachten had.</para>
    /// </remarks>
    protected IBillingViews? Facturatie { get; set; }

    /// <summary>
    /// Richt de container in met een aangemelde gebruiker en de diensten die een pagina vraagt.
    /// </summary>
    /// <param name="gebruiker">De aangemelde gebruiker.</param>
    /// <param name="rollen">De app-rollen voor <c>AuthorizeView</c> en de beleiden.</param>
    /// <param name="beleiden">De autorisatiebeleiden die deze gebruiker haalt.</param>
    /// <param name="klanten">
    /// De klantenlijst waaruit de scope-resolver leest. <c>null</c> betekent de standaardlijst.
    /// </param>
    protected void MeldAan(
        ClaimsPrincipal gebruiker,
        string[] rollen,
        string[] beleiden,
        CustomerRecord[]? klanten = null)
    {
        ArgumentNullException.ThrowIfNull(gebruiker);

        var lijst = klanten ?? Autorisatiebron.Standaard();

        var autorisatie = AddAuthorization();
        autorisatie.SetAuthorized(gebruiker.Identity?.Name ?? "test");
        autorisatie.SetRoles(rollen);
        autorisatie.SetPolicies(beleiden);

        // Bovenop de bUnit-dubbel een echte principal, zodat de claims kloppen die de resolver
        // leest: het e-mailadres uit preferred_username en het rolclaim uit Entra. Zonder deze
        // regel levert IsInRole false en slaagt elke autorisatietest om de verkeerde reden.
        Services.AddSingleton<AuthenticationStateProvider>(new VasteAanmelding(gebruiker));

        // Het logtabblad is een interactief eiland en laadt bij de eerste render zijn
        // collocated JS-module. Die module doet één ding — melden of het tabblad op de voorgrond
        // staat — en er is in een test geen browser om dat te vragen. Loose in plaats van een
        // SetupModule per test: er valt aan die module niets te asserteren, en een strikte
        // JS-laag zou elke pagina met een eiland laten omvallen op een detail dat niets met
        // zichtbaarheid te maken heeft.
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(Weergaven);
        Services.AddSingleton(Tabbladen);
        Services.AddSingleton(Autorisatiebron.Resolver(lijst, Autorisatiebron.StandaardEndpoint));
        Services.AddSingleton(Autorisatiebron.Klantenlijst(lijst));

        // Het contractscherm vraagt deze twee: de weergavelaag voor de leeskant en de opslag voor
        // het eiland dat schrijft. Ze staan hier en niet per test, want elke pagina valt onder het
        // zichtbaarheidsvangnet en dat rendert ze allemaal — een pagina die op een ontbrekende
        // dienst omvalt toont geen verboden woorden en laat dat vangnet dus groen staan om de
        // verkeerde reden.
        Services.AddSingleton(Contracten ?? new VasteContractweergaven(Opslag, lijst));
        Services.AddSingleton<IPortalDataStore>(Opslag);

        // Het urenscherm vraagt deze twee: de weergavelaag voor beide rollen en de opslag voor de
        // formulieren die schrijven. Ze staan hier en niet per test, om dezelfde reden als de twee
        // regels erboven: elke pagina valt onder het zichtbaarheidsvangnet en dat rendert ze
        // allemaal, en een pagina die op een ontbrekende dienst omvalt toont geen verboden woorden
        // en laat dat vangnet dus groen staan om de verkeerde reden.
        Services.AddSingleton(Uren ?? VasteUrenweergaven.Bouw(Opslag, lijst));
        Services.AddSingleton<IPortalHoursStore>(Opslag);

        // Het facturatiescherm vraagt deze twee. Ze staan hier en niet per test om dezelfde reden als
        // de regels erboven: elke pagina valt onder het zichtbaarheidsvangnet en dat rendert ze
        // allemaal, en een pagina die op een ontbrekende dienst omvalt toont geen verboden woorden en
        // laat dat vangnet dus groen staan om de verkeerde reden.
        //
        // De opslag staat er als IPortalCostsStore bij hoewel dat scherm niets schrijft: hij is de
        // leesbron, en één opslag voor kosten, contract en uren is de voorwaarde om te kunnen meten dat
        // Azure en de uren op één totaal komen (§3.7).
        Services.AddSingleton(Facturatie ?? VasteFactuurweergaven.Bouw(Opslag, lijst));
        Services.AddSingleton<IPortalCostsStore>(Opslag);

        // Het urenscherm leest de klok zelf om te bepalen welke maand "deze maand" is. Dezelfde
        // stilstaande klok als de weergavelaag, anders wijst de standaardweergave naar een andere
        // maand dan het viewmodel voorselecteert.
        Services.AddSingleton(Weergavelaag.Klok);
    }

    /// <summary>Richt de container in voor een klantgebruiker met precies één omgeving.</summary>
    protected void MeldKlantAan(CustomerRecord[]? klanten = null) =>
        MeldAan(Testprincipals.Klant(), [PortalRoles.Customer], [PortalPolicies.Customer], klanten);

    /// <summary>Richt de container in voor een Soratus-operator.</summary>
    protected void MeldOperatorAan(CustomerRecord[]? klanten = null) =>
        MeldAan(Testprincipals.Operator(), [PortalRoles.Operator], [PortalPolicies.Operator], klanten);

    /// <summary>
    /// Rendert een paginatype met zijn routeparameters ingevuld.
    /// </summary>
    /// <param name="pagina">Het paginatype.</param>
    /// <returns>De gerenderde pagina.</returns>
    protected IRenderedComponent<Bunit.Rendering.ContainerFragment> RenderPagina(Type pagina) =>
        RenderPagina(pagina, Paginaverzameling.Parameters(pagina));

    /// <summary>
    /// Rendert een paginatype met een gekozen klant-slug in de route, in plaats van de slug waar
    /// de testgebruiker recht op heeft.
    /// </summary>
    /// <param name="pagina">Het paginatype.</param>
    /// <param name="slug">De slug zoals hij in de URL staat.</param>
    /// <returns>De gerenderde pagina.</returns>
    /// <remarks>
    /// Voor twee soorten tests: een slug van een vreemde klant, en een slug in een afwijkende
    /// schrijfwijze. De opzoektabel vergelijkt hoofdletterongevoelig, dus <c>ACME-Logistiek</c>
    /// resolvet — en dan hoort het scherm alsnog de canonieke vorm te tonen.
    /// </remarks>
    protected IRenderedComponent<Bunit.Rendering.ContainerFragment> RenderPagina(
        Type pagina,
        string slug)
    {
        ArgumentNullException.ThrowIfNull(pagina);

        var parameters = new Dictionary<string, object?>(
            Paginaverzameling.Parameters(pagina),
            StringComparer.Ordinal);

        if (!parameters.ContainsKey("Slug"))
        {
            // Een onbekende parameter aan een component meegeven werpt pas tijdens het renderen,
            // met een melding over de component in plaats van over de test. Dit is duidelijker.
            throw new InvalidOperationException(
                $"De pagina {pagina.Name} heeft geen parameter Slug, dus er valt geen klant-slug " +
                "in de route te zetten. Is de parameter hernoemd, dan hoort Paginaverzameling " +
                "mee te veranderen.");
        }

        parameters["Slug"] = slug;

        return RenderPagina(pagina, parameters);
    }

    private IRenderedComponent<Bunit.Rendering.ContainerFragment> RenderPagina(
        Type pagina,
        IReadOnlyDictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(pagina);

        return Render(builder =>
        {
            // ASP0006 wil literale volgnummers, en dat is voor met de hand geschreven
            // rendercode ook het juiste advies: de nummers zijn dan de bronvolgorde en de
            // differ kan erop rekenen. Hier is het paginatype pas op looptijd bekend en wordt
            // deze boom precies één keer opgebouwd, dus er is geen bronvolgorde om te volgen
            // en geen tweede render om tegen te differen.
#pragma warning disable ASP0006
            var volgnummer = 0;
            builder.OpenComponent(volgnummer++, pagina);

            foreach (var (naam, waarde) in parameters)
            {
                builder.AddComponentParameter(volgnummer++, naam, waarde);
            }
#pragma warning restore ASP0006

            builder.CloseComponent();
        });
    }

    /// <summary>
    /// Of de laatst gerenderde pagina heeft doorgestuurd in plaats van iets te tonen.
    /// </summary>
    /// <returns><c>true</c> als er is genavigeerd.</returns>
    /// <remarks>
    /// Een doorstuurpagina — de landingsroute die een operator naar het overzicht brengt — rendert
    /// met opzet niets. Dat is geen kapotte pagina, en de test die eist dat er íets gebeurt moet
    /// dat verschil kunnen zien.
    /// </remarks>
    protected bool IsDoorgestuurd() => Doorstuurdoel() is not null;

    /// <summary>
    /// Waarheen de laatst gerenderde pagina heeft doorgestuurd, of <c>null</c> als er niet is
    /// genavigeerd.
    /// </summary>
    /// <returns>Het pad, bijvoorbeeld <c>/overzicht</c>.</returns>
    /// <remarks>
    /// <strong><see cref="BunitNavigationManager.History"/> staat nieuwste eerst.</strong> Hier stond
    /// <c>Last()</c>, en dat leverde de óudste navigatie op. Dat bleef onzichtbaar zolang elke
    /// aanroeper er precies één had — bij de landingsroutes navigeert alleen de pagina zelf, en dan
    /// zijn de eerste en de laatste dezelfde. Een test die eerst zelf naar een adres met een
    /// querystring gaat en daarna meet waar een POST heen stuurde, kreeg zijn eigen beginadres terug
    /// en verweet dat de pagina. Gemeten op de urenschrijfacties: <c>[0]</c> was de redirect uit
    /// <c>Done</c>, <c>[1]</c> het beginadres van de test.
    /// </remarks>
    protected string? Doorstuurdoel()
    {
        var navigatie = Services.GetRequiredService<BunitNavigationManager>();

        if (navigatie.History.Count == 0)
        {
            return null;
        }

        var laatste = navigatie.History.First();

        return "/" + navigatie.ToBaseRelativePath(
            navigatie.ToAbsoluteUri(laatste.Uri).ToString());
    }

    /// <summary>Een aanmelding die altijd dezelfde gebruiker teruggeeft.</summary>
    private sealed class VasteAanmelding(ClaimsPrincipal gebruiker) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(gebruiker));
    }
}
