using System.Net.Http.Headers;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Soratus.Mcp.Uren;

/// <summary>
/// Zet op elk verzoek naar het portaal een bearer-token van de identiteit van de aanroeper.
/// </summary>
/// <remarks>
/// <para>Er is geen sleutel, geen client secret en geen service-identiteit. Deze server draait in
/// Claude Code op iemands machine, dus het token hoort van díe persoon te zijn: dan is "wie heeft
/// dit geboekt" een echt antwoord, en dan is toegang intrekken één handeling in Entra in plaats van
/// een geheim dat op onbekend hoeveel machines staat.</para>
///
/// <para><c>DefaultAzureCredential</c> pakt op een ontwikkelmachine de aanmelding van de Azure CLI
/// of Visual Studio op. Dat is bewust de hele autorisatieketen: het portaal controleert op dat token
/// de app-rol <c>Operator</c>, precies zoals het dat op het scherm doet, en §2 zegt dat uren boeken
/// operatorwerk is.</para>
/// </remarks>
internal sealed class PortalTokenHandler(TokenCredential credential, IOptions<UrenOptions> options)
    : DelegatingHandler
{
    /// <summary>
    /// Hoe lang vóór het verlopen er een nieuw token wordt gehaald.
    /// </summary>
    /// <remarks>
    /// De credentials in Azure.Identity cachen zelf, maar niet allemaal: <c>AzureCliCredential</c>
    /// start voor elke aanvraag een <c>az</c>-proces, en dat kost bijna een seconde. Eén boeking is
    /// twee verzoeken (metagegevens en de POST), dus zonder deze cache betaalt elke aanroep die
    /// seconde dubbel.
    /// </remarks>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly UrenOptions _options = options.Value;

    private AccessToken? _cached;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        AccessToken token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AccessToken> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsFresh(_cached))
        {
            return _cached!.Value;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsFresh(_cached))
            {
                return _cached!.Value;
            }

            AccessToken token = await credential
                .GetTokenAsync(new TokenRequestContext([_options.Scope]), cancellationToken)
                .ConfigureAwait(false);

            _cached = token;
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsFresh(AccessToken? token) =>
        token is { } value && value.ExpiresOn - RefreshMargin > DateTimeOffset.UtcNow;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gate.Dispose();
        }

        base.Dispose(disposing);
    }
}
