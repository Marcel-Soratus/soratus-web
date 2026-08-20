using System.Globalization;
using Soratus.Portal.Data;
using Soratus.Portal.Security;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// De urenkant van de portaalopslag in het geheugen (§3.6): lezen, boeken, corrigeren, fiatteren en
/// afwijzen, met echte etags en de echte overgangsregels.
/// </summary>
/// <remarks>
/// <para><strong>Dit is dezelfde klasse als de rest van <see cref="Vasteportaalopslag"/> en geen
/// tweede opslag.</strong> Het urenscherm leest de urenregels én het contract, en dat laatste voor
/// precies één getal: de bundel. Zouden dat twee fixtures zijn, dan is het saldo dat de ene berekent
/// niet dat van de andere, en dan meet een test over het maandtotaal niets.</para>
///
/// <para>Hij staat wél in een eigen bestand. De reden is praktisch: er werken meerdere agents in deze
/// repo aan het bestand met de contractkant, en een nieuw bestand botst niet.</para>
///
/// <para><strong>De regels komen uit de productiecode en worden hier niet nagebouwd.</strong>
/// <see cref="HourBooking.Validate"/>, <see cref="HourCorrection.Validate"/>,
/// <see cref="HourRejection.Validate"/> en <see cref="HourEntryTransitions"/> worden aangeroepen,
/// niet geïmiteerd. De meldingen zijn letterlijk die van <c>CosmosPortalHoursStore</c>, en de
/// volgorde van de controles ook — eerst de overgang, dan de etag, want "deze regel is al
/// gefiatteerd" is een preciezere mededeling dan "iemand anders was eerder". Een fixture die zijn
/// eigen regels verzint laat een scherm groen staan op gedrag dat in productie niet bestaat.</para>
///
/// <para><strong>Rijk gevuld, en dat is het punt.</strong> Er staan standaard zes regels in augustus
/// en twee in juli: gefiatteerde regels uit alle drie de bronnen, twee te fiatteren regels, een
/// afgewezen regel met een reden, en een correctie. Een lege urenopslag zou elk urenscherm laten
/// renderen zonder er iets op te zetten, en dan bewijst een zichtbaarheidstest niets — hij staat
/// groen omdat er niets staat.</para>
/// </remarks>
internal sealed partial class Vasteportaalopslag
{
    /// <summary>De maand waarin de standaardgegevens het grootste deel van hun uren hebben.</summary>
    /// <remarks>
    /// Gelijk aan de maand van <see cref="Testgegevens.Nu"/>, want dat is de maand die het scherm
    /// standaard toont. Zouden de uren in een andere maand staan, dan is de standaardweergave leeg en
    /// meet elke rendertest de lege staat.
    /// </remarks>
    public static string Dezemaand { get; } = HourMonths.Of(Testgegevens.Nu);

    /// <summary>De maand ervoor, voor de historie en het jaartotaal.</summary>
    public static string Vorigemaand { get; } =
        HourMonths.Of(Testgegevens.Nu.AddMonths(-1));

    /// <summary>De omschrijving van een gefiatteerde regel die de klant hoort te zien.</summary>
    public const string Gefiatteerdeomschrijving = "Koppeling voorraadstanden afgerond";

    /// <summary>De omschrijving van een te fiatteren regel die de klant niet hoort te zien.</summary>
    /// <remarks>
    /// Met opzet een tekenreeks die nergens anders voorkomt. Een test die zoekt of een te fiatteren
    /// regel op het klantscherm staat, kijkt dan echt naar deze regel en niet naar een andere.
    /// </remarks>
    public const string Tefiatterenomschrijving = "Nog te beoordelen sprintwerk";

    /// <summary>De omschrijving van de afgewezen regel.</summary>
    public const string Afgewezenomschrijving = "Dubbel geboekt overleg";

    /// <summary>De reden waarom die regel is afgewezen.</summary>
    public const string Afwijsreden = "Al geboekt op het work item ernaast";

    /// <summary>De omschrijving van de correctie.</summary>
    public const string Correctieomschrijving = "Verkeerde maand gecorrigeerd";

