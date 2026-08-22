using Soratus.Portal.Security;

namespace Soratus.Portal.Support;

/// <summary>
/// Eén bubbel in de draad.
/// </summary>
/// <remarks>
/// <para><strong>Drie vormen, en de vorm is wat er in mag staan.</strong> Dit is de plek waar §3.8
/// wordt afgedwongen in plaats van afgesproken:</para>
/// <list type="bullet">
///   <item><description>
///     <see cref="SupportSaidBubble"/> — een mens zei iets. Vrije tekst, een naam, geen bron en geen
///     merkteken.
///   </description></item>
///   <item><description>
///     <see cref="SupportAnswerBubble"/> — de eerstelijn antwoordde. Draagt het merkteken
///     <see cref="SupportText.FirstLineBadge"/> én de bron, en die twee velden zijn niet-nullable en
///     worden in de constructor op leegheid gecontroleerd. Er bestaat dus geen antwoord van de
///     eerstelijn zonder bron: niet als iemand een <c>@if</c> vergeet, niet als een document
///     beschadigd is, niet over een half jaar.
///   </description></item>
///   <item><description>
///     <see cref="SupportHandoffBubble"/> — de eerstelijn wist het niet. Draagt het merkteken en géén
///     enkel gegeven: geen bedrag, geen urenstand, geen bron. Er ís geen bron, want er is geen
///     bewering.
///   </description></item>
/// </list>
///
/// <para><strong>De twee AI-vormen zijn niet in elkaar te veranderen, en dat is het hele punt.</strong>
/// Een bubbel die iets beweert heeft een bron; een bubbel zonder bron kan niets beweren. De
/// tussentoestand waar de eis van fase 5 over gaat — een antwoord dat klinkt als een antwoord en op
/// niets rust — heeft geen type.</para>
///
/// <para><strong>Waarom de drie vormen door beide rollen worden gebruikt.</strong> Dezelfde afweging als
/// bij de runtabel (§14 van de fase-0-afwijkingen): er staat één tabel omdat het enige verschil in één
/// tooltip zat, en twee tabellen zouden een tweede kopie van dezelfde kolomsporen betekenen. Hier is de
/// bubbel voor beide rollen letterlijk hetzelfde — een klant hóórt te zien wat de eerstelijn hem
/// antwoordde, en een operator hoort dat óók te zien, anders kan hij niet nakijken wat er namens Soratus
/// is gezegd. Het rolverschil zit dus niet in de bubbel maar in de weergave eromheen, en dat is een
/// typeverschil: zie <see cref="CustomerSupportView"/> en <see cref="OperatorSupportView"/>.</para>
///
/// <para>De constructor is <c>private protected</c>: buiten deze assembly is er geen vierde vorm bij te
/// maken.</para>
/// </remarks>
public abstract record SupportBubble
{
    /// <summary>Alleen de drie vormen hieronder.</summary>
    /// <param name="at">Wanneer het bericht is vastgelegd, in UTC.</param>
    /// <param name="text">De tekst van de bubbel, al geschoond.</param>
    private protected SupportBubble(DateTimeOffset at, string text)
    {
        At = at;
        Text = text;
    }

    /// <summary>
    /// Wanneer het bericht is vastgelegd, in UTC.
    /// </summary>
    /// <remarks>
    /// UTC en niet Nederlandse tijd; omzetten gebeurt bij het weergeven met <c>TimeFormat</c>. Punt 7
    /// van de fase-0-afwijkingen.
    /// </remarks>
    public DateTimeOffset At { get; }

    /// <summary>
    /// De tekst, al door <see cref="SupportBody.Clean"/> heen.
    /// </summary>
    /// <remarks>
    /// De projectie schoont opnieuw, ook al is er bij het schrijven al geschoond. Dat is de tweede van
    /// de twee plekken die punt 13 van de fase-0-afwijkingen eist: deze is de laatste stap voordat de
    /// tekst de HTML in gaat, en hij dekt wat de schrijfkant niet kan dekken — een document dat langs
    /// een ander pad in de container terecht is gekomen.
    /// </remarks>
    public string Text { get; }
}

