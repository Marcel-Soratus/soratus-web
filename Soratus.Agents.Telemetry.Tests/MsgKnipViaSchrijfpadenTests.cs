using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.Logging;
using Soratus.Agents.Telemetry.Tests.Hulpmiddelen;

namespace Soratus.Agents.Telemetry.Tests;

/// <summary>
/// Dezelfde knip, nu over de echte schrijfpaden in plaats van via de knipfunctie zelf.
/// </summary>
/// <remarks>
/// <para>Dit is het verschil tussen "de regel werkt" en "de regel geldt". Er zijn twee paden waarop
/// een <c>LogRecord</c> ontstaat, en beide moeten geknipt worden:</para>
///
/// <para>Het <c>ILoggerProvider</c>-pad is de route waarlangs een bestaande agent logt — gewone
/// <c>ILogger</c>-aanroepen, zonder dat er iets van deze bibliotheek in de code van de agent staat.
/// Precies daarom moet het daar zeker gelden: wie niets van ons weet, kan zich ook niet aan een
/// afspraak houden.</para>
///
/// <para>Het tweede pad is <c>AgentRun.Fail</c>, dat de boodschap van een uitzondering in
/// <c>msg</c> zet. Dat is niet theoretisch: de boodschap van een <c>CosmosException</c> is een
/// halve pagina met diagnostiek over meerdere regels, en die zou zonder knip rechtstreeks in het
/// veld belanden dat de klant leest.</para>
/// </remarks>
public class MsgKnipViaSchrijfpadenTests
{
    private const string Frame =
        "   at Soratus.Sync.Validators.StockLineValidator.Validate(StockLine line) in /src/Sync/StockLineValidator.cs:line 42";

    [Fact]
    public async Task ViaDeLoggerproviderWordtEenMeerregeligBerichtGeknipt()
    {
        OpvangendeSink sink = await Proefagent.LogAsync(logger => logger.AgentEvent(
            "payload.dump",
            "De voorraadregels konden niet worden gevalideerd.\n" + Frame + "\n" + Frame));

        LogRecord regel = Enige(sink, "payload.dump");

        Assert.Equal("De voorraadregels konden niet worden gevalideerd." + MessageTruncation.Marker, regel.Message);
        Assert.Equal(Frame + "\n" + Frame, Overloop(regel));
    }

    [Fact]
    public async Task ViaDeLoggerproviderBlijftEenLangeRegelHeel()
    {
        // Het geval waarvoor de afbreektest van de logtabel bestaat. Zou dit geknipt worden, dan
        // heeft die test geen onderwerp meer.
        string proza = new('a', 1_417);

        OpvangendeSink sink = await Proefagent.LogAsync(logger => logger.AgentEvent("payload.dump", proza));

        LogRecord regel = Enige(sink, "payload.dump");

        Assert.Equal(proza, regel.Message);
        Assert.Null(Overloop(regel));
    }

