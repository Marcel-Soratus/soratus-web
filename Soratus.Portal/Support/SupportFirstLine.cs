using System.Text.Json.Serialization;

namespace Soratus.Portal.Support;

/// <summary>
/// Waarom de eerstelijn geen antwoord geeft en de vraag naar een mens gaat.
/// </summary>
/// <remarks>
/// <para><strong>Een enum en geen string, en dat is hier de scherpste regel van deze map.</strong>
/// Dezelfde reden als bij <see cref="Mail.StatementRefusal"/> en <see cref="Mail.StatementFigureGap"/>:
/// een reden die als tekst reist komt op een dag uit een <c>catch</c>-blok — een
/// <c>Exception.Message</c>, een pad, een resource-id. Bij de mail lag die tekst in de inbox van de
/// klant. Hier staat hij in zijn berichtendraad, en die blijft staan; een postbus kan een klant
/// leegmaken en dit scherm niet.</para>
///
/// <para><strong>Deze waarden komen niet als woord in de bubbel terecht — geen van de vier.</strong>
/// De zin die de klant leest is er één, met de hand geschreven, en hij is voor alle vier hetzelfde:
/// zie <see cref="SupportText.Handoff"/>. Dat is een bewust verlies. "Deze vraag valt buiten de
/// gegevens die ik heb" is nuttiger dan "ik weet het niet zeker", en het is óók een bewering — over
/// wat wij wel en niet van deze klant weten — en zodra het model uit vier zinnen mag kiezen, mag het
/// vier verschillende dingen beweren. Een reden die alleen de operator ziet kan niet onwaar zijn tegen
/// een klant.</para>
///
/// <para>De volgorde is niet willekeurig: <see cref="NotSure"/> staat vooraan omdat een niet-gezette
/// enum die waarde leest, en van de vier is dit de enige die niets beweert.</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SupportEscalation>))]
public enum SupportEscalation
{
    /// <summary>
    /// De eerstelijn weet het niet zeker.
    /// </summary>
    /// <remarks>
    /// De veilige stand, en de standaardwaarde. Een document met een leeg of onleesbaar
    /// <c>escalation</c>-veld leest hierop uit, en dat is de enige van de vier die geen bewering doet
    /// over waaróm het niet lukte.
    /// </remarks>
    [JsonStringEnumMemberName("notSure")]
    NotSure,

    /// <summary>De vraag gaat niet over gegevens die het portaal van deze klant heeft.</summary>
    [JsonStringEnumMemberName("outsideTheData")]
    OutsideTheData,

    /// <summary>
    /// De vraag vraagt een besluit of een afspraak en geen feit.
    /// </summary>
    /// <remarks>
    /// "Kan de bundel omhoog", "wanneer is agent X klaar". Daar bestaat geen portaalgegeven voor, en
    /// een antwoord zou een toezegging zijn. Dat is niet iets dat een eerstelijn hoort te doen, ook
    /// niet als hij het zou kunnen.
    /// </remarks>
    [JsonStringEnumMemberName("needsAHuman")]
    NeedsAHuman,

    /// <summary>
    /// Het portaal heeft het antwoord niet aangenomen.
    /// </summary>
    /// <remarks>
    /// <para><strong>Deze waarde is het oordeel van het portaal en niet van de eerstelijn.</strong> Hij
    /// staat er voor het geval dat er wél een antwoord kwam, maar niet één dat kan bestaan: een
    /// grondslag die niet is aangeboden, een uitzondering uit de naad, een naad die halverwege opgaf,
    /// of <c>null</c>.</para>
    ///
    /// <para><strong>Eén waarde voor die vier gevallen, en dat is opzet.</strong> Voor de operator is
    /// de handeling in alle vier dezelfde — kijken wat de eerstelijn deed en de klant zelf antwoorden —
    /// en het onderscheid staat in de logregel, waar het thuishoort. Dezelfde afweging als bij
    /// <see cref="Mail.StatementFigureGap.ContractIncomplete"/>, waar drie contractgaten met opzet tot
    /// één waarde zijn teruggebracht.</para>
    /// </remarks>
    [JsonStringEnumMemberName("answerNotUsable")]
    AnswerNotUsable,
}

