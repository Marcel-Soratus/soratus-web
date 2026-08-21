using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soratus.Portal.Data;
using Soratus.Portal.Mail;
using Soratus.Portal.Security;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Maandoverzicht;

/// <summary>
/// De testdubbels van de mailkant, en de echte <see cref="MonthlyStatementService"/> erop.
/// </summary>
/// <remarks>
/// <para>Drie dubbels en één echte klasse. De opslag, de bedragen en de verzender zijn vervangen —
/// die praten met Cosmos, met de kostensessie en met Azure — en de dienst die de volgorde bepaalt is
/// de echte. Dat is met opzet de smalste vervanging die de vraag kan beantwoorden waar dit ontwerp
/// over gaat: gaat de claim vóór de mail, en wat gebeurt er bij een onbekende uitkomst.</para>
///
/// <para>De klok staat stil op <see cref="Testgegevens.Nu"/> (19 augustus 2026), dus de vorige maand
/// is juli 2026 en augustus is niet afgesloten. Die twee maanden zijn in deze tests de grens.</para>
/// </remarks>
internal sealed class Maandoverzichtbank
{
    /// <summary>De maand die volgens de stilstaande klok is afgesloten.</summary>
    public const string AfgeslotenMaand = "2026-07";

    /// <summary>De maand die volgens de stilstaande klok nog loopt.</summary>
    public const string LopendeMaand = "2026-08";

    private readonly ICustomerScopeResolver _resolver =
        Autorisatiebron.Resolver(Autorisatiebron.Standaard());

    /// <summary>
    /// Zet de bank op.
    /// </summary>
    /// <param name="bedragen">De bedragen die de kostenkant teruggeeft, of <c>null</c> voor geen meting.</param>
    /// <param name="opties">De mailinstellingen, of <c>null</c> voor "ingericht en niet droog".</param>
    /// <param name="zonderToegang">Laat de toegangslijst leeg: dan is er geen contactpersoon.</param>
    public Maandoverzichtbank(
        MonthlyStatementFigures? bedragen = null,
        PortalMailOptions? opties = null,
        bool zonderToegang = false)
    {
        Bedragen = bedragen ?? Volledig();
        Opties = opties ?? Ingericht();
        Opslag = new Vasteportaalopslag(zonderToegang: zonderToegang);
        Bevestigingen = new Vasteverzendbevestigingen();

        // De verzender kijkt bij elke aanroep of er al een claim staat. Dat is de enige manier om de
        // volgorde te meten in plaats van alleen de aantallen: claimen en versturen leveren beide een
        // teller op, en die tellers zijn hetzelfde ongeacht welke van de twee eerst gaat.
        Verzender = new Vasteverzender(Opties, maand => Bevestigingen.Document(maand) is not null);

        Dienst = new MonthlyStatementService(
            Options.Create(Opties),
            new Vastemaandbedragen(() => Bedragen),
            Opslag,
            Bevestigingen,
            Verzender,
            new Stilstaandeklok(Testgegevens.Nu),
            NullLogger<MonthlyStatementService>.Instance);
    }

    /// <summary>De bedragen die de kostenkant teruggeeft. Te wisselen tussen twee aanroepen.</summary>
    public MonthlyStatementFigures? Bedragen { get; set; }

    /// <summary>De mailinstellingen.</summary>
    public PortalMailOptions Opties { get; }

    /// <summary>De portaalopslag met klant, contract en toegangen.</summary>
    public Vasteportaalopslag Opslag { get; }

    /// <summary>De verzendbevestigingen.</summary>
    public Vasteverzendbevestigingen Bevestigingen { get; }

    /// <summary>De verzender.</summary>
    public Vasteverzender Verzender { get; }

    /// <summary>De echte dienst.</summary>
    public MonthlyStatementService Dienst { get; }

    /// <summary>Het schrijfrecht van de operator op de standaardklant.</summary>
    /// <returns>De scope.</returns>
    public async Task<CustomerWriteScope> SchrijfrechtAsync() =>
        await _resolver.ResolveWriteAsync(Testprincipals.Operator(), Vasteportaalopslag.Standaardklant)
        ?? throw new InvalidOperationException(
            "De operator kreeg geen schrijfrecht op de standaardklant. Dan meet deze test niets.");

    /// <summary>Bedragen waarin alles bekend en volledig is.</summary>
    /// <param name="maand">De maand, of <c>null</c> voor <see cref="AfgeslotenMaand"/>.</param>
    /// <returns>De bedragen.</returns>
    public static MonthlyStatementFigures Volledig(string? maand = null) => new()
    {
        CustomerId = Vasteportaalopslag.Standaardklant,
        Month = maand ?? AfgeslotenMaand,
        MeasuredAt = Testgegevens.Nu - TimeSpan.FromHours(9),
        AzureAmount = 36.79m,
        ExtraHoursAmount = 250.00m,
        ExtraHours = 2m,
        BundledHours = 8m,
        UsedHours = 10m,
        Total = 286.79m,
        AmountsAreComplete = true,
    };

