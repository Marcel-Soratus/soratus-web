using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;
using Soratus.Portal.Alerts;
using Soratus.Portal.Data;
using Soratus.Portal.Mail;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Tests.Maandoverzicht;

namespace Soratus.Portal.Tests.Storingsmelder;

/// <summary>
/// De testdubbels van de storingsmelder, en de echte <see cref="AgentFaultAlerter"/> erop.
/// </summary>
/// <remarks>
/// <para>Drie dubbels en één echte klasse, dezelfde afweging als bij de mailkant: de bron (die praat
/// met Cosmos van elke klant), de markeringen (die praten met Cosmos) en de verzendlaag (die praat met
/// Azure) zijn vervangen, en de klasse die de volgorde bepaalt is de echte. Wat er in die klasse te
/// meten valt <em>is</em> de volgorde — ontdubbelen vóór afremmen, afremmen vóór claimen, claimen vóór
/// versturen — en een test die haar vervangt meet zijn eigen kopie daarvan.</para>
///
/// <para><strong>De verzenddubbel is die van de mailkant en geen tweede.</strong> Dat is het punt van
/// de gedeelde laag: als de melder een eigen dubbel nodig had, dan gebruikte hij de laag niet.</para>
///
/// <para>De klok staat stil op <see cref="Testgegevens.Nu"/>, dus elke drempel is met een verschoven
/// moment te bereiken zonder te wachten.</para>
/// </remarks>
internal sealed class Storingsmelderbank
{
    /// <summary>Zet de bank op.</summary>
    /// <param name="opties">De melderinstellingen, of <c>null</c> voor de standaard met één ontvanger.</param>
    /// <param name="mailopties">De mailinstellingen, of <c>null</c> voor "ingericht en niet droog".</param>
    public Storingsmelderbank(AgentAlertOptions? opties = null, PortalMailOptions? mailopties = null)
    {
        Opties = opties ?? Standaard();
        Mailopties = mailopties ?? Maandoverzichtbank.Ingericht();
        Bron = new Vastestoringsbron();
        Markeringen = new Vastemarkeringen();
        Verzender = new Vasteverzender(Mailopties);
        Klok = new Verzetbareklok(Testgegevens.Nu);

        Melder = new AgentFaultAlerter(
            Bron,
            Markeringen,
            Verzender,
            Options.Create(Opties),
            Options.Create(Mailopties),
            Klok,
            NullLogger<AgentFaultAlerter>.Instance);
    }

    /// <summary>De melderinstellingen.</summary>
    public AgentAlertOptions Opties { get; }

    /// <summary>De mailinstellingen.</summary>
    public PortalMailOptions Mailopties { get; }

    /// <summary>Wat de melder te lezen krijgt.</summary>
    public Vastestoringsbron Bron { get; }

    /// <summary>De markeringen in het geheugen.</summary>
    public Vastemarkeringen Markeringen { get; }

    /// <summary>De verzendlaag, gedeeld met de mailkant.</summary>
    public Vasteverzender Verzender { get; }

    /// <summary>De klok, te verzetten zonder te wachten.</summary>
    public Verzetbareklok Klok { get; }

    /// <summary>De echte melder.</summary>
    public AgentFaultAlerter Melder { get; }

    /// <summary>Eén ronde.</summary>
    /// <returns>Het aantal meldingen dat is verstuurd of aangeboden.</returns>
    public Task<int> RondeAsync() => Melder.RunAsync(CancellationToken.None);

    /// <summary>De melderinstellingen zoals de tests ze standaard gebruiken.</summary>
    /// <returns>De instellingen.</returns>
    public static AgentAlertOptions Standaard() => new()
    {
        Recipients = ["storingen@soratus.com"],
    };

    /// <summary>Een agent met een mislukte laatste run: <see cref="AgentStatus.Failed"/>.</summary>
    /// <param name="agentName">De technische naam.</param>
    /// <param name="startedAt">Wanneer het proces startte, of <c>null</c> voor zes uur terug.</param>
    /// <returns>De momentopname.</returns>
    public static AgentSnapshot Mislukt(string agentName, DateTimeOffset? startedAt = null) =>
        new(
            Registratie(agentName, Testgegevens.Nu, startedAt),
            Testgegevens.Run(RunResult.Failed, Testgegevens.Nu - TimeSpan.FromMinutes(1), agentName)
                with
                {
                    ErrorType = "SoratusAgent.Sync.ValidationException",
                    ErrorMessage = "Regel 41 mist een grootboekrekening.",
                });

