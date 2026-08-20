using System.Reflection;
using Microsoft.AspNetCore.Components;
using Soratus.Portal.Security;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// Somt de routeerbare pagina's van het portaal op via reflectie.
/// </summary>
/// <remarks>
/// Met opzet géén handmatige lijst. Een lijst die je zelf bijhoudt vergeet iemand over een half
/// jaar aan te vullen, en dan valt precies de nieuwe pagina buiten het vangnet — de pagina waarvan
/// nog niemand heeft nagedacht wat een klant erop mag zien.
/// </remarks>
internal static class Paginaverzameling
{
    /// <summary>De assembly van het portaal.</summary>
    public static Assembly Portaal { get; } = typeof(CustomerScope).Assembly;

    /// <summary>De klant-slug waar de testklantgebruiker recht op heeft.</summary>
    public const string Klantslug = "acme-logistiek";

    /// <summary>
    /// Elke component met een <see cref="RouteAttribute"/>: dat is precies elke
    /// <c>@page</c>-component.
    /// </summary>
    /// <returns>De paginatypen, op naam gesorteerd.</returns>
    public static IReadOnlyList<Type> Alle() =>
    [
        .. Portaal
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(typeof(IComponent).IsAssignableFrom)
            .Where(t => t.GetCustomAttributes<RouteAttribute>().Any())
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
    ];

    /// <summary>De routes van één paginatype.</summary>
    /// <param name="pagina">Het paginatype.</param>
    /// <returns>De routesjablonen, bijvoorbeeld <c>/klant/{customerId}</c>.</returns>
    public static IReadOnlyList<string> Routes(Type pagina) =>
    [
        .. pagina.GetCustomAttributes<RouteAttribute>().Select(a => a.Template)
    ];

    /// <summary>
    /// Zoekt de pagina die op dit routesjabloon zit.
    /// </summary>
    /// <param name="route">Het routesjabloon.</param>
    /// <returns>Het paginatype, of <c>null</c> als die route (nog) niet bestaat.</returns>
    public static Type? MetRoute(string route) =>
        Alle().FirstOrDefault(p => Routes(p).Any(r =>
            string.Equals(r, route, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Zoekt een pagina op zijn volledige typenaam.
    /// </summary>
    /// <param name="volledigeNaam">De naam, bijvoorbeeld
    /// <c>Soratus.Portal.Components.Pages.Overzicht</c>.</param>
    /// <returns>Het paginatype, of <c>null</c> als het niet bestaat.</returns>
    /// <remarks>
    /// Nodig naast <see cref="MetRoute"/> zolang twee pagina's dezelfde route kunnen delen: dan
    /// levert een zoektocht op route de verkeerde pagina op zonder dat je dat merkt.
    /// </remarks>
    public static Type? MetNaam(string volledigeNaam) =>
        Alle().FirstOrDefault(p =>
            string.Equals(p.FullName, volledigeNaam, StringComparison.Ordinal));

    /// <summary>
    /// De parameterwaarden waarmee een pagina te renderen is: alles wat in de route als
    /// <c>{naam}</c> voorkomt, gevuld met een bestaande waarde.
    /// </summary>
    /// <param name="pagina">Het paginatype.</param>
    /// <returns>Naam-waardeparen voor de componentparameters.</returns>
    /// <remarks>
    /// De klant-slug is die van de klant waar de testgebruiker recht op heeft. Zou hier een
    /// vreemde slug staan, dan geeft elke pagina 404 en toont hij niets — en dan is een
    /// zichtbaarheidstest die niets vindt niets waard.
    /// </remarks>
    public static IReadOnlyDictionary<string, object?> Parameters(Type pagina)
    {
        var waarden = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in pagina.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetCustomAttribute<ParameterAttribute>() is null)
            {
                continue;
            }

            if (property.PropertyType != typeof(string))
            {
                continue;
            }

            waarden[property.Name] = property.Name switch
            {
                "Slug" or "CustomerId" or "CustomerSlug" or "KlantId" => Klantslug,
                "AgentName" or "AgentNaam" => "factuur-intake",
                "RunId" => "r-8f3c",
                _ => "test",
            };
        }

        return waarden;
    }
}
