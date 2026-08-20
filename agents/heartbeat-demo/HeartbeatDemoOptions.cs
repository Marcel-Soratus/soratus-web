namespace Soratus.Agents.HeartbeatDemo;

/// <summary>De knoppen van de referentie-agent, uit de sectie <c>HeartbeatDemo</c>.</summary>
public sealed class HeartbeatDemoOptions
{
    /// <summary>
    /// De basis van de toevalsgenerator. Samen met het minuutnummer van de run bepaalt dit
    /// precies welke runs falen en waar de lange logregel valt, zodat het portaal met
    /// herhaalbare data getest kan worden.
    /// </summary>
    public int Seed { get; set; } = 20260819;

    /// <summary>Eén op de zoveel runs mislukt. Nul zet het falen uit.</summary>
    public int FailureRate { get; set; } = 10;

    /// <summary>Eén op de zoveel runs bevat een héél lange logregel voor het uitklappaneel.</summary>
    public int LongLineRate { get; set; } = 7;
}
