namespace Soratus.Portal.Support;

/// <summary>
/// Een vraag van de klant, klaar om te worden vastgelegd (§3.8, "Berichtendraad klant ↔ Soratus").
/// </summary>
/// <remarks>
/// <para><strong>Er is geen afzenderveld dat uit een formulier komt, en er is geen soortveld.</strong>
/// Dat is de manier waarop het rolverschil hier wordt afgedwongen. <see cref="Author"/> wordt door de
/// pagina gevuld uit de aangemelde gebruiker en staat daarom niet op
/// <see cref="SupportQuestionForm"/> — het type dat de POST bindt heeft alleen een tekstveld. Zou de
/// naam bindbaar zijn, dan zou een zelfgemaakte POST hem kunnen zetten, en dan staat er in de draad
/// een bericht op naam van iemand die het niet heeft geschreven.</para>
///
/// <para>En er is geen veld waarmee de afzender <see cref="SupportAuthor.Soratus"/> of
/// <see cref="SupportAuthor.FirstLine"/> kan worden. Dat is dezelfde vorm als bij
/// <c>HourBooking</c>, waar het ontbreken van een statusveld de vaste regel van §5 afdwingt: zou de
/// afzender hier een parameter zijn, dan bestond er een aanroep waarmee een klant een bericht van
/// Soratus in zijn eigen draad zet — en die aanroep zou compileren.</para>
/// </remarks>
public sealed record SupportQuestion
{
    /// <summary>
    /// De naam van de klantgebruiker zoals hij op het scherm hoort te staan.
    /// </summary>
    /// <remarks>
    /// Uit de aanmelding en niet uit het formulier. Zie de opmerkingen bij dit type en bij
    /// <see cref="SupportMessageDocument.Who"/>.
    /// </remarks>
    public string Author { get; init; } = string.Empty;

    /// <summary>De vraag, zoals de klant hem heeft getypt.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Controleert de invoer.
    /// </summary>
    /// <returns><c>null</c> als het klopt, anders de melding voor het formulier.</returns>
    public string? Validate() =>
        string.IsNullOrWhiteSpace(Author)
            ? "Er is niet vast te stellen wie dit bericht stuurt. Meld je opnieuw aan."
            : SupportBody.Validate(Text);
}

/// <summary>
/// Een antwoord van een mens van Soratus, klaar om te worden vastgelegd (§3.8).
/// </summary>
/// <remarks>
/// <para><strong>Geen afzenderveld, en dat is hier sterker dan bij <see cref="SupportQuestion"/>.</strong>
/// De naam komt van <see cref="Security.PortalWriteScope.Actor"/> — het bewijs waarmee de aanroeper
/// binnenkomt draagt hem al, dus er is geen parameter waarin een verkeerde naam past. Dezelfde
/// constructie als bij het fiatteren van een urenregel, waar <c>approvedBy</c> uit de scope komt en
/// niet uit het formulier.</para>
///
/// <para><strong>En er is geen grondslagveld en geen escalatieveld.</strong> Een mens die antwoordt
/// schrijft proza en wijst niet naar een bron; de bronregel hoort bij de eerstelijn en aan die eis
/// hangt <see cref="SupportAnswer"/>. Zou een menselijk antwoord ook een grondslag kunnen dragen, dan
/// bestond er een pad waarlangs de bronregel — het merkteken dat een antwoord op een aanwijsbaar
/// gegeven rust — onder een tekst komt te staan die iemand vrij heeft getypt.</para>
/// </remarks>
public sealed record SupportReply
{
    /// <summary>Het antwoord, zoals de operator het heeft getypt.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Controleert de invoer.
    /// </summary>
    /// <returns><c>null</c> als het klopt, anders de melding voor het formulier.</returns>
    public string? Validate() => SupportBody.Validate(Text);
}

/// <summary>
/// Welk deel van de draad er wordt opgehaald.
/// </summary>
/// <remarks>
/// <para><strong>Twee vormen en geen derde, en er is geen constructor.</strong> Dezelfde constructie en
/// dezelfde reden als bij <see cref="Data.HoursQuery"/>: er is bewust geen vorm die "alles" zegt. Een
/// draad groeit onbeperkt door, en een query zonder grens is een query die pas over drie jaar te duur
/// wordt — als niemand meer weet waar hij vandaan komt.</para>
///
/// <para><strong>Waarom er wél een tweede vorm is, anders dan bij de uren.</strong> Bij uren is de
/// grens een maand of een jaar, en dat is een grens die de gebruiker zelf kiest en begrijpt. Bij een
/// draad bestaat zo'n natuurlijke grens niet: er is alleen "recenter" en "ouder". Zonder
/// <see cref="Before"/> zou een lange draad zijn oudste berichten stil onbereikbaar maken, en dat is
/// bij een gesprek over een factuur precies het deel waar de vraag over gaat. Eén vorm zou hier dus
/// gegevens laten verdwijnen die er staan.</para>
/// </remarks>
public sealed record SupportThreadQuery
{
    /// <summary>
    /// Hoeveel berichten er per keer worden opgehaald.
    /// </summary>
    /// <remarks>
    /// Ruim genoeg dat de meeste draden in één keer passen, en klein genoeg dat de eerste weergave niet
    /// op een gesprek van drie jaar wacht. Het is één getal en geen instelling: een paginagrootte die
    /// per aanroeper verschilt maakt van "is er meer" een vraag met twee antwoorden.
    /// </remarks>
    public const int PageSize = 50;

    private SupportThreadQuery(string? olderThan)
    {
        OlderThan = olderThan;
    }

    /// <summary>
    /// De documentsleutel waarvóór wordt gelezen, of <c>null</c> voor het recentste deel.
    /// </summary>
    /// <remarks>
    /// Heet <c>OlderThan</c> en niet <c>Before</c>, zodat hij niet met de fabrieksmethode botst — en
    /// dezelfde naam als <see cref="SupportMessagePage.OlderThan"/>, want dat is hetzelfde ding: het
    /// ene deel geeft hem terug, het volgende verzoek geeft hem mee.
    /// </remarks>
    public string? OlderThan { get; }

    /// <summary>De recentste <see cref="PageSize"/> berichten.</summary>
    /// <returns>De query.</returns>
    public static SupportThreadQuery Newest() => new(olderThan: null);

    /// <summary>
    /// De <see cref="PageSize"/> berichten die ouder zijn dan dit bericht.
    /// </summary>
    /// <param name="messageId">De documentsleutel van het oudste bericht dat nu in beeld is.</param>
    /// <returns>De query, of het recentste deel als de sleutel leeg is.</returns>
    /// <remarks>
    /// <para><strong>De sleutel komt uit de adresbalk en wordt niet vertrouwd als sleutel maar als
    /// grens.</strong> Hij gaat als parameter naar Cosmos en wordt daar vergeleken, niet
    /// samengevoegd; een verzonnen waarde levert dus een leeg of een compleet deel op en nooit een
    /// fout. Een lege waarde valt terug op het recentste deel, want dat is wat iemand met een
    /// afgekapte link bedoelde.</para>
    ///
    /// <para>Dat dit werkt hangt aan één eigenschap van <see cref="SupportDocumentKeys.Id"/>: die
    /// sleutel sorteert chronologisch. Er staat een test op die eigenschap, want als hij verdwijnt
    /// verandert deze query stil van "de vorige vijftig" in "vijftig willekeurige".</para>
    /// </remarks>
    public static SupportThreadQuery Before(string? messageId) =>
        new(string.IsNullOrWhiteSpace(messageId) ? null : messageId.Trim());
}