    /// <summary>Een agent die te lang zwijgt: <see cref="AgentStatus.Degraded"/>.</summary>
    /// <param name="agentName">De technische naam.</param>
    /// <param name="startedAt">Wanneer het proces startte, of <c>null</c> voor zes uur terug.</param>
    /// <param name="silence">Hoe lang hij zwijgt, of <c>null</c> voor ruim boven de meldgrens.</param>
    /// <returns>De momentopname.</returns>
    public static AgentSnapshot Zwijgt(
        string agentName,
        DateTimeOffset? startedAt = null,
        TimeSpan? silence = null) =>
        new(
            Registratie(
                agentName,
                Testgegevens.Nu - (silence ?? AgentStatusThresholds.Alert + TimeSpan.FromMinutes(1)),
                startedAt),
            Testgegevens.Run(RunResult.Ok, Testgegevens.Nu - TimeSpan.FromHours(1), agentName));

    /// <summary>Een gezonde agent.</summary>
    /// <param name="agentName">De technische naam.</param>
    /// <returns>De momentopname.</returns>
    public static AgentSnapshot Gezond(string agentName) =>
        new(
            Registratie(agentName, Testgegevens.Nu, startedAt: null),
            Testgegevens.Run(RunResult.Ok, Testgegevens.Nu - TimeSpan.FromMinutes(2), agentName));

    /// <summary>Wat de bron over één klant teruggeeft.</summary>
    /// <param name="agents">De agents.</param>
    /// <param name="customerId">De klantslug.</param>
    /// <param name="name">De klantnaam.</param>
    /// <returns>Het leesresultaat.</returns>
    public static CustomerAgentScan Klant(
        IReadOnlyList<AgentSnapshot> agents,
        string customerId = "acme-logistiek",
        string name = "Acme Logistiek") =>
        new(customerId, name, agents, Unavailable: null);

    private static AgentRegistration Registratie(
        string agentName,
        DateTimeOffset heartbeat,
        DateTimeOffset? startedAt) =>
        Testgegevens.Registratie(heartbeat, agentName: agentName)
            with { StartedAt = startedAt ?? Testgegevens.Nu - TimeSpan.FromHours(6) };
}

/// <summary>Een klok die je verzet in plaats van erop te wachten.</summary>
/// <remarks>
/// Een eigen klok naast <c>Stilstaandeklok</c> uit de mailmap: die staat stil, en hier is het verzetten
/// juist de meting. Het herhaalvenster is zes uur, en zes uur wachten is geen test.
/// </remarks>
internal sealed class Verzetbareklok(DateTimeOffset moment) : TimeProvider
{
    /// <summary>Het huidige moment. Te zetten.</summary>
    public DateTimeOffset Nu { get; set; } = moment;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Nu;

    /// <summary>Zet de klok vooruit.</summary>
    /// <param name="span">Hoeveel.</param>
    public void Vooruit(TimeSpan span) => Nu += span;
}

/// <summary>De bron, met wat de test erin zet.</summary>
internal sealed class Vastestoringsbron : IAgentFaultSource
{
    /// <summary>Wat er wordt teruggegeven.</summary>
    public List<CustomerAgentScan> Klanten { get; } = [];

    /// <summary>Hoe vaak er is gelezen.</summary>
    public int Aanroepen { get; private set; }

    /// <summary>
    /// De fout die het lezen oplevert, of <c>null</c> voor een geslaagde lezing.
    /// </summary>
    /// <remarks>
    /// Bestaat om te meten wat er van een ronde wordt als het lezen omvalt. Dat is sinds fase 6 niet
    /// meer alleen een logregel: de ronde is een run van de agent <c>storingsmelder</c>, en een ronde
    /// die omvalt is een mislukte run. Dezelfde hook en dezelfde reden als
    /// <c>Vastekostenopslag.Leesfout</c>.
    /// </remarks>
    public Exception? Leesfout { get; set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<CustomerAgentScan>> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        Aanroepen++;

        if (Leesfout is not null)
        {
            throw Leesfout;
        }

        return Task.FromResult<IReadOnlyList<CustomerAgentScan>>([.. Klanten]);
    }
}

/// <summary>
/// De markeringen in het geheugen, met de eigenschap die het ontwerp draagt: een tweede claim op
/// dezelfde agent botst.
/// </summary>
/// <remarks>
/// <para><strong>De botsing wordt hier nagebouwd en dat is een beperking van deze test.</strong> In
/// productie komt de <c>409</c> van Cosmos op een <c>CreateItemAsync</c> met een afgeleide sleutel en de
/// <c>412</c> op een <c>ReplaceItemAsync</c> met een etag; hier komt hij uit een
/// <see cref="Dictionary{TKey,TValue}"/>. Wat deze dubbel dus bewijst is dat de melder de claim vóór de
/// mail zet en op een botsing niets verstuurt — niet dat Cosmos die botsing werkelijk geeft. Dezelfde
/// beperking en dezelfde reden als bij <c>Vasteverzendbevestigingen</c>; de 409-eigenschap zelf is
/// elders in dit project gemeten (<c>infra.md</c>, de klant-batch).</para>
/// </remarks>
internal sealed class Vastemarkeringen : IAgentAlertStore
{
    private readonly Dictionary<string, AgentAlertDocument> _documenten = new(StringComparer.Ordinal);
    private int _etags;

