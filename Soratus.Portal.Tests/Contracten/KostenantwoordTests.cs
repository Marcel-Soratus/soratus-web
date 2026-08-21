using System.Text.Json;
using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// Het uitlezen van een antwoord van <c>Microsoft.CostManagement/query</c>.
/// </summary>
/// <remarks>
/// <para><strong>De antwoorden in dit bestand zijn geen verzinsels.</strong> Ze zijn op 21 augustus
/// 2026 letterlijk overgenomen uit twee aanroepen tegen
/// <c>subscriptions/501a66d2-…/resourceGroups/MBV/providers/Microsoft.CostManagement/query</c> met
/// <c>api-version=2023-11-01</c> — één met <c>granularity: None</c> en één met
/// <c>granularity: Daily</c>. Dat is de reden dat er getallen als
/// <c>1.0543425734745e-05</c> en <c>37.4563985414928</c> in staan: op ronde getallen zijn de twee
/// valkuilen die hier worden gemeten onzichtbaar.</para>
///
/// <para>Die twee valkuilen: de kolomvolgorde verschilt per vraag, en een geslaagd antwoord kan nul
/// rijen hebben zonder dat dat nul euro betekent.</para>
/// </remarks>
public class KostenantwoordTests
{
    /// <summary>
    /// Het gemeten antwoord op <c>granularity: None</c> met groepering op <c>ServiceName</c>.
    /// </summary>
    /// <remarks>
    /// Let op de kolomvolgorde: <c>Cost, ServiceName, Currency</c> — de dienstnaam staat op index 1.
    /// </remarks>
    private const string ZonderDagen = """
        {
          "properties": {
            "nextLink": null,
            "columns": [
              { "name": "Cost", "type": "Number" },
              { "name": "ServiceName", "type": "String" },
              { "name": "Currency", "type": "String" }
            ],
            "rows": [
              [37.4563985414928, "Azure App Service", "EUR"],
              [0.033319210410106, "Azure Cosmos DB", "EUR"],
              [0.0, "Bandwidth", "EUR"],
              [0.000242498791899135, "Key Vault", "EUR"],
              [0.0, "Microsoft Entra", "EUR"]
            ]
          }
        }
        """;

    /// <summary>
    /// Het gemeten antwoord op <c>granularity: Daily</c>, ingekort tot twee dagen.
    /// </summary>
    /// <remarks>
    /// <strong>De kolomvolgorde is hier anders: <c>Cost, UsageDate, ServiceName, Currency</c>.</strong>
    /// De dienstnaam staat op index 2 en niet op index 1. Een lezer met vaste indices haalt hier de
    /// dienstnaam uit de datumkolom en levert een dienst <c>20260801</c> op — geen crash, maar een
    /// verkeerd bedrag per dienst dat alleen opvalt als iemand het subtotaal natelt.
    /// </remarks>
    private const string MetDagen = """
        {
          "properties": {
            "nextLink": null,
            "columns": [
              { "name": "Cost", "type": "Number" },
              { "name": "UsageDate", "type": "Number" },
              { "name": "ServiceName", "type": "String" },
              { "name": "Currency", "type": "String" }
            ],
            "rows": [
              [1.87672978078461, 20260801, "Azure App Service", "EUR"],
              [0.00147764132181829, 20260801, "Azure Cosmos DB", "EUR"],
              [1.0543425734745e-05, 20260801, "Key Vault", "EUR"],
              [1.87672978078461, 20260802, "Azure App Service", "EUR"],
              [0.00158697326799326, 20260802, "Azure Cosmos DB", "EUR"],
              [1.0543425734745e-05, 20260802, "Key Vault", "EUR"]
            ]
          }
        }
        """;

    /// <summary>Het gemeten antwoord op een resource group die niet bestaat.</summary>
    /// <remarks>
    /// <strong>HTTP 200.</strong> Dit is niet een foutantwoord dat op 200 lijkt — het ís een geslaagd
    /// antwoord, en het is niet te onderscheiden van een bestaande omgeving over een periode die nog
    /// niet is geboekt. Beide gemeten, dezelfde dag, dezelfde api-versie.
    /// </remarks>
    private const string Leeg = """
        {
          "properties": {
            "nextLink": null,
            "columns": [
              { "name": "Cost", "type": "Number" },
              { "name": "ServiceName", "type": "String" },
              { "name": "Currency", "type": "String" }
            ],
            "rows": []
          }
        }
        """;

    private static AzureCostQueryReading Lees(string json) =>
        AzureCostQuery.Read(
            JsonSerializer.Deserialize<AzureCostQueryResponse>(json)
            ?? throw new InvalidOperationException("Het testantwoord is niet te deserialiseren."));

