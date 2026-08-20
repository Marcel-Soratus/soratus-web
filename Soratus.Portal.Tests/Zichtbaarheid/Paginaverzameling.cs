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

            waarden[property.Name] = Waarde(property.Name);
        }

        return waarden;
    }

    /// <summary>De naam van de agent waarmee de pagina's worden gerenderd.</summary>
    public const string Agentnaam = "factuur-intake";

    /// <summary>
    /// De waarde die een onbekende parameternaam krijgt.
    /// </summary>
    /// <remarks>
    /// Staat hier als constante zodat een test kan zien dát een parameter op de terugval is
    /// uitgekomen. Dat is niet cosmetisch: een pagina die met een niet-bestaande sleutel rendert
    /// toont een 404 of een lege staat, en dan controleert het vangnet op verboden woorden een
    /// pagina waar niets op staat. Zie <see cref="VangnetdekkingTests"/>.
    /// </remarks>
    public const string Terugval = "test";

    /// <summary>
    /// De namen van de parameters in de routes van een pagina, zonder de constraints eromheen.
    /// </summary>
    /// <param name="pagina">Het paginatype.</param>
    /// <returns>De namen zoals ze in de route staan, bijvoorbeeld <c>Slug</c>.</returns>
    /// <remarks>
    /// Uit het routesjabloon en niet uit de properties: een routeparameter die geen
    /// <c>[Parameter]</c>-property heeft, of een die er wel is maar niet als <c>string</c>, is
    /// precies het geval dat hier moet opvallen.
    /// </remarks>
    public static IReadOnlyList<string> Routeparameters(Type pagina) =>
    [
        .. Routes(pagina)
            .SelectMany(r => r.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .Where(segment => segment.StartsWith('{') && segment.EndsWith('}'))
            .Select(segment => segment[1..^1])
            // {*rest} is een catch-all, {id:int} draagt een constraint en {id?} is optioneel.
            .Select(naam => naam.TrimStart('*').TrimEnd('?').Split(':')[0])
            .Where(naam => naam.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>
    /// De routeparameters van een pagina die géén bestaande waarde krijgen: ze staan niet in
    /// <see cref="Parameters"/>, of ze komen op <see cref="Terugval"/> uit.
    /// </summary>
    /// <param name="pagina">Het paginatype.</param>
    /// <returns>De namen die niet zijn gevuld, met de reden erachter.</returns>
    public static IReadOnlyList<string> ParametersZonderEchteWaarde(Type pagina)
    {
        var gevuld = Parameters(pagina);
        var ontbreekt = new List<string>();

        foreach (var naam in Routeparameters(pagina))
        {
            var sleutel = gevuld.Keys.FirstOrDefault(
                k => string.Equals(k, naam, StringComparison.OrdinalIgnoreCase));

            if (sleutel is null)
            {
                ontbreekt.Add(
                    $"{naam} — geen [Parameter]-property van het type string met die naam");
                continue;
            }

            if (Equals(gevuld[sleutel], Terugval))
            {
                ontbreekt.Add($"{naam} — valt terug op \"{Terugval}\"");
            }
        }

        return ontbreekt;
    }

    /// <summary>
    /// De waarde die bij een parameternaam hoort.
    /// </summary>
    /// <param name="naam">De naam van de componentparameter.</param>
    /// <returns>Een bestaande waarde, of <see cref="Terugval"/> voor een onbekende naam.</returns>
    /// <remarks>
    /// De vergelijking is hoofdletterongevoelig, en dat is geen kosmetiek. De route van het
    /// agentdetail heet <c>{Agentnaam}</c> met een kleine n; een ordinale vergelijking met
    /// <c>AgentNaam</c> gaat daar langs en de pagina rendert dan de agent <c>"test"</c>. Dat is
    /// precies het soort stille afwijking waar een zichtbaarheidstest niets van merkt — hij
    /// rendert iets, en de fixture antwoordt braaf op elke naam.
    /// </remarks>
    private static string Waarde(string naam) => naam switch
    {
        _ when Is(naam, "Slug", "CustomerId", "CustomerSlug", "KlantId") => Klantslug,
        _ when Is(naam, "AgentName", "AgentNaam", "Agent") => Agentnaam,
        _ when Is(naam, "RunId") => "r-8f3c",
        _ => Terugval,
    };

    private static bool Is(string naam, params string[] namen) =>
        namen.Contains(naam, StringComparer.OrdinalIgnoreCase);
}
