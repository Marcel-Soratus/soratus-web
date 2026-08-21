using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry;
using Soratus.Agents.Telemetry.HostedAgents;
using Soratus.Agents.Telemetry.Logging;

namespace Soratus.Agents.AspNetCore.Internal;

/// <summary>
/// Legt elke aanroep van een endpoint met <see cref="SoratusAgentMetadata"/> vast als één run.
/// </summary>
/// <remarks>
/// <para><strong>Waarom middleware en niet een endpoint-filter.</strong> Een endpoint-filter zou
/// nul extra regels kosten — hij kan mee in dezelfde <c>WithSoratusAgent</c>-aanroep — en dat is
/// aantrekkelijk. Twee dingen wegen zwaarder. Een filter draait alleen om een minimal-API-handler:
/// een MVC-controller met dezelfde metadata krijgt dan een hartslag en nooit een run, en dat is de
/// duurste fout die dit contract kan maken, want in het portaal ziet dat eruit als een dienst die
/// niemand aanroept. En een filter is klaar zodra de handler zijn resultaat teruggeeft, terwijl het
/// wegschrijven van dat resultaat er nog na komt; bij een chat die zijn antwoord in stukjes stuurt
/// is dat het grootste deel van de tijd. Middleware kost één regel in de opstartcode en heeft
/// geen van beide gaten.</para>
///
/// <para><strong>Wat de afloop bepaalt.</strong> Een ontsnapte uitzondering is
/// <see cref="RunResult.Failed"/>. Een antwoord met een 5xx-code ook, want dan is de dienst zelf
/// omgevallen — ook als iemand de uitzondering onderweg netjes heeft afgehandeld. Een 4xx is dat
/// níet: dan heeft de aanroeper iets verkeerd meegestuurd en heeft de dienst juist goed gewerkt
/// door het te weigeren. Wie dat anders wil, roept <c>Fail</c> zelf aan op de run.</para>
///
/// <para><strong>Een afgebroken aanroep is geen storing.</strong> Verbreekt de aanroeper de
/// verbinding — een dichtgeklapt tabblad tijdens een chat — dan is dat geen falen van de dienst, en
/// een rode stip in het portaal zou een klant onterecht laten schrikken. De run sluit dan met zijn
/// gewone uitkomst en er komt één <c>warn</c>-regel bij die zegt wat er gebeurde. Wat het contract
/// hiervoor mist is een eigen uitkomst naast <c>ok</c>, <c>failed</c> en <c>skipped</c>; dat is
/// gemeld en niet zelf verzonnen.</para>
/// </remarks>
internal sealed class SoratusAgentRunMiddleware(
    RequestDelegate next,
    ISoratusHostedAgents agents,
    ILogger<SoratusAgentRunMiddleware> logger)
{
    /// <summary>Vanaf deze code geldt het antwoord als een storing van de dienst zelf.</summary>
    internal const int FailureStatusCode = 500;

    /// <summary>Verwerkt één verzoek.</summary>
    /// <param name="context">Het verzoek.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        SoratusAgentMetadata? metadata = context.GetEndpoint()?.Metadata.GetMetadata<SoratusAgentMetadata>();
        if (metadata is null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        ISoratusHostedAgent agent = agents.GetOrAdd(metadata.Declaration);

        await using IAgentRun run = await agent
            .StartRunAsync(metadata.Declaration.Trigger, context.RequestAborted)
            .ConfigureAwait(false);

        // Zodat de handler er zonder eigen leidingwerk bij kan om verwerkte regels te melden.
        context.Features.Set(run);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.AgentWarning(
                "run.aborted",
                "De aanroeper heeft de verbinding verbroken voordat het werk klaar was.");
            throw;
        }
        catch (Exception exception)
        {
            run.Fail(exception);
            throw;
        }

        if (context.Response.StatusCode >= FailureStatusCode)
        {
            run.Fail(
                $"Http{context.Response.StatusCode}",
                $"De aanroep is met foutcode {context.Response.StatusCode} beëindigd.");
        }
    }
}