/// <summary>
/// De vraag zoals de eerstelijn hem krijgt: de woorden van de klant, en alles waarop een antwoord mag
/// rusten.
/// </summary>
/// <remarks>
/// <para><strong>Wat er níet op staat is het belangrijkste.</strong> Geen klantslug, geen scope, geen
/// verbinding met de opslag, geen contract, geen toegangslijst, geen e-mailadres. De eerstelijn krijgt
/// geen sleutel waarmee hij iets kan opvragen; hij krijgt de gegevens die hij mag gebruiken, en verder
/// niets. Dat is dezelfde vorm als de naad van de mailkant — <see cref="Mail.MonthlyStatementFigures"/>
/// draagt geen dienstuitsplitsing omdat het retourtype die velden niet heeft — met dit verschil dat de
/// grens hier aan de <em>ingang</em> zit en niet aan de uitgang.</para>
///
/// <para><strong>Er staat ook geen gesprek op, alleen deze ene vraag.</strong> Dat maakt de eerstelijn
/// dommer: hij ziet niet dat de klant hetzelfde drie berichten eerder al vroeg. Het is met opzet zo
/// gelaten. De draad bevat vrije tekst van een operator, en die tekst is precies de soort tekst waarvan
/// punt 13 en 14 van de fase-0-afwijkingen zeggen dat er interne dingen in kunnen staan. Wie hier ooit
/// de historie bij wil, moet eerst bedenken wat een oude operatorregel in een prompt doet — en dat is
/// een eigen ronde.</para>
/// </remarks>
public sealed record SupportEnquiry
{
    /// <summary>
    /// De vraag van de klant, al geschoond door <see cref="SupportBody.Clean"/>.
    /// </summary>
    /// <remarks>
    /// Vrije tekst, en de eerstelijn hoort hem als tekst te behandelen en niet als opdracht. Dat is
    /// niet met een instructie geregeld maar met de vorm van <see cref="SupportAnswer"/>: wat er ook in
    /// deze tekst staat, de uitkomst is een keuze uit <see cref="Grounds"/> of een escalatie.
    /// </remarks>
    public required string Question { get; init; }

    /// <summary>
    /// Alles waarop een antwoord mag rusten: de agentstatus, de uren en de bedragen van deze klant, in
    /// de vorm waarin de klant ze al mag zien.
    /// </summary>
    /// <remarks>
    /// Begrensd op <see cref="SupportGrounds.Maximum"/>. Een lege lijst is een geldige toestand — een
    /// klant zonder agents, zonder uren en zonder gemeten maand — en dan is er niets om op te
    /// antwoorden. Dat hoort dan ook een escalatie te worden en geen antwoord over niets.
    /// </remarks>
    public required IReadOnlyList<SupportGround> Grounds { get; init; }
}

