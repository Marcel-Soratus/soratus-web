using System.Reflection;
using Soratus.Agents.Contracts;
using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Bereikt de rekenhulpjes van <c>CosmosAgentTelemetryStore</c> die zonder Cosmos te testen zijn.
/// </summary>
/// <remarks>
/// <para><c>TrimYoungestGroup</c> is <c>private static</c> en zit midden in een methode die een
/// container nodig heeft. De regel die hij uitvoert is niettemin pure rekenkunde en precies het
/// soort ding dat op de grens fout gaat, dus hij hoort een test te hebben.</para>
///
/// <para>Reflectie in plaats van de methode <c>internal</c> maken: een testproject hoort de
/// zichtbaarheidsgrenzen van de code die het test niet op te rekken, en dezelfde afweging staat al
/// bij <see cref="Autorisatiebron"/>. De prijs is dat een naamswijziging hier pas op looptijd
/// opvalt; daarom is de melding bij het mislukken expliciet.</para>
/// </remarks>
internal static class Opslaglaag
{
    private static readonly MethodInfo Trim = ZoekTrim();

    /// <summary>
    /// Laat de jongste groep regels met dezelfde tijdstempel vallen, met de productiecode zelf.
    /// </summary>
    /// <param name="regels">De regels, oplopend gesorteerd. Wordt ter plaatse aangepast.</param>
    /// <returns>Dezelfde lijst, voor het gemak van de aanroeper.</returns>
    public static List<LogRecord> TrimJongsteGroep(List<LogRecord> regels)
    {
        ArgumentNullException.ThrowIfNull(regels);

        Trim.Invoke(null, [regels]);

        return regels;
    }

    private static MethodInfo ZoekTrim() =>
        typeof(IAgentTelemetryStore).Assembly
            .GetType("Soratus.Portal.Data.CosmosAgentTelemetryStore", throwOnError: true)!
            .GetMethod(
                "TrimYoungestGroup",
                BindingFlags.Static | BindingFlags.NonPublic,
                [typeof(List<LogRecord>)])
        ?? throw new MissingMethodException(
            "CosmosAgentTelemetryStore.TrimYoungestGroup(List<LogRecord>) is niet gevonden. Die " +
            "methode is de grensregel van de live tail: valt de paginagrens midden in een groep " +
            "regels met dezelfde tijdstempel, dan blijft die groep liggen tot hij compleet is. Is " +
            "hij hernoemd of verplaatst, dan hoort deze hulpklasse mee te veranderen — en niet de " +
            "test die erop staat weg te vallen.");
}
