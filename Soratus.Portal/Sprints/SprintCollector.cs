using Microsoft.Extensions.Options;
using Soratus.Portal.Views;

namespace Soratus.Portal.Sprints;

/// <summary>
/// De taak die de sprint per klant bij Azure DevOps ophaalt en wegschrijft (§4, <c>devops-sync</c>).
/// </summary>
/// <remarks>
/// <para><strong>Waarom er wordt verzameld en niet bij elke paginaweergave opgehaald.</strong> §3.4 zegt
/// "het portaal haalt bij openen de laatste status op" en §4 zet <c>devops-sync</c> op "elke 15 min". Die
/// twee spreken elkaar tegen; dit is de kant die het is geworden, met drie argumenten in gewicht:</para>
///
/// <list type="number">
///   <item><description>
///     <strong>§3.4 vraagt zelf het tijdstip van laatste ophalen op het scherm.</strong> Bij een ophaling
///     per paginaweergave is dat tijdstip altijd "nu", en dan zegt het niets. Dat veld heeft alleen
///     betekenis als de lezing ouder kan zijn dan de pagina — de spec vraagt er dus met dat ene veld om
///     dat dit een momentopname is.
///   </description></item>
///   <item><description>
///     <strong>Bij een mislukte ophaling is de vorige lezing mét tijdstip eerlijker dan een verse
///     mislukking</strong>, want die heeft niets gemeten. Dat is de regel van punt 32 en punt 39, en hij
///     geldt hier om een eigen reden: de vraag die een klant op dit scherm stelt is "schiet mijn werk op",
///     en "veertien minuten oud" beantwoordt die vraag terwijl een foutmelding hem niet beantwoordt. En
///     zonder opslag ís er geen vorige lezing om te tonen — een ophaling per paginaweergave kan die regel
///     dus niet volgen, hoe je hem ook programmeert.
///   </description></item>
///   <item><description>
///     <strong>Het aanroepbudget van DevOps is niet gemeten.</strong> Dat is geen reden om aan te nemen dat
///     het schaars is — de kostenlane heeft geleerd dat je in geen van beide richtingen mag aannemen — maar
///     het is een reden om de kant te kiezen waar het aantal aanroepen niet van het aantal openstaande
///     tabbladen afhangt. Eén operator met twee tabbladen trok de emmer van Cost Management leeg; of dat
///     hier kan is onbekend, en verzamelen maakt het onmogelijk in plaats van onwaarschijnlijk.
///   </description></item>
/// </list>
///
/// <para><strong>De prijs, eerlijk: het scherm loopt tot een kwartier achter, en dat is minder te
/// verdedigen dan bij de kosten.</strong> Daar loopt de bron zelf al acht uur achter, dus "live" bestaat
/// er niet. Hier bestaat live wél — DevOps weet het meteen — en het portaal kiest er bewust tegen. Wat dat
/// goedmaakt is dat het tijdstip op het scherm staat, en dat de vraag die dit scherm beantwoordt geen vraag
/// van minuten is.</para>
///
/// <para><strong>Deze taak schrijft, en dat maakt hem gevaarlijker op een ontwikkelmachine dan de
/// kostencollector.</strong> Die leest bij Azure; deze schrijft sprintdocumenten in de partitie van een
/// echte klant. Een laptop die aan blijft staan vult dus de opslag van een klant met wat het DevOps-token
/// van die ontwikkelaar mocht ophalen. Vandaar dat de registratie in <c>Program.cs</c> achter een
/// <c>IsDevelopment</c>-voorwaarde staat, in code en niet als vlag in <c>appsettings.Development.json</c> —
/// dezelfde vorm als bij de kostencollector, met een reden die een graad zwaarder is.</para>
///
/// <para><strong>Er wordt nooit teruggeschreven naar DevOps.</strong> §3.4, en het staat hier omdat dit de
/// enige klasse is die zowel het bord leest als de opslag schrijft. De richting is één kant op: agents maken
/// items aan in DevOps, het portaal haalt op en toont. Er is in deze klasse geen aanroep die iets bij DevOps
/// verandert, en <see cref="IDevOpsSprintClient"/> heeft er ook geen.</para>
///
/// <para><strong>Hij meldt zich niet als agent, en dat is een vervolgpunt en geen vergissing.</strong> §4
/// noemt <c>devops-sync</c> in de lijst met beheeragents, en de kostencollector publiceert zich inmiddels
/// wél. Die aankondiging staat in <c>Soratus.Portal/Platform/</c>, en die map is van een andere sessie; er
/// een declaratie bij schrijven zou twee sessies in hetzelfde bestand zetten. Wat er dus ontbreekt is de
/// zichtbaarheid — dat deze taak vannacht niet heeft gedraaid staat alleen in een logregel en niet als
/// laatste run naast een gepubliceerd plan. Gemeld.</para>
/// </remarks>
internal sealed class SprintCollector(
    ISprintCollectorStore store,
    IDevOpsSprintClient client,
    IOptions<SprintOptions> options,
    TimeProvider timeProvider,
    ILogger<SprintCollector> logger) : BackgroundService
{
    private readonly SprintOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            // Luidruchtig, want dit is de vlag waarmee een omgeving stil zonder sprintgegevens kan draaien.
            logger.LogInformation(
                "PortalSprints:Enabled staat uit. De sprint wordt niet opgehaald; het sprintscherm toont "
                + "wat er in de opslag staat, met het tijdstip erbij.");
            return;
        }

        logger.LogInformation(
            "De sprintcollector draait elke {Interval} minuut/minuten tegen {Endpoint} "
            + "(api-version {Version}). Een klant wordt overgeslagen als zijn lezing jonger is dan "
            + "{Freshness}.",
            _options.IntervalMinutes,
            _options.Endpoint,
            _options.ApiVersion,
            _options.Freshness);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Eerst wachten en dan werken, en niet andersom. Een uitrol is anders een ronde, en een dag met
            // vijf uitrollen zou vijf extra ronden zijn. Dezelfde keuze als bij de kostencollector.
            try
            {
                await Task
                    .Delay(_options.Interval, timeProvider, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await RunAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Een ronde die omvalt mag het portaal niet meenemen: een BackgroundService die een
                // uitzondering laat ontsnappen stopt de host, en er is niets aan een mislukte sprintlezing
                // dat het bekijken van een agentstatus in de weg staat. Over een kwartier opnieuw.
                logger.LogError(
                    exception,
                    "De sprintronde is afgebroken. Er is niets half weggeschreven — elke klant is een "
                    + "eigen schrijfactie — en de volgende ronde leest alles opnieuw.");
            }
        }
    }

    /// <summary>
    /// Eén ronde: per klant met een bruikbaar bord de sprint ophalen en wegschrijven.
    /// </summary>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het aantal klanten waarvoor er een lezing is weggeschreven.</returns>
    /// <remarks>
    /// <para><c>internal</c> en met een uitkomst, zodat een test één ronde kan doen zonder een kwartier te
    /// wachten. Dat is dezelfde afweging als bij elke klok in dit portaal: een drempel die alleen door te
    /// wachten te bereiken is, wordt niet getest.</para>
    ///
    /// <para><strong>De vlag staat hier óók, en met opzet twee keer.</strong> In
    /// <see cref="ExecuteAsync"/> is het een planningsbeslissing — er wordt niet gewacht op een moment dat
    /// toch niets doet — en hier is het de garantie. Dit is de enige methode die werk doet en ze is
    /// <c>internal</c>, dus een tweede aanroeper is mogelijk. Eén veld, één betekenis, dus geen tweede
    /// waarheid; twee plekken waar hij geldt, waarvan één te testen is. Dat is gat 3 uit punt 41, hier
    /// meteen dichtgezet: met de controle alleen in <see cref="ExecuteAsync"/> zou een test op de vlag de
    /// lus moeten starten, en met een klok die niet wacht draait die lus eindeloos — dan levert het negeren
    /// van de vlag geen rode test op maar een test die hangt.</para>
    ///
    /// <para>Werpt niet, om dezelfde reden als de <c>catch</c> in <see cref="ExecuteAsync"/>.</para>
    /// </remarks>
    internal async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return 0;
        }

        IReadOnlyList<SprintTarget> targets;

        try
        {
            targets = await store.TargetsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "De sprintcollector kon de klantenlijst niet uit de opslag lezen. Er is niets opgehaald "
                + "en er is niets weggeschreven; de vorige lezingen blijven staan.");
            return 0;
        }

        var scoped = new List<(SprintTarget Target, DevOpsScope Scope)>();

        foreach (var target in targets)
        {
            if (DevOpsScope.TryParse(target.Scope, out var scope) && scope is not null)
            {
                scoped.Add((target, scope));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(target.Scope))
            {
                // Onbruikbaar en niet leeg. Dat kan alleen als iemand het document met de hand heeft
                // aangepast — beide formulieren valideren — en het is het enige geval waarin een klant een
                // bord heeft en toch niet wordt opgehaald. Dat hoort niet stil te zijn.
                logger.LogWarning(
                    "Het DevOps-bord van klant {CustomerId} is niet te lezen en wordt daarom niet "
                    + "bevraagd: {Reason}",
                    target.CustomerId,
                    DevOpsScope.Validate(target.Scope));
            }
        }

        // De verhouding, en niet alleen het aantal. Zo is "vijf klanten, één bord" in het log te zien in
        // plaats van af te leiden — en dan valt op als er negen klanten zijn en nog steeds één bord.
        logger.LogInformation(
            "Sprintronde: {Scoped} van {Total} klant(en) heeft een bruikbaar DevOps-bord.",
            scoped.Count,
            targets.Count);

        if (scoped.Count == 0)
        {
            return 0;
        }

        var written = 0;

        foreach (var (target, scope) in scoped)
        {
            if (await SkipAsync(target.CustomerId, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (await ReadAsync(target.CustomerId, scope, cancellationToken).ConfigureAwait(false))
            {
                written++;
            }
        }

        return written;
    }

    /// <summary>
    /// Of deze klant wordt overgeslagen omdat zijn lezing nog vers is.
    /// </summary>
    /// <param name="customerId">De klantslug.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns><c>true</c> als er niet hoeft te worden opgehaald.</returns>
    /// <remarks>
    /// <para><strong>Dit is de wederzijdse uitsluiting tussen twee portaalinstanties, en geen slot.</strong>
    /// Zie <see cref="SprintOptions.FreshnessFactor"/> en <see cref="ISprintCollectorStore.ReadAtAsync"/>
    /// voor waarom het geen claimdocument is en wat het niet dekt.</para>
    ///
    /// <para><strong>Een mislukte puntlezing slaat niet over.</strong> Dat is de goede kant: een aanroep te
    /// veel is goedkoop, en een klant die nooit meer wordt opgehaald omdat de puntlezing struikelt is dat
    /// niet. Dezelfde afweging als bij de besparing in de kostencollector, waar een onleesbare toestand
    /// leidt tot "die maand wordt dan gewoon opnieuw opgevraagd".</para>
    /// </remarks>
    private async Task<bool> SkipAsync(string customerId, CancellationToken cancellationToken)
    {
        try
        {
            if (await store.ReadAtAsync(customerId, cancellationToken).ConfigureAwait(false) is not { } last)
            {
                return false;
            }

            var age = timeProvider.GetUtcNow() - last;

            if (age >= _options.Freshness)
            {
                return false;
            }

            // Information en geen warning: op een portaal met twee instanties is dit elk kwartier het
            // normale gedrag van de ene van de twee.
            logger.LogInformation(
                "De sprint van {CustomerId} is {Age} oud en dus jonger dan {Freshness}; deze ronde wordt "
                + "hij niet opnieuw opgehaald.",
                customerId,
                age,
                _options.Freshness);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Het tijdstip van de vorige sprintlezing van {CustomerId} was niet te lezen; hij wordt "
                + "daarom gewoon opgehaald.",
                customerId);

            return false;
        }
    }

    /// <summary>
    /// Leest de sprint van één klant en schrijft het resultaat weg als er iets te schrijven is.
    /// </summary>
    /// <param name="customerId">De klantslug.</param>
    /// <param name="scope">Het bord van deze klant.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns><c>true</c> als er een document is weggeschreven.</returns>
    /// <remarks>
    /// <para><strong>De dag komt uit de weergavezone van het portaal en niet uit UTC, en dat is precies
    /// omgekeerd aan de kostencollector.</strong> Daar gaat UTC naar de volledigheidscontrole omdat Azure in
    /// UTC boekt, en een oordeel over de boeking van Azure hoort niet van de Nederlandse zomertijd af te
    /// hangen. Hier is het andersom: een iteratie is een <em>kalenderperiode</em> die een mens op een bord
    /// heeft ingevuld, en op 1 september om 00:30 Nederlandse tijd is het in UTC nog 31 augustus. Zou UTC de
    /// dag bepalen, dan zou het portaal in dat halfuur de sprint van augustus als de huidige aanwijzen
    /// terwijl het bord september zegt. De grens tussen twee maandsprintjes zou dan twee uur na middernacht
    /// liggen, en dat is een grens die niemand heeft afgesproken.</para>
    ///
    /// <para><strong>Bij een mislukte aanroep wordt er niets weggeschreven.</strong> De vorige lezing blijft
    /// staan met haar eigen tijdstip erbij — punt 39 letterlijk. Zou hier een document met
    /// <see cref="SprintState.Unknown"/> worden geschreven, dan wist één geweigerd verzoek een sprint die er
    /// wél was.</para>
    ///
    /// <para><strong>Bij een onleesbaar antwoord wordt er wél geschreven</strong>, als
    /// <see cref="SprintState.Unknown"/> met de reden. Dat overschrijft dus een goede lezing van een
    /// kwartier eerder, en dat is de juiste richting: het betekent dat onze lezer niet meer bij de API past,
    /// en dat is een defect dat zichtbaar hoort te zijn. Van de twee mogelijke fouten — geen sprint of een
    /// sprint met te weinig items — is alleen de eerste zichtbaar.</para>
    /// </remarks>
    private async Task<bool> ReadAsync(
        string customerId,
        DevOpsScope scope,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, PortalTimeZone.Display).DateTime);

        SprintAnswer answer;

        try
        {
            answer = await client.ReadAsync(scope, today, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "api.retry — de sprint van {CustomerId} is niet opgehaald. De vorige lezing blijft staan.",
                customerId);
            return false;
        }

        if (answer.Kind == SprintAnswerKind.NotAvailable)
        {
            logger.LogWarning(
                "api.retry — geen sprint voor {CustomerId} na {Calls} respons(en): {Reason} De vorige "
                + "lezing blijft staan.",
                customerId,
                answer.Calls,
                answer.Reason);
            return false;
        }

        if (answer.Kind == SprintAnswerKind.Unreadable)
        {
            await store
                .WriteAsync(
                    new SprintWrite(
                        customerId,
                        SprintState.Unknown,
                        scope.Path,
                        now,
                        Sprint: null,
                        Items: [],
                        Undated: [],
                        Overlapping: [],
                        DatedCount: 0,
                        answer.Reason),
                    cancellationToken)
                .ConfigureAwait(false);

            logger.LogError(
                "Het antwoord van Azure DevOps over {CustomerId} was niet te gebruiken: {Reason} Er staat "
                + "nu 'niet opgehaald' en met opzet geen halve sprint.",
                customerId,
                answer.Reason);

            return true;
        }

        var choice = answer.Choice;

        await store
            .WriteAsync(
                new SprintWrite(
                    customerId,
                    choice.State,
                    scope.Path,
                    now,
                    choice.Current,
                    answer.Items,
                    Refs(choice.Undated),
                    Refs(choice.Overlapping),
                    choice.DatedCount,
                    Failure: null),
                cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <summary>Zet iteraties om in de verwijzingen die op het scherm komen.</summary>
    /// <param name="iterations">De iteraties, of <c>null</c>.</param>
    /// <returns>Naam en pad per iteratie.</returns>
    /// <remarks>
    /// Alleen naam en pad. De datums gaan niet mee: bij de iteraties zonder datums zijn ze er niet, en bij
    /// de overlappende is het aanwijzen van de namen genoeg om ze te kunnen corrigeren. Een veld meeslaan
    /// dat niemand leest is een veld waarvan de volgende lezer denkt dat het ergens voor dient.
    /// </remarks>
    private static IReadOnlyList<SprintIterationRef> Refs(IReadOnlyList<DevOpsIteration>? iterations) =>
    [
        .. (iterations ?? []).Select(iteration => new SprintIterationRef
        {
            Name = iteration.Name,
            Path = iteration.Path,
        }),
    ];
}
