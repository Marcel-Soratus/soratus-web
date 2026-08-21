using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// De bufferlaag tussen de agent en de opslag. Alles wat de agent produceert gaat hier in een
/// begrensd kanaal; één achtergrondlus haalt het eruit en schrijft het weg.
/// </summary>
/// <remarks>
/// Dit is de kern van de belofte dat telemetrie een agent nooit omlegt. Schrijven naar het
/// kanaal is een niet-blokkerende <c>TryWrite</c>: is de buffer vol, dan valt de regel weg en
/// gaat de agent verder. Wachten op Cosmos zou betekenen dat een storing bij ons het werk van
/// de klant stillegt, en geheugen laten volgroeien zou betekenen dat de agent uiteindelijk
/// alsnog omvalt — alleen later en onduidelijker.
///
/// Runs en registraties hebben een eigen kanaal. Anders drukt een logstorm precies de
/// documenten weg die het portaal nodig heeft om status te bepalen.
/// </remarks>
internal sealed class TelemetryWriter : BackgroundService
{
    private readonly SoratusTelemetryOptions _options;
    private readonly ITelemetrySink _sink;
    private readonly ILogger<TelemetryWriter> _logger;

    private readonly Channel<LogRecord> _logs;
    private readonly Channel<PendingDocument> _documents;
    private readonly CancellationTokenSource _abort = new();

    private long _droppedLogs;
    private long _droppedDocuments;
    private DateTimeOffset _lastDropWarning = DateTimeOffset.MinValue;
    private DateTimeOffset _lastConfigurationWarning = DateTimeOffset.MinValue;
    private Task? _pumps;