    [Fact]
    public void DeVijfDienstenKomenUitDeApiEnNietUitEenLijst()
    {
        // §3.7 noemt Container Apps, Azure OpenAI, Storage, Log Analytics en Key Vault. Vier van die
        // vijf komen in de werkelijke uitvoer niet voor. Een vaste lijst in onze code zou vandaag al de
        // helft missen en zou op de dag dat er een dienst bijkomt stil geld buiten het subtotaal laten
        // vallen — en dat is precies het soort fout dat een factuur haalt zonder dat iemand het ziet.
        var lezing = Lees(ZonderDagen);

        Assert.Equal(
            ["Azure App Service", "Azure Cosmos DB", "Bandwidth", "Key Vault", "Microsoft Entra"],
            lezing.Lines.Select(regel => regel.Service));

        Assert.Equal("EUR", lezing.Currency);
        Assert.Empty(lezing.Days);
    }

    [Fact]
    public void EenDienstMetEenBedragOnderEenCentBlijftStaan()
    {
        // Key Vault kostte over de hele maand € 0,000242498791899135. Zou de lezing hier afronden, dan
        // is die dienst nul en verdwijnt hij uit de betekenis van de uitsplitsing. Het afronden hoort
        // één keer te gebeuren, op het bedrag dat wordt doorbelast.
        var lezing = Lees(ZonderDagen);

        Assert.Equal(
            0.000242498791899135m,
            lezing.Lines.Single(regel => regel.Service == "Key Vault").Amount);
    }

    [Fact]
    public void EenDienstMetExactNulBlijftOokStaan()
    {
        // De spiegel. Bandwidth en Microsoft Entra stonden op exact € 0,0000 en zijn gewone regels. Zou
        // de lezing nulregels weglaten, dan is een maand met alleen nulregels niet van een maand zonder
        // regels te onderscheiden — en dat is precies het verschil tussen een gemeten nul en een
        // onbekend bedrag.
        var lezing = Lees(ZonderDagen);

        Assert.Equal(0m, lezing.Lines.Single(regel => regel.Service == "Bandwidth").Amount);
        Assert.Equal(5, lezing.Lines.Count);
    }

    [Fact]
    public void DeKolomvolgordeKomtUitColumnsEnWordtNietAangenomen()
    {
        // De valkuil van dit bestand. Bij granularity Daily staat ServiceName op index 2 en niet op
        // index 1. Een lezer met vaste indices levert hier diensten op die "20260801" heten.
        var lezing = Lees(MetDagen);

        Assert.Equal(
            ["Azure App Service", "Azure Cosmos DB", "Key Vault"],
            lezing.Lines.Select(regel => regel.Service));

        Assert.DoesNotContain(lezing.Lines, regel => regel.Service.StartsWith("2026", StringComparison.Ordinal));
    }

