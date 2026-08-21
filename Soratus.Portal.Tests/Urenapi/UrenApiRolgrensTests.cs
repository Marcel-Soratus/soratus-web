using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Soratus.Portal.Api;
using Soratus.Portal.Security;

namespace Soratus.Portal.Tests.Urenapi;

/// <summary>
/// De rolgrens op het bearer-pad: is hij werkelijk dezelfde als die van het scherm?
/// </summary>
/// <remarks>
/// <para>Deze klasse bestaat om één ding niet aan te nemen. <c>Program.cs</c> draagt de meting van het
/// <em>cookiepad</em>: de rolclaim komt gemapt binnen als <see cref="ClaimTypes.Role"/> en niet als
/// <c>roles</c>, en stond <c>RoleClaimType</c> op <c>"roles"</c>, dan gaf <c>IsInRole</c> altijd
/// <c>false</c> en stond elk rolbeleid <em>stil dicht</em>. Het bearer-pad is een andere handler met
/// een andere tokenhandler. Dat het daar hetzelfde werkt is dus geen gevolgtrekking maar een vraag, en
/// deze tests zijn het antwoord.</para>
///
/// <para>De meting loopt in twee lagen: <see cref="DeClaimsDieOpHetBearerpadAankomen"/> drukt af wat
/// de echte handler oplevert en pint het vast, en de aanroepen erna gaan door de hele pijplijn en
/// kijken naar de HTTP-code. Alleen dat tweede is bewijs dat het beleid werkt; alleen dat eerste
/// vertelt waaróm.</para>
/// </remarks>
[Collection(Urenapicollectie.Naam)]
public sealed class UrenApiRolgrensTests
{
    private readonly Urenapihost _host;

    /// <summary>Neemt het draaiende portaal aan.</summary>
    /// <param name="host">Het portaal.</param>
    public UrenApiRolgrensTests(Urenapihost host)
    {
        _host = host;
        _host.Schrijver.Reset();
    }

    /// <summary>
    /// Drukt af welke claimtypen de bearer-handler werkelijk oplevert, en pint de twee vast waar de
    /// autorisatie op rust.
    /// </summary>
    [Fact]
    public async Task DeClaimsDieOpHetBearerpadAankomen()
    {
        var gebruiker = await Aanmelden(_host.Token([PortalRoles.Operator]));

        Assert.NotNull(gebruiker);

        var gemeten = string.Join(
            Environment.NewLine,
            gebruiker.Claims
                .Select(claim => $"  {claim.Type} = {claim.Value}")
                .OrderBy(regel => regel, StringComparer.Ordinal));

        // Deze regel staat er zodat de meting in de testuitvoer zichtbaar is en niet alleen in de
        // assertie eronder. Wie hem later leest hoeft niet te geloven wat er in het commentaar staat.
        Assert.False(string.IsNullOrWhiteSpace(gemeten), gemeten);

        // Dit is de kern: het rolclaim komt gemapt binnen, net als op het cookiepad. Zou het als
        // "roles" aankomen, dan is deze assertie rood en niet het rolbeleid stil dicht.
        Assert.Contains(
            gebruiker.Claims,
            claim => claim.Type == ClaimTypes.Role && claim.Value == PortalRoles.Operator);

        Assert.DoesNotContain(gebruiker.Claims, claim => claim.Type == "roles");

        // En dit is wat het beleid en de scope-resolver er allebei mee doen.
        Assert.True(gebruiker.IsInRole(PortalRoles.Operator));
        Assert.False(gebruiker.IsInRole(PortalRoles.Customer));

        // De naam uit het token, want daar komt "geboekt door" vandaan. Zonder NameClaimType op "name"
        // staat hier het aanmeldadres of niets, en dan boekt het portaal op een oid.
        Assert.Equal("Marcel de Graaf", gebruiker.Identity?.Name);
    }

