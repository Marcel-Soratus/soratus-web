using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.HostedAgents;

/// <summary>
/// De agents die dit proces herbergt, opvraagbaar op naam.
/// </summary>
/// <remarks>
/// <para>Injecteer dit in de laag die de aanroepen ontvangt — een middleware, een
/// wachtrijlus — en vraag per aanroep de agent op wiens naam het werk gebeurt.</para>
///
/// <para>De lijst groeit vanuit de geregistreerde <see cref="IHostedAgentSource"/>-bronnen en
/// vanuit <see cref="GetOrAdd"/>, en krimpt nooit binnen één proces. Dat is opzet: een agent die
/// vandaag is aangeroepen en morgen niet meer wordt aangekondigd, hoort niet stil uit het
/// portaal te verdwijnen — dan zou zijn laatste run onvindbaar worden.</para>
/// </remarks>
public interface ISoratusHostedAgents
{
    /// <summary>Wat dit proces op dit moment herbergt.</summary>
    IReadOnlyList<ISoratusHostedAgent> All { get; }

    /// <summary>
    /// De agent met deze naam, of <c>null</c> als dit proces hem niet herbergt.
    /// </summary>
    /// <param name="agentName">De technische naam.</param>
    /// <returns>De agent, of <c>null</c>.</returns>
    ISoratusHostedAgent? Find(string agentName);

    /// <summary>
    /// De agent uit deze aankondiging; hij wordt aangemaakt als hij nog niet bestond.
    /// </summary>
    /// <param name="declaration">De aankondiging, meestal uit de metadata van het endpoint.</param>
    /// <returns>De agent.</returns>
    /// <exception cref="ArgumentNullException">Als <paramref name="declaration"/> <c>null</c> is.</exception>
    /// <exception cref="InvalidOperationException">
    /// Als de aankondiging niet door <see cref="HostedAgentDeclaration.Validate"/> komt.
    /// </exception>
    /// <remarks>
    /// De aanroepkant geeft de aankondiging mee in plaats van alleen een naam, en dat is geen
    /// gemak maar de reden dat er geen tweede lijst bestaat. Wie eerst ergens moet aangeven welke
    /// agents er zijn en daarna per aanroep een naam opzoekt, heeft twee plekken die uiteen kunnen
    /// lopen — en de fout die dan ontstaat is een agent die aanroepen verwerkt zonder in het
    /// portaal te staan, of andersom. Hier is de aankondiging bij het endpoint de enige bron;
    /// <see cref="IHostedAgentSource"/> leest diezelfde aankondigingen zodat de hartslag niet op
    /// de eerste aanroep hoeft te wachten.
    ///
    /// <para>Bestaat de naam al met een <em>andere</em> aankondiging, dan blijft de eerste staan.
    /// Twee endpoints die dezelfde agentnaam met een verschillende typeaanduiding aankondigen is
    /// een inrichtingsfout, maar niet één die een lopende host mag omleggen; hij wordt gemeld op
    /// de gewone logger van de host.</para>
    /// </remarks>
    ISoratusHostedAgent GetOrAdd(HostedAgentDeclaration declaration);
}