    [Fact]
    public void DeDagenKomenEruitEnDeBedragenWordenPerDienstOpgeteld()
    {
        // Met dagkorrel staat elke dienst er één keer per dag in; gemeten waren dat vijfenzestig rijen
        // over twintig dagen. De volledigheidscontrole heeft de dagen nodig, de uitsplitsing de som.
        var lezing = Lees(MetDagen);

        Assert.Equal([new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2)], lezing.Days);
        Assert.Equal(
            1.87672978078461m * 2,
            lezing.Lines.Single(regel => regel.Service == "Azure App Service").Amount);
    }

    [Fact]
    public void WetenschappelijkeNotatieWordtGelezenEnNietStilNul()
    {
        // 1.0543425734745e-05 is hoe de API een dag Key Vault opschrijft. Een lezer die dat niet
        // aankan en de rij overslaat, levert een subtotaal op dat te laag is — en een bedrag dat te
        // laag is ziet er net zo geloofwaardig uit als een bedrag dat klopt.
        var lezing = Lees(MetDagen);

        Assert.Equal(
            1.0543425734745e-05m * 2,
            lezing.Lines.Single(regel => regel.Service == "Key Vault").Amount);
    }

    [Fact]
    public void NulRijenLeverenNulRegelsOpEnGeenBedragVanNul()
    {
        // De kern. Dit antwoord kwam van een resource group die niet bestaat — en het is
        // ononderscheidbaar van een bestaande omgeving over een periode die nog niet is geboekt. Er
        // komt dus geen enkel getal uit, ook geen nul, en geen valuta: een verzonnen valuta naast een
        // bedrag dat we niet hebben is een tweede onwaarheid op dezelfde regel.
        var lezing = Lees(Leeg);

        Assert.Empty(lezing.Lines);
        Assert.Empty(lezing.Days);
        Assert.Null(lezing.Currency);
    }

    [Fact]
    public void EenOntbrekendeBedragkolomWerptEnLevertGeenLegeLezing()
    {
        // Zou dit "geen regels" opleveren, dan is een gewijzigd antwoordformaat niet van een klant
        // zonder verbruik te onderscheiden, en dan factureren we maanden lang niets zonder dat er iets
        // rood staat. De aanroeper hoort hiervan AzureCostState.Unknown te maken.
        const string zonderKosten = """
            {
              "properties": {
                "columns": [
                  { "name": "ServiceName", "type": "String" },
                  { "name": "Currency", "type": "String" }
                ],
                "rows": [["Azure App Service", "EUR"]]
              }
            }
            """;

        var fout = Assert.Throws<InvalidOperationException>(() => Lees(zonderKosten));

        Assert.Contains("Cost", fout.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EenRijMetTeWeinigWaardenWerptInPlaatsVanDeRestOpTeTellen()
    {
        // Een deel van het antwoord lezen zou een subtotaal opleveren dat te laag is. Dat is dezelfde
        // fout als de overgeslagen rij hierboven en hij is even onzichtbaar.
        const string scheve = """
            {
              "properties": {
                "columns": [
                  { "name": "Cost", "type": "Number" },
                  { "name": "ServiceName", "type": "String" },
                  { "name": "Currency", "type": "String" }
                ],
                "rows": [
                  [37.45, "Azure App Service", "EUR"],
                  [1.23, "Azure Cosmos DB"]
                ]
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => Lees(scheve));
    }

    [Fact]
    public void EenBedragDatGeenGetalIsWerptEnWordtGeenNul()
    {
        // Dit gat is met een mutatietest gevonden: een `return 0m;` vóór de throw in Amount() maakte
        // niets rood. Er stonden tests op een ontbrekende kolom, een scheve rij en een onleesbare dag,
        // maar niet op een bedrag dat geen getal is — en dat is juist het geval waarin een nul de
        // factuur haalt.
        const string tekstueel = """
            {
              "properties": {
                "columns": [
                  { "name": "Cost", "type": "Number" },
                  { "name": "ServiceName", "type": "String" }
                ],
                "rows": [["37,45", "Azure App Service"]]
              }
            }
            """;

        var fout = Assert.Throws<InvalidOperationException>(() => Lees(tekstueel));

        Assert.Contains("geen nul", fout.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOntbrekendBedragWerptOokAlsDeWaardeNullIs()
    {
        // De spiegelvorm van hierboven: JSON null in de bedragkolom. Ook geen nul.
        const string leegbedrag = """
            {
              "properties": {
                "columns": [
                  { "name": "Cost", "type": "Number" },
                  { "name": "ServiceName", "type": "String" }
                ],
                "rows": [[null, "Azure App Service"]]
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => Lees(leegbedrag));
    }

    [Fact]
    public void EenOnleesbareDagWerptWantZonderDagenIsDeMaandNietTeWegen()
    {
        const string kromme = """
            {
              "properties": {
                "columns": [
                  { "name": "Cost", "type": "Number" },
                  { "name": "UsageDate", "type": "Number" },
                  { "name": "ServiceName", "type": "String" }
                ],
                "rows": [[1.0, 20261301, "Azure App Service"]]
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => Lees(kromme));
    }

    [Fact]
    public void DeVervolgpaginaKomtMeeEnWordtNietWeggegooid()
    {
        // Op de gemeten scope was nextLink altijd null — vijf diensten, met dagkorrel vijfenzestig
        // rijen — maar een grotere klant kan pagineren, en een lezer die de vervolgpagina niet ophaalt
        // heeft een subtotaal dat te laag is. Dat is onzichtbaar, dus de waarde hoort de aanroeper te
        // bereiken.
        const string metVervolg = """
            {
              "properties": {
                "nextLink": "https://management.azure.com/…&$skiptoken=abc",
                "columns": [
                  { "name": "Cost", "type": "Number" },
                  { "name": "ServiceName", "type": "String" }
                ],
                "rows": [[1.0, "Azure App Service"]]
              }
            }
            """;

        var vervolg = Lees(metVervolg).NextLink;

        Assert.NotNull(vervolg);
        Assert.Contains("skiptoken", vervolg, StringComparison.Ordinal);
    }

    [Fact]
    public void EenLeegAntwoordGeeftDeVervolgpaginaOokTerug()
    {
        // De vroege uitweg bij nul rijen mag de vervolgpagina niet laten vallen. Een lege eerste pagina
        // met een vervolg erachter is theoretisch, maar niets in de API sluit het uit — en zou de
        // uitweg de link weggooien, dan is het subtotaal nul terwijl er kosten zijn. Dat is de
        // gevaarlijkste van alle uitkomsten en hij zou hier ontstaan.
        const string leegMetVervolg = """
            {
              "properties": {
                "nextLink": "https://management.azure.com/…&$skiptoken=leeg",
                "columns": [
                  { "name": "Cost", "type": "Number" },
                  { "name": "ServiceName", "type": "String" }
                ],
                "rows": []
              }
            }
            """;

        var lezing = Lees(leegMetVervolg);

        Assert.Empty(lezing.Lines);
        Assert.NotNull(lezing.NextLink);
        Assert.Contains("skiptoken", lezing.NextLink, StringComparison.Ordinal);
    }
}
