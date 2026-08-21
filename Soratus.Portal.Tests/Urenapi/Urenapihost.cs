using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Soratus.Portal.Api;
using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Tests.Urenapi;

/// <summary>
/// Het échte portaal, met een echte pijplijn, en één ding vervangen: de opslag.
/// </summary>
/// <remarks>
/// <para><strong>Waarom <see cref="WebApplicationFactory{TEntryPoint}"/> en niet een nagebouwde
/// pijplijn.</strong> Wat hier gemeten moet worden is juist het samenspel: dat het rolbeleid op het
/// bearer-schema staat en niet op het standaardschema, dat de claims die aankomen dezelfde rol
/// opleveren als op het scherm, en dat de antiforgery-middleware een POST met JSON doorlaat. Alle
/// drie zijn eigenschappen van de orde en de registratie in <c>Program.cs</c>. Een test die die
/// wiring zelf opbouwt meet zijn eigen kopie, en dat is precies de meting die in dit project al een
/// keer voor een geldige is doorgegaan.</para>
///
/// <para><strong>Wat er wél is vervangen, en waarom dat de meting niet aantast.</strong> Drie
/// dingen, alle drie buiten het pad dat wordt gemeten:</para>
/// <list type="number">
/// <item><description><see cref="IMcpHoursWriter"/> door <see cref="Vasteurenschrijver"/>. Er wordt
/// niets naar Cosmos geschreven — dat is een harde regel voor deze sessie — en de tests kunnen nu
/// zien wat het endpoint aan de opslag zou hebben aangeboden. De <em>bron</em> en de <em>stand</em>
/// die de echte schrijver vastzet worden apart gemeten, met een test op de code van die
/// schrijver.</description></item>
/// <item><description>De twee achtergronddiensten <c>TelemetryWarmup</c> en
/// <c>PortalDirectoryRefresh</c>. Die zoeken Cosmos op bij het opstarten, en de tweede zou de
/// klantenlijst naar de opslag migreren. Ze worden gericht verwijderd en niet met een
/// <c>RemoveAll&lt;IHostedService&gt;</c>: de webserver zelf is óók een
/// <c>IHostedService</c>.</description></item>
/// <item><description>De tokenvalidatie krijgt een lokale sleutel in plaats van de metadata van
/// Entra. De <em>claimafhandeling</em> — het mappen, <c>RoleClaimType</c>, <c>NameClaimType</c> —
/// blijft staan zoals het portaal hem zet, want dat is wat er gemeten wordt.</description></item>
/// </list>
///
/// <para>De klantenlijst komt uit de gewone <c>appsettings.json</c> van het portaal en wordt niet
/// overschreven; de tests vragen de eerste klant op bij <see cref="ICustomerDirectory"/> in plaats
/// van een slug in te typen, zodat ze niet omvallen als die lijst verandert.</para>
/// </remarks>
public sealed class Urenapihost : WebApplicationFactory<Program>
{
    /// <summary>De sleutel waarmee de testtokens worden ondertekend.</summary>
    /// <remarks>
    /// <para>Geen geheim en niets dat ergens anders geldig is: hij bestaat alleen in het geheugen van
    /// deze test. Wat hij vervangt is de publieke sleutel van Entra, die het portaal in productie uit de
    /// metadata haalt — een netwerkaanroep die een test niet hoort te doen.</para>
    ///
    /// <para><strong>Per host en niet statisch, en dat is gemeten.</strong> Een
    /// <see cref="SymmetricSecurityKey"/> draagt zijn eigen <c>CryptoProviderFactory</c>, en die cachet
    /// de ondertekenaars. Werd één sleutelobject door meerdere hosts gebruikt, dan gingen de
    /// validaties van de tweede host stuk zodra de eerste host was opgeruimd: alle aanroepen kwamen
    /// daarna als 401 terug. Los draaien slaagde en samen draaien niet — precies het soort valse
    /// meting waar een test niets meer waard is.</para>
    /// </remarks>
    private readonly SymmetricSecurityKey _sleutel =
        new(Encoding.UTF8.GetBytes("soratus-portal-testsleutel-alleen-in-het-geheugen-0123456789"));