/// <summary>
/// Een bubbel van een mens: de klant, of iemand van Soratus.
/// </summary>
/// <remarks>
/// <para><strong>Eén type voor beide mensen, met een vlag, en dat is geen rolfilter.</strong> Het is
/// dezelfde afweging als bij <c>RunsTable</c>: een vlag die de <em>uitlijning</em> en de kleur bepaalt
/// is opmaak, geen bevoegdheid. Er staat geen gegeven achter die vlag dat de één wel en de ander niet
/// mag zien — beide bubbels dragen precies dezelfde velden, want beide rollen mogen het hele gesprek
/// lezen. Waar wél een bevoegdheid achter zit, staat een eigen type: zie
/// <see cref="OperatorHandoff"/>.</para>
///
/// <para>Wie het schreef staat als naam en niet als verwijzing naar een gebruiker. Dezelfde reden als
/// bij <c>HourEntryDocument.By</c>: de tabel met gebruikers bestaat niet, en de naam die in de draad
/// hoort te staan is die van het moment van schrijven en niet die van vandaag.</para>
/// </remarks>
public sealed record SupportSaidBubble : SupportBubble
{
    /// <summary>Alleen de projectie maakt bubbels.</summary>
    /// <param name="at">Wanneer.</param>
    /// <param name="text">Wat.</param>
    /// <param name="who">De naam zoals hij onder de bubbel hoort te staan.</param>
    /// <param name="fromCustomer">Of dit de klant was.</param>
    internal SupportSaidBubble(DateTimeOffset at, string text, string? who, bool fromCustomer)
        : base(at, text)
    {
        Who = who;
        FromCustomer = fromCustomer;
    }

    /// <summary>De naam van de schrijver, of <c>null</c> als die niet is vastgelegd.</summary>
    public string? Who { get; }

    /// <summary>Of dit de klant was.</summary>
    public bool FromCustomer { get; }
}

/// <summary>
/// Een antwoord van de eerstelijn: met merkteken en met bron.
/// </summary>
/// <remarks>
/// <para><strong>Beide bronvelden zijn niet-nullable, en dat is de eis van §3.8 als
/// type-eigenschap.</strong> "Elke AI-bubbel toont de bron waarop het antwoord is gebaseerd" is hier
/// geen afspraak die iemand in de markup kan vergeten: er bestaat geen instantie van dit type zonder
/// bronregel en zonder pad. De projectie kan een bericht waarvan de bron niet meer te bepalen is dus
/// niet als antwoord tonen — zij heeft geen vorm om het in te zetten, en laat het bij de klant weg.
/// </para>
///
/// <para><strong>En het pad staat er náást het label, niet in plaats daarvan.</strong> Een bronregel
/// die alleen een woord is, is een bewering over een bewering. Met het pad erbij is het antwoord na te
/// kijken op het scherm waar het getal vandaan komt, en dat is wat een bron tot bron maakt.</para>
///
/// <para>Er is geen veld voor een zekerheid, een percentage of een voorbehoud. Dat lijkt informatie die
/// je wilt hebben en het is precies de verkeerde: een antwoord met "85% zeker" erbij is nog steeds een
/// antwoord, en de klant kan het verschil met een echt antwoord niet zien. De twijfel heeft hier een
/// eigen vorm, en dat is <see cref="SupportHandoffBubble"/>.</para>
/// </remarks>
public sealed record SupportAnswerBubble : SupportBubble
{
    /// <summary>Alleen de projectie maakt bubbels.</summary>
    /// <param name="at">Wanneer.</param>
    /// <param name="text">Het antwoord.</param>
    /// <param name="groundLabel">De bronregel. Niet leeg.</param>
    /// <param name="groundPath">Het pad naar het scherm waar de bron staat. Niet leeg.</param>
    /// <exception cref="ArgumentException">
    /// Als de bronregel of het pad leeg is. Dat is de vorm waarin §3.8 hier staat: een antwoord van de
    /// eerstelijn zonder bron is geen antwoord dat je verkeerd rendert, het is een antwoord dat niet
    /// gemaakt kan worden.
    /// </exception>
    internal SupportAnswerBubble(
        DateTimeOffset at,
        string text,
        string groundLabel,
        string groundPath)
        : base(at, text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groundLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(groundPath);

        GroundLabel = groundLabel;
        GroundPath = groundPath;
    }

    /// <summary>De bronregel, bijvoorbeeld <c>Uren · juli 2026</c>.</summary>
    public string GroundLabel { get; }

    /// <summary>Het pad naar het scherm waar de bron staat.</summary>
    public string GroundPath { get; }
}

