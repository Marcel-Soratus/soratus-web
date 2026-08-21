namespace Soratus.Agents.Telemetry.HostedAgents;

/// <summary>
/// Waar de host zijn geherbergde agents vandaan haalt.
/// </summary>
/// <remarks>
/// <para>Dit is de naad waarlangs <c>Soratus.Agents.Telemetry</c> vrij blijft van ASP.NET Core.
/// De vraag "welke agents herbergt dit proces" heeft per host een ander antwoord — een
/// webapplicatie leest zijn endpoints, een wachtrijhost zijn abonnementen — maar wat er daarna
/// met dat antwoord gebeurt is voor alle hosts hetzelfde: registreren, kloppen, runs
/// wegschrijven. Alleen het antwoord is hostspecifiek, dus alleen het antwoord staat buiten deze
/// bibliotheek.</para>
///
/// <para>Er kunnen er meerdere geregistreerd zijn; alles wat ze opleveren wordt samengevoegd op
/// naam.</para>
/// </remarks>
public interface IHostedAgentSource
{
    /// <summary>
    /// De agents die deze host op dit moment herbergt.
    /// </summary>
    /// <returns>De aankondigingen; leeg mag.</returns>
    /// <remarks>
    /// Wordt bij elke hartslag opnieuw gevraagd en niet één keer bij het opstarten. Dat is geen
    /// verspilling maar de reparatie van een val: een bron die zijn antwoord pas kent nadat de
    /// host zijn verzoekpijplijn heeft gebouwd, zou bij één keer vragen een lege lijst geven —
    /// en een agent die daardoor ontbreekt is in het portaal niet te zien als fout, maar als
    /// afwezigheid.
    /// </remarks>
    IReadOnlyList<HostedAgentDeclaration> GetAgents();
}