    /// <summary>Wie de te fiatteren regels heeft gefiatteerd. Alleen de operator ziet deze naam.</summary>
    public const string Fiatteur = "Ruben Vos";

    /// <summary>De mens achter een MCP-regel: hij werkte in Claude Code en niet in het portaal.</summary>
    public const string Mcpboeker = "Claude Code — Marcel";

    /// <summary>Het aantal gefiatteerde uren in <see cref="Dezemaand"/> in de standaardgegevens.</summary>
    /// <remarks>
    /// Als constante, zodat een test het maandtotaal kan controleren zonder de regels op te tellen —
    /// een test die dezelfde som maakt als de productiecode toetst niets. Verandert de seed, dan
    /// hoort dit getal mee te veranderen en dat is het moment om te kijken of het nog klopt.
    /// </remarks>
    public const decimal Gefiatteerdemaanduren = 9.5m;

    /// <summary>Het aantal te fiatteren uren in <see cref="Dezemaand"/> in de standaardgegevens.</summary>
    public const decimal Tefiatterenmaanduren = 4m;

    private readonly Dictionary<string, List<HourEntryDocument>> _uren =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Of de standaardregels al zijn neergezet.</summary>
    /// <remarks>
    /// De seed is lui en niet in de constructor, en dat is geen luiheid maar een grens: de
    /// constructor staat in het andere deel van deze klasse, en dit deel bestaat juist om dat bestand
    /// niet te hoeven aanraken. Lui seeden heeft hier geen nadeel — er is geen enkele lezing die
    /// vóór de eerste aanroep gebeurt.
    /// </remarks>
    private bool _urengezaaid;

    /// <summary>Elke boeking die deze opslag heeft gekregen, in volgorde.</summary>
    public List<HourBooking> Boekingen { get; } = [];

    /// <summary>Elke correctie die deze opslag heeft gekregen, in volgorde.</summary>
    public List<HourCorrection> Correcties { get; } = [];

    /// <summary>Elke fiattering: de regel en de etag waarop hij is gebaseerd.</summary>
    /// <remarks>
    /// De etag staat erbij omdat het urenscherm hem op static SSR niet kan meegeven en dus altijd
    /// <c>null</c> doorgeeft. Dat is een besluit met een reden (zie <c>Uren.razor</c>), en een besluit
    /// dat je opschrijft hoort ook meetbaar te zijn.
    /// </remarks>
    public List<(string EntryId, string? BasedOnETag)> Fiatteringen { get; } = [];

    /// <summary>Elke afwijzing die deze opslag heeft gekregen, in volgorde.</summary>
    public List<HourRejection> Afwijzingen { get; } = [];

    /// <summary>De urenregels zoals ze nu in de opslag staan, nieuwste eerst.</summary>
    /// <param name="klant">De klantslug.</param>
    /// <returns>De documenten.</returns>
    public IReadOnlyList<HourEntryDocument> Urenregels(string klant = Standaardklant) =>
        [.. Gesorteerd(Urenlijst(klant))];

    /// <summary>
    /// Haalt alle urenregels weg, zodat het scherm de lege staat toont.
    /// </summary>
    /// <remarks>
    /// Een methode en geen constructorvlag, om dezelfde reden als bij de luie seed: de constructor
    /// staat in het andere deel van deze klasse. Roep hem aan vóór de eerste lezing.
    /// </remarks>
    public void GeenUren()
    {
        _urengezaaid = true;
        _uren.Clear();
    }

    /// <summary>
    /// Zet een extra urenregel neer, buiten de scopes om.
    /// </summary>
    /// <param name="regel">De regel. De etag wordt gezet door deze opslag.</param>
    /// <param name="klant">De klantslug.</param>
    /// <remarks>
    /// Voor de standen die het portaal zelf niet kan maken: een regel op een onleesbare datum, een
    /// regel uit een koppeling (er is nog geen aannamepad, zie <see cref="IPortalHoursStore"/>), of
    /// een regel in een jaar waar je heen wilt kunnen bladeren.
    /// </remarks>
    public void LegUrenregelVast(HourEntryDocument regel, string klant = Standaardklant)
    {
        ArgumentNullException.ThrowIfNull(regel);

        Urenlijst(klant).Add(regel with { ETag = NieuweEtag() });
    }

