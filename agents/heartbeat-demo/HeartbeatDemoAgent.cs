using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry;
using Soratus.Agents.Telemetry.Logging;
using Soratus.Agents.Telemetry.Scheduling;

namespace Soratus.Agents.HeartbeatDemo;

/// <summary>
/// De referentie-agent. Doet niets nuttigs, maar bewijst de hele keten: runs, drie logniveaus,
/// een echte uitzondering met stacktrace en een teruggedraaide transactie.
/// </summary>
public sealed class HeartbeatDemoAgent(
    ISoratusAgent agent,
    ILogger<HeartbeatDemoAgent> logger,
    IOptions<HeartbeatDemoOptions> options) : IScheduledAgent
{
    private readonly HeartbeatDemoOptions _options = options.Value;

    /// <inheritdoc />
    public async Task ExecuteRunAsync(IAgentRun run, CancellationToken cancellationToken)
    {
        // Het minuutnummer als runnummer: daarmee ligt het gedrag vast bij een gegeven seed en
        // klok, ook over een herstart heen. Geen Random zonder seed.
        long minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var random = new Random(unchecked(_options.Seed + (int)minute));
        int batch = random.Next(0, 8);

        if (batch == 0)
        {
            // Niets te doen. Dat kan de bibliotheek niet raden — van buiten ziet een leeg
            // wachtinterval er hetzelfde uit als een vastgelopen lus — dus we melden het zelf.
            agent.ReportLifecycle(AgentLifecycle.IdleWaiting);
            logger.AgentEvent("batch.empty", "Geen documenten aangetroffen; niets te doen.", new { minute });
            return;
        }

        agent.ReportLifecycle(AgentLifecycle.Running);
        logger.AgentEvent("batch.started", $"Batch met {batch} documenten opgepakt.", new { batch, minute });

        for (int i = 1; i <= batch; i++)
        {
            await Task.Delay(random.Next(20, 120), cancellationToken);
            string documentId = $"INV-{minute % 10_000:0000}-{i}";

            if (random.Next(6) == 0)
            {
                logger.AgentWarning("api.retry", $"Document {documentId} kwam traag terug; opnieuw geprobeerd.",
                    new { documentId, attempt = 2 });
            }

            if (random.Next(12) == 0)
            {
                run.FailedItems();
                logger.AgentError("document.rejected", $"Document {documentId} is afgekeurd.",
                    Afkeuring(documentId), new { documentId });
                continue;
            }

            run.Processed();
            logger.AgentEvent("document.processed", $"Factuur {documentId} verwerkt.", new { docId = documentId });
        }

        if (_options.LongLineRate > 0 && minute % _options.LongLineRate == 0)
        {
            // Twee gevallen, en ze testen tegengestelde dingen. Deze moet de knip overléven: één
            // ononderbroken regel van ruim duizend tekens, zodat de logtabel op afbreken getest
            // blijft worden. Zou hij geknipt worden, dan is de afbreektest zijn onderwerp kwijt.
            logger.AgentEvent("payload.dump", string.Join(' ',
                    Enumerable.Repeat("Deze regel is met opzet belachelijk lang zodat het portaal kan bewijzen dat hij netjes afbreekt.", 14)),
                new { lines = 1, purpose = "afbreektest van de logtabel — moet heel blijven" });

            // En deze moet juist geknipt worden: één zin, daarna regelafbrekingen met wat op een
            // stacktrace lijkt. In msg hoort alleen de eerste regel te overleven; de rest hoort
            // onder msgOverflow te staan en is dus operator-only.
            logger.AgentEvent("payload.trace", string.Join('\n',
                    ["De voorraadregels van deze batch konden niet volledig worden gevalideerd.",
                     .. Enumerable.Repeat(
                        "   at Soratus.Demo.Validators.StockLineValidator.Validate(StockLine line) in /src/Demo/Validators/StockLineValidator.cs:line 42",
                        16)]),
                new { lines = 17, purpose = "kniptest op de regelovergang — moet geknipt worden" });
        }

        if (_options.FailureRate > 0 && minute % _options.FailureRate == 0)
        {
            run.MarkRolledBack();
            ContactBoekhouding(batch);
        }
    }

    /// <summary>
    /// Levert een uitzondering die echt gegooid is geweest, want alleen dan zit er een
    /// stacktrace in — een uitzondering die je alleen construeert heeft er geen.
    /// </summary>
    private static Exception Afkeuring(string documentId)
    {
        try
        {
            throw new InvalidDataException($"Bedrag ontbreekt op {documentId}.");
        }
        catch (InvalidDataException exception)
        {
            return exception;
        }
    }

    /// <summary>Gooit een echte uitzondering, zodat de stacktrace in <c>extra</c> ergens over gaat.</summary>
    private static void ContactBoekhouding(int batch) =>
        throw new HttpRequestException($"Het boekhoudpakket gaf 502 terug bij het boeken van {batch} documenten.");
}