/// <summary>
/// De eerstelijn wist het niet en heeft de vraag doorgezet.
/// </summary>
/// <remarks>
/// <para><strong>Dit type draagt geen enkel gegeven, en dat is wat het veilig maakt.</strong> Geen
/// bedrag, geen urenstand, geen agentnaam, en ook geen reden — die is operator-only en hangt aan
/// <see cref="OperatorHandoff"/>. Een bubbel die niets beweert heeft geen bron nodig, en een bubbel
/// zonder bron kan niets beweren. Dat is geen woordspel: het is de reden dat "elke AI-bubbel toont de
/// bron" en "ik weet het niet is een eersteklas uitkomst" geen tegenspraak zijn.</para>
///
/// <para>De reactietermijn staat er niet in de tekst maar als aparte regel op de weergave, uit het
/// contract. Zie <see cref="SupportText.SlaNotice"/>.</para>
/// </remarks>
public sealed record SupportHandoffBubble : SupportBubble
{
    /// <summary>Alleen de projectie maakt bubbels.</summary>
    /// <param name="at">Wanneer.</param>
    /// <param name="text">De zin uit <see cref="SupportText.Handoff"/>.</param>
    internal SupportHandoffBubble(DateTimeOffset at, string text)
        : base(at, text)
    {
    }
}

/// <summary>
/// Of er een eerstelijn is die kan antwoorden.
/// </summary>
/// <remarks>
/// <para><strong>Twee waarden, en <see cref="NotConfigured"/> staat vooraan.</strong> Dezelfde regel als
/// bij elke andere opsomming in dit portaal: de standaardwaarde hoort de veilige te zijn. Een
/// eerstelijn waarvan we niet kunnen vaststellen dat hij bestaat, is er niet — en dan hoort het scherm
/// te zeggen dat een mens antwoordt.</para>
///
/// <para>Dit is de toestand die §29 van de fase-0-afwijkingen bij de mailkant met zoveel woorden
/// eist: geen plaatshouder achter een naad, want een plaatshouder die "niets gemeten" antwoordt is
/// niet te onderscheiden van een echte "niets gemeten". Hier zou een plaatshouder altijd escaleren, en
/// dat is niet te onderscheiden van een eerstelijn die het niet weet. Deze twee waarden maken het
/// onderscheid zichtbaar op het scherm.</para>
/// </remarks>
public enum SupportFirstLineState
{
    /// <summary>
    /// Er is geen eerstelijn aangesloten. Elke vraag gaat naar een mens.
    /// </summary>
    NotConfigured,

    /// <summary>Er is een eerstelijn; hij antwoordt of hij escaleert.</summary>
    Available,
}

/// <summary>
/// De supportdraad zoals de klant hem ziet (§3.8).
/// </summary>
/// <remarks>
/// <para><strong>Wat er níet op staat is de helft van het ontwerp.</strong> Geen escalatieredenen, geen
/// berichten die niet toe te wijzen zijn, geen antwoorden waarvan de bron is weggevallen, geen naam van
/// een model, geen versie. Die hangen aan <see cref="OperatorSupportView"/>, en dit type heeft ze
/// niet — dus ze kunnen niet in de paginabron belanden, ook niet als iemand er over een half jaar een
/// tooltip bij zet. Dezelfde vorm als <c>CustomerLogLine</c> (§12), <c>CustomerRunRow</c> (§14),
/// <c>CustomerAgentsView</c> (§9) en <c>CustomerHoursView</c> (fase 3) — voor de zesde keer.</para>
///
/// <para><strong>En wat er wél op staat en niet op de operatorweergave.</strong> De uitweg naar een mens
/// (<see cref="SupportText.HumanEscape"/>), de reactietermijn, en de toestand van de eerstelijn. Dat is
/// de andere kant van hetzelfde besluit: §3.8 zegt dat in de operatorrol een mens antwoordt en de agent
/// er niet tussen springt, en die eis is hier geen <c>@if</c> maar een ontbrekend veld op het andere
/// type.</para>
///
/// <para><strong>De klantcontext staat er niet als paneel op, en dat is letterlijk §3.8.</strong> Er is
/// geen lijst met agents, geen contractkaart, geen urenoverzicht naast de draad — dat is simpelweg
/// alles wat we van de klant weten, en die staat op de vier andere schermen. Wat er wél is, is de
/// bronregel onder een antwoord: één aanwijzing naar precies het gegeven waar het antwoord op rust, met
/// een pad ernaartoe.</para>
/// </remarks>
public sealed record CustomerSupportView
{
    /// <summary>De klantslug.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam, voor de kop van het scherm.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd, in UTC.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>De bubbels, oudste eerst.</summary>
    public required IReadOnlyList<SupportBubble> Bubbles { get; init; }

    /// <summary>
    /// Het pad naar het oudere deel van de draad, of <c>null</c> als dit het begin is.
    /// </summary>
    public string? OlderPath { get; init; }

