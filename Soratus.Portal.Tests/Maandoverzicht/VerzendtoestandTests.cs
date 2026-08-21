using System.Text.Json;
using Soratus.Portal.Mail;

namespace Soratus.Portal.Tests.Maandoverzicht;

/// <summary>
/// De drie verzendtoestanden en de overgangen ertussen.
/// </summary>
/// <remarks>
/// De verzendbevestiging is een vastgelegd feit en geen <c>bool</c>. Dat is in dit portaal de vierde
/// keer dezelfde afweging — <c>AccessEntraState</c>, "geen document betekent geen status" (punt 2),
/// een contractbedrag dat ontbreekt (punt 15) en <c>recorded</c> in de MCP-server — en deze tests
/// leggen vast dat de vorm niet stil terugvalt op twee waarden.
/// </remarks>
public class VerzendtoestandTests
{
    [Fact]
    public void ErZijnDrieToestandenEnOnbekendIsDeStandaardwaarde()
    {
        var waarden = Enum.GetValues<StatementSendState>();

        Assert.Equal(3, waarden.Length);

        // De standaardwaarde van een niet-gezette enum hoort de veilige te zijn. Stond Sent op nul,
        // dan zou een document met een leeg of onleesbaar state-veld lezen als "verstuurd".
        Assert.Equal(StatementSendState.Unknown, default(StatementSendState));
        Assert.Equal(0, (int)StatementSendState.Unknown);
    }

    [Fact]
    public void ErIsGeenToestandDieZegtDatDeVerzendingNogLoopt()
    {
        // Dat lijkt informatie die je wilt hebben en het is precies de verkeerde: het verschil tussen
        // "loopt nog" en "onbekend" is alleen door de tijd te bepalen, en een proces dat halverwege
        // omvalt laat "loopt nog" staan. Dan staat er een toestand die zegt dat er iemand aan het werk
        // is terwijl er niemand is.
        var namen = Enum.GetNames<StatementSendState>();

        Assert.DoesNotContain("InFlight", namen);
        Assert.DoesNotContain("Sending", namen);
        Assert.DoesNotContain("Pending", namen);
    }

    [Fact]
    public void ErIsGeenToestandVoorNooitGeprobeerd()
    {
        // Die vierde toestand is de afwezigheid van het document. Punt 2 van de fase-0-afwijkingen:
        // geen document betekent geen status. Zou hij als enumwaarde bestaan, dan kan er een document
        // met die waarde staan zonder dat er iets is gebeurd — en dan is de afwezigheid van het
        // document geen antwoord meer op dezelfde vraag.
        Assert.DoesNotContain("NotAttempted", Enum.GetNames<StatementSendState>());
        Assert.DoesNotContain("None", Enum.GetNames<StatementSendState>());
    }

    [Fact]
    public void DeOpslagvormenZijnStabielEnKomenUitDeSerializer()
    {
        // De tekst in het document komt uit de converter en niet uit een switch ernaast. Zou hier een
        // schrijfwijze verschuiven, dan levert een query nul documenten op in plaats van een fout — en
        // dan zegt het scherm dat er nooit is gemaild.
        Assert.Equal("\"unknown\"", JsonSerializer.Serialize(StatementSendState.Unknown));
        Assert.Equal("\"sent\"", JsonSerializer.Serialize(StatementSendState.Sent));
        Assert.Equal("\"notSent\"", JsonSerializer.Serialize(StatementSendState.NotSent));
    }

    [Fact]
    public void ElkeWeigeringHeeftEenEigenOpslagvorm()
    {
        var vormen = Enum.GetValues<StatementRefusal>()
            .Select(refusal => JsonSerializer.Serialize(refusal))
            .ToArray();

        Assert.Equal(vormen.Length, vormen.Distinct(StringComparer.Ordinal).Count());
        Assert.All(vormen, vorm => Assert.DoesNotContain(' ', vorm));
    }

    [Fact]
    public void ZonderBevestigingMagErWordenVerstuurd() =>
        Assert.Null(StatementTransitions.WhyNotSend(null));

    [Fact]
    public void NaEenVerstuurdOverzichtMagErGeenTweedeUit()
    {
        var waarom = StatementTransitions.WhyNotSend(Bevestiging(StatementSendState.Sent));

        Assert.NotNull(waarom);
        Assert.Contains("al verstuurd", waarom, StringComparison.Ordinal);
    }

    [Fact]
    public void BijEenOnbekendeUitkomstMagErNietOpnieuwWordenVerstuurd()
    {
        // De vaste stelregel van dit project, hier als eigenschap in plaats van als gewoonte.
        var waarom = StatementTransitions.WhyNotSend(Bevestiging(StatementSendState.Unknown));

        Assert.NotNull(waarom);
        Assert.DoesNotContain("mislukt", waarom, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NaEenZekereMislukkingMagErOpnieuwWordenVerstuurd() =>
        Assert.Null(StatementTransitions.WhyNotSend(Bevestiging(StatementSendState.NotSent)));

    [Fact]
    public void VaststellenKanAlleenBijEenOnbekendeUitkomst()
    {
        Assert.Null(StatementTransitions.WhyNotRelease(Bevestiging(StatementSendState.Unknown)));
        Assert.NotNull(StatementTransitions.WhyNotRelease(null));
        Assert.NotNull(StatementTransitions.WhyNotRelease(Bevestiging(StatementSendState.Sent)));
        Assert.NotNull(StatementTransitions.WhyNotRelease(Bevestiging(StatementSendState.NotSent)));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("ok", false)]
    [InlineData("   niets   ", false)]
    [InlineData("Gebeld met Jan, niets ontvangen.", true)]
    public void EenVaststellingMoetIetsZeggen(string tekst, bool geldig)
    {
        var vaststelling = new StatementRelease("2026-07", tekst, BasedOnETag: null);

        Assert.Equal(geldig, vaststelling.Validate() is null);
    }

    [Fact]
    public void EenTeLangeVaststellingWordtGeweigerd()
    {
        var vaststelling = new StatementRelease(
            "2026-07",
            new string('a', StatementRelease.MaximumNoteLength + 1),
            BasedOnETag: null);

        Assert.NotNull(vaststelling.Validate());
    }

    [Fact]
    public void DeSleutelVanEenBevestigingIsAfgeleidVanDeMaand()
    {
        // Dit is het slot op een dubbele mail: één klant, één maand, één sleutel. Een willekeurige
        // sleutel zou een tweede claim laten slagen en dan gaat er een tweede mail uit.
        Assert.Equal("statement-2026-07", StatementDocumentKeys.Id("2026-07"));
        Assert.NotEqual(StatementDocumentKeys.Id("2026-07"), StatementDocumentKeys.Id("2026-08"));
    }

    private static StatementDocument Bevestiging(StatementSendState toestand) => new()
    {
        Id = StatementDocumentKeys.Id("2026-07"),
        PartitionKey = "acme-logistiek",
        CustomerId = "acme-logistiek",
        Month = "2026-07",
        State = toestand,
        AttemptedAt = new DateTimeOffset(2026, 8, 1, 6, 0, 0, TimeSpan.Zero),
    };
}
