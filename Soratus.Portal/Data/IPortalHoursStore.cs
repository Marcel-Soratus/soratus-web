using Soratus.Portal.Security;

namespace Soratus.Portal.Data;

/// <summary>
/// De enige toegang tot de urenregels: lezen, boeken, fiatteren, afwijzen en corrigeren (§3.6).
/// </summary>
/// <remarks>
/// <para><strong>Waarom dit naast <see cref="IPortalDataStore"/> staat en niet erin.</strong> Dezelfde
/// afweging als bij <see cref="Views.IContractViews"/> tegenover <see cref="Views.IPortalViews"/>:
/// <see cref="IPortalDataStore"/> is de autorisatiebron van het portaal. Elke methode daar raakt de
/// vraag wie ergens bij mag. Urenregels raken die vraag niet — ze staan alleen in dezelfde container,
/// om de reden die bij <see cref="HourEntryDocument"/> staat. Eén interface voor beide zou betekenen
/// dat een pagina die uren boekt hetzelfde bewijs in handen heeft als een pagina die toegang uitdeelt,
/// en dat is precies het verschil dat <see cref="PortalWriteScope"/> destijds heeft ingevoerd.</para>
///
/// <para><strong>De rolscheiding zit in de naam van de leesmethode en niet alleen in de
/// scope.</strong> Anders dan bij het contract — waar beide rollen hetzelfde document lezen en de
/// projectie het verschil maakt — leveren de twee leesmethoden hier een <em>andere verzameling
/// documenten</em> op. Een klant mag alleen gefiatteerde regels zien (§2), en dat wordt in de
/// <c>WHERE</c>-clausule afgedwongen en niet in de projectie. Overloads met dezelfde naam zouden dat
/// verschil onzichtbaar maken; wie <see cref="GetApprovedHoursAsync"/> leest weet wat hij krijgt.</para>
///
/// <para><strong>Fiatteren en afwijzen zijn operator-only (§2), en dat is hier geen controle maar een
/// typebeperking.</strong> Alle vier de schrijfmethoden nemen een <see cref="CustomerWriteScope"/>. Een
/// klantpagina heeft dat argument niet en kan het niet maken — de constructor is <c>internal</c> en
/// alleen <see cref="CustomerScopeResolver"/> roept hem aan, na een oordeel over de rol.</para>
///
/// <para><strong>Wat er niet op deze interface staat: het aannamepad van een koppeling.</strong> De
/// MCP-server <c>soratus-uren</c> en <c>devops-sync</c> schieten regels in die als
/// <see cref="HourEntryStatus.Pending"/> landen (§5). Dat pad heeft een aanroeper die geen mens is, en
/// er bestaat geen bewijstype voor zo'n aanroeper — <see cref="CustomerWriteScope"/> betekent
/// "operator die naar deze klant kijkt" en dat is een koppeling niet. Zo'n scope verzinnen zonder te
/// weten hoe hij uit een token volgt, levert een leesbaar type op met een gat erachter. Het staat als
/// afstemmingspunt in het rapport van fase 3; de documentvorm en de sleutelregel liggen wél vast (zie
/// <see cref="HourEntryDocument"/> en <see cref="HourEntryKeys.ForIntegration"/>), zodat dat pad niets
/// hoeft te verzinnen.</para>
///
/// <para>Er is precies één implementatie: <see cref="CosmosPortalHoursStore"/>. Geen seed-variant en
/// geen in-memory variant, om dezelfde reden als bij de andere twee stores.</para>
/// </remarks>
public interface IPortalHoursStore
{
    /// <summary>
    /// De gefiatteerde urenregels van deze klant, zoals de klant ze mag lezen (§2).
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="query">Één maand of één jaar. Zie <see cref="HoursQuery"/>.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De regels, nieuwste eerst.</returns>
    /// <remarks>
    /// <para><strong>Het filter op gefiatteerd zit in de query en niet in de projectie.</strong> Dat is
    /// het verschil tussen "de klant ziet ze niet" en "de klant krijgt ze niet". Een te fiatteren regel
    /// die het geheugen van het klantverzoek bereikt, kan langs een serialisatiegrens of in een
    /// foutmelding alsnog naar buiten komen; een regel die de query niet oplevert kan dat niet. Dezelfde
    /// afweging als bij punt 12 van de fase-0-afwijkingen, waar het antwoord op <c>extra</c> een type
    /// zonder dat veld was in plaats van een <c>@if</c>.</para>
    ///
    /// <para>Afgewezen regels vallen hier dus ook buiten, en dat is dubbel gedekt: ze zijn niet
    /// gefiatteerd, en de reden waarom ze zijn afgewezen is een operatorafweging die de klant niets
    /// zegt.</para>
    /// </remarks>
    Task<IReadOnlyList<HourEntryDocument>> GetApprovedHoursAsync(
        CustomerScope scope,
        HoursQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Alle urenregels van deze klant, in elke stand, voor de operator (§3.6).
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="query">Één maand of één jaar.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De regels, nieuwste eerst, inclusief de te fiatteren en de afgewezen.</returns>
    /// <remarks>
    /// Vraagt een schrijfbewijs om te lezen, net als
    /// <see cref="IPortalDataStore.GetContractAsync(CustomerWriteScope, CancellationToken)"/> en om
    /// dezelfde reden: dit is de lezing waar het boekformulier en de fiatteerknoppen op worden
    /// gebouwd, en de etags die daarvoor nodig zijn komen hieruit. Er wordt geen recht mee opgerekt —
    /// §2 geeft de operator op uren zowel lezen als boeken en fiatteren.
    /// </remarks>
    Task<IReadOnlyList<HourEntryDocument>> GetHoursAsync(
        CustomerWriteScope scope,
        HoursQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Boekt uren in het portaal (§3.6, "Uren boeken").
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="booking">Maand, uren, categorie, boeker en omschrijving.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De nieuwe regel, of een melding als de invoer niet klopt, of een conflict als dezelfde regel al
    /// bestaat.
    /// </returns>
    /// <remarks>
    /// <para><strong>De regel landt meteen als gefiatteerd, en dat is geen uitzondering op §5.</strong>
    /// Die regel gaat over wat een agent of koppeling inschiet. Een operator die dit formulier
    /// verstuurt <em>ís</em> het akkoord van Soratus; hem zijn eigen boeking laten fiatteren is een
    /// tweede klik zonder een tweede oordeel. Dat is ook wat de mockup doet.</para>
    ///
    /// <para><strong>Een conflict betekent hier iets anders dan bij de andere schrijfmethoden.</strong>
    /// Elders is het "iemand anders was eerder"; hier is het "deze regel staat er al". De sleutel is
    /// afgeleid van het moment en de inhoud (zie <see cref="HourEntryKeys.ForPortal"/>), dus twee
    /// verzendingen van hetzelfde formulier — en dat gebeurt, want dit portaal is static SSR en er is
    /// geen JavaScript dat de knop uitzet — leveren dezelfde sleutel op en dus één regel.</para>
    /// </remarks>
    Task<PortalWriteResult<HourEntryDocument>> BookHoursAsync(
        CustomerWriteScope scope,
        HourBooking booking,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legt een handmatige correctie op het maandtotaal vast (§3.6).
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="correction">De maand, het aantal uren (mag negatief) en de reden.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De nieuwe regel, of een melding, of een conflict.</returns>
    /// <remarks>
    /// <para><strong>Een correctie is nóg een gefiatteerde urenregel en niet een ander soort
    /// getal.</strong> Bron <see cref="HourEntrySource.Portal"/>, categorie
    /// <see cref="HourCategories.Correction"/>, stand <see cref="HourEntryStatus.Approved"/>. Daarmee
    /// blijft het maandtotaal een zuivere som — de acceptatie-eis van fase 3 — én is de correctie
    /// zichtbaar als rij en meldbaar in de tooltip, wat §3.6 óók vraagt. Zie besluit 16 in
    /// <c>docs/agent-portal/fase-0-afwijkingen.md</c>.</para>
    ///
    /// <para>Er wordt hier dus niets gewijzigd: er komt een document bij. Een bestaande gefiatteerde
    /// regel wordt door geen enkele methode op deze interface aangeraakt, en dat is wat het
    /// maandtotaal van vorige maand hetzelfde houdt als vorige maand.</para>
    /// </remarks>
    Task<PortalWriteResult<HourEntryDocument>> CorrectHoursAsync(
        CustomerWriteScope scope,
        HourCorrection correction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fiatteert één urenregel (§3.6). Vanaf dat moment telt hij mee.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="entryId">De id van de regel, zoals hij op het scherm stond.</param>
    /// <param name="basedOnETag">
    /// De etag van de regel zoals hij op het scherm stond, of <c>null</c> om te fiatteren zoals hij nu
    /// is.
    /// </param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De gefiatteerde regel, of een conflict als iemand anders er intussen iets mee heeft gedaan, of
    /// een melding als de regel al gefiatteerd was.
    /// </returns>
    /// <remarks>
    /// Ook een eerder afgewezen regel kan hier langs; zie
    /// <see cref="HourEntryTransitions.WhyNotApprove"/>. Er wordt niets automatisch herhaald bij een
    /// conflict, om dezelfde reden als bij het contract: bij twee operators die dezelfde regel
    /// beoordelen is "opnieuw proberen" hetzelfde als de laatste laten winnen.
    /// </remarks>
    Task<PortalWriteResult<HourEntryDocument>> ApproveHoursAsync(
        CustomerWriteScope scope,
        string entryId,
        string? basedOnETag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Wijst één urenregel af (§3.6). Hij blijft staan met de reden erbij.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="rejection">De regel, de reden en de etag waarop de beoordeling rust.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De afgewezen regel, of een conflict, of een melding.</returns>
    /// <remarks>
    /// <para><strong>Het document wordt niet verwijderd.</strong> Daarin wijkt dit af van
    /// <see cref="IPortalDataStore.RevokeAccessAsync"/>, waar de afwezigheid van het document juist het
    /// antwoord is. Het verschil: daar is het document zélf het recht, hier is het een bewering van een
    /// koppeling waarover een oordeel is gegeven. Verwijderen gooit twee dingen weg — de reden, die
    /// maanden later bij een factuurvraag nodig is, en het besluit zelf: een koppeling die zijn aanroep
    /// herhaalt schrijft dezelfde sleutel terug (<see cref="HourEntryKeys.ForIntegration"/>), en die
    /// herhaling slaagt zodra het document weg is. Afwijzen zou dan geen besluit zijn maar een
    /// handeling die je elke keer opnieuw doet.</para>
    ///
    /// <para>Dat een lijst vol afgewezen regels onbruikbaar wordt is een echt bezwaar, en het is in de
    /// weergave opgelost in plaats van in de opslag: afgewezen regels staan niet in de specificatie
    /// maar in een eigen lijst. Zie <see cref="Views.OperatorHoursView.Rejected"/>.</para>
    /// </remarks>
    Task<PortalWriteResult<HourEntryDocument>> RejectHoursAsync(
        CustomerWriteScope scope,
        HourRejection rejection,
        CancellationToken cancellationToken = default);
}
