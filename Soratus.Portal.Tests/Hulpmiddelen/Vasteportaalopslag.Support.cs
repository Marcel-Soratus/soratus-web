using Soratus.Portal.Data;
using Soratus.Portal.Security;
using Soratus.Portal.Support;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// De supportkant van de portaalopslag in het geheugen (§3.8): de draad lezen, een vraag van de klant,
/// een antwoord van een mens, en wat de eerstelijn ervan maakte.
/// </summary>
/// <remarks>
/// <para><strong>Dezelfde klasse als de rest van <see cref="Vasteportaalopslag"/> en geen tweede
/// opslag.</strong> Het supportscherm leest de draad én het contract — dat laatste voor precies één
/// veld, de SLA, want de escalatie moet de reactietermijn noemen (§3.8). Zouden dat twee fixtures zijn,
/// dan is de SLA die de escalatie noemt niet die op de contractkaart, en dan meet een test over de
/// escalatie niets.</para>
///
/// <para>Hij staat wél in een eigen bestand, om dezelfde praktische reden als de urenkant: er werken
/// meerdere sessies in deze repository, en een nieuw bestand botst niet. De interface staat in dít deel
/// van de partial declaratie, precies zoals <c>IPortalCostsStore</c> dat in
/// <c>Vasteportaalopslag.Kosten.cs</c> doet.</para>
///
/// <para><strong>De regels komen uit de productiecode en worden hier niet nagebouwd.</strong>
/// <see cref="CosmosSupportStore.Accept"/> — de beslissing die de acceptatie-eis van fase 5 afdwingt —
/// wordt aangeroepen en niet geïmiteerd. Idem <see cref="SupportBody.Clean"/>,
/// <see cref="SupportText.Answer"/>, <see cref="SupportText.Handoff"/> en
/// <see cref="SupportDocumentKeys.Id"/>. Een fixture die zijn eigen versie van die beslissing maakt,
/// laat een test groen staan op gedrag dat in productie niet bestaat — en juist die beslissing is wat
/// hier getest hoort te worden.</para>
///
/// <para><strong>Leeg bij de start, en dat is anders dan bij de uren.</strong> Daar staan standaard zes
/// regels, omdat een leeg urenscherm niets bewijst. Een lege draad bewijst wél iets: het is de gewone
/// begintoestand van een klant, en de lege staat is onderdeel van §3.8. Elke test die berichten nodig
/// heeft, zet ze zelf neer — en dan staat in die test welk bericht welk gedrag oplevert.</para>
/// </remarks>
internal sealed partial class Vasteportaalopslag : ISupportStore
{
    private readonly Dictionary<string, List<SupportMessageDocument>> _support =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Elk verzoek dat aan de eerstelijn is voorgelegd, in volgorde.</summary>
    /// <remarks>
    /// Staat er zodat een test kan zien wát er is aangeboden. Dat is niet cosmetisch: de hele eis rust
    /// op de verzameling grondslagen die de eerstelijn heeft gekregen, en een test die alleen de
    /// uitkomst meet, meet het gevolg en niet de invariant.
    /// </remarks>
    public List<SupportEnquiry> Verzoeken { get; } = [];

    /// <summary>Elk antwoord dat de eerstelijn heeft teruggegeven, in volgorde. <c>null</c> mag.</summary>
    public List<SupportAnswer?> Antwoorden { get; } = [];

    /// <summary>De berichten zoals ze nu in de opslag staan, oudste eerst.</summary>
    /// <param name="klant">De klantslug.</param>
    /// <returns>De documenten.</returns>
    public IReadOnlyList<SupportMessageDocument> Supportberichten(string klant = Standaardklant) =>
        [.. Draad(klant).OrderBy(m => m.Id, StringComparer.Ordinal)];

