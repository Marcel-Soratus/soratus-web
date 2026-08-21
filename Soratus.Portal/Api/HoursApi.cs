using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Api;

/// <summary>
/// De rolgrens van de API, en die van het scherm, uit één bron.
/// </summary>
/// <remarks>
/// <para><strong>Waarom er twee beleidsnamen zijn en niet één.</strong> Een beleid bepaalt niet alleen
/// <em>welke rol</em> nodig is maar ook <em>met welk schema</em> er wordt aangemeld. Het scherm meldt
/// aan met een cookie (na OpenID Connect), de API met een bearer-token; dat zijn twee handlers. Zou
/// <see cref="PortalPolicies.Operator"/> aan het bearer-schema worden gebonden, dan authenticeert elke
/// operatorpagina van het portaal ineens uitsluitend via bearer en is de browser buitengesloten. Eén
/// beleid voor beide kan dus niet.</para>
///
/// <para><strong>Wat wél één is: de eis zelf.</strong> Beide beleiden krijgen hun rol-eis van
/// <see cref="RequireOperator"/>, en dat is de enige plaats in dit portaal waar staat wie uren mag
/// boeken. Zouden ze elk hun eigen <c>RequireRole</c> hebben, dan is "de API heeft dezelfde rolgrens
/// als het scherm" een bewering over twee regels code die onafhankelijk kunnen veranderen — en dan
/// wijkt precies één van de twee ooit af, in de richting die niemand opmerkt.</para>
/// </remarks>
public static class HoursApiPolicy
{
    /// <summary>
    /// De naam van het beleid op <c>POST /api/uren</c>: de app-rol <see cref="PortalRoles.Operator"/>,
    /// aangemeld met een bearer-token.
    /// </summary>
    public const string OperatorBearer = "portal.operator.bearer";

    /// <summary>
    /// De rol-eis: de app-rol <see cref="PortalRoles.Operator"/> en niets anders.
    /// </summary>
    /// <param name="policy">Het beleid in opbouw.</param>
    /// <remarks>
    /// §2 van de spec zegt dat uren boeken operatorwerk is. Er staat hier geen tweede voorwaarde —
    /// geen scope-eis, geen vlag, geen lijst met toegestane clients. Een scope-eis zou een tweede
    /// autorisatieregel zijn die naast de rolmatrix gaat leven, en dan is de rolmatrix niet meer de
    /// plek waar staat wie wat mag. Dat er maar één scope op de registratie staat
    /// (<c>Uren.Boeken</c>) en dat een client die niet vooraf is geautoriseerd er geen token voor
    /// krijgt, is een grens in Entra en hoort daar te blijven.
    /// </remarks>
    public static void RequireOperator(AuthorizationPolicyBuilder policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy.RequireRole(PortalRoles.Operator);
    }

    /// <summary>
    /// Dezelfde rol-eis, vastgezet op het bearer-schema.
    /// </summary>
    /// <param name="policy">Het beleid in opbouw.</param>
    /// <remarks>
    /// <para>Zonder deze binding valt het beleid terug op het standaardschema, en dat is in dit portaal
    /// OpenID Connect. Een aanroep met een bearer-token zou dan geen <c>401</c> opleveren maar een
    /// <c>302</c> naar de aanmeldpagina van Entra — een omleiding als antwoord op een POST met JSON.
    /// De MCP-server leest dat als "onbekend of er geboekt is" en dat is de duurste van de vijf
    /// uitkomsten.</para>
    ///
    /// <para>Het bindt ook de andere kant af: een browsersessie met een cookie komt hier niet door,
    /// ook niet met de operatorrol. Dat is wat dit endpoint buiten het bereik van elke pagina in elk
    /// tabblad houdt, en het is de eerste van de twee redenen dat antiforgery hier niets te doen heeft
    /// — zie <see cref="HoursApi"/>.</para>
    /// </remarks>
    public static void RequireOperatorOnBearer(AuthorizationPolicyBuilder policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        RequireOperator(policy);
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
    }
}