/// <summary>
/// Wat de eerstelijn teruggeeft: een antwoord dat op aangewezen grondslagen rust, of een escalatie.
/// </summary>
/// <remarks>
/// <para><strong>Twee vormen en geen derde, en er is geen constructor.</strong> Dezelfde constructie als
/// <see cref="Data.HoursQuery"/>: geen publieke setters, geen constructor, alleen fabrieksmethoden. Zo
/// bestaat de toestand "geen van beide gevuld" niet — en dat is hier niet netheid maar de eis van
/// fase 5.</para>
///
/// <para><strong>Er is geen tekstveld. Nergens.</strong> Dat is het antwoord op "een agent die vrij over
/// de gegevens mag praten, kan een getal noemen dat hij niet heeft gekregen". Hij mag hier niet vrij
/// praten: hij mag wijzen. Het portaal stelt de zin samen uit <see cref="SupportGround.Fact"/> — een
/// tekst die het portaal zelf heeft opgemaakt uit zijn eigen weergaven, met dezelfde opmaakfuncties
/// als het scherm waar de bronregel naartoe wijst. Een verzonnen bedrag heeft geen veld om in te
/// reizen.</para>
///
/// <para><strong>En <see cref="Escalate"/> is een eersteklas uitkomst en geen tekst.</strong> "Ik weet
/// het niet" is hier geen zin die het model kan kiezen maar de andere helft van het type. Een model dat
/// niets weet kan niet in de verleiding komen om toch iets te zeggen, want de vorm waarin "toch iets
/// zeggen" past bestaat niet.</para>
///
/// <para><strong>Wat dit níet oplost.</strong> Een gekaapte of slecht werkende eerstelijn kan de
/// <em>verkeerde</em> grondslag kiezen: op een vraag over juli de bubbel van juni. Dat is een fout die
/// een klant kan zien — de bronregel noemt de maand en verwijst naar het scherm — en dat is precies het
/// verschil met een verzonnen getal, dat er hetzelfde uitziet als een echt getal. Kleiner, en niet nul.
/// </para>
/// </remarks>
public sealed record SupportAnswer
{
    /// <summary>Alleen de twee fabrieksmethoden maken antwoorden.</summary>
    private SupportAnswer(SupportGround? ground, SupportEscalation? escalation)
    {
        Ground = ground;
        Escalation = escalation;
    }

    /// <summary>De grondslag waarop het antwoord rust, of <c>null</c> bij een escalatie.</summary>
    public SupportGround? Ground { get; }

    /// <summary>De escalatiereden, of <c>null</c> als dit een antwoord is.</summary>
    public SupportEscalation? Escalation { get; }

    /// <summary>Of dit een antwoord is en geen escalatie.</summary>
    public bool IsGrounded => Ground is not null;

    /// <summary>
    /// Een antwoord dat op deze ene grondslag rust.
    /// </summary>
    /// <param name="ground">De grondslag. Verplicht, en er is er precies een.</param>
    /// <returns>Het antwoord.</returns>
    /// <remarks>
    /// <para><strong>Een grondslag per antwoord, en dat is een besluit met een prijs.</strong> De eerste
    /// opzet liet er meerdere toe -- <c>GroundedIn(ground, params more)</c> -- en die is afgewezen toen
    /// bleek wat hij oplevert: de bubbel draagt een bronregel, dus een tekst die uit drie feiten is
    /// opgebouwd zou onder een bron staan die maar een derde ervan dekt. Dan is de bronregel geen bron
    /// meer maar een suggestie, en dat is precies de fout die de eis in 3.8 met dat merkteken wil
    /// uitsluiten. De andere uitweg -- drie bronregels onder een bubbel -- maakt van een antwoord een
    /// rapport.</para>
    ///
    /// <para>De prijs is dat een vraag die twee gegevens nodig heeft niet in een antwoord past. Dat
    /// blijkt geen prijs te zijn: 3.8 noemt de antwoorden zelf, en het zijn er vier -- agentstatus met
    /// uitleg en runs, uren tegen bundel, laatste factuur met betaalstatus, open sprintitems -- en elk
    /// daarvan is een gegeven. De grondslag draagt dat hele gegeven in een regel; zie
    /// <see cref="SupportGround.Fact"/>.</para>
    ///
    /// <para>Of de meegegeven grondslag ook echt is aangeboden, kan dit type niet weten. Dat controleert
    /// <see cref="CosmosSupportStore.Accept"/> bij het aannemen: een grondslag die niet in de
    /// <see cref="SupportEnquiry.Grounds"/> van dit verzoek stond, wordt niet aangenomen. De constructor
    /// van <see cref="SupportGround"/> is <c>internal</c>, dus een implementatie buiten deze assembly kan
    /// er geen maken -- deze controle vangt het geval dat zij er een van een ander verzoek teruggeeft.
    /// </para>
    /// </remarks>
    public static SupportAnswer GroundedIn(SupportGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);

