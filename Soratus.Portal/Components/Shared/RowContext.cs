namespace Soratus.Portal.Components.Shared;

/// <summary>
/// Wat één rij aan zijn cellen doorgeeft: de kolomindeling en welke kolom de volgende cel is.
/// </summary>
/// <remarks>
/// Een implementatiedetail van <c>DataRow</c> en <c>DataCell</c>; een pagina maakt dit nooit
/// zelf. Publiek omdat een cascading parameter een publieke property vereist en die niet van een
/// minder toegankelijk type mag zijn.
///
/// De teller werkt omdat cellen binnen één rij in documentvolgorde initialiseren en een
/// component maar één keer initialiseert. Elke cel houdt zijn kolomnummer daarna vast, ook als
/// de rij opnieuw rendert.
/// </remarks>
public sealed class RowContext
{
    private int _next;

    /// <summary>Maakt de context van één rij.</summary>
    /// <param name="grid">De kolomindeling van de tabel waar deze rij in staat.</param>
    public RowContext(RowGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        Grid = grid;
    }

    /// <summary>De kolomindeling van de tabel.</summary>
    public RowGrid Grid { get; }

    /// <summary>
    /// Neemt de kolom voor de volgende cel in deze rij.
    /// </summary>
    /// <returns>
    /// De kolom, of <c>null</c> als de rij meer cellen bevat dan de tabel zichtbare kolommen
    /// heeft. Een cel zonder kolom valt terug op geen label en geen uitlijning; hij verdwijnt
    /// niet, want een halve rij op het scherm is erger dan een cel zonder kop.
    /// </returns>
    public GridColumn? TakeColumn()
    {
        var index = _next++;
        return index < Grid.Visible.Count ? Grid.Visible[index] : null;
    }
}