/// <summary>
/// <c>POST /api/uren</c>: het schrijfpad waarlangs de MCP-server <c>soratus-uren</c> uren inschiet.
/// </summary>
/// <remarks>
/// <para><strong>Antiforgery: dit endpoint heeft er niets aan en loopt er ook niet op stuk.</strong>
/// Het portaal roept <c>app.UseAntiforgery()</c> aan voor zijn formulieren, en dit endpoint zit achter
/// diezelfde middleware. Gemeten (zie <c>UrenApiAntiforgeryTests</c>): een POST met JSON en een
/// bearer-token, zonder antiforgery-token, komt door. Dat is geen geluk. De middleware valideert
/// alleen als het endpoint <c>IAntiforgeryMetadata</c> draagt met validatie aan, en dat metadata komt
/// er bij een minimal-API-endpoint alleen op als het formulierinvoer bindt
/// (<c>IFormCollection</c>, <c>IFormFile</c>, <c>[FromForm]</c>). Dit endpoint bindt JSON.</para>
///
/// <para><strong>Er is bewust géén <c>DisableAntiforgery()</c> aangeroepen.</strong> Die aanroep zou
/// vandaag niets doen — er is niets uit te zetten — en het gevaar zit precies in wat hij morgen
/// betekent: hij zet de validatie ook uit als dit endpoint ooit formulierinvoer gaat binden, en dan is
/// er een gat waar niemand naar kijkt. Wat de toekomst wél afdekt is de meting: er staat een test op
/// dat een bearer-POST zonder antiforgery-token door de <em>echte</em> pijplijn komt. Verandert het
/// standaardgedrag van een volgende .NET-versie, dan wordt die test rood en niet de eerste
/// urenboeking van een operator.</para>
///
/// <para><strong>Waarom er geen gat in de formulieren ontstaat.</strong> Dat zou het geval zijn als
/// het antwoord "zet <c>UseAntiforgery</c> uit" of "verplaats de mapping ervoor" was geweest; beide
/// raken elk formulier in het portaal. Hier verandert er niets aan de middleware en niets aan de orde
/// van de pijplijn. En omgekeerd kan dit endpoint niet als CSRF-doelwit dienen: het beleid staat vast
/// op het bearer-schema, dus een cookie uit een browsersessie authenticeert hier niet — een pagina op
/// een andere site kan dus geen boeking doen op de sessie van een ingelogde operator, ook niet als hij
/// een <c>fetch</c> met <c>credentials: include</c> doet.</para>
/// </remarks>
public static class HoursApi
{
    /// <summary>
    /// Registreert de bearer-tokenvalidatie, het rolbeleid en het schrijfpad van de koppeling.
    /// </summary>
    /// <param name="services">De container.</param>
    /// <param name="configuration">De configuratie; de sectie <c>AzureAd</c> wordt gelezen.</param>
    /// <returns><paramref name="services"/>, om door te ketenen.</returns>
    /// <remarks>
    /// <para><strong><c>AddAuthentication()</c> zonder argument, en dat is geen slordigheid.</strong>
    /// De overload met een schemanaam zet <c>DefaultScheme</c>, en die staat in dit portaal op
    /// OpenID Connect. Een tweede <c>AddAuthentication(JwtBearerDefaults.AuthenticationScheme)</c> zou
    /// hem overschrijven, en dan authenticeert élk verzoek — ook elke pagina — met bearer in plaats van
    /// met de cookie. Het portaal is dan stil onbruikbaar in de browser: geen fout, alleen een
    /// gebruiker die nooit is aangemeld.</para>
    ///
    /// <para>Er staat om die reden een test op dat het standaard uitdaging- én aanmeldschema nog
    /// steeds OpenID Connect is en dat het cookieschema er nog naast staat. Bewust niet gemeten met een
    /// echte GET op de startpagina: die zou de metadata van Entra ophalen, en een test die het netwerk
    /// nodig heeft valt op de build-agent om een andere reden om dan de reden waarvoor hij bestaat.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddHoursApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddAuthentication()
            .AddMicrosoftIdentityWebApi(
                configuration.GetSection("AzureAd"),
                JwtBearerDefaults.AuthenticationScheme);

        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            // Gemeten op het bearer-pad, met een token dat oid, tid, name, scp en
            // "roles": ["Operator"] draagt (UrenApiRolgrensTests.DeClaimsDieOpHetBearerpadAankomen
            // drukt de lijst af en pint hem vast). Wat er door de handler heen aankomt:
            //
            //   aud · exp · iat · nbf · iss · name
            //   http://schemas.microsoft.com/identity/claims/objectidentifier   (uit oid)
            //   http://schemas.microsoft.com/identity/claims/scope              (uit scp)
            //   http://schemas.microsoft.com/identity/claims/tenantid           (uit tid)
            //   http://schemas.microsoft.com/ws/2008/06/identity/claims/role    (uit roles)
            //
            // Dus: net als op het cookiepad komt de rol binnen als ClaimTypes.Role en niet als
            // "roles". Dat is hier apart gemeten en niet overgenomen van het cookiepad, want dat zijn
            // twee handlers met twee tokenhandlers en de aanname dat ze hetzelfde doen heeft dit
            // project al twee deploys gekost.
            //
            // Deze twee regels staan er expliciet, ook al is dit voor RoleClaimType de standaard van
            // TokenValidationParameters: stopt een toekomstige versie met mappen, dan hoort dit hier
            // op grond van een nieuwe meting te worden aangepast en niet stilzwijgend te verschuiven.
            // Voor NameClaimType is het géén standaard: Microsoft.Identity.Web zet daar
            // "preferred_username", en dat is het aanmeldadres en niet de naam. Hij staat hier op
            // "name" — dezelfde waarde als op het cookiepad, want "geboekt door" hoort op een
            // MCP-regel hetzelfde te lezen als op een regel uit het scherm. Let op dat "name" níet
            // wordt gemapt: hij staat niet in de inbound-claimtabel, anders dan "roles".
            options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
            options.TokenValidationParameters.NameClaimType = "name";

