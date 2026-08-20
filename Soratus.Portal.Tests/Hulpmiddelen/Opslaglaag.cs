using Soratus.Agents.Contracts;
using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Bereikt de rekenregel van <see cref="CosmosAgentTelemetryStore"/> die zonder Cosmos te testen is.
/// </summary>
/// <remarks>
/// <para><c>TrimYoungestGroup</c> zit midden in een methode die een container nodig heeft, maar de
/// regel die hij uitvoert is pure rekenkunde en precies het soort ding dat op de grens fout gaat.
/// Dus hoort hij een test te hebben.</para>
///
/// <para>Hier stond reflectie op een <c>private static</c>. Dat kostte iets: een naamswijziging viel
/// dan pas tijdens een testrun op, als een uitzondering over een methode die niet meer bestaat, in
/// plaats van als bouwfout op de regel die moet veranderen. De methode is daarom <c>internal</c> —
/// zichtbaar voor dit project via de <c>InternalsVisibleTo</c> in <c>Soratus.Portal.csproj</c>, en
/// buiten het portaal nog altijd onbestaand. Dat is precies wat die verwijzing is: de klassen waar
/// het echte werk in zit zijn de klassen die een test hoort aan te roepen.</para>
///
/// <para>Wat blijft is de reden dat deze wikkel bestaat en niet de aanroep zelf in de tests staat:
/// hij geeft de lijst terug, zodat een test hem in één uitdrukking kan opbouwen en nakijken.</para>
/// </remarks>
internal static class Opslaglaag
{
    /// <summary>
    /// Laat de jongste groep regels met dezelfde tijdstempel vallen, met de productiecode zelf.
    /// </summary>
    /// <param name="regels">De regels, oplopend gesorteerd. Wordt ter plaatse aangepast.</param>
    /// <returns>Dezelfde lijst, voor het gemak van de aanroeper.</returns>
    public static List<LogRecord> TrimJongsteGroep(List<LogRecord> regels)
    {
        ArgumentNullException.ThrowIfNull(regels);

        CosmosAgentTelemetryStore.TrimYoungestGroup(regels);

        return regels;
    }
}
