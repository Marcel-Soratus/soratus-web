using Microsoft.Extensions.DependencyInjection;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.Tests.Hulpmiddelen;

namespace Soratus.Agents.Telemetry.Tests;

/// <summary>
/// <c>errorMessage</c> op een run is klantzichtbaar en krijgt dezelfde knip als <c>msg</c>;
/// <c>errorType</c> houdt zijn volledige typenaam.
/// </summary>
/// <remarks>
/// <para><strong>Waarom errorMessage geknipt wordt.</strong> Het staat op de run en niet in
/// <c>extra</c>, dus het heeft geen operator-only vangnet — het portaal zet het in de tooltip van
/// de resultaatbadge, voor de klant net zo goed als voor de operator. En het is vrije tekst:
/// <c>Fail(exception)</c> zet er <c>exception.Message</c> in, en de boodschap van een
/// <c>CosmosException</c> is een halve pagina diagnostiek over meerdere regels. Zonder knip landt
/// die ongefilterd op het scherm van de klant.</para>
///
/// <para>Er gaat niets verloren: de volledige boodschap staat in de bijbehorende
/// <c>run.failed</c>-logregel, en die is operator-only. Bij een uitzondering onder
/// <c>extra._exception.message</c>; bij een zelf opgegeven boodschap onder
/// <c>extra.msgOverflow</c>, want ook de tekst van die logregel gaat langs de knip op <c>msg</c>.
/// Twee wegen naar dezelfde belofte, en daarom staat er op beide een test — de belofte "er gaat
/// niets verloren" was aanvankelijk te grof opgeschreven en gold aantoonbaar maar voor één van de
/// twee.</para>
///
/// <para><strong>Waarom errorType níet wordt ingekort.</strong> Voor de operator is de naamruimte
/// juist het nuttige deel — <c>Sync.ValidationException</c> is een ander defect dan
/// <c>Mail.ValidationException</c>, en na inkorten zijn die twee niet meer te onderscheiden.
/// Afkappen bij het schrijven zou dat onherstelbaar weggooien, en dat is het verschil met
/// <c>errorMessage</c>: daar blijft de volledige tekst bewaard, hier niet. Of dit veld naar de
/// klant geprojecteerd mag worden is een vraag voor het portaal.</para>
/// </remarks>
public class FoutveldenOpDeRunTests
{
    private const string Frame =
        "   at Soratus.Sync.Validators.StockLineValidator.Validate(StockLine line) in /src/Sync/StockLineValidator.cs:line 42";

    [Fact]
    public async Task EenMeerregeligeUitzonderingsboodschapWordtGekniptInErrorMessage()
    {
        OpvangendeSink sink = await Draai(run =>
            run.Fail(new InvalidOperationException("Het boekhoudpakket gaf 502 terug.\n" + Frame)));

        RunRecord run = Afgerond(sink);

        Assert.Equal("Het boekhoudpakket gaf 502 terug." + MessageTruncation.Marker, run.ErrorMessage);
        Assert.DoesNotContain("/src/", run.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeVolledigeBoodschapBlijftInDeLogregelStaan()
    {
        const string boodschap = "Het boekhoudpakket gaf 502 terug.\n" + Frame;

        OpvangendeSink sink = await Draai(run => run.Fail(new InvalidOperationException(boodschap)));

        LogRecord regel = Assert.Single(sink.Logs, r => r.Event == "run.failed");
        string volledig = regel.Extra!.Value.GetProperty("_exception").GetProperty("message").GetString()!;

        Assert.Equal(boodschap, volledig);
    }

    [Fact]
    public async Task OokEenZelfOpgegevenBoodschapWordtGeknipt()
    {
        OpvangendeSink sink = await Draai(run => run.Fail("Http502", "De koppeling gaf 502.\n" + Frame));

        Assert.Equal("De koppeling gaf 502." + MessageTruncation.Marker, Afgerond(sink).ErrorMessage);
    }

    [Fact]
    public async Task OokBijEenZelfOpgegevenBoodschapBlijftDeRestBewaard()
    {
        // De belofte "er gaat niets verloren" moet ook op deze overload gelden. Ze komt hier langs
        // een andere weg uit dan bij een uitzondering: er is geen extra._exception, dus de rest
        // belandt in de msgOverflow van dezelfde run.failed-regel. Dat is de knip op msg die zijn
        // werk doet, want de logregel draagt de onafgekapte boodschap in zijn tekst.
        OpvangendeSink sink = await Draai(run => run.Fail("Http502", "De koppeling gaf 502.\n" + Frame));

        LogRecord regel = Assert.Single(sink.Logs, r => r.Event == "run.failed");
        string overloop = regel.Extra!.Value.GetProperty(MessageTruncation.OverflowKey).GetString()!;

        Assert.Equal(Frame, overloop);
    }

    [Fact]
    public async Task EenNetteFoutboodschapVanEenRegelBlijftHeel()
    {
        const string zin = "Onbekende SKU BK-77004 in de WMS-export; de batch is niet doorgezet.";

        OpvangendeSink sink = await Draai(run => run.Fail("ValidationError", zin));

        Assert.Equal(zin, Afgerond(sink).ErrorMessage);
    }

    [Fact]
    public async Task ErrorTypeHoudtDeVolledigeTypenaam()
    {
        // Bewust vastgelegd: de naamruimte is wat de operator nodig heeft, en inkorten bij het
        // schrijven is onomkeerbaar. Gaat iemand dit later toch afkappen, dan valt deze test om en
        // is de afweging opnieuw aan de orde.
        OpvangendeSink sink = await Draai(run => run.Fail(new InvalidOperationException("Mislukt.")));

        Assert.Equal("System.InvalidOperationException", Afgerond(sink).ErrorType);
    }

    private static async Task<OpvangendeSink> Draai(Action<IAgentRun> werk) =>
        await Proefagent.DraaiAsync(async diensten =>
        {
            var agent = diensten.GetRequiredService<ISoratusAgent>();
            await using IAgentRun run = await agent.StartRunAsync(TriggerKind.Manual);
            werk(run);
        });

    private static RunRecord Afgerond(OpvangendeSink sink) =>
        Assert.Single(sink.Runs, r => r.Result == RunResult.Failed);
}