            // De scope api://soratus-portal/.default levert een token op waarvan de aud óf de appId
            // van de registratie is (bij requestedAccessTokenVersion 2) óf de App ID URI (bij v1).
            // Microsoft.Identity.Web accepteert van zichzelf alleen de eerste vorm en "api://<appId>",
            // dus een v1-token zou hier een 401 opleveren waarvan de melding niets over de oorzaak
            // zegt. Beide vormen staan daarom toegestaan; het Entra-blok in punt 28 van
            // fase-0-afwijkingen.md zet requestedAccessTokenVersion op 2, zodat het in de praktijk de
            // eerste is.
            var clientId = configuration["AzureAd:ClientId"];
            string[] audiences =
            [
                .. new[] { clientId, $"api://{clientId}", "api://soratus-portal" }
                    .Where(audience => !string.IsNullOrWhiteSpace(audience))
                    .OfType<string>(),
            ];

            options.TokenValidationParameters.ValidAudiences = audiences;
            options.TokenValidationParameters.ValidateAudience = true;

            // Een AudienceValidator gaat vóór ValidAudiences: staat er een, dan doet de regel hierboven
            // niets en is het gedrag precies wat het zonder deze hele PostConfigure ook was — een
            // wijziging die lijkt te werken.
            //
            // Eerlijk over wat hiervan gemeten is: op Microsoft.Identity.Web 4.10.0 stáát er geen, dus
            // deze regel weghalen maakt vandaag geen enkele test rood (mutatie M7). Hij blijft staan als
            // voorzorg tegen een versie die er wél een zet, en niet omdat hij nu iets doet. Wat de
            // doelgroepen wél werkelijk afdwingt is de regel erboven: die weghalen maakt 21 tests rood
            // (M8), waaronder het token op de App ID URI.
            options.TokenValidationParameters.AudienceValidator = null;
        });

        // Het schrijfpad van de koppeling. Scoped, net als IPortalHoursStore, en achter een eigen
        // interface: zie IMcpHoursWriter voor waarom deze methode niet op IPortalHoursStore staat.
        services.AddScoped<IMcpHoursWriter, CosmosMcpHoursWriter>();

        return services;
    }

    /// <summary>
    /// Zet <c>POST /api/uren</c> op de routetabel.
    /// </summary>
    /// <param name="endpoints">De routebouwer.</param>
    /// <returns>Het endpoint, om er metadata aan toe te voegen.</returns>
    public static IEndpointConventionBuilder MapHoursApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .MapPost(HourBookingApiContract.Path, BookAsync)
            .RequireAuthorization(HoursApiPolicy.OperatorBearer)
            .WithName("UrenBoeken")
            .ExcludeFromDescription();
    }

    /// <summary>
    /// Legt één urenregel vast als te fiatteren.
    /// </summary>
    /// <param name="request">Het verzoek: vijf velden. Zie <see cref="HourBookingRequest"/>.</param>
    /// <param name="user">De aanroeper, uit het bearer-token.</param>
    /// <param name="scopes">De scope-resolver; beoordeelt rol en klant.</param>
    /// <param name="directory">De klantenlijst, voor de <c>customers</c>-uitbreiding bij een afwijzing.</param>
    /// <param name="writer">Het schrijfpad dat altijd op te fiatteren uitkomt.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// <c>201</c> met de vastgelegde regel, of <c>400</c>/<c>422</c>/<c>409</c> met
    /// <c>application/problem+json</c>.
    /// </returns>
    /// <remarks>
    /// <para><strong>Het bewijstype is <see cref="CustomerWriteScope"/>, hetzelfde als op het
    /// scherm.</strong> Dat is de conclusie van een vraag die in <c>mcp-uren.md</c> nog anders stond:
    /// daar heet dit "een bewijstype voor een aanroeper die geen mens is". Dat was juist voor het
    /// eerste ontwerp, waarin de server een service-identiteit gebruikte. Het huidige ontwerp haalt via
    /// device-code een token op de identiteit van de <em>persoon</em> achter Claude Code, en die
    /// persoon is dezelfde operator die het boekformulier van §3.6 mag versturen. Er is dus geen
    /// aanroeper zonder mens en geen scope-soort die nog niet bestaat.</para>
    ///
    /// <para><strong>Wat de vaste regel uit §5 hier vasthoudt, is dan niet het bewijstype maar wat er
    /// mee te doen is.</strong> Deze methode heeft <see cref="IMcpHoursWriter"/> in handen en niet
    /// <see cref="IPortalHoursStore"/>: er is langs dit pad geen aanroep die fiatteert, geen aanroep
    /// die corrigeert, en geen parameter waarin een stand past. En er is geen tweede endpoint. Een
    /// eigen scope-type zou daar niets aan toevoegen — het zou de <em>store</em> beschermen tegen een
    /// aanroep die vanaf hier niet te doen is.</para>
    ///
    /// <para><see cref="ICustomerScopeResolver.ResolveWriteAsync(ClaimsPrincipal, string, CancellationToken)"/>
    /// wordt gebruikt en niet de klantslug uit het verzoek, en dat doet twee dingen die het beleid niet
    /// doet: het weigert een klant die niet bestaat (anders staat er een urenregel in een partitie die
    /// geen klant is) en het toetst de operatorrol nog een tweede keer, in de code die het bewijs
    /// maakt. Die tweede toets is geen wantrouwen tegen het beleid maar het antwoord op de vraag wat er
    /// gebeurt als iemand het beleid ooit van dit endpoint afhaalt.</para>
    /// </remarks>
    internal static async Task<IResult> BookAsync(
        [FromBody] HourBookingRequest? request,
        ClaimsPrincipal user,
        ICustomerScopeResolver scopes,
        ICustomerDirectory directory,
        IMcpHoursWriter writer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(writer);

        if (request is null)
        {
            return Refused(
                StatusCodes.Status400BadRequest,
                "Er is geen boeking meegestuurd. Verwacht een JSON-object met de velden cid, month, " +
                "hours, category en note.");
        }

        var scope = await scopes
            .ResolveWriteAsync(user, request.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (scope is null)
        {
            // Het beleid heeft de rol al getoetst, dus dit is in de praktijk een onbekende klantslug.
            // De bekende slugs gaan mee in de uitbreiding: dat is precies de kennis die het portaal
            // heeft en de MCP-server niet, en de plausibelste vergissing van een taalmodel is een
            // bedrijfsnaam in plaats van een slug.
            return Refused(
                StatusCodes.Status422UnprocessableEntity,
                $"Het portaal kent geen klant '{request.CustomerId}'. Gebruik de klantslug uit de " +
                "portaal-URL (/klant/<slug>/…) en niet de bedrijfsnaam.",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["customers"] = directory.All.Select(customer => customer.Id).ToArray(),
                });
        }

        // Hier komt "by" vandaan, en nergens anders. Actor is de naam uit het token, en anders de oid;
        // hetzelfde veld dat een boeking via het scherm op zijn naam krijgt, uit dezelfde eigenschap.
        var booking = request.ToBooking(scope.Actor);

        var result = await writer
            .BookPendingAsync(scope, booking, cancellationToken)
            .ConfigureAwait(false);

        switch (result.Status)
        {
            case PortalWriteStatus.Saved:
                var response = HourBookingResponse.From(result.Value!);
                return Results.Created(response.ReviewPath(), response);

            case PortalWriteStatus.Invalid:
                // 422 en niet 400: de JSON is gelezen en begrepen, maar wat erin staat kan niet. Dat
                // onderscheid is voor de aanroeper geen filosofie — een 400 is bij hem een vormfout in
                // het verzoek en een 422 een inhoudelijke afwijzing die hij kan herstellen.
                return Refused(
                    StatusCodes.Status422UnprocessableEntity,
                    result.Message!,
                    extensions: HourCategories.IsBookable(request.Category?.Trim())
                        ? null
                        : new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            // De geldige waarden komen uit HourCategories en worden hier niet
                            // overgeschreven. Dit is de enige plek waar de aanroeper ze leert; er is
                            // met opzet geen metadata-endpoint, want een tweede plek die de lijst kent
                            // gaat achterlopen.
                            ["categories"] = HourCategories.Bookable,
                        });

            case PortalWriteStatus.Conflict:
                // Een 409 en niet een 500. De MCP-server maakt daar zijn duurste onderscheid op: een
                // 5xx betekent bij hem "ONBEKEND of er geboekt is" en dus een aanroeper die het
                // misschien nog eens probeert, terwijl een 409 betekent "niet geboekt, er staat er al
                // een". Dat verschil moet dus over de draad en niet in een algemene fout verdwijnen.
                return Refused(StatusCodes.Status409Conflict, result.Message!);

            default:
                throw new InvalidOperationException(
                    $"Onbekende uitkomst {result.Status} van het schrijfpad van de koppeling.");
        }
    }

    /// <summary>
    /// Een afwijzing als <c>application/problem+json</c> (RFC 9457).
    /// </summary>
    /// <param name="statusCode">De code.</param>
    /// <param name="detail">Waarom er niet geboekt is, in het Nederlands.</param>
    /// <param name="extensions">
    /// De uitbreidingen <c>categories</c> of <c>customers</c>, of <c>null</c>.
    /// </param>
    /// <returns>Het antwoord.</returns>
    /// <remarks>
    /// De titel is voor elke afwijzing dezelfde, en de reden staat in <c>detail</c>. Dat is wat de
    /// MCP-server leest: hij neemt <c>detail</c>, en alleen als dat leeg is <c>title</c>. Een titel per
    /// geval zou dus alleen zichtbaar zijn als er geen reden is, en dan is er iets anders mis.
    /// </remarks>
    private static IResult Refused(
        int statusCode,
        string detail,
        IDictionary<string, object?>? extensions = null) =>
        Results.Problem(new ProblemDetails
        {
            Title = "Ongeldige boeking",
            Detail = detail,
            Status = statusCode,
            Extensions = extensions ?? new Dictionary<string, object?>(StringComparer.Ordinal),
        });
}
