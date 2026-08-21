namespace Soratus.Agents.AspNetCore.Internal;

/// <summary>Houdt bij of de aanroeplaag daadwerkelijk in de verzoekpijplijn is gezet.</summary>
/// <remarks>
/// Bestaat om één stille fout luid te maken. Wie <c>WithSoratusAgent</c> op zijn endpoints zet maar
/// <c>UseSoratusAgentRuns</c> vergeet, krijgt agents met een keurige hartslag die nooit een run
/// wegschrijven — en in het portaal is dat niet te onderscheiden van drie diensten die niemand
/// aanroept. Zie <see cref="EndpointWiringCheck"/> voor wat er dan gebeurt.
/// </remarks>
internal sealed class RunMiddlewareMarker
{
    private int _installed;

    /// <summary>Of de aanroeplaag in de pijplijn staat.</summary>
    internal bool Installed => Volatile.Read(ref _installed) != 0;

    /// <summary>Meldt dat de aanroeplaag in de pijplijn is gezet.</summary>
    internal void MarkInstalled() => Volatile.Write(ref _installed, 1);
}