    /// <summary>Mailinstellingen waarin mailen is ingericht en de proefdraaimodus uit staat.</summary>
    /// <returns>De instellingen.</returns>
    public static PortalMailOptions Ingericht() => new()
    {
        Endpoint = "https://acs-soratus-test.europe.communication.azure.com/",
        FromAddress = "DoNotReply@soratus.com",
        ReplyToAddress = "hallo@soratus.com",
        DryRun = false,
        PortalBaseUri = "https://portal.soratus.com",
    };
}

/// <summary>Een klok die stilstaat.</summary>
/// <remarks>
/// Een eigen kopie in deze map en niet de <c>StilstaandeKlok</c> uit <c>Weergavelaag</c>: die is
/// daar <c>private</c>. Drie regels dupliceren is hier goedkoper dan een gedeeld hulpmiddel wijzigen
/// waar twee andere sessies in werken.
/// </remarks>
internal sealed class Stilstaandeklok(DateTimeOffset moment) : TimeProvider
{
    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => moment;
}

/// <summary>De bedragen, uit een functie zodat een test ze tussen twee aanroepen kan wisselen.</summary>
internal sealed class Vastemaandbedragen(Func<MonthlyStatementFigures?> bedragen) : IMonthlyStatementFigures
{
    /// <summary>Hoe vaak er om bedragen is gevraagd.</summary>
    public int Aanroepen { get; private set; }

    /// <inheritdoc />
    public Task<MonthlyStatementFigures?> BuildStatementAsync(
        CustomerWriteScope scope,
        string month,
        CancellationToken cancellationToken = default)
    {
        Aanroepen++;

        return Task.FromResult(bedragen());
    }
}

/// <summary>
/// Een verzendlaag die niets verstuurt en teruggeeft wat de test wil.
/// </summary>
/// <remarks>
/// <para>Houdt bij wat hij heeft gekregen. Dat is de enige manier om te meten wat er in een mail zou
/// staan: de echte verzendlaag geeft de tekst niet terug.</para>
///
/// <para><strong>De stand komt uit <see cref="PortalMailOptions.Outbox"/> en niet uit een eigen
/// veld.</strong> Dat is opzet: zou deze dubbel de proefdraaimodus zelf uitrekenen, dan meet elke test
/// erop zijn eigen kopie van die beslissing en blijft hij groen als de echte laag hem omdraait.</para>
/// </remarks>
internal sealed class Vasteverzender(PortalMailOptions opties, Func<string, bool>? geclaimd = null)
    : IMailOutbox
{
    /// <summary>Wat de verzender teruggeeft.</summary>
    public MailDelivery Uitkomst { get; set; } = MailDelivery.Accepted;

    /// <summary>Elke mail die hij heeft gekregen, in volgorde.</summary>
    public List<OutgoingMail> Verstuurd { get; } = [];

    /// <inheritdoc />
    public MailOutboxState State => opties.Outbox();

    /// <summary>
    /// Of er bij elke verzending al een claim stond.
    /// </summary>
    /// <remarks>
    /// <c>false</c> zodra er ook maar één mail is verstuurd zonder claim. Dit is de meting van de
    /// volgorde: draai je claimen en versturen om, dan blijven de aantallen gelijk en verandert
    /// alleen deze waarde. Zonder dit veld zou een test op de volgorde niets meten.
    /// </remarks>
    public bool GeclaimdBijElkeVerzending { get; private set; } = true;

    /// <inheritdoc />
    public Task<MailSendResult> SendAsync(
        OutgoingMail mail,
        CancellationToken cancellationToken = default)
    {
        // Dezelfde eis als op de echte laag: aanbieden terwijl er niet verstuurd mag worden is een fout
        // in de aanroeper en geen toestand. Zonder deze regel zou een mutatie die de proefdraaicontrole
        // uit MonthlyStatementService haalt, hier stil doorgaan.
        Assert.Equal(MailOutboxState.Ready, State);

        Verstuurd.Add(mail);

        if (geclaimd is not null)
        {
            var maand = mail.Subject.Contains("juli 2026", StringComparison.Ordinal)
                ? "2026-07"
                : "2026-08";

            GeclaimdBijElkeVerzending &= geclaimd(maand);
        }

        return Task.FromResult(new MailSendResult(
            Uitkomst,
            Uitkomst == MailDelivery.Accepted ? "operatie-0001" : null));
    }
}

/// <summary>
/// De verzendbevestigingen in het geheugen, met de eigenschap die het ontwerp draagt: een tweede
/// claim op dezelfde maand botst.
/// </summary>
/// <remarks>
/// <para><strong>De botsing wordt hier nagebouwd en dat is een beperking van deze test.</strong> In
/// productie komt de <c>409</c> van Cosmos op een <c>CreateItemAsync</c> met een afgeleide sleutel;
/// hier komt hij uit een <see cref="Dictionary{TKey,TValue}"/>. Wat deze dubbel dus bewijst is dat
/// de dienst de claim vóór de mail zet en op een botsing stopt — niet dat Cosmos die botsing
/// werkelijk geeft. Dat laatste is elders in dit project gemeten (<c>infra.md</c>, de klant-batch) en
/// het staat als niet-gemeten punt in het rapport.</para>
/// </remarks>
internal sealed class Vasteverzendbevestigingen : IStatementStore
{
    private readonly Dictionary<string, StatementDocument> _documenten = new(StringComparer.Ordinal);

