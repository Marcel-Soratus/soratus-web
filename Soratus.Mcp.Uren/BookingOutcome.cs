namespace Soratus.Mcp.Uren;

/// <summary>
/// Wat er met een boeking is gebeurd. Vijf gevallen, elk met een eigen melding.
/// </summary>
/// <remarks>
/// <para>Een gesloten hiërarchie in plaats van een resultaat met een <c>bool</c> en een tekst. De
/// reden is de melding aan de aanroeper: het verschil tussen "geweigerd, er staat niets" en "het
/// portaal was niet bereikbaar, en of er iets staat weet ik niet" is voor wie uren boekt het hele
/// verhaal, en met één vlag is dat verschil niet uit te drukken.</para>
///
/// <para>De constructor is privaat, dus er kan buiten dit bestand geen zesde geval bij komen. Wie
/// er wél een nodig heeft, komt het hier tegen en moet dan ook <see cref="BookingReport"/> langs —
/// dat is de bedoeling.</para>
/// </remarks>
public abstract record BookingOutcome
{
    private BookingOutcome()
    {
    }

    /// <summary>De regel is vastgelegd, als te fiatteren.</summary>
    /// <param name="Entry">De urenregel zoals het portaal hem teruggaf.</param>
    public sealed record Booked(HourBookingResponse Entry) : BookingOutcome;

    /// <summary>
    /// Er is niets vastgelegd, en dat is een gewone uitkomst: de invoer klopte niet.
    /// </summary>
    /// <param name="Reasons">De redenen, elk als één leesbare regel.</param>
    /// <param name="Sent">
    /// Of het verzoek het portaal heeft bereikt. <c>false</c> betekent dat de afwijzing hier is
    /// vastgesteld en er nooit iets de deur uit is gegaan.
    /// </param>
    public sealed record Refused(IReadOnlyList<string> Reasons, bool Sent) : BookingOutcome;

    /// <summary>
    /// Het portaal was niet te bereiken of wilde deze aanroeper niet.
    /// </summary>
    /// <param name="Reason">Wat er aan de hand is, en waar het aan ligt.</param>
    /// <param name="MayHaveLanded">
    /// Of de regel mogelijk toch is vastgelegd. Bij een tijdslimiet of een afgebroken verbinding is
    /// dat niet uit te sluiten, en dan hoort de melding dat te zeggen in plaats van "mislukt".
    /// </param>
    public sealed record Unavailable(string Reason, bool MayHaveLanded) : BookingOutcome;

    /// <summary>
    /// Het portaal gaf een regel terug die niet op <c>pending</c> staat.
    /// </summary>
    /// <param name="Entry">Wat er terugkwam.</param>
    /// <param name="Reason">Waarom dit niet klopt.</param>
    /// <remarks>
    /// Dit is de uitkomst die er hopelijk nooit is, en juist daarom bestaat hij apart. §5 legt vast
    /// dat alles wat een koppeling inschiet als te fiatteren landt. Zou het portaal iets anders
    /// terugsturen, dan is die regel gebroken, en dan is het gevaarlijkste wat deze server kan doen
    /// het als een geslaagde boeking melden — want dan telt er iets mee in de facturatie waar
    /// niemand naar heeft gekeken.
    /// </remarks>
    public sealed record Suspect(HourBookingResponse Entry, string Reason) : BookingOutcome;

    /// <summary>Er is bewust niets verstuurd omdat de server proefdraait.</summary>
    /// <param name="Request">Het verzoek dat verstuurd zou zijn.</param>
    public sealed record DryRun(HourBookingRequest Request) : BookingOutcome;
}