    /// <summary>
    /// Het beleid van de API en dat van het scherm hebben dezelfde eis, en verschillen alleen in het
    /// aanmeldschema.
    /// </summary>
    [Fact]
    public async Task HetBeleidVanDeApiEnDatVanHetSchermEisenDezelfdeRol()
    {
        var beleiden = _host.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var scherm = await beleiden.GetPolicyAsync(PortalPolicies.Operator);
        var api = await beleiden.GetPolicyAsync(HoursApiPolicy.OperatorBearer);

        Assert.NotNull(scherm);
        Assert.NotNull(api);

        Assert.Equal(Rollen(scherm), Rollen(api));
        Assert.Equal([PortalRoles.Operator], Rollen(api));

        // De eis is één; het schema is wat ze onderscheidt. Het scherm bindt geen schema — dan geldt
        // het standaardschema, en dat is OpenID Connect — en de API bindt uitsluitend bearer.
        Assert.Empty(scherm.AuthenticationSchemes);
        Assert.Equal([JwtBearerDefaults.AuthenticationScheme], api.AuthenticationSchemes);
    }

    /// <summary>
    /// Het toevoegen van het bearer-schema heeft de browseraanmelding niet overgenomen.
    /// </summary>
    /// <remarks>
    /// De val is <c>AddAuthentication(JwtBearerDefaults.AuthenticationScheme)</c>: die overload zet
    /// <c>DefaultScheme</c>, en dan meldt élk verzoek — ook elke pagina — met bearer aan in plaats van
    /// met de cookie. Er komt geen foutmelding uit; er komt een portaal uit waarin niemand ooit is
    /// aangemeld.
    /// </remarks>
    [Fact]
    public async Task DeBrowseraanmeldingIsNietOvergenomenDoorHetBearerschema()
    {
        var schemas = _host.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        var uitdaging = await schemas.GetDefaultChallengeSchemeAsync();
        var aanmelding = await schemas.GetDefaultAuthenticateSchemeAsync();

        Assert.Equal(OpenIdConnectDefaults.AuthenticationScheme, uitdaging?.Name);
        Assert.Equal(OpenIdConnectDefaults.AuthenticationScheme, aanmelding?.Name);

        // En het cookieschema staat er nog, naast het nieuwe bearer-schema.
        Assert.NotNull(await schemas.GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme));
        Assert.NotNull(await schemas.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme));
    }

    /// <summary>Zonder token komt er niets door, en het is een 401 en geen omleiding.</summary>
    [Fact]
    public async Task ZonderTokenIsHet401EnGeenOmleidingNaarEntra()
    {
        using var client = _host.Client(token: null);

        using var antwoord = await client.PostAsJsonAsync(HourBookingApiContract.Path, Boeking());

        Assert.Equal(HttpStatusCode.Unauthorized, antwoord.StatusCode);
        Assert.Equal(0, _host.Schrijver.Aanroepen);
    }

    /// <summary>Met de klantrol is het 403. Dit is de aanroep die "het beleid werkt" bewijst.</summary>
    [Fact]
    public async Task MetDeKlantrolIsHet403()
    {
        using var client = _host.Client(_host.Token([PortalRoles.Customer]));

        using var antwoord = await client.PostAsJsonAsync(HourBookingApiContract.Path, Boeking());

        Assert.Equal(HttpStatusCode.Forbidden, antwoord.StatusCode);
        Assert.Equal(0, _host.Schrijver.Aanroepen);
    }

    /// <summary>
    /// Een geldige aanmelding zónder rolclaim is 403 en niet 201.
    /// </summary>
    /// <remarks>
    /// Dit is de val uit <c>stand-van-zaken.md</c>: een app-roltoewijzing met
    /// <c>appRoleId 00000000-…</c> laat je wél binnen maar levert geen rolclaim. Het token is dan
    /// geldig en de handler is tevreden; alleen de rol ontbreekt. Het portaal hoort dat te weigeren, en
    /// <c>soratus-uren controleer</c> hoort het te melden voordat iemand het als een portaalfout gaat
    /// zoeken.
    /// </remarks>
    [Fact]
    public async Task EenGeldigTokenZonderRolclaimIsOok403()
    {
        using var client = _host.Client(_host.Token(rollen: null));

        using var antwoord = await client.PostAsJsonAsync(HourBookingApiContract.Path, Boeking());

        Assert.Equal(HttpStatusCode.Forbidden, antwoord.StatusCode);
        Assert.Equal(0, _host.Schrijver.Aanroepen);
    }

    /// <summary>Met de operatorrol landt de boeking.</summary>
    [Fact]
    public async Task MetDeOperatorrolLandtDeBoeking()
    {
        using var client = _host.Client(_host.Token([PortalRoles.Operator]));

        using var antwoord = await client.PostAsJsonAsync(HourBookingApiContract.Path, Boeking());

        Assert.Equal(HttpStatusCode.Created, antwoord.StatusCode);
        Assert.Equal(1, _host.Schrijver.Aanroepen);
    }

    /// <summary>
    /// Een token voor een andere doelgroep komt er niet door, en een token op de App ID URI wél.
    /// </summary>
    /// <remarks>
    /// De scope <c>api://soratus-portal/.default</c> levert een <c>aud</c> op die óf de appId van de
    /// registratie is óf de App ID URI, afhankelijk van <c>requestedAccessTokenVersion</c>. Beide horen
    /// door te komen; iets anders niet. Zonder de <c>AudienceValidator = null</c> in
    /// <see cref="HoursApi.AddHoursApi"/> valt de tweede regel hieronder om, en dan is de eerste echte
    /// aanmeldpoging een 401 met een melding die niets over de oorzaak zegt.
    /// </remarks>
    [Fact]
    public async Task DeDoelgroepWordtGetoetstEnDeAppIdUriHoortErbij()
    {
        using var vreemd = _host.Client(
            _host.Token([PortalRoles.Operator], doelgroep: "api://iemand-anders"));

        using var geweigerd = await vreemd.PostAsJsonAsync(HourBookingApiContract.Path, Boeking());
        Assert.Equal(HttpStatusCode.Unauthorized, geweigerd.StatusCode);

        using var uri = _host.Client(
            _host.Token([PortalRoles.Operator], doelgroep: "api://soratus-portal"));

        using var toegelaten = await uri.PostAsJsonAsync(HourBookingApiContract.Path, Boeking());
        Assert.Equal(HttpStatusCode.Created, toegelaten.StatusCode);
    }

    /// <summary>
    /// De scope-resolver weigert dezelfde rollen als het beleid, met dezelfde principal.
    /// </summary>
    /// <remarks>
    /// De tweede helft van de rolgrens. Het beleid houdt het verzoek buiten de handler; de resolver
    /// houdt het bewijs buiten de opslag. Dat is geen dubbelop maar het antwoord op de vraag wat er
    /// gebeurt als iemand het beleid ooit van dit endpoint afhaalt — dan is er nog steeds geen
    /// <see cref="CustomerWriteScope"/> te krijgen.
    /// </remarks>
    [Fact]
    public async Task DeScoperesolverWeegtDezelfdePrincipalHetzelfde()
    {
        using var scope = _host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ICustomerScopeResolver>();

        var operatorgebruiker = await Aanmelden(_host.Token([PortalRoles.Operator]));
        var klant = await Aanmelden(_host.Token([PortalRoles.Customer]));

        Assert.NotNull(await resolver.ResolveWriteAsync(operatorgebruiker, _host.EersteKlant));
        Assert.Null(await resolver.ResolveWriteAsync(klant, _host.EersteKlant));

        // En een klant die niet bestaat levert ook voor een operator geen bewijs op: anders staat er
        // een urenregel in een partitie die geen klant is.
        Assert.Null(await resolver.ResolveWriteAsync(operatorgebruiker, "bestaat-niet"));
    }

    /// <summary>De rollen die dit beleid eist, ongeacht hoe ze zijn opgeschreven.</summary>
    private static string[] Rollen(AuthorizationPolicy beleid) =>
    [
        .. beleid.Requirements
            .OfType<RolesAuthorizationRequirement>()
            .SelectMany(eis => eis.AllowedRoles)
            .OrderBy(rol => rol, StringComparer.Ordinal),
    ];

    private object Boeking() => new
    {
        cid = _host.EersteKlant,
        month = "2026-08",
        hours = 3.5m,
        category = "Ontwikkeling",
        note = "Koppeling met de voorraadservice afgemaakt.",
    };

    /// <summary>
    /// Laat de échte bearer-handler het token valideren en geeft de principal terug die eruit komt.
    /// </summary>
    private async Task<ClaimsPrincipal?> Aanmelden(string token)
    {
        using var scope = _host.Services.CreateScope();

        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = $"Bearer {token}";

        var resultaat = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);

        return resultaat.Succeeded ? resultaat.Principal : null;
    }
}
