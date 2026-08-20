using Soratus.Portal.Security;

namespace Soratus.Portal.Data;

/// <summary>
/// Wat er uit de opslag van één klant is opgehaald voor het overzicht — of waarom dat niet lukte.
/// </summary>
/// <param name="Scope">De klant.</param>
/// <param name="Agents">Zijn agents met hun laatste afgeronde run. Leeg als er niets te lezen was.</param>
/// <param name="Today">De runs die vandaag zijn gestart.</param>
/// <param name="Last24Hours">De runs van de laatste 24 uur.</param>
/// <param name="Unavailable">
/// <c>null</c> als het lezen lukte; anders de reden waarom niet.
/// </param>
/// <remarks>
/// Het onderscheid tussen "deze klant heeft geen agents" en "deze klant konden we niet lezen" staat
/// hier expliciet in, en dat is de hele reden dat dit type bestaat. Nu elke klant een eigen
/// Cosmos-account krijgt, is één onbereikbaar account een gewoon en te verwachten geval. Een
/// overzicht dat die klant dan als "0 agents" toont liegt, en een overzicht dat hem weglaat
/// verbergt precies datgene waarvoor je 's ochtends het overzicht opent.
/// </remarks>
public sealed record CustomerTelemetry(
    CustomerScope Scope,
    IReadOnlyList<AgentSnapshot> Agents,
    RunTally Today,
    RunTally Last24Hours,
    TelemetryUnavailable? Unavailable)
{
    /// <summary>Of de opslag van deze klant antwoordde.</summary>
    public bool IsAvailable => Unavailable is null;
}

/// <summary>
/// Waarom de opslag van een klant niets opleverde.
/// </summary>
/// <param name="Reason">
/// Eén zin in het Nederlands, leesbaar voor wie de code niet kent. Bedoeld om op het scherm te
/// zetten.
/// </param>
/// <param name="Detail">
/// De technische bijzonderheid, bijvoorbeeld de statuscode of het uitzonderingstype. Voor de
/// operator, niet voor de klant.
/// </param>
public sealed record TelemetryUnavailable(string Reason, string? Detail);
