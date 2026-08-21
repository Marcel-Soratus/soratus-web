using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Soratus.Portal.Api;
using Soratus.Portal.Security;

namespace Soratus.Portal.Tests.Urenapi;

/// <summary>
/// Antiforgery: het endpoint heeft er niets aan, loopt er niet op stuk, en opent geen gat.
/// </summary>
/// <remarks>
/// <para>Drie vragen, en ze hebben drie verschillende antwoorden nodig. Loopt een bearer-POST zonder
/// antiforgery-token stuk op de middleware die voor de formulieren staat? Is dat een eigenschap van
/// dit endpoint of een toevalligheid van deze .NET-versie? En is er, om dit endpoint te laten werken,
/// iets uitgezet dat de formulieren nodig hebben?</para>
///
/// <para>De eerste is een gedragsvraag en die wordt door de echte pijplijn beantwoord. De tweede is een
/// vraag over metadata: de middleware valideert alleen als het endpoint
/// <see cref="IAntiforgeryMetadata"/> draagt met validatie aan, en dat komt er bij een
/// minimal-API-endpoint alleen op als het formulierinvoer bindt. De derde wordt beantwoord door te
/// kijken of de <em>andere</em> endpoints hun validatie nog hebben — als die weg was, zou dit endpoint
/// ook werken en zou niemand het merken.</para>
/// </remarks>
[Collection(Urenapicollectie.Naam)]
public sealed class UrenApiAntiforgeryTests
{
    private readonly Urenapihost _host;

    /// <summary>Neemt het draaiende portaal aan.</summary>
    /// <param name="host">Het portaal.</param>
    public UrenApiAntiforgeryTests(Urenapihost host)
    {
        _host = host;
        _host.Schrijver.Reset();
    }

    /// <summary>
    /// Een POST met JSON en een bearer-token, zonder antiforgery-token, komt door de échte pijplijn.
    /// </summary>
    /// <remarks>
    /// Dit is de test die het waard is om te hebben. Verandert het standaardgedrag van een volgende
    /// .NET-versie — bijvoorbeeld doordat validatie de standaard wordt voor élke POST — dan wordt deze
    /// test rood en niet de eerste urenboeking van een operator. Dat is ook de reden dat er geen
    /// <c>DisableAntiforgery()</c> op het endpoint staat: die zou vandaag niets doen en morgen de
    /// validatie ook uitzetten als dit endpoint ooit formulierinvoer gaat binden.
    /// </remarks>
    [Fact]
    public async Task EenBearerpostZonderAntiforgerytokenKomtDoor()
    {
        using var client = _host.Client(_host.Token([PortalRoles.Operator]));

        using var antwoord = await client.PostAsJsonAsync(
            HourBookingApiContract.Path,
            new
            {
                cid = _host.EersteKlant,
                month = "2026-08",
                hours = 1.25m,
                category = "Beheer",
                note = "Logregels van de nachtelijke run nagekeken.",
            });

        Assert.Equal(HttpStatusCode.Created, antwoord.StatusCode);
        Assert.Equal(1, _host.Schrijver.Aanroepen);
    }

    /// <summary>
    /// Het endpoint vraagt geen antiforgery-validatie, en de rest van het portaal doet dat nog wel.
    /// </summary>
    /// <remarks>
    /// De tweede assertie is de belangrijkste van de twee. Zou <c>app.UseAntiforgery()</c> zijn
    /// weggehaald of de mapping ervoor zijn verplaatst om dit endpoint aan de praat te krijgen, dan
    /// slaagt de test hierboven ook — en dan is er een gat in élk formulier van het portaal. Deze
    /// assertie is er zodat die route niet stil open kan.
    /// </remarks>
    [Fact]
    public void HetEndpointVraagtGeenValidatieEnDeFormulierenNogWel()
    {
        var endpoints = _host.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .ToArray();

        var uren = Assert.Single(
            endpoints,
            endpoint => endpoint.RoutePattern.RawText == HourBookingApiContract.Path);

        Assert.NotEqual(true, uren.Metadata.GetMetadata<IAntiforgeryMetadata>()?.RequiresValidation);

        Assert.Contains(
            endpoints,
            endpoint => endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>()?.RequiresValidation == true);
    }
}
