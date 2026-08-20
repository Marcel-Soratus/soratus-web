namespace Soratus.Portal.Components.Shared;

/// <summary>
/// De kolomindeling van één tabel: welke kolommen er zijn, hoe breed ze zijn en hoeveel ruimte
/// er tussen zit.
/// </summary>
/// <remarks>
/// Maak er per tabel één van, bij voorkeur als <c>static readonly</c> veld op de pagina, en geef
/// hem aan <c>DataCard</c>. Die zet er <c>--row-cols</c> en <c>--row-gap</c> van op de kaart;
/// <c>DataRowHeader</c> en <c>DataCell</c> lezen dezelfde definitie voor koppen en labels.
/// </remarks>
public sealed class RowGrid
{
    /// <summary>De standaardafstand tussen kolommen: de <c>--gap-col</c> uit §8 (10px).</summary>
    public const string DefaultGap = "var(--gap-col)";

    /// <summary>Maakt een kolomindeling met de standaardafstand tussen kolommen.</summary>
    /// <param name="columns">De kolommen, in schermvolgorde. Minstens één.</param>
    public RowGrid(params GridColumn[] columns)
        : this(DefaultGap, columns)
    {
    }

    /// <summary>Maakt een kolomindeling.</summary>
    /// <param name="gap">
    /// De horizontale afstand tussen kolommen als CSS-lengte, bijvoorbeeld <c>12px</c>. §8 houdt
    /// dit tussen 10 en 12px.
    /// </param>
    /// <param name="columns">De kolommen, in schermvolgorde. Minstens één.</param>
    /// <exception cref="ArgumentException">Als er geen kolommen zijn, of een kolom geen track heeft.</exception>
    public RowGrid(string gap, params GridColumn[] columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gap);
        ArgumentNullException.ThrowIfNull(columns);

        if (columns.Length == 0)
        {
            throw new ArgumentException("Een tabel zonder kolommen bestaat niet.", nameof(columns));
        }

        var visible = columns.Where(c => c.Visible).ToArray();

        if (visible.Length == 0)
        {
            throw new ArgumentException(
                "Alle kolommen staan op Visible = false; er valt niets te tonen.",
                nameof(columns));
        }

        foreach (var column in visible)
        {
            if (string.IsNullOrWhiteSpace(column.Track))
            {
                throw new ArgumentException(
                    $"Kolom '{column.Header}' heeft geen grid-track.",
                    nameof(columns));
            }
        }

        Gap = gap;
        Columns = columns;
        Visible = visible;
        Template = string.Join(' ', visible.Select(c => c.Track));
    }

    /// <summary>De horizontale afstand tussen kolommen, als CSS-lengte.</summary>
    public string Gap { get; }

    /// <summary>Alle kolommen, ook de verborgen.</summary>
    public IReadOnlyList<GridColumn> Columns { get; }

    /// <summary>
    /// Alleen de zichtbare kolommen, in schermvolgorde. Dit is de lijst waar koppen en cellen
    /// op mee tellen: de i-de <c>DataCell</c> in een rij hoort bij de i-de zichtbare kolom.
    /// </summary>
    public IReadOnlyList<GridColumn> Visible { get; }

    /// <summary>De waarde voor <c>grid-template-columns</c>, samengesteld uit de zichtbare tracks.</summary>
    public string Template { get; }
}