    /// <summary>
    /// Of er een eerstelijn is die kan antwoorden.
    /// </summary>
    /// <remarks>
    /// Staat op het scherm en niet alleen in een logregel. Een klant die een vraag stelt hoort te weten
    /// of er direct iets kan komen of dat hij op een mens wacht; dat is dezelfde eerlijkheid als "live
    /// tail loopt ~1 minuut achter" uit de ontwerpregels van de spec.
    /// </remarks>
    public required SupportFirstLineState FirstLine { get; init; }

    /// <summary>
    /// Wat er gebeurt als de eerstelijn het niet weet: naar wie, en binnen welke termijn.
    /// </summary>
    /// <remarks>
    /// Uit <see cref="Data.ContractDocument.Sla"/>, via <see cref="SupportText.SlaNotice"/>. Er wordt
    /// niets omgerekend en er wordt geen termijn verzonnen als het contract er geen noemt.
    /// </remarks>
    public required string SlaNotice { get; init; }

    /// <summary>De melding als de draad nog leeg is.</summary>
    public required string EmptyNotice { get; init; }
}

/// <summary>
/// Eén escalatie van de eerstelijn, met de reden. Operator-only.
/// </summary>
/// <remarks>
/// <para><strong>Dit type bestaat alleen op het operatorpad, en dat is waarom de reden een enum kan
/// blijven.</strong> De klant leest één zin die wij hebben geschreven; de operator leest waaróm, in
/// woorden uit <see cref="SupportText.EscalationLabel"/>. Zou de reden een veld op de bubbel zijn, dan
/// stond hij op het klantpad en moest een <c>@if</c> hem verbergen — en dan is de vraag welk woord de
/// klant leest een vraag over de markup in plaats van over het type.</para>
///
/// <para><strong>Een eigen lijst en geen veld in de draad, om de reden die het urenscherm al
/// gebruikt.</strong> <c>OperatorHoursView.Rejected</c> zet de afgewezen regels in een eigen lijst in
/// plaats van ze tussen de specificatie te laten staan, omdat een lijst vol afgewezen regels de
/// bruikbare lijst onbruikbaar maakt. Hier hetzelfde: een operator die wil weten waar de eerstelijn op
/// vastloopt, scant liever een lijst dan een gesprek.</para>
/// </remarks>
public sealed record OperatorHandoff
{
    /// <summary>Alleen de operatorprojectie maakt deze.</summary>
    /// <param name="at">Wanneer.</param>
    /// <param name="reason">Waarom.</param>
    internal OperatorHandoff(DateTimeOffset at, SupportEscalation reason)
    {
        At = at;
        Reason = reason;
    }

    /// <summary>Wanneer de eerstelijn de vraag doorzette, in UTC.</summary>
    public DateTimeOffset At { get; }

    /// <summary>Waarom. Zie <see cref="SupportText.EscalationLabel"/>.</summary>
    public SupportEscalation Reason { get; }
}

/// <summary>
/// Een bericht dat niet als bubbel te tonen is. Operator-only.
/// </summary>
/// <remarks>
/// <para><strong>Dit is de spiegel van "de klant ziet dit niet".</strong> De klantprojectie laat een
/// bericht weg dat niet toe te wijzen is (<see cref="SupportAuthor.Unknown"/>) of een antwoord van de
/// eerstelijn waarvan de bron niet meer te bepalen is. Zonder dit type zou dat weglaten stil zijn — en
/// een bericht dat verdwijnt zonder dat iemand het merkt is erger dan een bericht dat er vreemd
/// uitziet. Het staat hier, met de sleutel erbij, zodat het te vinden is.</para>
///
/// <para>De tekst van zo'n bericht staat hier <em>niet</em>. Wat er niet toe te wijzen is, hoort niet met
/// ónze stem op een scherm te komen: de reden dat het wordt weggelaten is precies dat we niet weten wie
/// het schreef. De sleutel is genoeg om het in de opslag op te zoeken.</para>
/// </remarks>
public sealed record OperatorUnusableMessage
{
    /// <summary>Alleen de operatorprojectie maakt deze.</summary>
    /// <param name="at">Wanneer.</param>
    /// <param name="messageId">De documentsleutel.</param>
    /// <param name="why">Wat er niet aan klopt.</param>
    internal OperatorUnusableMessage(DateTimeOffset at, string messageId, string why)
    {
        At = at;
        MessageId = messageId;
        Why = why;
    }

    /// <summary>Wanneer het bericht is vastgelegd, in UTC.</summary>
    public DateTimeOffset At { get; }

    /// <summary>De documentsleutel.</summary>
    public string MessageId { get; }

    /// <summary>Wat er niet aan klopt, in woorden voor een operator.</summary>
    public string Why { get; }
}

