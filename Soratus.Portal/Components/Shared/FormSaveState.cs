namespace Soratus.Portal.Components.Shared;

/// <summary>
/// Waar een formulier staat in het bewaren.
/// </summary>
/// <remarks>
/// De pagina houdt deze stand bij, niet de kaart: alleen de pagina weet of de schrijfactie is
/// gelukt. De kaart houdt er wel zijn eigen, kortere vlag naast voor het venster tussen de klik en
/// het antwoord van de pagina — zie de opmerking over dubbel indienen in <c>FormCard</c>.
///
/// Er is geen aparte toestand voor "niets gewijzigd". Dat is geen fase van het bewaren maar een
/// eigenschap van het formulier, en die komt binnen als <c>Dirty</c>. Anders zou een formulier
/// tegelijk <c>Saved</c> en <c>Unchanged</c> moeten zijn, en dan kiest de kaart maar welke van de
/// twee hij toont.
/// </remarks>
public enum FormSaveState
{
    /// <summary>Er loopt niets. De begintoestand, en de toestand na het verlaten van een melding.</summary>
    Idle,

    /// <summary>De schrijfactie loopt.</summary>
    Saving,

    /// <summary>De laatste schrijfactie is gelukt.</summary>
    Saved,

    /// <summary>
    /// De laatste schrijfactie is mislukt. Geef dan ook <c>Error</c> mee: een rode melding zonder
    /// tekst laat de operator raden of het aan hem of aan ons lag.
    /// </summary>
    Failed,
}
