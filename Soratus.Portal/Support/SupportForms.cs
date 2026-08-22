namespace Soratus.Portal.Support;

/// <summary>
/// Wat de klant in het vraagformulier typt, zoals de browser het oplevert (§3.8).
/// </summary>
/// <remarks>
/// <para><strong>Er staat één veld op, en dat is het hele punt van dit type.</strong> Geen naam, geen
/// afzender, geen soort bericht, geen grondslag. Model binding op static SSR vult wat er op het
/// gebonden type staat, en niets meer: een zelfgemaakte POST kan dus niet bepalen wie de afzender is en
/// niet dat dit een bericht van Soratus of van de eerstelijn zou zijn. De naam komt uit de aanmelding
/// en wordt door de pagina toegevoegd in <see cref="ToQuestion"/>.</para>
///
/// <para>Dat is dezelfde constructie als bij <c>HourBookingForm</c>, waar het ontbreken van een
/// statusveld de vaste regel van §5 afdwingt — met dit verschil dat het hier niet om een bevoegdheid
/// gaat maar om identiteit. Een bericht op naam van iemand die het niet schreef is in een gesprek
/// erger dan een verkeerd getal: het is een uitspraak die iemand wordt toegeschreven.</para>
/// </remarks>
public sealed class SupportQuestionForm
{
    /// <summary>De vraag, zoals de klant hem heeft getypt.</summary>
    public string? Text { get; set; }

    /// <summary>
    /// De melding onder het veld, of <c>null</c> als er niets aan de hand is.
    /// </summary>
    /// <returns>De melding.</returns>
    /// <remarks>
    /// Uit <see cref="SupportBody.Validate"/> en niet met een eigen tekst, zodat het formulier niets
    /// weigert wat de schrijfkant zou toestaan en niets doorlaat wat zij weigert. Eén definitie van
    /// "klopt dit".
    /// </remarks>
    public string? Error() => SupportBody.Validate(Text);

    /// <summary>
    /// De vraag voor de opslag.
    /// </summary>
    /// <param name="author">
    /// De naam van de aangemelde gebruiker. Komt uit de aanmelding en niet uit dit formulier.
    /// </param>
    /// <returns>De vraag.</returns>
    public SupportQuestion ToQuestion(string author) => new()
    {
        Author = author,
        Text = Text ?? string.Empty,
    };
}

/// <summary>
/// Wat de operator in het antwoordformulier typt (§3.8: in de operatorrol antwoordt een mens).
/// </summary>
/// <remarks>
/// <para><strong>Ook hier één veld, en de naam komt nu zelfs niet van de pagina maar uit de
/// scope.</strong> Zie <see cref="ISupportStore.PostReplyAsync"/>: het bewijs waarmee de aanroeper
/// binnenkomt draagt de naam al.</para>
///
/// <para><strong>En er is geen veld waarmee dit antwoord een bron of een merkteken krijgt.</strong> Een
/// mens die antwoordt schrijft proza; de bronregel en het merkteken "AI · eerstelijn" horen bij de
/// eerstelijn en hangen aan <see cref="SupportAnswerBubble"/>. Zou een operator een bron kunnen
/// meegeven, dan bestond er een pad waarlangs het merkteken onder een tekst komt die iemand vrij heeft
/// getypt — en dan zegt dat merkteken niets meer.</para>
/// </remarks>
public sealed class SupportReplyForm
{
    /// <summary>Het antwoord, zoals de operator het heeft getypt.</summary>
    public string? Text { get; set; }

    /// <summary>De melding onder het veld, of <c>null</c>.</summary>
    /// <returns>De melding.</returns>
    public string? Error() => SupportBody.Validate(Text);

    /// <summary>Het antwoord voor de opslag.</summary>
    /// <returns>Het antwoord.</returns>
    public SupportReply ToReply() => new() { Text = Text ?? string.Empty };
}
