using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Support;

/// <summary>
/// Eén deel van de draad, met de grens naar het oudere deel erbij.
/// </summary>
/// <remarks>
/// <para><strong><see cref="OlderThan"/> is de vraag "is er meer" en niet het antwoord "hoeveel".</strong>
/// Een teller zou een tweede query vragen (of een <c>COUNT</c> over de hele partitie) en dat getal doet
/// niets: de lezer wil weten of er een knop hoort te staan. De store leest daarom één bericht meer dan
/// hij teruggeeft en laat dat ene weg.</para>
/// </remarks>
/// <param name="Messages">De berichten, oudste eerst — de leesrichting van een gesprek.</param>
/// <param name="OlderThan">
/// De documentsleutel waarvóór het volgende deel begint, of <c>null</c> als dit het begin van de draad
/// is.
/// </param>
public sealed record SupportMessagePage(
    IReadOnlyList<SupportMessageDocument> Messages,
    string? OlderThan);

/// <summary>
/// De opslag van de supportdraad (§3.8): lezen, een vraag van de klant, een antwoord van een mens, en
/// het vastleggen van wat de eerstelijn ervan maakte.
/// </summary>
/// <remarks>
/// <para><strong>Een eigen interface naast <see cref="IPortalDataStore"/> en
/// <see cref="IPortalHoursStore"/>, om dezelfde reden als daar staat:</strong> die eerste is de
/// autorisatiebron van het portaal, en een pagina die een bericht plaatst hoort niet hetzelfde bewijs
/// in handen te hebben als een pagina die toegang uitdeelt.</para>
///
/// <para><strong>Er is geen methode die een bestaand bericht wijzigt of verwijdert.</strong> Dat is
/// geen omissie. Een draad is een verslag: "dit hebben jullie mij geantwoord" is een vraag die maanden
/// later komt, en een antwoord dat achteraf te wijzigen is maakt van dat verslag een bewering zonder
/// bron. Dezelfde regel als bij een gefiatteerde urenregel, die ook niet verdwijnt maar waar een
/// correctie tegenover komt te staan. Wil iemand iets terugnemen, dan is dat een volgend bericht.</para>
///
/// <para><strong>De drie schrijfmethoden verschillen niet in wat ze mogen maar in wat ze
/// kúnnen.</strong> Geen van de drie heeft een afzender als parameter:</para>
/// <list type="bullet">
///   <item><description>
///     <see cref="PostQuestionAsync"/> schrijft altijd <see cref="SupportAuthor.Customer"/>;
///   </description></item>
///   <item><description>
///     <see cref="PostReplyAsync"/> schrijft altijd <see cref="SupportAuthor.Soratus"/>, met de naam
///     uit de scope;
///   </description></item>
///   <item><description>
///     <see cref="RecordFirstLineAsync"/> schrijft altijd <see cref="SupportAuthor.FirstLine"/>, en is
///     de enige die dat kan.
///   </description></item>
/// </list>
/// <para>Er bestaat dus geen aanroep waarmee een klant een bericht van Soratus in zijn draad zet, geen
/// aanroep waarmee een operator zich als de eerstelijn voordoet, en geen aanroep waarmee de eerstelijn
/// vrije tekst plaatst. Dat is dezelfde vorm als het ontbrekende statusveld op <c>HourBooking</c>: de
/// verkeerde aanroep is niet fout, hij is <em>niet te schrijven</em>.</para>
/// </remarks>
public interface ISupportStore
{
    /// <summary>
    /// Leest een deel van de draad met een leesrecht op deze klant.
    /// </summary>
    /// <param name="scope">Het leesrecht. Levert de partitiesleutel, en daarmee de grens.</param>
    /// <param name="query">Welk deel. Zie <see cref="SupportThreadQuery"/>.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De berichten, oudste eerst.</returns>
    /// <remarks>
    /// <para><strong>Beide overloads geven dezelfde documenten terug, en dat is geen slordigheid.</strong>
    /// Anders dan bij de uren — waar de klantoverload de te fiatteren regels niet uit de opslag
    /// haalt — is er hier niets in de draad wat de operator wel en de klant niet mag zien. De klant
    /// hoort te zien wat de eerstelijn hem heeft geantwoord, en de operator hoort dat óók te zien:
    /// anders kan hij niet nakijken wat er namens Soratus is gezegd, en dat is precies de eis dat een
    /// mens de eerstelijn kan overnemen.</para>
    ///
    /// <para>Het rolverschil zit dus niet in deze methode maar in de projectie erna. Zie
    /// <see cref="ISupportViews"/>: één documentvorm, twee weergavetypen, en de ene is niet uit de
    /// andere te maken.</para>
    ///
    /// <para><strong>Twee overloads en niet één, om de reden die
    /// <see cref="CustomerWriteScope"/> zelf noemt.</strong> Een <see cref="CustomerScope"/> bestaat
    /// alleen voor een klant met een ingerichte telemetrie-opslag. Een operator die naar een klant in
    /// onboarding kijkt heeft die dus niet, en juist die klant heeft vragen. Zou er één overload op de
    /// leesscope staan, dan was de supportdraad van een klant zonder uitgerolde agents voor Soratus
    /// onbereikbaar.</para>
    /// </remarks>
    Task<SupportMessagePage> ReadThreadAsync(
        CustomerScope scope,
        SupportThreadQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Leest een deel van de draad met een schrijfrecht op deze klant.
    /// </summary>
    /// <param name="scope">Het schrijfrecht. Levert de partitiesleutel.</param>
    /// <param name="query">Welk deel.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De berichten, oudste eerst.</returns>
    /// <remarks>Zie de andere overload voor waarom er twee zijn en waarom ze hetzelfde teruggeven.</remarks>
    Task<SupportMessagePage> ReadThreadAsync(
        CustomerWriteScope scope,
        SupportThreadQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legt een vraag van de klant vast.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant, van de gebruiker die de vraag stelt.</param>
    /// <param name="question">De vraag.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het weggeschreven bericht, of een melding.</returns>
    /// <remarks>
    /// <para><strong>Dit is de eerste schrijfactie in dit portaal die op een leesrecht rust, en dat
    /// hoort benoemd te worden.</strong> Elke andere schrijfkant neemt een
    /// <see cref="CustomerWriteScope"/>, en die volgt uitsluitend uit de operatorrol. §2 geeft de klant
    /// bij Support wél iets: <em>"bericht sturen: ✓"</em>. Het is de enige regel in de rolmatrix waar
    /// een klant iets mag veranderen.</para>
    ///
    /// <para>Waarom een <see cref="CustomerScope"/> daarvoor voldoende bewijs is: het enige dat deze
    /// methode kan wegschrijven is een bericht van de klant zelf, in de partitie van die klant, met de
    /// afzender vast op <see cref="SupportAuthor.Customer"/>. Er is geen veld waarin iets anders past.
    /// Een leesrecht is dus precies genoeg — en een nieuw scope-type zou een tweede plek zijn waar
    /// bepaald wordt wie bij welke klant mag.</para>
    ///
    /// <para><strong>Wat dat wél openlaat, en het staat als punt van twijfel in het rapport.</strong>
    /// Een operator krijgt van <see cref="ICustomerScopeResolver.ResolveAsync"/> óók een
    /// <see cref="CustomerScope"/> — dat is de rol. Deze methode is dus door een operator aan te roepen,
    /// en dan staat er een bericht in de draad met de afzender "klant" en zijn naam eronder. Het
    /// supportscherm doet dat niet: de operatortak heeft geen vraagformulier, en dat is een
    /// typeverschil. Maar de aanroep is te schrijven, en de naam in het bericht is dan de enige plek
    /// waar het te zien is.</para>
    /// </remarks>
    Task<PortalWriteResult<SupportMessageDocument>> PostQuestionAsync(
        CustomerScope scope,
        SupportQuestion question,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legt het antwoord van een mens van Soratus vast.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant. Levert de partitiesleutel en de naam.</param>
    /// <param name="reply">Het antwoord.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het weggeschreven bericht, of een melding.</returns>
    Task<PortalWriteResult<SupportMessageDocument>> PostReplyAsync(
        CustomerWriteScope scope,
        SupportReply reply,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legt vast wat de eerstelijn van een vraag heeft gemaakt: een antwoord met een bron, of een
    /// escalatie.
    /// </summary>
    /// <param name="scope">Het leesrecht op de klant wiens vraag het was.</param>
    /// <param name="enquiry">
    /// Het verzoek zoals het aan de eerstelijn is gegeven. Draagt de grondslagen die zijn
    /// <em>aangeboden</em>, en dat is waar het antwoord tegen wordt gehouden.
    /// </param>
    /// <param name="answer">
    /// Het antwoord van de eerstelijn, of <c>null</c>. <c>null</c> is toegestaan en levert een escalatie
    /// op: een implementatie die halverwege opgeeft hoort niet te moeten kiezen tussen een uitzondering
    /// en een verzonnen antwoord.
    /// </param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het weggeschreven bericht, of een melding.</returns>
    /// <remarks>
    /// <para><strong>Dit is de plek waar de acceptatie-eis van fase 5 wordt afgedwongen, en het is één
    /// plek.</strong> Er is geen tweede methode die een bericht van de eerstelijn kan wegschrijven, en
    /// deze neemt zowel het antwoord als het verzoek waarin de grondslagen zaten. Wat er gebeurt:</para>
    /// <list type="number">
    ///   <item><description>
    ///     is <paramref name="answer"/> <c>null</c>, of is het een escalatie, dan komt er een
    ///     escalatiebericht — met de reden als enum en zonder één getal erin;
    ///   </description></item>
    ///   <item><description>
    ///     is het een antwoord, dan moet de grondslag erin ook in
    ///     <see cref="SupportEnquiry.Grounds"/> staan. Zo niet, dan wordt het antwoord niet aangenomen
    ///     en komt er een escalatie met <see cref="SupportEscalation.AnswerNotUsable"/>;
    ///   </description></item>
    ///   <item><description>
    ///     de tekst van het bericht komt van <see cref="SupportText.Answer"/> en dus uit die
    ///     grondslag. <paramref name="answer"/> heeft geen tekstveld, dus er is niets van de eerstelijn
    ///     dat hier terecht kan komen behalve de keuze zelf.
    ///   </description></item>
    /// </list>
    ///
    /// <para><strong>Er komt dus altijd een bericht, en nooit een antwoord zonder bron.</strong> Dat is
    /// het verschil tussen deze vorm en een instructie aan een model: een antwoord zonder aanwijsbare
    /// bron is hier geen fout die je kunt maken maar een toestand die niet bestaat.</para>
    /// </remarks>
    Task<PortalWriteResult<SupportMessageDocument>> RecordFirstLineAsync(
        CustomerScope scope,
        SupportEnquiry enquiry,
        SupportAnswer? answer,
        CancellationToken cancellationToken = default);
}