    /// <summary>
    /// Zet een bericht neer buiten de schrijfpaden om.
    /// </summary>
    /// <param name="bericht">Het bericht. De etag wordt door deze opslag gezet.</param>
    /// <param name="klant">De klantslug.</param>
    /// <remarks>
    /// Voor de standen die het portaal zelf niet kán maken, en dat is precies waar de leeskant voor
    /// bestaat: een bericht met een onbekende afzender, een antwoord van de eerstelijn zonder
    /// grondslag, een bericht met een grondslag die geen maand is. De identiteit van het portaal heeft
    /// schrijfrecht op de hele container <c>customers</c>, dus zulke documenten zijn geen theorie — ze
    /// zijn wat er staat als iemand er ooit langs een ander pad in schrijft.
    /// </remarks>
    public void ZetSupportbericht(SupportMessageDocument bericht, string klant = Standaardklant)
    {
        ArgumentNullException.ThrowIfNull(bericht);

        Draad(klant).Add(bericht with { ETag = NieuweEtag() });
    }

    /// <inheritdoc />
    public Task<SupportMessagePage> ReadThreadAsync(
        CustomerScope scope,
        SupportThreadQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult(Lees(scope.CustomerId, query));
    }

    /// <inheritdoc />
    public Task<SupportMessagePage> ReadThreadAsync(
        CustomerWriteScope scope,
        SupportThreadQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult(Lees(scope.CustomerId, query));
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<SupportMessageDocument>> PostQuestionAsync(
        CustomerScope scope,
        SupportQuestion question,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(question);

        if (question.Validate() is { } fout)
        {
            return Task.FromResult(PortalWriteResult<SupportMessageDocument>.Invalid(fout));
        }

        return Task.FromResult(Schrijf(
            scope.CustomerId,
            SupportAuthor.Customer,
            SupportBody.Clean(question.Text),
            question.Author.Trim(),
            grondslag: null,
            escalatie: null));
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<SupportMessageDocument>> PostReplyAsync(
        CustomerWriteScope scope,
        SupportReply reply,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(reply);

        if (reply.Validate() is { } fout)
        {
            return Task.FromResult(PortalWriteResult<SupportMessageDocument>.Invalid(fout));
        }

        return Task.FromResult(Schrijf(
            scope.CustomerId,
            SupportAuthor.Soratus,
            SupportBody.Clean(reply.Text),
            scope.Actor,
            grondslag: null,
            escalatie: null));
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<SupportMessageDocument>> RecordFirstLineAsync(
        CustomerScope scope,
        SupportEnquiry enquiry,
        SupportAnswer? answer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(enquiry);

        Verzoeken.Add(enquiry);
        Antwoorden.Add(answer);

        // De echte beslissing, uit de productiecode. Zie de opmerkingen bij deze klasse: een fixture
        // die dit zelf beslist, meet zijn eigen versie van de eis.
        var aangenomen = CosmosSupportStore.Accept(enquiry, answer);

        return Task.FromResult(aangenomen is null
            ? Schrijf(
                scope.CustomerId,
                SupportAuthor.FirstLine,
                SupportText.Handoff(),
                wie: null,
                grondslag: null,
                escalatie: answer?.Escalation ?? SupportEscalation.AnswerNotUsable)
            : Schrijf(
                scope.CustomerId,
                SupportAuthor.FirstLine,
                SupportText.Answer(aangenomen),
                wie: null,
                grondslag: aangenomen,
                escalatie: null));
    }

    // ── Binnenwerk ──────────────────────────────────────────────────────────────────────────────

    private List<SupportMessageDocument> Draad(string klant)
    {
        if (!_support.TryGetValue(klant, out var berichten))
        {
            berichten = [];
            _support[klant] = berichten;
        }

        return berichten;
    }

    /// <summary>
    /// Leest één deel van de draad, met dezelfde ordening en dezelfde grens als
    /// <see cref="CosmosSupportStore"/>.
    /// </summary>
    /// <remarks>
    /// De ordening is ordinaal op de documentsleutel, precies zoals de <c>ORDER BY c.id DESC</c> van de
    /// echte opslag. Dat is geen toevallige gelijkenis maar de eigenschap waarop het bladeren rust; er
    /// staat een aparte test op dat die sleutel chronologisch sorteert.
    /// </remarks>
    private SupportMessagePage Lees(string klant, SupportThreadQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var nieuwsteEerst = Draad(klant)
            .Where(m => query.OlderThan is null
                || string.CompareOrdinal(m.Id, query.OlderThan) < 0)
            .OrderByDescending(m => m.Id, StringComparer.Ordinal)
            .Take(SupportThreadQuery.PageSize + 1)
            .ToList();

        var meer = nieuwsteEerst.Count > SupportThreadQuery.PageSize;
        var deel = meer
            ? nieuwsteEerst.Take(SupportThreadQuery.PageSize).ToList()
            : nieuwsteEerst;

        deel.Reverse();

        return new SupportMessagePage(deel, meer && deel.Count > 0 ? deel[0].Id : null);
    }

    private PortalWriteResult<SupportMessageDocument> Schrijf(
        string klant,
        SupportAuthor afzender,
        string tekst,
        string? wie,
        SupportGround? grondslag,
        SupportEscalation? escalatie)
    {
        if (tekst.Length == 0)
        {
            return PortalWriteResult<SupportMessageDocument>.Invalid(
                "Er is geen bericht om vast te leggen: na het schonen bleef er geen tekst over.");
        }

        var nu = Klokstand();

        var sleutel = SupportDocumentKeys.Id(
            nu,
            $"{afzender}|{wie}|{grondslag?.Kind}|{grondslag?.Key}|{escalatie}|{tekst}");

        var draad = Draad(klant);

        if (draad.Any(m => string.Equals(m.Id, sleutel, StringComparison.Ordinal)))
        {
            // CreateItemAsync op een afgeleide sleutel, dus een 409 en geen tweede bericht. Dezelfde
            // melding als de echte opslag.
            return PortalWriteResult<SupportMessageDocument>.Conflict(
                "Dit bericht staat er al. Waarschijnlijk is het formulier twee keer verstuurd; er is "
                + "één bericht vastgelegd en geen twee.",
                current: null);
        }

        var document = new SupportMessageDocument
        {
            Id = sleutel,
            PartitionKey = klant,
            CustomerId = klant,
            Author = afzender,
            Who = wie,
            Text = tekst,
            GroundKind = grondslag?.Kind,
            GroundKey = grondslag?.Key,
            Escalation = escalatie,
            CreatedAt = nu,
            ETag = NieuweEtag(),
        };

        draad.Add(document);

        return PortalWriteResult<SupportMessageDocument>.Saved(document);
    }

    /// <summary>
    /// Het moment van de volgende schrijfactie.
    /// </summary>
    /// <remarks>
    /// <para><strong>Elke schrijfactie krijgt een eigen milliseconde, en dat is geen cosmetiek.</strong>
    /// De klok van de tests staat stil (<see cref="Weergavelaag.Klok"/>), en de documentsleutel is
    /// afgeleid van het moment plus de inhoud. Twee berichten met dezelfde tekst binnen dezelfde
    /// milliseconde botsen dus — en dat is in productie het gewenste gedrag (een dubbel verstuurd
    /// formulier) maar in een test die drie berichten neerzet een fout die niets met het onderwerp te
    /// maken heeft.</para>
    ///
    /// <para>Een oplopende klok maakt de draad bovendien een echte draad: de volgorde van de sleutels
    /// is de volgorde waarin de tests de berichten hebben geschreven, en dat is precies wat het
    /// bladeren gebruikt.</para>
    /// </remarks>
    private DateTimeOffset Klokstand() => Testgegevens.Nu.AddMilliseconds(_supportklok++);

    private int _supportklok;
}
