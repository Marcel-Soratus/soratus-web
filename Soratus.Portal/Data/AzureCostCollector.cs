using System.Globalization;
using Microsoft.Extensions.Options;

namespace Soratus.Portal.Data;

/// <summary>
/// De dagelijkse taak die het Azure-verbruik per klant per maand ophaalt en wegschrijft (§4,
/// <c>kosten-collector</c>).
/// </summary>
/// <remarks>
/// <para><strong>Waarom hij hier draait en niet als eigen dienst.</strong> Alles wat hij nodig heeft
/// staat al in het portaal en nergens anders: de managed identity die als enige
/// <c>Cost Management Reader</c> op de resource group heeft (B5 van het haalbaarheidsonderzoek), het
/// schrijfrecht op de portaalopslag, de klantenlijst, en de lees-, volledigheids- en rekencode van deze
/// map. Een eigen deployable zou vier dingen vragen die vandaag geen van alle bestaan — een eigen
/// identity, een rolverlening in elk abonnement waar een klant leeft, een eigen Cosmos-verlening en een
/// eigen uitrol — en er één ding voor teruggeven dat we niet nodig hebben: een tweede aanroeper. En een
/// tweede aanroeper is precies wat je hier níet wil, want <strong>het budget hangt aan de aanroeper en
/// niet aan de scope</strong> (de header heet <c>clienttype-retry-after</c>). Van alles wat dit werk kan
/// gebruiken, is het portaal het enige dat het recht al heeft.</para>
///
/// <para><strong>Wat dat kost, en wat het antwoord daarop is: het portaal kan meer dan één instantie
/// hebben, en dan draaien er twee collectors.</strong> Dat is niet alleen dubbel werk — ze verdelen de
/// emmer tot geen van beide nog een bedrag krijgt. Daarom claimt de run zichzelf: een document met een
/// van de dag afgeleide id, geschreven vóór de eerste aanroep, met een <c>CreateItemAsync</c> en geen
/// upsert. De tweede instantie krijgt een <c>409</c> en doet niets. Dat is de vorm die
/// <c>Soratus.Portal/Mail/</c> voor de dubbele mail gebruikt; zie
/// <see cref="AzureCostRunDocument"/> voor het verschil in betekenis — daar is het een slot op een
/// onherhaalbare handeling, hier op een schaars budget.</para>
///
/// <para><strong>Vier gedragsregels die uit de metingen volgen en niet uit een voorkeur.</strong></para>
///
/// <list type="number">
///   <item><description>
///     <strong>Een 429 is geen mislukte run.</strong> Bij de gemeten uitvalskans zou de collector
///     permanent amber staan en zou de storingsmelder van fase 6 gaan mailen over een gezonde agent.
///     Hij logt als <c>warn</c> met <c>api.retry</c> en de run slaagt.
///   </description></item>
///   <item><description>
///     <strong>Een mislukte aanroep schrijft niets.</strong> Geen document met
///     <see cref="AzureCostState.Unknown"/> erin: de lezing van gisteren blijft staan mét haar eigen
///     <see cref="AzureCostDocument.MeasuredAt"/>, en dat is wat §32 als het eerlijkere antwoord
///     aanwijst — het bewaarde getal is werkelijk gemeten, de mislukte aanroep heeft niets gemeten.
///     Er is precies één uitzondering, en dat is de derde regel.
///   </description></item>
///   <item><description>
///     <strong>Een antwoord dat er wél was en niet te lezen viel, wórdt weggeschreven — als
///     <see cref="AzureCostState.Unknown"/> met een reden.</strong> Punt 33 wijst dat uitdrukkelijk
///     aan: een onleesbaar bedrag werpt en wordt geen nul, en de aanroeper hoort er <c>Unknown</c> van
///     te maken. Dat het daarmee een goed getal van gisteren overschrijft is de juiste richting: het
///     betekent dat onze lezer niet meer bij de API past, en dat is een defect dat zichtbaar hoort te
///     zijn. Van de twee mogelijke fouten — geen bedrag of een te laag bedrag — is alleen de eerste
///     zichtbaar.
///   </description></item>
///   <item><description>
///     <strong>De volledigheid wordt gecontroleerd en het draaimoment niet verschoven.</strong> Dat is
///     de aanbeveling van §6 van het onderzoek, en de controle bestaat al:
///     <see cref="AzureCostCompleteness.Judge"/>. Er wordt hier geen tweede geschreven en er staat
///     nergens een percentage of een drempel.
///   </description></item>
/// </list>
///
/// <para><strong>Wat een overgeslagen dag kost: niets.</strong> Elke run leest de hele maand, dus een
/// dag die door een herstart of door een claim van een andere instantie wegvalt, wordt de volgende nacht
/// ingehaald. Ook voor de volledigheid maakt het niet uit: <see cref="AzureCostCompleteness"/> eist dat
/// de laatste dag van de maand er staat én dat er minstens twee dagen ná de maand is gemeten, en een
/// maand die op de 3e wordt gelezen heet net zo goed volledig als een maand die op de 2e wordt gelezen.
/// Dat is het eigenlijke argument voor een dagclaim zonder verlooptijd — er is niets dat verloopt.</para>
///
/// <para>Er wordt bij het opstarten niet meteen gemeten, maar gewacht tot het eerstvolgende
/// <see cref="AzureCostOptions.RunHourUtc"/>. Een uitrol is anders een aanroep, en een dag met vijf
/// uitrollen zou een dag met vijf runs zijn.</para>
/// </remarks>
internal sealed class AzureCostCollector(
    IAzureCostCollectorStore store,
    IAzureCostClient client,
    IOptions<AzureCostOptions> options,
    TimeProvider timeProvider,
    ILogger<AzureCostCollector> logger) : BackgroundService
{
    private readonly AzureCostOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            // Luidruchtig, want dit is de vlag waarmee een omgeving stil zonder kostenmeting kan
            // draaien. Zie AzureCostOptions.Enabled: hij staat standaard aan en in Development uit.
            logger.LogInformation(
                "PortalCosts:Enabled staat uit. Het Azure-verbruik wordt niet opgehaald; het "
                + "facturatiescherm toont wat er in de opslag staat.");
            return;
        }

        logger.LogInformation(
            "De kostencollector draait dagelijks om {Hour:D2}:00 UTC, met {Pause} s tussen twee "
            + "aanroepen aan Cost Management.",
            _options.RunHourUtc,
            _options.PauseSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await SleepAsync(stoppingToken).ConfigureAwait(false))
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
                // Een run die omvalt mag het portaal niet meenemen: een BackgroundService die een
                // uitzondering laat ontsnappen stopt de host, en er is niets aan een mislukte
                // kostenmeting dat het bekijken van een agentstatus in de weg staat. Morgen opnieuw.
                logger.LogError(
                    exception,
                    "De kostenrun is afgebroken. Er is niets half weggeschreven — elke maand is een "
                    + "eigen schrijfactie — en de volgende run leest de hele maand opnieuw.");
            }
        }
    }

    /// <summary>
    /// Eén dagelijkse run: claimen, en per klant per maand meten.
    /// </summary>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het aantal maanden dat is weggeschreven.</returns>
    /// <remarks>
    /// <para><c>internal</c> en met een uitkomst, zodat een test één run kan doen zonder tot 04:00 te
    /// wachten. Dat is dezelfde afweging als bij elke klok in dit portaal: een drempel die alleen door
    /// te wachten te bereiken is, wordt niet getest.</para>
    ///
    /// <para>Werpt niet. Een run die omvalt mag het portaal niet meenemen — een
    /// <see cref="BackgroundService"/> die een uitzondering laat ontsnappen stopt de host — en er is
    /// niets aan een mislukte kostenmeting dat het bekijken van een agentstatus in de weg staat.</para>
    /// </remarks>
    internal async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            // Dezelfde vlag als in ExecuteAsync, en met opzet twee keer. Daar is het een
            // planningsbeslissing — er wordt niet gewacht op een moment dat toch niets doet — en hier is
            // het de garantie. Dit is de enige methode die werk doet en ze is <c>internal</c>, dus een
            // tweede aanroeper is mogelijk; de vlag hoort te gelden op de plek waar de aanroepen
            // ontstaan. Eén veld, één betekenis, dus geen tweede waarheid.
            //
            // Gevonden met een mutatie: met alleen de controle in ExecuteAsync was er geen test die de
            // vlag kon bewijzen zonder de dagelijkse lus te draaien, en een test die dat probeert hangt
            // in plaats van rood te worden.
            return 0;
        }

        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        IReadOnlyList<AzureCostTarget> targets;

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
                "De kostencollector kon de klantenlijst niet uit de opslag lezen. Er is niets "
                + "gemeten en er is niets weggeschreven; de vorige lezingen blijven staan.");
            return 0;
        }

        var scoped = new List<(AzureCostTarget Target, AzureScope Scope)>();

        foreach (var target in targets)
        {
            if (AzureScope.TryParse(target.Scope, out var scope) && scope is not null)
            {
                scoped.Add((target, scope));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(target.Scope))
            {
                // Onbruikbaar en niet leeg. Dat kan alleen als iemand het document met de hand heeft
                // aangepast — de schrijfkant valideert — en het is het enige geval waarin een klant
                // een scope heeft en toch niet wordt gemeten. Dat hoort niet stil te zijn.
                logger.LogWarning(
                    "De Azure-scope van klant {CustomerId} is niet te lezen en wordt daarom niet "
                    + "bevraagd: {Reason}",
                    target.CustomerId,
                    AzureScope.Validate(target.Scope));
            }
        }

        if (scoped.Count == 0)
        {
            // Geen fout en geen stilte. Vandaag is dit de normale toestand: er is één echte klant en
            // zijn scope moet met de hand worden vastgelegd, want bestaande documenten worden niet
            // gemigreerd — uit `envFull` raden zou precies de fout maken waartegen AzureScope bestaat.
            logger.LogInformation(
                "Geen enkele klant heeft een Azure-scope vastgelegd. Er is niets te meten; het "
                + "facturatiescherm meldt per klant dat er niets is ingericht.");
            return 0;
        }

        if (!await store.ClaimAsync(today, scoped.Count, cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        var written = 0;
        var first = true;

        foreach (var (target, scope) in scoped)
        {
            foreach (var month in await MonthsAsync(target.CustomerId, today, cancellationToken)
                .ConfigureAwait(false))
            {
                if (!first)
                {
                    // De stilte tussen twee aanroepen. Gemeten: drieënvijftig seconden was niet genoeg
                    // en na tien minuten stilte kwam er alsnog een 429 omdat er een tweede aanroeper in
                    // dezelfde tenant meedeed. Zie AzureCostOptions.PauseSeconds.
                    await Task
                        .Delay(_options.Pause, timeProvider, cancellationToken)
                        .ConfigureAwait(false);
                }

                first = false;

                if (await MeasureAsync(target.CustomerId, scope, month, today, cancellationToken)
                    .ConfigureAwait(false))
                {
                    written++;
                }
            }
        }

        logger.LogInformation(
            "Kostenrun van {Day} klaar: {Written} maand(en) vastgelegd over {Customers} klant(en).",
            today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            written,
            scoped.Count);

        return written;
    }

    /// <summary>
    /// Welke maanden er voor deze klant worden opgevraagd, in de volgorde waarin ze worden gedaan.
    /// </summary>
    /// <param name="customerId">De klantslug.</param>
    /// <param name="today">Vandaag, in UTC.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De maanden als <c>jjjj-MM</c>.</returns>
    /// <remarks>
    /// <para><strong>De vorige maand vóór de lopende, en dat is geen smaak.</strong> De vorige maand is
    /// de maand die gefactureerd gaat worden; de lopende is een concept dat morgen opnieuw wordt
    /// gelezen. Loopt het budget halverwege de run leeg, dan is de maand die je wilt hebben degene die
    /// je het eerst hebt gedaan.</para>
    ///
    /// <para><strong>En de vorige maand wordt overgeslagen zodra hij volledig is.</strong> Een maand op
    /// <see cref="AzureCostState.Measured"/> kan niet meer veranderen — de volledigheidsregel eist dat
    /// de laatste dag er staat en dat er twee dagen ná de maand is gemeten, en aan beide is niets meer
    /// te doen. Hem opnieuw opvragen kost een aanroep uit het schaarse ding; de puntlezing die dat
    /// vaststelt kost ongeveer één RU uit het goedkope. Voor achtentwintig van de eenendertig dagen van
    /// een maand halveert dat het aantal aanroepen per klant.</para>
    ///
    /// <para>Verder terug dan één maand gaat de collector niet. Een maand die drie maanden geleden
    /// nooit is gemeten, wordt door deze taak niet ingehaald: dat is een handmatige inhaalslag en geen
    /// nachtelijke gewoonte, want hij kost per maand per klant een aanroep uit hetzelfde budget en zou
    /// de metingen van vannacht verdringen. Gemeld als open punt.</para>
    /// </remarks>
    private async Task<IReadOnlyList<string>> MonthsAsync(
        string customerId,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var running = Month(today);
        var previous = Month(new DateOnly(today.Year, today.Month, 1).AddDays(-1));

        try
        {
            var state = await store
                .StateAsync(customerId, previous, cancellationToken)
                .ConfigureAwait(false);

            return state == AzureCostState.Measured ? [running] : [previous, running];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // De besparing lukt niet. Dan wordt de vorige maand gewoon opgevraagd: een aanroep te veel
            // is duur, maar een maand die nooit definitief wordt is duurder.
            logger.LogWarning(
                exception,
                "De opgeslagen toestand van {Month} bij klant {CustomerId} was niet te lezen; die "
                + "maand wordt daarom opnieuw opgevraagd.",
                previous,
                customerId);

            return [previous, running];
        }
    }

    /// <summary>
    /// Meet één maand van één klant en schrijft het resultaat weg als er iets te schrijven is.
    /// </summary>
    /// <param name="customerId">De klantslug.</param>
    /// <param name="scope">De scope van deze klant.</param>
    /// <param name="month">De maand als <c>jjjj-MM</c>.</param>
    /// <param name="today">Vandaag, in UTC.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns><c>true</c> als er een document is weggeschreven.</returns>
    /// <remarks>
    /// <para><strong><paramref name="today"/> gaat in UTC naar
    /// <see cref="AzureCostCompleteness.Judge"/> en niet in de tijdzone van de lezer, die de
    /// parameterbeschrijving daar noemt.</strong> De verrekentermijn is er een van dágen
    /// (<see cref="AzureCostCompleteness.SettlementDays"/> is twee) en het verschil tussen UTC en
    /// Nederlandse tijd is ten hoogste twee uur, dus de keuze kan geen enkel oordeel omdraaien. Wat hem
    /// beslist: Azure boekt in UTC, en een oordeel over de boeking van Azure dat van de Nederlandse
    /// zomertijd afhangt zou een afhankelijkheid zijn die er niet is.</para>
    /// </remarks>
    private async Task<bool> MeasureAsync(
        string customerId,
        AzureScope scope,
        string month,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        AzureCostAnswer answer;

        try
        {
            answer = await client
                .ReadAsync(scope, month, today, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "api.retry — het Azure-verbruik van {CustomerId} over {Month} is niet opgehaald. De "
                + "vorige lezing blijft staan.",
                customerId,
                month);
            return false;
        }

        if (answer.Kind == AzureCostAnswerKind.NotAvailable)
        {
            // Regel 2: niets wegschrijven. De lezing van gisteren blijft staan met haar eigen tijdstip
            // erbij, en dat is eerlijker dan een verse mislukking. Warn en niet error: regel 1 — een
            // 429 is geen mislukte run.
            logger.LogWarning(
                "api.retry — geen bedrag voor {CustomerId} over {Month} na {Calls} respons(en): "
                + "{Reason} De vorige lezing blijft staan.",
                customerId,
                month,
                answer.Calls,
                answer.Reason);
            return false;
        }

        var now = timeProvider.GetUtcNow();

        if (answer.Kind == AzureCostAnswerKind.Unreadable)
        {
            await store
                .WriteAsync(
                    new AzureCostWrite(
                        customerId,
                        month,
                        AzureCostState.Unknown,
                        [],
                        Currency: null,
                        scope.Path,
                        now,
                        CoversThrough: null,
                        answer.Reason),
                    cancellationToken)
                .ConfigureAwait(false);

            logger.LogError(
                "Het antwoord van Cost Management over {CustomerId} / {Month} was niet te lezen: "
                + "{Reason} Er staat nu 'onbekend' en met opzet geen bedrag.",
                customerId,
                month,
                answer.Reason);

            return true;
        }

        var verdict = AzureCostCompleteness.Judge(month, answer.Days, today);

        // Regels én geen dag binnen de maand: dan zijn de bedragen van een andere periode. De regel van
        // Judge negeert zulke dagen en noemt de maand daarmee leeg — de veilige kant — maar dan
        // zou hier een document ontstaan dat "geen regels" zegt naast een subtotaal dat wél bestaat,
        // want AzureCostReading.Subtotal is de som van de regels. Dat is geen toestand maar een defect,
        // en het hoort als zodanig op het scherm.
        if (verdict.State == AzureCostState.NoLines && answer.Lines.Count > 0)
        {
            await store
                .WriteAsync(
                    new AzureCostWrite(
                        customerId,
                        month,
                        AzureCostState.Unknown,
                        [],
                        Currency: null,
                        scope.Path,
                        now,
                        CoversThrough: null,
                        $"Cost Management gaf bedragen die buiten {month} vallen."),
                    cancellationToken)
                .ConfigureAwait(false);

            logger.LogError(
                "Cost Management gaf voor {CustomerId} / {Month} {Lines} dienst(en) met bedragen "
                + "buiten die maand. Er staat nu 'onbekend' en met opzet geen bedrag.",
                customerId,
                month,
                answer.Lines.Count);

            return true;
        }

        await store
            .WriteAsync(
                new AzureCostWrite(
                    customerId,
                    month,
                    verdict.State,
                    answer.Lines,
                    answer.Currency,
                    scope.Path,
                    now,
                    verdict.CoversThrough,
                    Failure: null),
                cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <summary>De maandsleutel van een dag.</summary>
    /// <param name="day">De dag.</param>
    /// <returns>De maand als <c>jjjj-MM</c>.</returns>
    /// <remarks>
    /// Dezelfde vorm als <c>HourMonths</c> en om dezelfde reden: Cosmos vergelijkt dit als tekst, en op
    /// <c>jjjj-MM</c> werkt een bereikfilter terwijl hij op elke andere vorm stil verkeerd sorteert.
    /// </remarks>
    private static string Month(DateOnly day) =>
        day.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// Wacht tot het eerstvolgende draaimoment.
    /// </summary>
    /// <param name="stoppingToken">Het stoptoken van de host.</param>
    /// <returns><c>false</c> als het portaal aan het afsluiten is.</returns>
    private async Task<bool> SleepAsync(CancellationToken stoppingToken)
    {
        var now = timeProvider.GetUtcNow();
        var target = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero)
            .AddHours(_options.RunHourUtc);

        if (target <= now)
        {
            target = target.AddDays(1);
        }

        try
        {
            await Task.Delay(target - now, timeProvider, stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
