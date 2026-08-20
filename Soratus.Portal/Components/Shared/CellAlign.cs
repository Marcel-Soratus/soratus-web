namespace Soratus.Portal.Components.Shared;

/// <summary>De uitlijning van een cel binnen zijn kolom.</summary>
/// <remarks>
/// Bewust maar twee waarden. Getallen staan rechts (§8: alle getalkolommen tabulair en rechts),
/// al het andere links. Gecentreerde tabelcellen bestaan niet in dit portaal, dus er is ook geen
/// waarde voor.
/// </remarks>
public enum CellAlign
{
    /// <summary>Links uitgelijnd. De standaard voor tekst.</summary>
    Start,

    /// <summary>Rechts uitgelijnd. Voor getallen, duur, bedragen en tijdstempels.</summary>
    End,
}
