using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Soratus.Agents.Telemetry.HostedAgents;

namespace Soratus.Agents.AspNetCore.Internal;

/// <summary>
/// Leest uit de endpoints van deze applicatie welke agents zij herbergt.
/// </summary>
/// <remarks>
/// <para>Dit is de hele reden dat er geen tweede lijst bestaat. De aanroeplaag leest bij elk
/// verzoek de <see cref="SoratusAgentMetadata"/> van het endpoint dat geraakt is; deze bron leest
/// diezelfde metadata van álle endpoints, zodat de hartslag niet op de eerste aanroep hoeft te
/// wachten. Eén plek waar staat wie de agents zijn, en dat is de plek waar het werk staat.</para>
///
/// <para>Er wordt bij elke hartslag opnieuw gelezen en niet één keer bij het opstarten. De
/// <see cref="EndpointDataSource"/> van een <c>WebApplication</c> is namelijk pas gevuld nadat de
/// verzoekpijplijn is gebouwd, en of dat vóór of ná de start van een achtergronddienst gebeurt is
/// een detail van de host. Eén keer lezen op het verkeerde moment levert nul agents op, en dat is
/// in het portaal niet te zien als fout maar als afwezigheid — de duurste soort fout die dit
/// contract kan maken.</para>
/// </remarks>
internal sealed class EndpointHostedAgentSource(EndpointDataSource endpoints) : IHostedAgentSource
{
    public IReadOnlyList<HostedAgentDeclaration> GetAgents()
    {
        List<HostedAgentDeclaration>? found = null;

        foreach (Endpoint endpoint in endpoints.Endpoints)
        {
            SoratusAgentMetadata? metadata = endpoint.Metadata.GetMetadata<SoratusAgentMetadata>();
            if (metadata is null)
            {
                continue;
            }

            found ??= [];
            found.Add(metadata.Declaration);
        }

        return found ?? [];
    }
}