    /// <summary>De opslag die het endpoint in plaats van Cosmos aanspreekt.</summary>
    public Vasteurenschrijver Schrijver { get; } = new();

    /// <summary>De eerste klant uit de lijst van het draaiende portaal.</summary>
    public string EersteKlant =>
        Services.GetRequiredService<ICustomerDirectory>().All[0].Id;

    /// <summary>De doelgroep waarvoor een geldig token wordt uitgegeven.</summary>
    /// <remarks>
    /// De appId van de registratie, uit de configuratie van het draaiende portaal. Niet ingetypt: dan
    /// meet de test zijn eigen waarde in plaats van die van de app.
    /// </remarks>
    public string Doelgroep =>
        Services.GetRequiredService<IConfiguration>()["AzureAd:ClientId"]
        ?? throw new InvalidOperationException("AzureAd:ClientId staat niet in de configuratie.");

    /// <summary>De tenant waarvoor een geldig token wordt uitgegeven.</summary>
    public string Tenant =>
        Services.GetRequiredService<IConfiguration>()["AzureAd:TenantId"]
        ?? throw new InvalidOperationException("AzureAd:TenantId staat niet in de configuratie.");

    /// <summary>
    /// Een ondertekend token zoals Entra het na een device-code-aanmelding zou afgeven.
    /// </summary>
    /// <param name="rollen">
    /// De waarden van het <c>roles</c>-claim. Leeg betekent: een toewijzing zonder rol — de val uit
    /// <c>stand-van-zaken.md</c> waarbij je wél binnenkomt maar geen rolclaim hebt.
    /// </param>
    /// <param name="naam">Het <c>name</c>-claim, of <c>null</c> om het weg te laten.</param>
    /// <param name="doelgroep">De <c>aud</c>, of <c>null</c> voor de appId van de registratie.</param>
    /// <param name="scope">De <c>scp</c>, of <c>null</c> om die weg te laten.</param>
    /// <returns>Het token, zonder het woord <c>Bearer</c> ervoor.</returns>
    public string Token(
        string[]? rollen = null,
        string? naam = "Marcel de Graaf",
        string? doelgroep = null,
        string? scope = "Uren.Boeken")
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["oid"] = "2f1c8a55-0000-4000-8000-abcdefabcdef",
            ["tid"] = Tenant,
        };

        if (naam is not null)
        {
            claims["name"] = naam;
        }

        if (scope is not null)
        {
            claims["scp"] = scope;
        }

        if (rollen is { Length: > 0 })
        {
            claims["roles"] = rollen;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = $"https://sts.windows.net/{Tenant}/",
            Audience = doelgroep ?? Doelgroep,
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(_sleutel, SecurityAlgorithms.HmacSha256),
            Claims = claims,
        });
    }

    /// <summary>Een client die een boeking verstuurt met dit token.</summary>
    /// <param name="token">Het token, of <c>null</c> om er geen mee te sturen.</param>
    /// <returns>De client.</returns>
    public HttpClient Client(string? token)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        }

        return client;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Laatste configuratiebron, dus deze wint van appsettings.json. Bootstrap uit, zodat er ook
        // langs een pad dat we niet zien niets naar Cosmos gaat.
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["PortalData:Bootstrap"] = "false" }));

        builder.ConfigureTestServices(services =>
        {
            foreach (var dienst in services
                .Where(dienst => dienst.ServiceType == typeof(IHostedService)
                    && dienst.ImplementationType is { } type
                    && type.Namespace == typeof(PortalDataOptions).Namespace)
                .ToList())
            {
                services.Remove(dienst);
            }

            services.AddSingleton<IMcpHoursWriter>(Schrijver);

            // Ná de PostConfigure van het portaal, dus deze wint. Alleen de herkomst van de sleutel en
            // de uitgever worden vervangen; RoleClaimType, NameClaimType en het mappen van claims
            // blijven staan zoals AddHoursApi ze zet.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.MetadataAddress = null!;
                options.ConfigurationManager = null!;
                options.RequireHttpsMetadata = false;

                options.TokenValidationParameters.IssuerSigningKey = _sleutel;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                options.TokenValidationParameters.ValidateIssuer = false;
                options.TokenValidationParameters.IssuerValidator = null;
                options.TokenValidationParameters.IssuerValidatorUsingConfiguration = null;
            });
        });
    }
}

