namespace Soratus.Portal.Mail;

/// <summary>
/// Het formulier waarmee een operator het maandoverzicht verstuurt.
/// </summary>
/// <remarks>
/// <para><strong>Eén veld, en geen enkel veld dat de inhoud van de mail bepaalt.</strong> Geen
/// ontvanger, geen onderwerp, geen begeleidende tekst, geen bedrag. Dat is de belangrijkste
/// eigenschap van dit type: alles wat de klant leest komt uit de opslag en uit
/// <see cref="StatementText"/>, en er is dus geen pad waarlangs iemand tekst in een klantmail typt.
/// </para>
///
/// <para>Dat was niet de eerste opzet. Een veld "opmerking voor de klant" is de voor de hand liggende
/// wens en het is precies de fout die de punten 13 en 14 van de fase-0-afwijkingen twee keer hebben
/// moeten repareren: vrije tekst die in een klantoppervlak belandt. Het verschil is dat die twee
/// keren op een scherm stonden. Wil een operator iets persoonlijks zeggen, dan schrijft hij een
/// gewone mail — dat is een handeling met een afzender die erbij staat, en geen tekst die het
/// portaal namens hem verstuurt.</para>
/// </remarks>
public sealed class StatementSendForm
{
    /// <summary>De maand als <c>jjjj-MM</c>, uit de keuzelijst.</summary>
    public string? Month { get; set; }
}

/// <summary>
/// Het formulier waarmee een operator vastlegt wat er is gebeurd bij een onbekende uitkomst.
/// </summary>
/// <remarks>
/// De vaststelling is verplicht en de melding zegt waarom: dit is de enige plek waar over een half
/// jaar staat waarom er over een maand twee keer — of geen keer — is gemaild. Dezelfde vorm en
/// dezelfde reden als de verplichte reden bij het afwijzen van een urenregel (punt 17).
/// </remarks>
public sealed class StatementReleaseForm
{
    /// <summary>De maand als <c>jjjj-MM</c>.</summary>
    public string? Month { get; set; }

    /// <summary>De etag van de bevestiging waarop de vaststelling rust.</summary>
    /// <remarks>
    /// <para>Gaat mee als <c>If-Match</c>. Op static SSR is de paginabron de enige plek om een etag
    /// tussen twee verzoeken vast te houden, en het contractscherm heeft met een test vastgelegd dat
    /// een schrijfvoorwaarde daar niet hoort te staan — dáár, want daar gaat het om een formulier met
    /// elf velden waarvan de etag de hele kaart afdekt.</para>
    ///
    /// <para>Hier is de afweging andersom uitgevallen en dat is een bewuste afwijking. Vaststellen dat
    /// er niets is aangekomen zet de deur open voor een tweede mail. Twee operators die tegelijk
    /// dezelfde onbekende maand vaststellen en daarna beide versturen, is precies het geval waarvoor
    /// de claim niet meer sluit — die is dan immers vrijgegeven. De etag is de enige manier om die
    /// twee op elkaar te laten botsen. De prijs is dat er een etag in de paginabron staat; de opbrengst
    /// is dat een dubbele mail een conflict oplevert.</para>
    /// </remarks>
    public string? ETag { get; set; }

    /// <summary>Wat de operator heeft vastgesteld.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Wat er niet klopt aan dit formulier, of <c>null</c>.
    /// </summary>
    /// <returns>De melding in het Nederlands, of <c>null</c>.</returns>
    /// <remarks>
    /// De toets op de vaststelling zelf staat in <see cref="StatementRelease.Validate"/> en niet hier;
    /// deze methode controleert alleen wat het formulier eigen is. Zou de tekstgrens hier óók staan,
    /// dan bestaan er twee opvattingen over wat een geldige vaststelling is en weigert de ene wat de
    /// andere doorlaat.
    /// </remarks>
    public string? Validate() =>
        string.IsNullOrWhiteSpace(Month)
            ? "Er is geen maand meegegeven."
            : ToRelease().Validate();

    /// <summary>De vaststelling zoals de opslag hem verwacht.</summary>
    /// <returns>De vaststelling.</returns>
    public StatementRelease ToRelease() =>
        new(Month?.Trim() ?? string.Empty, Note?.Trim() ?? string.Empty, ETag);
}
