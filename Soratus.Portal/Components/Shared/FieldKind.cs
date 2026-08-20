namespace Soratus.Portal.Components.Shared;

/// <summary>
/// Wat voor waarde een veld bevat. Bepaalt het invoertype, het toetsenbord op een telefoon en of
/// de waarde mono en tabulair wordt afgedrukt (§8: getallen rechts uitgelijnd in mono).
/// </summary>
/// <remarks>
/// De waarde blijft in alle gevallen een <c>string</c>. Dat is geen luiheid maar een eis: een
/// formulier op static SSR krijgt zijn waarden als tekst uit de POST terug, en de parameters van
/// een interactief eiland moeten serialiseerbaar zijn. Parsen en formatteren hoort bij de pagina,
/// die weet of "8" acht uur of acht procent is.
/// </remarks>
public enum FieldKind
{
    /// <summary>Vrije tekst van één regel.</summary>
    Text,

    /// <summary>Vrije tekst van meer regels; rendert een <c>textarea</c>.</summary>
    Multiline,

    /// <summary>
    /// Een heel getal, bijvoorbeeld de urenbundel per maand. <c>type="number"</c> met
    /// <c>inputmode="numeric"</c>; mono en rechts uitgelijnd.
    /// </summary>
    Number,

    /// <summary>
    /// Een bedrag, bijvoorbeeld het uurtarief buiten bundel. Mono en rechts uitgelijnd, maar
    /// bewust <c>type="text"</c> met <c>inputmode="decimal"</c> en niet <c>type="number"</c>.
    /// </summary>
    /// <remarks>
    /// Reden: <c>type="number"</c> verwacht de punt als decimaalteken in de waarde die het
    /// element teruggeeft, terwijl een Nederlandse operator een komma typt. Browsers verschillen
    /// in wat ze dan doen — sommige geven een lege waarde terug, en dan verdwijnt een tarief
    /// stil bij het bewaren. Met tekst komt er precies terug wat er is getypt en beslist de
    /// pagina wat "125,50" betekent.
    /// </remarks>
    Amount,

    /// <summary>
    /// Een datum. <c>type="date"</c>, dus de waarde is en blijft <c>yyyy-MM-dd</c> — dat is de
    /// vorm die het element voorschrijft, niet een weergavekeuze. Wat de klant leest is aan de
    /// pagina (zie <c>ReadOnlyText</c>).
    /// </summary>
    Date,

    /// <summary>Een e-mailadres. <c>type="email"</c>, dus het juiste toetsenbord op een telefoon.</summary>
    Email,

    /// <summary>Een keuze uit een lijst; rendert een <c>select</c> met <c>Options</c>.</summary>
    Select,
}