        return new SupportAnswer(ground, escalation: null);
    }

    /// <summary>
    /// Geen antwoord: de vraag gaat naar een mens.
    /// </summary>
    /// <param name="reason">Waarom. Zie <see cref="SupportEscalation"/>.</param>
    /// <returns>De escalatie.</returns>
    public static SupportAnswer Escalate(SupportEscalation reason) => new(ground: null, reason);
}

/// <summary>
/// De AI-eerstelijnsagent (§3.8): één vraag erin, één antwoord of een escalatie eruit.
/// </summary>
/// <remarks>
/// <para><strong>Dit is een naad en er is met opzet geen implementatie.</strong> Dezelfde zet als bij
/// <see cref="Mail.IMonthlyStatementFigures"/> toen de mailkant werd gebouwd, en om een zwaardere
/// reden: de moeilijke eis van fase 5 is dat de eerstelijn niets mag verzinnen, en dat is een
/// ontwerpvraag die een eigen ronde verdient. Wat hier staat is de vorm waarin een antwoord moet
/// passen, en die vorm is waar de eis wordt afgedwongen — niet de aanroep naar een model.</para>
///
/// <para><strong>Er is ook geen registratie in <c>Program.cs</c>, en dat is niet vergeten.</strong> §29
/// van de fase-0-afwijkingen wijst een plaatshouder achter een naad uitdrukkelijk af: die antwoordt
/// "niets gemeten", en dat is niet te onderscheiden van een echte "niets gemeten". Hier zou een
/// plaatshouder altijd escaleren, en dat is niet te onderscheiden van een eerstelijn die het niet weet
/// — een storing die zich voordoet als werkende functionaliteit. Daarom wordt hij <em>opgevraagd en
/// niet geëist</em>: <see cref="SupportDesk"/> haalt hem met <c>GetService</c> op, en de afwezigheid is
/// een eigen toestand die op het scherm staat
/// (<see cref="CustomerSupportView.FirstLine"/>). Een klant leest dan dat een mens antwoordt, en dat is
/// waar.</para>
///
/// <para><strong>Over het model, voor wie dit straks implementeert.</strong> In deze map staat geen
/// modelnaam, en er hoort er ook geen in te komen: dat is een configuratiewaarde en niet een
/// letterlijke waarde in code. De marketingsite heeft daar een eigen afspraak over in
/// <c>handoff/CLAUDE.md</c> (daar staat één model vast en het wisselen ervan vraagt om overleg); die
/// afspraak geldt voor die site en niet voor dit portaal. Wat hier vastligt is iets anders en het is
/// vormvrij: welk model er ook onder deze naad hangt, hij kan geen getal terugsturen.</para>
///
/// <para><strong>Wat een implementatie niet kan.</strong> Zij kan geen <see cref="SupportGround"/>
/// maken (interne constructor), geen tekst teruggeven (er is geen veld), geen klant opzoeken (de
/// <see cref="SupportEnquiry"/> draagt geen sleutel) en niets naar Cosmos schrijven (het
/// schrijfrecht op de container <c>customers</c> hangt aan de identiteit van het portaal, en die deelt
/// hij niet — zie <see cref="SupportMessageDocument"/>). Wat zij kan is kiezen.</para>
/// </remarks>
public interface ISupportFirstLine
{
    /// <summary>
    /// Beantwoordt één vraag van één klant, of escaleert.
    /// </summary>
    /// <param name="enquiry">De vraag en de grondslagen waarop een antwoord mag rusten.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// Het antwoord, of <c>null</c>. <c>null</c> is toegestaan en betekent hetzelfde als
    /// <see cref="SupportEscalation.AnswerNotUsable"/>: er komt geen antwoord in de draad en een mens
    /// neemt het over. Dat het toegestaan is, is opzet — een implementatie die halverwege opgeeft hoort
    /// niet te moeten kiezen tussen een uitzondering en een verzonnen antwoord.
    /// </returns>
    Task<SupportAnswer?> AnswerAsync(
        SupportEnquiry enquiry,
        CancellationToken cancellationToken = default);
}