    public TelemetryWriter(
        IOptions<SoratusTelemetryOptions> options,
        ITelemetrySink sink,
        ILogger<TelemetryWriter> logger)
    {
        _options = options.Value;
        _sink = sink;
        _logger = logger;

        _logs = Channel.CreateBounded<LogRecord>(new BoundedChannelOptions(Math.Max(1, _options.LogBufferCapacity))
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

        _documents = Channel.CreateBounded<PendingDocument>(
            new BoundedChannelOptions(Math.Max(1, _options.DocumentBufferCapacity))
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    /// <summary>Zet een logregel in de buffer. Blokkeert nooit.</summary>
    internal void Enqueue(LogRecord log)
    {
        if (!_logs.Writer.TryWrite(log))
        {
            long dropped = Interlocked.Increment(ref _droppedLogs);
            ReportDrops(dropped);
        }
    }

    /// <summary>Zet een run in de buffer. Blokkeert nooit.</summary>
    internal void Enqueue(RunRecord run)
    {
        if (!_documents.Writer.TryWrite(new PendingDocument(null, run)))
        {
            Interlocked.Increment(ref _droppedDocuments);
            ReportDrops(Interlocked.Read(ref _droppedLogs));
        }
    }

    /// <summary>Zet een registratie in de buffer. Blokkeert nooit.</summary>
    internal void Enqueue(AgentRegistration registration)
    {
        if (!_documents.Writer.TryWrite(new PendingDocument(registration, null)))
        {
            Interlocked.Increment(ref _droppedDocuments);
            ReportDrops(Interlocked.Read(ref _droppedLogs));
        }
    }

    /// <summary>
    /// Schrijft een registratie meteen weg, buiten de buffer om. Alleen voor het laatste
    /// document bij afsluiten, wanneer de achtergrondlus al kan zijn gestopt.
    /// </summary>
    internal Task WriteRegistrationDirectAsync(AgentRegistration registration, CancellationToken cancellationToken) =>
        WithRetryAsync(
            ct => _sink.UpsertRegistrationAsync(registration, ct),
            "registratie",
            cancellationToken);

    /// <summary>
    /// Of het leegdraaipad gewapend is: staan de pompen, dan heeft <see cref="StopAsync"/> iets om
    /// op te wachten.
    /// </summary>
    /// <remarks>
    /// <para>Bestaat alleen om te meten, en dat is met opzet. De fout die dit veld blootlegt is
    /// <em>niet</em> te betrappen met een test op het gedrag: die hangt af van de vraag of de planner
    /// het lijf van <c>ExecuteAsync</c> heeft laten lopen vóór het afsluiten, en een test die van de
    /// planner afhangt is flaky. Gemeten: met de reparatie weggehaald bleven alle 82 tests zes runs
    /// op rij groen op Windows, terwijl dezelfde code op een Linux-runner rood ging.</para>
    ///
    /// <para>Wat er dan overblijft is de invariant zelf vastpinnen in plaats van zijn gevolg: als
    /// <c>StartAsync</c> terugkomt, moet dit waar zijn. Dat is deterministisch, en het gaat rood
    /// zodra iemand de pompen terugzet in <c>ExecuteAsync</c>.</para>
    /// </remarks>
    internal bool DrainPathArmed => _pumps is not null;

    /// <summary>
    /// Start de pompen, en wel hiér.
    /// </summary>
    /// <remarks>
    /// <para><strong>Ze stonden in <see cref="ExecuteAsync"/>, en dat verloor telemetrie.</strong>
    /// Het lijf van <c>ExecuteAsync</c> van een <c>BackgroundService</c> is niet gegarandeerd
    /// gelopen als <c>StartAsync</c> terugkomt — dat hangt van de planner af. Stopte een host
    /// binnen dat venster, dan was <c>_pumps</c> nog <c>null</c>, sloeg
    /// <see cref="StopAsync"/> het leegdraaien over, en was de inhoud van de kanalen weg.</para>
    ///
    /// <para>Dat is geen randgeval maar het gewone geval voor een kortlevende agent: een taak die
    /// twee seconden werkt en afsluit, verliest zo <em>al</em> zijn telemetrie. En stil — de kanalen
    /// worden netjes afgesloten, er valt niets in de drop-teller, en er staat geen waarschuwing in
    /// de log. In het portaal ziet dat uit als een agent die niets te melden had.</para>
    ///
    /// <para>Gevonden doordat één van de drie regelovergangvarianten van
    /// <c>MsgKnipViaSchrijfpadenTests</c> op een Linux-runner rood ging met een lége sink terwijl de
    /// andere twee slaagden. Dat was dus geen verschil per regelovergang: die variant verloor de
    /// race. Op Windows haalde het doorschrijfinterval van 20 ms het altijd.</para>
    ///
    /// <para>Dezelfde vorm is eerder in <c>HostedAgentsRegistrationService</c> gemeten en daar op
    /// dezelfde manier opgelost: wat vóór het einde van <c>StartAsync</c> moet zijn gebeurd, hoort
    /// in <c>StartAsync</c>.</para>
    /// </remarks>
    /// <param name="cancellationToken">Annuleringstoken van de host.</param>
    /// <returns>De taak van het opstarten.</returns>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Bewust niet op stoppingToken lopen: bij afsluiten willen we leegdraaien, niet afbreken.
        // StopAsync sluit de kanalen, waarna de lussen op eigen kracht eindigen.
        _pumps ??= Task.WhenAll(PumpLogsAsync(_abort.Token), PumpDocumentsAsync(_abort.Token));

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Geeft de taak terug die <see cref="StartAsync"/> al heeft gemaakt. De null-vergevende
    /// operator kan hier: <c>BackgroundService.StartAsync</c> roept dit aan, en dat is de
    /// <c>base</c>-aanroep aan het einde van onze eigen <c>StartAsync</c> — dus na de toewijzing.
    /// </remarks>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _pumps!;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logs.Writer.TryComplete();
        _documents.Writer.TryComplete();

        if (_pumps is not null)
        {
            Task drained = await Task.WhenAny(_pumps, Task.Delay(_options.ShutdownDrainTimeout, CancellationToken.None))
                .ConfigureAwait(false);

            if (drained != _pumps)
            {
                _logger.LogWarning(
                    "Telemetriebuffer was na {Timeout} nog niet leeg; de rest wordt niet weggeschreven.",
                    _options.ShutdownDrainTimeout);
            }
        }

        long droppedLogs = Interlocked.Read(ref _droppedLogs);
        long droppedDocuments = Interlocked.Read(ref _droppedDocuments);
        if (droppedLogs > 0 || droppedDocuments > 0)
        {
            _logger.LogWarning(
                "Deze agent heeft in totaal {DroppedLogs} logregels en {DroppedDocuments} documenten laten vallen.",
                droppedLogs,
                droppedDocuments);
        }

        await _abort.CancelAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        _abort.Dispose();
        base.Dispose();
    }

    private async Task PumpLogsAsync(CancellationToken cancellationToken)
    {
        int batchSize = Math.Clamp(_options.LogBatchSize, 1, 100);
        var batch = new List<LogRecord>(batchSize);

        while (!cancellationToken.IsCancellationRequested)
        {
            batch.Clear();

            try
            {
                if (!await _logs.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Even blijven verzamelen, zodat er een batch wegschrijft en niet één document per
            // regel. Bij afsluiten is het kanaal al gesloten en loopt dit meteen leeg.
            DateTimeOffset deadline = DateTimeOffset.UtcNow + _options.FlushInterval;
            while (batch.Count < batchSize)
            {
                if (_logs.Reader.TryRead(out LogRecord? log))
                {
                    batch.Add(log);
                    continue;
                }

                TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                try
                {
                    using var linger = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linger.CancelAfter(remaining);
                    if (!await _logs.Reader.WaitToReadAsync(linger.Token).ConfigureAwait(false))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (batch.Count == 0)
            {
                continue;
            }

            LogRecord[] payload = [.. batch];
            await WithRetryAsync(
                    ct => _sink.WriteLogsAsync(payload, ct),
                    $"batch van {payload.Length} logregels",
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task PumpDocumentsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            PendingDocument document;
            try
            {
                if (!await _documents.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                if (!_documents.Reader.TryRead(out document))
                {
                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (document.Run is { } run)
            {
                await WithRetryAsync(
                        ct => _sink.UpsertRunAsync(run, ct),
                        $"run {run.Id}",
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (document.Registration is { } registration)
            {
                await WithRetryAsync(
                        ct => _sink.UpsertRegistrationAsync(registration, ct),
                        "registratie",
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Probeert een schrijfactie een paar keer met oplopende wachttijd en geeft daarna op.
    /// Opgeven is hier de juiste keuze: eeuwig blijven proberen laat de buffer vollopen en
    /// verplaatst het probleem alleen.
    /// </summary>
    private async Task WithRetryAsync(
        Func<CancellationToken, Task> write,
        string what,
        CancellationToken cancellationToken)
    {
        TimeSpan delay = _options.RetryBaseDelay;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await write(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (TelemetryConfigurationException exception)
            {
                // Opnieuw proberen heeft geen zin en zou de oorzaak begraven onder drie
                // identieke waarschuwingen. Eén keer luid melden, dan hooguit elk kwartier.
                if (DateTimeOffset.UtcNow - _lastConfigurationWarning >= TimeSpan.FromMinutes(15))
                {
                    _lastConfigurationWarning = DateTimeOffset.UtcNow;
                    _logger.LogError(exception, "Telemetrie kan niet worden weggeschreven door een inrichtingsfout.");
                }

                return;
            }
            catch (Exception exception)
            {
                if (attempt > _options.WriteRetries)
                {
                    _logger.LogError(
                        exception,
                        "Telemetrie voor {What} kon na {Attempts} pogingen niet worden weggeschreven en is weggegooid.",
                        what,
                        attempt);
                    return;
                }

                // Bij tussentijdse pogingen alleen het bericht, niet de uitzondering. De
                // Cosmos-SDK hangt een halve pagina diagnostiek aan elke fout; drie keer per
                // batch maakt de log van de host onleesbaar precies wanneer je hem nodig hebt.
                _logger.LogWarning(
                    "Telemetrie voor {What} mislukte (poging {Attempt}: {Reason}); nieuwe poging over {Delay}.",
                    what,
                    attempt,
                    exception.Message,
                    delay);

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                delay *= 2;
            }
        }
    }

    /// <summary>
    /// Meldt het vollopen van de buffer één keer, en daarna hoogstens elke vijf minuten. Eén
    /// waarschuwing per weggevallen regel zou het probleem verergeren dat we melden.
    /// </summary>
    private void ReportDrops(long droppedLogs)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - _lastDropWarning < TimeSpan.FromMinutes(5))
        {
            return;
        }

        _lastDropWarning = now;
        _logger.LogWarning(
            "De telemetriebuffer is vol; er zijn inmiddels {DroppedLogs} logregels en {DroppedDocuments} documenten gevallen. De agent draait door.",
            droppedLogs,
            Interlocked.Read(ref _droppedDocuments));
    }

    /// <summary>Eén wachtend document: óf een registratie, óf een run.</summary>
    private readonly record struct PendingDocument(AgentRegistration? Registration, RunRecord? Run);
}
