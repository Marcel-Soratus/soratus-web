using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Soratus.Mcp.Uren;

namespace Soratus.Mcp.Uren.Tests;

/// <summary>
/// De publieke vorm van de tool: de naam, de parameters en wat de beschrijving belooft.
/// </summary>
/// <remarks>
/// Deze tests kijken naar de attributen en niet naar het gedrag, want dit is wat een client ziet
/// vóórdat hij iets aanroept. Twee dingen kunnen hier stil fout gaan: een naam die de Messages-API
/// weigert (en dan faalt élke prompt in de sessie, niet pas deze tool), en een parameter die
/// hernoemd wordt en daarmee de vorm uit §5 verandert zonder dat iemand het merkt.
/// </remarks>
public class ToolvormTests
{
    private static readonly MethodInfo Methode = typeof(UrenTools)
        .GetMethod(nameof(UrenTools.BoekenAsync), BindingFlags.Public | BindingFlags.Instance)!;

    private static McpServerToolAttribute Attribuut =>
        Methode.GetCustomAttribute<McpServerToolAttribute>()!;

    [Fact]
    public void DeToolnaamPastOpHetPatroonVanDeMessagesApi()
    {
        // Claude Code stuurt de naam met een voorvoegsel mee als toolnaam naar de Messages-API, en
        // die eist dit patroon. Een punt — zoals in de notatie 'uren.boeken' uit §5 — levert een 400
        // op bij elke prompt in de sessie.
        Assert.Matches("^[a-zA-Z0-9_-]{1,64}$", UrenTools.ToolName);
        Assert.Equal(UrenTools.ToolName, Attribuut.Name);
    }

    [Fact]
    public void OokMetHetVoorvoegselVanClaudeCodeBlijftDeNaamBinnenDeGrens()
    {
        string volledig = $"mcp__soratus-uren__{UrenTools.ToolName}";

        Assert.Matches("^[a-zA-Z0-9_-]{1,64}$", volledig);
    }

    [Fact]
    public void DeParametersZijnPreciesDeVijfUitDeSpec()
    {
        string[] namen = [.. Methode.GetParameters()
            .Where(static p => p.ParameterType != typeof(CancellationToken))
            .Select(static p => p.Name!)];

        // §5: uren.boeken({ klant, maand, uren, categorie, omschrijving }). Het MCP-SDK gebruikt de
        // parameternaam letterlijk als veldnaam in het JSON-schema, dus dit ís de publieke vorm.
        Assert.Equal(["klant", "maand", "uren", "categorie", "omschrijving"], namen);
    }

    [Fact]
    public void ElkeParameterHeeftEenBeschrijving()
    {
        foreach (ParameterInfo parameter in Methode.GetParameters()
            .Where(static p => p.ParameterType != typeof(CancellationToken)))
        {
            DescriptionAttribute? beschrijving = parameter.GetCustomAttribute<DescriptionAttribute>();

            Assert.NotNull(beschrijving);
            Assert.False(
                string.IsNullOrWhiteSpace(beschrijving.Description),
                $"Parameter '{parameter.Name}' heeft geen beschrijving. Dat is de enige uitleg die " +
                "een taalmodel krijgt voordat het uren boekt.");
        }
    }

    [Fact]
    public void DeBeschrijvingZegtDatDeRegelNogGefiatteerdMoetWorden()
    {
        // Dit is de enige plek waar de vaste regel uit §5 bij de aanroeper terechtkomt vóórdat hij
        // boekt. Zonder deze regel gaat een model op grond van "geboekt" melden dat het klaar is.
        string beschrijving = Methode.GetCustomAttribute<DescriptionAttribute>()!.Description!;

        Assert.Contains("te fiatteren", beschrijving, StringComparison.Ordinal);
        Assert.Contains("Soratus-operator", beschrijving, StringComparison.Ordinal);
        Assert.Contains("nooit", beschrijving, StringComparison.Ordinal);
    }

    [Fact]
    public void DeToolIsNietAlsIdempotentAangemerkt()
    {
        // Twee keer boeken levert twee regels op; er is geen idempotentiesleutel. Een client die op
        // deze annotatie afgaat om zonder te vragen opnieuw te proberen, hoort dat te weten.
        Assert.False(Attribuut.Idempotent);
        Assert.False(Attribuut.ReadOnly);
        Assert.False(Attribuut.Destructive);
    }
}
