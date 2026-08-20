namespace Soratus.Portal.Components.Shared;

/// <summary>
/// Eén tab van een <c>TabBar</c>: zijn id in de URL en zijn label op het scherm.
/// </summary>
/// <param name="Id">
/// De waarde die in de query terechtkomt, bijvoorbeeld <c>logs</c>. Kleine letters, geen
/// spaties: dit staat in een deelbare URL.
/// </param>
/// <param name="Label">Het label op de tab, bijvoorbeeld <c>Logs</c>.</param>
/// <param name="Href">
/// Waar de tab naartoe wijst. Laat leeg voor de standaard <c>?tab={Id}</c> — een relatieve
/// query die de browser tegen de huidige URL oplost, zodat de tab werkt zonder dat de tabbalk
/// de route van de pagina kent.
/// </param>
/// <remarks>
/// Geef <paramref name="Href"/> wél mee als de pagina meer in zijn querystring heeft staan dan
/// de tab. Een kale <c>?tab=runs</c> vervangt de hele query en gooit dan bijvoorbeeld een
/// zoekterm weg.
/// </remarks>
public readonly record struct TabItem(string Id, string Label, string? Href = null);
