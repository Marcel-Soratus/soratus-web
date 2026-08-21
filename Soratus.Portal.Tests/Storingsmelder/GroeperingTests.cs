using Soratus.Agents.Contracts;
using Soratus.Portal.Alerts;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Storingsmelder;

/// <summary>
/// Wat er in één melding hoort: welke agents meetellen, en welke bij elkaar horen.
/// </summary>
/// <remarks>
/// Deze tests raken de melder niet aan. Wat hier gemeten wordt is de pure functie
/// <see cref="AgentFaults.From"/>: de selectie (productie, en waarover gemeld hoort te worden) en de
/// groepering (§42: één host, drie diensten).
/// </remarks>
public class GroeperingTests
{
    [Fact]
    public void DrieDienstenInEenProcesLeverenEenGroepOp()
    {
        // Punt 42: bij de eerste echte klant zijn de drie "agents" diensten binnen één webapplicatie.
        // Valt het proces uit, dan worden ze alle drie tegelijk degraded — één oorzaak. Het veld waaraan
        // dat te zien is, is startedAt, en dat is bij geherbergde agents exact gelijk.
        var start = Testgegevens.Nu - TimeSpan.FromHours(3);

        var groepen = AgentFaults.From(
            [
                Storingsmelderbank.Klant(
                [
                    Storingsmelderbank.Zwijgt("boekhoud-chat", start),
                    Storingsmelderbank.Zwijgt("financieel-overzicht", start),
                    Storingsmelderbank.Zwijgt("declaraties-import", start),
                ]),
            ],
            Testgegevens.Nu);

        var groep = Assert.Single(groepen);

        Assert.Equal(3, groep.Faults.Count);
        Assert.Equal(start, groep.StartedAt);

        // Op naam gesorteerd, zodat twee meldingen over dezelfde host dezelfde vorm hebben.
        Assert.Equal(
            ["boekhoud-chat", "declaraties-import", "financieel-overzicht"],
            groep.Faults.Select(fault => fault.AgentName));
    }

    [Fact]
    public void TweeAgentsMetEenEigenProcesLeverenTweeGroepenOp()
    {
        // De keerzijde, en die hoort er te zijn: twee losse agents zijn niet in dezelfde milliseconde
        // gestart, dus er zijn twee oorzaken en twee meldingen. Er staat nergens een controle op "is
        // dit een geherbergde agent"; het veld doet het werk.
        var groepen = AgentFaults.From(
            [
                Storingsmelderbank.Klant(
                [
                    Storingsmelderbank.Zwijgt("uren-sync", Testgegevens.Nu - TimeSpan.FromHours(3)),
                    Storingsmelderbank.Zwijgt("devops-sync", Testgegevens.Nu - TimeSpan.FromHours(9)),
                ]),
            ],
            Testgegevens.Nu);

        Assert.Equal(2, groepen.Count);
        Assert.All(groepen, groep => Assert.Single(groep.Faults));
    }

    [Fact]
    public void TweeKlantenMetDezelfdeStarttijdBlijvenTweeGroepen()
    {
        // De sleutel is klant plus startedAt en niet startedAt alleen. Twee klanten die per ongeluk
        // dezelfde starttijd hebben — of een seed die dat zo zet — horen niet in één mail te belanden;
        // dan zou een operator een melding over klant A lezen met een dienst van klant B erin.
        var start = Testgegevens.Nu - TimeSpan.FromHours(3);

        var groepen = AgentFaults.From(
            [
                Storingsmelderbank.Klant([Storingsmelderbank.Zwijgt("a", start)], "acme", "Acme"),
                Storingsmelderbank.Klant([Storingsmelderbank.Zwijgt("b", start)], "bakker", "Bakker"),
            ],
            Testgegevens.Nu);

        Assert.Equal(2, groepen.Count);
        Assert.Equal(["acme", "bakker"], groepen.Select(groep => groep.CustomerId));
    }

    [Fact]
    public void EenAgentBuitenProductieTeltNietMee()
    {
        // Punt 9, met dezelfde reden en een scherper gevolg: de interne klant draait heartbeat-demo op
        // dev, die meestal uit staat en dus permanent degraded is. Zonder dit filter zou de melder daar
        // elke zes uur over mailen, en dan is de melder binnen een week weggefilterd.
        var demo = Storingsmelderbank.Zwijgt("heartbeat-demo");

        var groepen = AgentFaults.From(
            [
                Storingsmelderbank.Klant(
                [
                    demo with
                    {
                        Registration = demo.Registration with
                        {
                            Environment = AgentEnvironment.Development,
                        },
                    },
                ]),
            ],
            Testgegevens.Nu);

        Assert.Empty(groepen);
    }

    [Fact]
    public void EenGezondeAgentLevertNietsOp()
    {
        var groepen = AgentFaults.From(
            [Storingsmelderbank.Klant([Storingsmelderbank.Gezond("factuur-intake")])],
            Testgegevens.Nu);

        Assert.Empty(groepen);
    }

    [Fact]
    public void EenKorteHaperingLevertNogGeenMelding()
    {
        // ShouldAlert meldt een degraded pas na AgentStatusThresholds.Alert. Een agent die drie minuten
        // zwijgt staat op het scherm al op amber en hoort nog geen mail op te leveren: een gemiste
        // hartslag tijdens een uitrol is geen storing. Deze test staat er om vast te leggen dat de
        // melder die grens niet zelf nog een keer bepaalt.
        var groepen = AgentFaults.From(
            [
                Storingsmelderbank.Klant(
                [
                    Storingsmelderbank.Zwijgt(
                        "factuur-intake",
                        silence: AgentStatusThresholds.Degraded + TimeSpan.FromMinutes(1)),
                ]),
            ],
            Testgegevens.Nu);

        Assert.Empty(groepen);
    }

    [Fact]
    public void EenMislukteRunMeldtMeteen()
    {
        var groepen = AgentFaults.From(
            [Storingsmelderbank.Klant([Storingsmelderbank.Mislukt("factuur-intake")])],
            Testgegevens.Nu);

        var fault = Assert.Single(Assert.Single(groepen).Faults);

        Assert.Equal(AgentStatus.Failed, fault.Status);
    }

    [Fact]
    public void EenGroepMetEenMislukteRunStaatVoorEenGroepDieAlleenZwijgt()
    {
        // De ordening bepaalt wie er binnen de rem valt. Eerst een afgerond feit, daarna de stilte.
        var groepen = AgentFaults.From(
            [
                Storingsmelderbank.Klant(
                    [Storingsmelderbank.Zwijgt("zwijger")],
                    "aaa-eerst-op-alfabet",
                    "Eerst"),
                Storingsmelderbank.Klant(
                    [Storingsmelderbank.Mislukt("mislukker")],
                    "zzz-laatst-op-alfabet",
                    "Laatst"),
            ],
            Testgegevens.Nu);

        Assert.Equal(2, groepen.Count);
        Assert.Equal("zzz-laatst-op-alfabet", groepen[0].CustomerId);
    }

    [Fact]
    public void EenKlantDieNietTeLezenWasLevertGeenMelding()
    {
        // "Wij konden niet lezen" is geen storing van de agent. Zonder deze eigenschap zou een hapering
        // van Cosmos een mail per agent van die klant opleveren.
        var groepen = AgentFaults.From(
            [new CustomerAgentScan("acme", "Acme", Agents: [], Unavailable: "CosmosException")],
            Testgegevens.Nu);

        Assert.Empty(groepen);
    }
}