    /// <summary>Hoe vaak er is geclaimd.</summary>
    public int Claims { get; private set; }

    /// <summary>Hoe vaak er een uitkomst is vastgelegd.</summary>
    public int Bevestigingen { get; private set; }

    /// <summary>Wat er nu in de opslag staat.</summary>
    /// <param name="maand">De maand.</param>
    /// <returns>Het document, of <c>null</c>.</returns>
    public StatementDocument? Document(string maand) =>
        _documenten.TryGetValue(maand, out var document) ? document : null;

    /// <inheritdoc />
    public Task<StatementDocument?> GetAsync(
        CustomerWriteScope scope,
        string month,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Document(month));

    /// <inheritdoc />
    public Task<IReadOnlyList<StatementDocument>> ListAsync(
        CustomerWriteScope scope,
        int year,
        CancellationToken cancellationToken = default)
    {
        var voorvoegsel = $"{year:D4}-";

        return Task.FromResult<IReadOnlyList<StatementDocument>>(
        [
            .. _documenten.Values
                .Where(document => document.Month.StartsWith(voorvoegsel, StringComparison.Ordinal))
                .OrderByDescending(document => document.Month, StringComparer.Ordinal),
        ]);
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<StatementDocument>> ClaimAsync(
        CustomerWriteScope scope,
        StatementClaim claim,
        CancellationToken cancellationToken = default)
    {
        Claims++;

        var bestaand = Document(claim.Month);

        if (StatementTransitions.WhyNotSend(bestaand) is { } waarom)
        {
            return Task.FromResult(PortalWriteResult<StatementDocument>.Conflict(waarom, bestaand));
        }

        var document = new StatementDocument
        {
            Id = StatementDocumentKeys.Id(claim.Month),
            PartitionKey = scope.CustomerId,
            CustomerId = scope.CustomerId,
            Month = claim.Month,
            State = StatementSendState.Unknown,
            AttemptedAt = Testgegevens.Nu,
            AttemptedBy = scope.Actor,
            Recipients = claim.Recipients,
            Subject = claim.Subject,
            MeasuredAt = claim.MeasuredAt,
            AzureAmount = claim.AzureAmount,
            ExtraHoursAmount = claim.ExtraHoursAmount,
            Total = claim.Total,
            Attempts = (bestaand?.Attempts ?? 0) + 1,
            ReleasedAt = bestaand?.ReleasedAt,
            ReleasedBy = bestaand?.ReleasedBy,
            ReleaseNote = bestaand?.ReleaseNote,
            ETag = $"etag-{Claims}",
        };

        _documenten[claim.Month] = document;

        return Task.FromResult(PortalWriteResult<StatementDocument>.Saved(document));
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<StatementDocument>> ConfirmAsync(
        CustomerWriteScope scope,
        string month,
        MailDelivery delivery,
        string? operationId,
        CancellationToken cancellationToken = default)
    {
        Bevestigingen++;

        if (Document(month) is not { } huidig)
        {
            return Task.FromResult(PortalWriteResult<StatementDocument>.Conflict(
                "De bevestiging bestaat niet meer.",
                current: null));
        }

        var bijgewerkt = delivery switch
        {
            MailDelivery.Accepted => huidig with
            {
                State = StatementSendState.Sent,
                SentAt = Testgegevens.Nu,
                OperationId = operationId,
            },
            MailDelivery.Refused => huidig with
            {
                State = StatementSendState.NotSent,
                Refusal = StatementRefusal.Rejected,
            },
            _ => huidig,
        };

        _documenten[month] = bijgewerkt;

        return Task.FromResult(PortalWriteResult<StatementDocument>.Saved(bijgewerkt));
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<StatementDocument>> ReleaseAsync(
        CustomerWriteScope scope,
        StatementRelease release,
        CancellationToken cancellationToken = default)
    {
        if (release.Validate() is { } ongeldig)
        {
            return Task.FromResult(PortalWriteResult<StatementDocument>.Invalid(ongeldig));
        }

        var huidig = Document(release.Month);

        if (StatementTransitions.WhyNotRelease(huidig) is { } waarom)
        {
            return Task.FromResult(PortalWriteResult<StatementDocument>.Invalid(waarom));
        }

        var bijgewerkt = huidig! with
        {
            State = StatementSendState.NotSent,
            ReleasedAt = Testgegevens.Nu,
            ReleasedBy = scope.Actor,
            ReleaseNote = release.Note.Trim(),
        };

        _documenten[release.Month] = bijgewerkt;

        return Task.FromResult(PortalWriteResult<StatementDocument>.Saved(bijgewerkt));
    }
}
