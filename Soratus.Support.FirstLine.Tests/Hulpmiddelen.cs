using System.Net;
using System.Text;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace Soratus.Support.FirstLine.Tests;

/// <summary>
/// Een <see cref="HttpMessageHandler"/> die het verzoek bewaart en een vast antwoord teruggeeft.
/// </summary>
/// <remarks>
/// Het verzoek bewaren en niet alleen antwoorden: de helft van wat hier gemeten wordt gaat over wat er
/// de deur uit gaat — welke velden er in het lichaam staan, dat er een bearer-token bij zit, en welk
/// adres er wordt aangeroepen.
/// </remarks>
internal sealed class Vasteafhandelaar(
    HttpStatusCode status,
    string lichaam,
    TimeSpan? wachten = null) : HttpMessageHandler
{
    /// <summary>Het lichaam van het laatste verzoek, of <c>null</c> als er niets is gevraagd.</summary>
    internal string? Verzoeklichaam { get; private set; }

    /// <summary>Het adres van het laatste verzoek.</summary>
    internal Uri? Adres { get; private set; }

    /// <summary>Het meegestuurde autorisatieschema en de waarde.</summary>
    internal string? Autorisatie { get; private set; }

    /// <summary>Hoe vaak er is aangeroepen. Nul is een meting en geen afwezigheid.</summary>
    internal int Aanroepen { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Aanroepen++;
        Adres = request.RequestUri;
        Autorisatie = request.Headers.Authorization?.ToString();

        if (request.Content is not null)
        {
            Verzoeklichaam = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        if (wachten is { } duur)
        {
            await Task.Delay(duur, cancellationToken);
        }

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(lichaam, Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>Een fabriek die altijd dezelfde afhandelaar gebruikt.</summary>
internal sealed class Vasteclientfabriek(HttpMessageHandler afhandelaar) : IHttpClientFactory
{
    /// <summary>De naam waarmee er om een client is gevraagd.</summary>
    internal string? Naam { get; private set; }

    public HttpClient CreateClient(string name)
    {
        Naam = name;

        // disposeHandler: false — de kiezer sluit zijn HttpClient af, en dan zou de afhandelaar na de
        // eerste aanroep onbruikbaar zijn en zou een tweede meting op een ObjectDisposedException
        // stuklopen in plaats van op de code die wordt gemeten.
        return new HttpClient(afhandelaar, disposeHandler: false);
    }
}

/// <summary>Een credential die een vast token teruggeeft; er wordt niets bij Entra opgehaald.</summary>
internal sealed class Vastecredential(string token = "vast-token") : TokenCredential
{
    public override AccessToken GetToken(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken) =>
        new(token, DateTimeOffset.UtcNow.AddHours(1));

    public override ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}

/// <summary>
/// Een logger die zijn regels bewaart, inclusief de uitzondering.
/// </summary>
/// <remarks>
/// Bestaat om één eis te kunnen meten die anders niet te meten is: dat de vraag van de klant en de
/// feiten van de klant nergens in een logregel belanden. Zie
/// <c>EerstelijnaanroepTests.ErKomtGeenKlanttekstInEenLogregel</c>.
/// </remarks>
internal sealed class Testlogger<T> : ILogger<T>
{
    /// <summary>De regels, in volgorde.</summary>
    internal List<string> Regels { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        Regels.Add($"{logLevel}: {formatter(state, exception)} {exception}");
    }
}