/// <summary>
/// De supportdraad zoals de operator hem ziet (§3.8): een mens antwoordt.
/// </summary>
/// <remarks>
/// <para><strong>Er staat geen eerstelijn op dit type.</strong> Geen
/// <see cref="SupportFirstLineState"/>, geen uitweg naar een mens, geen vraagformulier. §3.8 zegt: in
/// de operatorrol antwoordt een mens en springt de agent er niet tussen — en dat is hier een
/// typeverschil en geen filter. De operatorweergave <em>heeft</em> die dingen niet, dus er is geen
/// <c>@if</c> die vergeten kan worden en geen pad waarlangs een antwoord van de eerstelijn op een
/// operatorbericht kan volgen.</para>
///
/// <para>Wat er wél op staat en niet op het klanttype: <see cref="Handoffs"/> met de redenen, en
/// <see cref="Unusable"/> met de berichten die de klant niet ziet. Dat is de spiegel die elke
/// "de klant ziet dit niet" hoort te hebben.</para>
/// </remarks>
public sealed record OperatorSupportView
{
    /// <summary>De klantslug.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd, in UTC.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>De bubbels, oudste eerst. Dezelfde als de klant ziet.</summary>
    public required IReadOnlyList<SupportBubble> Bubbles { get; init; }

    /// <summary>Het pad naar het oudere deel van de draad, of <c>null</c>.</summary>
    public string? OlderPath { get; init; }

    /// <summary>
    /// De reactietermijn uit het contract, zodat de operator weet waar de klant op rekent.
    /// </summary>
    /// <remarks>
    /// Dezelfde tekst als de klant leest, uit dezelfde functie. Zouden die twee verschillen, dan zegt
    /// het portaal aan twee kanten iets anders over dezelfde afspraak.
    /// </remarks>
    public required string SlaNotice { get; init; }

    /// <summary>Waar de eerstelijn op vastliep, met de reden. Operator-only.</summary>
    public required IReadOnlyList<OperatorHandoff> Handoffs { get; init; }

    /// <summary>Berichten die de klant niet ziet, met de reden. Operator-only.</summary>
    public required IReadOnlyList<OperatorUnusableMessage> Unusable { get; init; }

    /// <summary>De melding als de draad nog leeg is.</summary>
    public required string EmptyNotice { get; init; }
}

/// <summary>
/// Bouwt de viewmodels van het supportscherm op (§3.8).
/// </summary>
/// <remarks>
/// <para><strong>De scope die je meegeeft bepaalt de vorm die je terugkrijgt.</strong> Een
/// <see cref="CustomerScope"/> levert <see cref="CustomerSupportView"/>, een
/// <see cref="CustomerWriteScope"/> levert <see cref="OperatorSupportView"/>. Geen conventie maar
/// overloadresolutie: er is geen manier om met een klantscope het operatorviewmodel te krijgen, want
/// die overload bestaat niet. Dezelfde constructie als <see cref="Views.IHourViews"/> en
/// <see cref="Views.IBillingViews"/>.</para>
///
/// <para><strong>Beide projecties gaan zelf uit de documenten en niet de één uit de ander.</strong> Dat
/// is punt 14 van de fase-0-afwijkingen: bestond er een pad van de volle vorm naar de smalle, dan is er
/// een pad waarlangs een veld kan meeliften.</para>
///
/// <para><strong>Schrijven loopt hier niet langs.</strong> Een pagina die een bericht plaatst roept
/// <see cref="SupportDesk"/> of <see cref="ISupportStore"/> aan en bouwt de weergave daarna opnieuw op.
/// Dezelfde afspraak als bij het contract en de uren.</para>
/// </remarks>
public interface ISupportViews
{
    /// <summary>
    /// Bouwt de draad zoals de klant hem ziet.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="query">Welk deel van de draad.</param>
    /// <param name="firstLine">Of er een eerstelijn is aangesloten.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave. Ook als de draad leeg is.</returns>
    Task<CustomerSupportView> BuildThreadAsync(
        CustomerScope scope,
        SupportThreadQuery query,
        SupportFirstLineState firstLine,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bouwt de draad zoals de operator hem ziet.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="query">Welk deel van de draad.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave, met de escalaties en de onbruikbare berichten erbij.</returns>
    /// <remarks>
    /// Deze overload neemt géén <see cref="SupportFirstLineState"/>, en dat is geen vergetelheid: de
    /// operatorweergave heeft geen veld om hem in te zetten. Zie <see cref="OperatorSupportView"/>.
    /// </remarks>
    Task<OperatorSupportView> BuildThreadAsync(
        CustomerWriteScope scope,
        SupportThreadQuery query,
        CancellationToken cancellationToken = default);
}