    [Fact]
    public async Task ViaEenGewoneLogInformationGeldtDezelfdeKnip()
    {
        // Geen AgentEvent, geen enkele verwijzing naar deze bibliotheek in de aanroep. Zo logt een
        // bestaande agent, en ook daar moet de knip gelden.
        OpvangendeSink sink = await Proefagent.LogAsync(logger =>
            logger.LogInformation("Validatie mislukt voor {Aantal} regels.\n{Trace}", 3, Frame));

        // De gebeurtenisnaam wordt hier afgeleid uit de categorie "Bakker.VoorraadSync"; de host
        // logt zelf ook, dus filteren op de eigen regel.
        LogRecord regel = Enige(sink, "voorraad.sync");

        Assert.Equal("Validatie mislukt voor 3 regels." + MessageTruncation.Marker, regel.Message);
        Assert.Equal(Frame, Overloop(regel));
        Assert.DoesNotContain("/src/", regel.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public async Task ViaDeLoggerproviderGeldenAlleDrieDeRegelovergangen(string overgang)
    {
        OpvangendeSink sink = await Proefagent.LogAsync(logger =>
            logger.AgentEvent("api.retry", "Een zin." + overgang + Frame));

        LogRecord regel = Enige(sink, "api.retry");

        Assert.Equal("Een zin." + MessageTruncation.Marker, regel.Message);
        Assert.Equal(Frame, Overloop(regel));
    }

    [Fact]
    public async Task DeOverloopStaatNaastDeBestaandeContextInExtra()
    {
        OpvangendeSink sink = await Proefagent.LogAsync(logger => logger.AgentEvent(
            "payload.dump",
            "Een zin.\n" + Frame,
            new { docId = "INV-2291" }));

        LogRecord regel = Enige(sink, "payload.dump");
        JsonElement extra = regel.Extra!.Value;

        Assert.Equal("INV-2291", extra.GetProperty("docId").GetString());
        Assert.Equal(Frame, extra.GetProperty(MessageTruncation.OverflowKey).GetString());
    }

    [Fact]
    public async Task EenEigenSleutelMetDeGereserveerdeNaamWordtOverschreven()
    {
        // msgOverflow is een gereserveerde naam. Een tweede sleutel ernaast zou betekenen dat het
        // portaal twee vormen moet kennen.
        OpvangendeSink sink = await Proefagent.LogAsync(logger => logger.AgentEvent(
            "payload.dump",
            "Een zin.\n" + Frame,
            new Dictionary<string, string> { [MessageTruncation.OverflowKey] = "van de bouwer" }));

        LogRecord regel = Enige(sink, "payload.dump");

        Assert.Equal(Frame, Overloop(regel));
    }

    [Fact]
    public async Task EenMeerregeligeUitzonderingsboodschapKomtNietInHetBericht()
    {
        OpvangendeSink sink = await Proefagent.DraaiAsync(async diensten =>
        {
            var agent = diensten.GetRequiredService<ISoratusAgent>();
            await using IAgentRun run = await agent.StartRunAsync(TriggerKind.Manual);
            run.Fail(new InvalidOperationException("Het boekhoudpakket gaf 502 terug.\n" + Frame));
        });

        LogRecord regel = Enige(sink, "run.failed");

        Assert.DoesNotContain("/src/", regel.Message, StringComparison.Ordinal);
        Assert.EndsWith(MessageTruncation.Marker, regel.Message, StringComparison.Ordinal);
        Assert.Equal(Frame, Overloop(regel));

        // De stacktrace zelf hoort er nog steeds bij te staan — verplaatst, niet weggegooid.
        Assert.True(regel.Extra!.Value.TryGetProperty("_exception", out _));
    }

    [Fact]
    public async Task DeRunIdWordtMeegevoerdOpEenGeknipteRegel()
    {
        // De knip mag niets anders aan de regel veranderen.
        OpvangendeSink sink = await Proefagent.DraaiAsync(async diensten =>
        {
            var agent = diensten.GetRequiredService<ISoratusAgent>();
            ILogger logger = diensten.GetRequiredService<ILoggerFactory>().CreateLogger(Proefagent.Categorie);

            await using IAgentRun run = await agent.StartRunAsync(TriggerKind.Manual);
            run.Processed();
            logger.AgentEvent("payload.dump", "Een zin.\n" + Frame);
        });

        LogRecord regel = Enige(sink, "payload.dump");

        Assert.NotNull(regel.RunId);
        Assert.StartsWith("r-", regel.RunId, StringComparison.Ordinal);
    }

    private static LogRecord Enige(OpvangendeSink sink, string gebeurtenis) =>
        Assert.Single(sink.Logs, regel => regel.Event == gebeurtenis);

    private static string? Overloop(LogRecord regel) =>
        regel.Extra?.TryGetProperty(MessageTruncation.OverflowKey, out JsonElement waarde) == true
            ? waarde.GetString()
            : null;
}
