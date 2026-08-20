using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
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
    /// Richt de container in met een aangemelde gebruiker en de diensten die een pagina vraagt.
    /// </summary>
    /// <param name="gebruiker">De aangemelde gebruiker.</param>
    /// <param name="rollen">De app-rollen voor <c>AuthorizeView</c> en de beleiden.</param>
    /// <param name="beleiden">De autorisatiebeleiden die deze gebruiker haalt.</param>
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
    protected string? Doorstuurdoel()
    {
        var navigatie = Services.GetRequiredService<BunitNavigationManager>();

        if (navigatie.History.Count == 0)
        {
            return null;
        }

        var laatste = navigatie.History.Last();

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
