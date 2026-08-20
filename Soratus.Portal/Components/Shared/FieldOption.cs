namespace Soratus.Portal.Components.Shared;

/// <summary>
/// Eén keuze in een <see cref="FieldKind.Select"/>-veld.
/// </summary>
/// <param name="Value">
/// De waarde die met het formulier meegaat. Dit is wat de pagina terugkrijgt, dus houd hem stabiel
/// — een rol die "Beheerder klant" heet mag zijn label veranderen zonder dat opgeslagen rijen
/// betekenisloos worden.
/// </param>
/// <param name="Label">Wat er in beeld staat.</param>
/// <remarks>
/// Een <c>record struct</c> en geen klasse: dit gaat over een render-mode-grens en dan moet het
/// serialiseerbaar zijn en geen gedrag dragen. De lijst met rollen uit §3.5 (Beheerder klant /
/// Lezer) is de eerste gebruiker.
/// </remarks>
public readonly record struct FieldOption(string Value, string Label)
{
    /// <summary>
    /// Een keuze waarvan de waarde ook het label is. Voor lijsten waar de tekst zelf het gegeven
    /// is, zoals de rollen uit §3.5.
    /// </summary>
    public FieldOption(string value)
        : this(value, value)
    {
    }
}