    /// <summary>Hoe vaak er is geclaimd, geslaagd of niet.</summary>
    public int Claims { get; private set; }

    /// <summary>Hoe vaak er een uitkomst is vastgelegd.</summary>
    public int Bevestigingen { get; private set; }

    /// <summary>Hoe vaak er is afgesloten.</summary>
    public int Afsluitingen { get; private set; }

    /// <summary>
    /// Laat elke claim botsen, alsof een andere instantie hem net heeft gedaan.
    /// </summary>
    public bool AndereInstantieWasEerder { get; set; }

    /// <summary>
    /// De agents waarvan de claim botst, alsof een andere instantie precies die net heeft gedaan.
    /// </summary>
    /// <remarks>
    /// Bestaat voor het gedeeltelijke geval: van drie diensten in één host claimt de ene instantie er
    /// twee en de andere één. Dat is de race die §42 niet dicht, en de eigenschap die dan moet gelden is
    /// dat elke mail precies noemt wat hij heeft geclaimd — anders noemen twee mails dezelfde dienst.
    /// </remarks>
    public HashSet<string> BotstOp { get; } = new(StringComparer.Ordinal);

    /// <summary>Wat er nu staat.</summary>
    /// <param name="customerId">De klantslug.</param>
    /// <param name="agentName">De agentnaam.</param>
    /// <returns>De markering, of <c>null</c>.</returns>
    public AgentAlertDocument? Document(string customerId, string agentName) =>
        _documenten.TryGetValue(AgentAlertDocumentKeys.Id(customerId, agentName), out var document)
            ? document
            : null;

    /// <summary>Zet een markering neer alsof een vorige ronde hem heeft geschreven.</summary>
    /// <param name="document">De markering.</param>
    public void Bestaat(AgentAlertDocument document) =>
        _documenten[document.Id] = document with { ETag = $"etag-{++_etags}" };

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentAlertDocument>> MarkersAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AgentAlertDocument>>([.. _documenten.Values]);

    /// <inheritdoc />
    public Task<AgentAlertDocument?> ClaimAsync(
        AgentAlertClaim claim,
        CancellationToken cancellationToken = default)
    {
        Claims++;

        var id = AgentAlertDocumentKeys.Id(claim.CustomerId, claim.AgentName);
        var staat = _documenten.TryGetValue(id, out var bestaand) ? bestaand : null;

        // De twee botsingen die Cosmos geeft. Zonder claim.Existing is het een CreateItemAsync en
        // botst hij op het bestaan van het document; mét is het een ReplaceItemAsync en botst hij op
        // de etag.
        if (AndereInstantieWasEerder
            || BotstOp.Contains(claim.AgentName)
            || (claim.Existing is null && staat is not null)
            || (claim.Existing is not null
                && !string.Equals(claim.Existing.ETag, staat?.ETag, StringComparison.Ordinal)))
        {
            return Task.FromResult<AgentAlertDocument?>(null);
        }

        var lopend = claim.Existing is { ClearedAt: null } ? claim.Existing : null;

        var document = new AgentAlertDocument
        {
            Id = id,
            PartitionKey = PortalDocumentIds.ReservedPartitionKey,
            CustomerId = claim.CustomerId,
            AgentName = claim.AgentName,
            Status = claim.Status,
            NotifiedAt = claim.Now,
            FirstNotifiedAt = lopend?.FirstNotifiedAt ?? claim.Now,
            Notifications = (lopend?.Notifications ?? 0) + 1,
            NotifiedBy = "test",
            Delivery = MailDelivery.Unknown,
            ClearedAt = null,
            ETag = $"etag-{++_etags}",
        };

        _documenten[id] = document;

        return Task.FromResult<AgentAlertDocument?>(document);
    }

    /// <inheritdoc />
    public Task ConfirmAsync(
        AgentAlertDocument claimed,
        MailDelivery delivery,
        string? operationId,
        CancellationToken cancellationToken = default)
    {
        Bevestigingen++;

        if (_documenten.TryGetValue(claimed.Id, out var huidig)
            && string.Equals(huidig.ETag, claimed.ETag, StringComparison.Ordinal))
        {
            _documenten[claimed.Id] = huidig with
            {
                Delivery = delivery,
                OperationId = operationId,
                ETag = $"etag-{++_etags}",
            };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(
        AgentAlertDocument marker,
        DateTimeOffset clearedAt,
        CancellationToken cancellationToken = default)
    {
        Afsluitingen++;

        if (_documenten.TryGetValue(marker.Id, out var huidig)
            && string.Equals(huidig.ETag, marker.ETag, StringComparison.Ordinal))
        {
            _documenten[marker.Id] = huidig with
            {
                ClearedAt = clearedAt,
                ETag = $"etag-{++_etags}",
            };
        }

        return Task.CompletedTask;
    }
}