    /// <summary>
    /// Een andere operator beoordeelt een urenregel terwijl hij op het scherm staat.
    /// </summary>
    /// <param name="entryId">De id van de regel.</param>
    /// <param name="stand">De stand die die ander eraan geeft.</param>
    /// <param name="klant">De klantslug.</param>
    /// <remarks>
    /// Buiten de scopes om, en dat hoort: dit is niet de gebruiker van het scherm maar een tweede
    /// operator in een ander verzoek. De etag schuift op, precies zoals in werkelijkheid.
    /// </remarks>
    public void EenAndereOperatorBeoordeeltDeRegel(
        string entryId,
        HourEntryStatus stand,
        string klant = Standaardklant)
    {
        var lijst = Urenlijst(klant);
        var index = lijst.FindIndex(regel => string.Equals(regel.Id, entryId, StringComparison.Ordinal));

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Er is geen urenregel {entryId} bij klant {klant}, dus er valt niets te beoordelen. " +
                "Kijk in Urenregels() welke er staan.");
        }

        lijst[index] = lijst[index] with
        {
            Status = stand,
            ApprovedAt = stand == HourEntryStatus.Approved ? Testgegevens.Nu : null,
            ApprovedBy = stand == HourEntryStatus.Approved ? "Sanne de Wit" : null,
            RejectedAt = stand == HourEntryStatus.Rejected ? Testgegevens.Nu : null,
            RejectedBy = stand == HourEntryStatus.Rejected ? "Sanne de Wit" : null,
            RejectionReason = stand == HourEntryStatus.Rejected ? "Buiten opdracht" : null,
            ETag = NieuweEtag(),
        };
    }

    // ── Lezen ───────────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<IReadOnlyList<HourEntryDocument>> GetApprovedHoursAsync(
        CustomerScope scope,
        HoursQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        // Het filter op gefiatteerd zit in de lezing en niet in de projectie, net als in de
        // WHERE-clausule van de echte opslag. Dat is het verschil tussen "de klant ziet ze niet" en
        // "de klant krijgt ze niet", en een fixture die hier alles teruggeeft zou een test over dat
        // verschil groen laten staan om de verkeerde reden.
        return Task.FromResult(Lees(scope.CustomerId, query, HourEntryStatus.Approved));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HourEntryDocument>> GetHoursAsync(
        CustomerWriteScope scope,
        HoursQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult(Lees(scope.CustomerId, query, stand: null));
    }

    // ── Schrijven ───────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<PortalWriteResult<HourEntryDocument>> BookHoursAsync(
        CustomerWriteScope scope,
        HourBooking booking,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(booking);

        Boekingen.Add(booking);

        if (booking.Validate() is { } melding)
        {
            return Task.FromResult(PortalWriteResult<HourEntryDocument>.Invalid(melding));
        }

        return Task.FromResult(Leg(
            scope,
            booking.Month.Trim(),
            booking.Hours,
            booking.Category.Trim(),
            booking.By.Trim(),
            booking.Note.Trim()));
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<HourEntryDocument>> CorrectHoursAsync(
        CustomerWriteScope scope,
        HourCorrection correction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(correction);

        Correcties.Add(correction);

        if (correction.Validate() is { } melding)
        {
            return Task.FromResult(PortalWriteResult<HourEntryDocument>.Invalid(melding));
        }

        // Categorie Correctie en bron Portaal, en dat staat hier vast en niet in de aanroeper —
        // precies zoals in CosmosPortalHoursStore. Zou het scherm de categorie mogen meegeven, dan
        // bestond er een aanroep waarmee een correctie een gewone boeking wordt.
        return Task.FromResult(Leg(
            scope,
            correction.Month.Trim(),
            correction.Hours,
            HourCategories.Correction,
            correction.By.Trim(),
            correction.Note.Trim()));
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<HourEntryDocument>> ApproveHoursAsync(
        CustomerWriteScope scope,
        string entryId,
        string? basedOnETag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        Fiatteringen.Add((entryId, basedOnETag));

        if (string.IsNullOrWhiteSpace(entryId))
        {
            return Task.FromResult(PortalWriteResult<HourEntryDocument>.Invalid(
                "Er is geen urenregel meegegeven om te fiatteren."));
        }

        return Task.FromResult(Beslis(
            scope,
            entryId.Trim(),
            basedOnETag,
            HourEntryTransitions.WhyNotApprove,
            huidig => huidig with
            {
                Status = HourEntryStatus.Approved,
                ApprovedAt = Testgegevens.Nu,
                ApprovedBy = scope.Actor,
                RejectedAt = null,
                RejectedBy = null,
                RejectionReason = null,
            }));
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<HourEntryDocument>> RejectHoursAsync(
        CustomerWriteScope scope,
        HourRejection rejection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(rejection);

        Afwijzingen.Add(rejection);

        if (rejection.Validate() is { } melding)
        {
            return Task.FromResult(PortalWriteResult<HourEntryDocument>.Invalid(melding));
        }

        return Task.FromResult(Beslis(
            scope,
            rejection.EntryId.Trim(),
            rejection.BasedOnETag,
            HourEntryTransitions.WhyNotReject,
            huidig => huidig with
            {
                Status = HourEntryStatus.Rejected,
                RejectedAt = Testgegevens.Nu,
                RejectedBy = scope.Actor,
                RejectionReason = rejection.Reason.Trim(),
                ApprovedAt = null,
                ApprovedBy = null,
            }));
    }

    // ── Binnenwerk ──────────────────────────────────────────────────────────────────────────────

    private IReadOnlyList<HourEntryDocument> Lees(
        string klant,
        HoursQuery query,
        HourEntryStatus? stand)
    {
        var van = query.Month ?? $"{query.Year:D4}-01";
        var tot = query.Month ?? $"{query.Year:D4}-12";

        return
        [
            .. Gesorteerd(Urenlijst(klant).Where(regel =>
                string.CompareOrdinal(regel.Month, van) >= 0
                && string.CompareOrdinal(regel.Month, tot) <= 0
                && (stand is null || regel.Status == stand))),
        ];
    }

    /// <summary>
    /// Nieuwste eerst, en bij gelijke datum op sleutel.
    /// </summary>
    /// <remarks>
    /// Letterlijk de ordening van <c>CosmosPortalHoursStore</c>. Zou deze fixture anders sorteren,
    /// dan legt een test over "de eerste rij op het scherm" een volgorde vast die in productie niet
    /// bestaat.
    /// </remarks>
    private static IEnumerable<HourEntryDocument> Gesorteerd(IEnumerable<HourEntryDocument> regels) =>
        regels
            .OrderByDescending(regel => regel.CreatedAt)
            .ThenBy(regel => regel.Id, StringComparer.Ordinal);

    /// <summary>
    /// Legt een nieuwe gefiatteerde regel vast: een boeking of een correctie.
    /// </summary>
    /// <remarks>
    /// De sleutel komt uit <see cref="HourEntryKeys.ForPortal"/> met dezelfde vingerafdruk als de
    /// echte opslag, en een tweede verzending van hetzelfde formulier binnen dezelfde milliseconde
    /// botst hier dus net zo. Die botsing is de enige bescherming tegen dubbel indienen die dit
    /// portaal op static SSR heeft, dus hij hoort in een test te kunnen worden aangetoond.
    /// </remarks>
    private PortalWriteResult<HourEntryDocument> Leg(
        CustomerWriteScope scope,
        string maand,
        decimal uren,
        string categorie,
        string boeker,
        string omschrijving)
    {
        var sleutel = HourEntryKeys.ForPortal(
            Testgegevens.Nu,
            string.Create(
                CultureInfo.InvariantCulture,
                $"portaal|{maand}|{categorie}|{uren}|{boeker}|{omschrijving}"));

        var id = PortalDocumentIds.HourEntry(sleutel);
        var lijst = Urenlijst(scope.CustomerId);

        if (lijst.Any(regel => string.Equals(regel.Id, id, StringComparison.Ordinal)))
        {
            return PortalWriteResult<HourEntryDocument>.Conflict(
                "Deze urenregel staat er al. Waarschijnlijk is het formulier twee keer verstuurd; er " +
                "is één regel vastgelegd en geen twee. Moet dit echt een tweede regel zijn, wijzig " +
                "dan de omschrijving.",
                lijst.First(regel => string.Equals(regel.Id, id, StringComparison.Ordinal)));
        }

        var document = new HourEntryDocument
        {
            Id = id,
            PartitionKey = scope.CustomerId,
            CustomerId = scope.CustomerId,
            Month = maand,
            Category = categorie,
            Note = omschrijving,
            Hours = uren,
            Source = HourEntrySource.Portal,
            By = boeker,
            Status = HourEntryStatus.Approved,
            CreatedAt = Testgegevens.Nu,
            CreatedBy = scope.Actor,
            ApprovedAt = Testgegevens.Nu,
            ApprovedBy = scope.Actor,
            ETag = NieuweEtag(),
        };

        lijst.Add(document);

        return PortalWriteResult<HourEntryDocument>.Saved(document);
    }

    private PortalWriteResult<HourEntryDocument> Beslis(
        CustomerWriteScope scope,
        string entryId,
        string? basedOnETag,
        Func<HourEntryStatus, string?> waaromNiet,
        Func<HourEntryDocument, HourEntryDocument> beslis)
    {
        var lijst = Urenlijst(scope.CustomerId);
        var index = lijst.FindIndex(regel => string.Equals(regel.Id, entryId, StringComparison.Ordinal));

        if (index < 0)
        {
            return PortalWriteResult<HourEntryDocument>.Conflict(
                $"Deze urenregel bestaat niet (meer) bij {scope.DisplayName}. Vernieuw het scherm.",
                current: null);
        }

        var huidig = lijst[index];

        // Eerst de overgang, dan de etag. Dezelfde volgorde als in de echte opslag, en om dezelfde
        // reden: "deze regel is al gefiatteerd" is een preciezere mededeling dan "iemand anders was
        // eerder", ook al is de oorzaak dezelfde.
        if (waaromNiet(huidig.Status) is { } geweigerd)
        {
            return PortalWriteResult<HourEntryDocument>.Invalid(geweigerd);
        }

        if (Verouderd(huidig.ETag, basedOnETag))
        {
            return PortalWriteResult<HourEntryDocument>.Conflict(
                "Deze urenregel is intussen door iemand anders beoordeeld. Je beslissing is niet " +
                "opgeslagen; bekijk de regel opnieuw.",
                huidig);
        }

        var nieuw = beslis(huidig) with { ETag = NieuweEtag() };

        lijst[index] = nieuw;

        return PortalWriteResult<HourEntryDocument>.Saved(nieuw);
    }

    /// <summary>Vandaag in de vaste vorm, in de zone waarin het portaal dagen afbakent.</summary>
    private static string Vandaag =>
        DateOnly
            .FromDateTime(TimeZoneInfo.ConvertTime(Testgegevens.Nu, PortalTimeZone.Display).DateTime)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private List<HourEntryDocument> Urenlijst(string klant)
    {
        if (!_urengezaaid)
        {
            _urengezaaid = true;
            Zaai();
        }

        if (!_uren.TryGetValue(klant, out var lijst))
        {
            lijst = [];
            _uren[klant] = lijst;
        }

        return lijst;
    }

    /// <summary>
    /// De standaardregels: alle drie de bronnen, alle drie de standen, en een correctie.
    /// </summary>
    /// <remarks>
    /// De uren zijn zo gekozen dat de som onderscheidend is: 9,5 gefiatteerd deze maand tegenover 4
    /// te fiatteren. Zou het gefiatteerde totaal gelijk zijn aan het totaal van alles, dan blijft een
    /// test over "het maandtotaal telt de te fiatteren regels niet mee" groen terwijl hij ze wél
    /// meetelt.
    /// </remarks>
    private void Zaai()
    {
        var lijst = new List<HourEntryDocument>();
        _uren[Standaardklant] = lijst;

        var dag = 1;

        void Leg(
            string maand,
            decimal uren,
            string categorie,
            string omschrijving,
            HourEntrySource bron,
            string boeker,
            HourEntryStatus stand)
        {
            // Elke regel krijgt een eigen tijdstip. Dat deed eerder een apart datumveld; dat is
            // verdwenen omdat het een duplicaat was van createdAt (punt 20), en een fixture waarin
            // alle regels op hetzelfde moment zijn vastgelegd laat de ordening op de id-tiebreak
            // leunen in plaats van op de sorteersleutel die in productie het werk doet.
            //
            // Later toegevoegd is later vastgelegd, dus de aflopende ordening zet de laatst
            // toegevoegde regel bovenaan — precies zoals de oplopende datums dat deden.
            var vastgelegd = Testgegevens.Nu - TimeSpan.FromDays(2) + TimeSpan.FromMinutes(dag++);

            lijst.Add(new HourEntryDocument
            {
                Id = PortalDocumentIds.HourEntry($"{maand}-{lijst.Count:D3}"),
                PartitionKey = Standaardklant,
                CustomerId = Standaardklant,
                Month = maand,
                Category = categorie,
                Note = omschrijving,
                Hours = uren,
                Source = bron,
                By = boeker,
                Status = stand,
                ExternalId = bron == HourEntrySource.Portal ? null : $"{bron}-{lijst.Count:D3}",
                CreatedAt = vastgelegd,
                CreatedBy = bron == HourEntrySource.Portal ? Wijzigdehet : bron.ToString(),
                ApprovedAt = stand == HourEntryStatus.Approved ? Testgegevens.Nu - TimeSpan.FromDays(1) : null,
                ApprovedBy = stand == HourEntryStatus.Approved ? Fiatteur : null,
                RejectedAt = stand == HourEntryStatus.Rejected ? Testgegevens.Nu - TimeSpan.FromDays(1) : null,
                RejectedBy = stand == HourEntryStatus.Rejected ? Fiatteur : null,
                RejectionReason = stand == HourEntryStatus.Rejected ? Afwijsreden : null,
                ETag = NieuweEtag(),
            });
        }

        // Deze maand: 9,5 u gefiatteerd (waarvan -0,5 correctie), 4 u te fiatteren, 1,5 u afgewezen.
        Leg(Dezemaand, 4m, HourCategories.Development, Gefiatteerdeomschrijving, HourEntrySource.Mcp, Mcpboeker, HourEntryStatus.Approved);
        Leg(Dezemaand, 3m, HourCategories.Maintenance, "Maandelijkse controle van de agents", HourEntrySource.Portal, Wijzigdehet, HourEntryStatus.Approved);
        Leg(Dezemaand, 3m, HourCategories.Support, "Vraag over de factuurintake beantwoord", HourEntrySource.DevOps, "Work item 4530", HourEntryStatus.Approved);
        Leg(Dezemaand, -0.5m, HourCategories.Correction, Correctieomschrijving, HourEntrySource.Portal, Wijzigdehet, HourEntryStatus.Approved);
        Leg(Dezemaand, 2.5m, HourCategories.Advice, Tefiatterenomschrijving, HourEntrySource.Mcp, Mcpboeker, HourEntryStatus.Pending);
        Leg(Dezemaand, 1.5m, HourCategories.Development, "Sprintwerk uit DevOps", HourEntrySource.DevOps, "Work item 4531", HourEntryStatus.Pending);
        Leg(Dezemaand, 1.5m, HourCategories.Advice, Afgewezenomschrijving, HourEntrySource.Mcp, Mcpboeker, HourEntryStatus.Rejected);

        // Vorige maand: ruim boven de bundel van 12 u, zodat het jaaroverzicht een overschrijding
        // heeft om te tonen en de stand "Boven bundel" ergens voorkomt.
        dag = 1;
        Leg(Vorigemaand, 10m, HourCategories.Development, "Eerste uitrol van de factuurintake", HourEntrySource.Portal, Wijzigdehet, HourEntryStatus.Approved);
        Leg(Vorigemaand, 6m, HourCategories.Development, "Nazorg op de uitrol", HourEntrySource.Mcp, Mcpboeker, HourEntryStatus.Approved);
    }
}
