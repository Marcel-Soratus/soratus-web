namespace Soratus.Portal.Components.Shared;

/// <summary>
/// Het afkappen van een logbericht voor een tooltip, op één plek voor alle logtabellen.
/// </summary>
/// <remarks>
/// <para><strong>Afkappen is geen weglaten — lees dit voordat je hier een grens verlegt.</strong>
/// Deze lengte en de ellipsis van <c>Truncate</c> op de berichtcel zijn béeld, geen filter. De
/// volledige tekst staat in de HTML, "paginabron bekijken" toont hem en een schermlezer leest hem
/// voluit. Gemeten op de langste regel in de data (3400 tekens) is de cel 766px breed met een
/// <c>scrollWidth</c> van 22214: clipping, geen verwijdering.</para>
///
/// <para>Dat is niet theoretisch. In de echte telemetrie staat een bericht (event
/// <c>payload.dump</c>) met een volledige .NET-stacktrace inclusief onze <c>/src/</c>-paden, en
/// <c>Message</c> staat óók op de klantvariant van een logregel. Wie naar het scherm kijkt, één
/// afgekapte regel ziet en daaruit concludeert dat de rest niet zichtbaar is, heeft het mis — en
/// dat is de gevaarlijkste soort gerustheid, want hij lijkt gemeten.</para>
///
/// <para>Repareer dat hier dus niet. Deze laag kan "lang en ongevaarlijk" niet van "lang en een
/// stacktrace" onderscheiden, en een heuristiek die het probeert (zoeken op <c>at </c>,
/// <c>/src/</c>, <c>line </c>) is dezelfde onsluitbare blokkeerlijst als bij
/// <see cref="LogJson"/> — met hetzelfde tweede bezwaar dat het beeld niet weet wie er kijkt. De
/// grens hoort bij het schrijven, in <c>Soratus.Agents.Telemetry</c>: een stacktrace hoort niet in
/// <c>msg</c>, en het contract zegt daar al "één zin".</para>
///
/// <para><strong>Waarom dit een eigen klasse is en geen constante per tabel.</strong> Er zijn twee
/// logtabellen — de operatorvariant met uitklap en de klantvariant zonder — en ze staan in
/// verschillende mappen. Zolang deze lengte en deze redenering in beide stonden, was het een
/// kwestie van tijd tot iemand er één verzette en de klant een ruimere tooltip kreeg dan de
/// operator, of omgekeerd. Dat is precies de vorm die in dit portaal al eerder is misgegaan met
/// gekopieerde CSS-klassen. Eén getal, één plek.</para>
/// </remarks>
public static class LogText
{
    /// <summary>
    /// Hoeveel tekens er van een bericht in de tooltip komen.
    /// </summary>
    /// <remarks>
    /// In de data zit een bericht van ruim 3400 tekens. Dat als tooltip tonen levert een blok tekst
    /// op dat het halve scherm bedekt en niet te scrollen is; op 400 tekens blijft er een tooltip
    /// over die je in één blik leest.
    /// </remarks>
    public const int MaxTitleLength = 400;

    /// <summary>
    /// Maakt de tooltiptekst bij een logbericht.
    /// </summary>
    /// <param name="message">Het bericht.</param>
    /// <param name="hint">
    /// Wat er achter het beletselteken komt te staan als het bericht is afgekapt, bijvoorbeeld
    /// "klik de regel open voor de volledige tekst". Laat leeg als er geen vervolg ís: verwijzen
    /// naar iets wat er niet is, is erger dan zwijgen. Een klantrij klapt niet uit en hoort hier
    /// dus niets mee te geven.
    /// </param>
    /// <returns>Het bericht, of het begin ervan met een beletselteken.</returns>
    public static string Title(string? message, string? hint = null)
    {
        if (string.IsNullOrEmpty(message) || message.Length <= MaxTitleLength)
        {
            return message ?? string.Empty;
        }

        var head = message[..Length(message)];

        return string.IsNullOrWhiteSpace(hint) ? $"{head}…" : $"{head}… ({hint})";
    }

    /// <summary>
    /// Waar er precies geknipt wordt: op <see cref="MaxTitleLength"/>, of één teken eerder als daar
    /// een surrogaatpaar zou breken.
    /// </summary>
    /// <remarks>
    /// Een emoji of een teken buiten het basisvlak staat in .NET als twéé chars in de tekenreeks.
    /// Knippen tussen die twee laat een losse surrogaat achter, en dat is geen geldige tekst: het
    /// komt als vervangingsteken in het attribuut terecht en kan de serialisatie van de render
    /// laten struikelen. Eén teken eerder stoppen kost niets en voorkomt dat.
    /// </remarks>
    private static int Length(string message) =>
        char.IsHighSurrogate(message[MaxTitleLength - 1])
            ? MaxTitleLength - 1
            : MaxTitleLength;
}