/// <summary>
/// De urenopslag, in het geheugen. Onthoudt wat het endpoint heeft aangeboden.
/// </summary>
/// <remarks>
/// Bewust géén tweede implementatie van de regels: hij zet zelf geen stand en geen bron, want dat is
/// werk van <c>CosmosMcpHoursWriter</c> en dat wordt daar gemeten. Wat hij teruggeeft is het document
/// zoals de opslag het zou hebben teruggegeven, met de waarden die de echte schrijver erop zet — dus
/// als een test hier per ongeluk <c>approved</c> in ziet, komt dat uit de test en niet uit het
/// portaal. Daarom staat de stand hier als constante en niet als iets dat het endpoint meegeeft: het
/// endpoint kán hem niet meegeven, en dat is de eigenschap die getest wordt.
/// </remarks>
public sealed class Vasteurenschrijver : IMcpHoursWriter
{
    /// <summary>Het schrijfrecht waarmee er is geboekt, of <c>null</c> als er niet is geboekt.</summary>
    public CustomerWriteScope? Bewijs { get; private set; }

    /// <summary>De boeking die het endpoint heeft aangeboden.</summary>
    public HourBooking? Aangeboden { get; private set; }

    /// <summary>Hoe vaak de opslag is aangesproken.</summary>
    public int Aanroepen { get; private set; }

    /// <summary>Wat de opslag teruggeeft; <c>null</c> voor "vastgelegd".</summary>
    public PortalWriteResult<HourEntryDocument>? Antwoord { get; set; }

    /// <summary>Zet de teller en de vastgelegde aanroep terug.</summary>
    public void Reset()
    {
        Bewijs = null;
        Aangeboden = null;
        Aanroepen = 0;
        Antwoord = null;
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<HourEntryDocument>> BookPendingAsync(
        CustomerWriteScope scope,
        HourBooking booking,
        CancellationToken cancellationToken = default)
    {
        Bewijs = scope;
        Aangeboden = booking;
        Aanroepen++;

        if (Antwoord is { } vast)
        {
            return Task.FromResult(vast);
        }

        // De invoercontrole van de echte schrijver, want die hoort bij het schrijfpad en niet bij het
        // endpoint: zonder deze regel zou een test op een afwijzing hier stil slagen.
        if (booking.Validate() is { } melding)
        {
            return Task.FromResult(PortalWriteResult<HourEntryDocument>.Invalid(melding));
        }

        var moment = DateTimeOffset.Parse("2026-08-21T09:15:00Z", System.Globalization.CultureInfo.InvariantCulture);

        return Task.FromResult(PortalWriteResult<HourEntryDocument>.Saved(new HourEntryDocument
        {
            Id = PortalDocumentIds.HourEntry("mcp-20260821091500000-abcdef01"),
            PartitionKey = scope.CustomerId,
            CustomerId = scope.CustomerId,
            Month = booking.Month,
            Category = booking.Category,
            Note = booking.Note,
            Hours = booking.Hours,
            Source = HourEntrySource.Mcp,
            Status = HourEntryStatus.Pending,
            By = booking.By,
            CreatedAt = moment,
            CreatedBy = HourBookingApiContract.CreatedBy,
        }));
    }
}

/// <summary>
/// Alle tests op het urenendpoint delen één draaiend portaal.
/// </summary>
/// <remarks>
/// Eén host in plaats van één per testklasse. Dat is niet alleen sneller: elke host bouwt zijn eigen
/// tokenvalidatie op, en die hosts naast elkaar laten leven is precies de opstelling waarin de
/// gedeelde <c>CryptoProviderFactory</c> van een sleutel stuk kan gaan. Zie
/// <see cref="Urenapihost"/>.
/// </remarks>
[CollectionDefinition(Naam)]
public sealed class Urenapicollectie : ICollectionFixture<Urenapihost>
{
    /// <summary>De naam waarmee een testklasse zich bij deze collectie aanmeldt.</summary>
    public const string Naam = "Urenapi";
}
