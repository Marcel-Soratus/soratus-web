using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Soratus.Support.FirstLine.Tests;

/// <summary>
/// De aanroep aan Azure OpenAI: wat er de deur uit gaat, en wat er van een antwoord overblijft.
/// </summary>
/// <remarks>
/// <para><strong>Er is geen echte aanroep gedaan.</strong> De identiteit van het portaal heeft nog
/// geen rol op <c>aoai-soratus-prod</c> (§47.5), en de opdracht sloot schrijfacties in Azure uit. Wat
/// hier wordt gemeten is dus de vorm van het verzoek en de behandeling van het antwoord, met een
/// afhandelaar ertussen — niet dat het model iets zinnigs kiest. Dat laatste is met een test ook niet
/// te meten.</para>
/// </remarks>
public class EerstelijnaanroepTests
{
    private const string Endpoint = "https://aoai-soratus-prod.openai.azure.com/";

    private static readonly FirstLineQuestion Drie = new()
    {
        Text = "Hoeveel uren heb ik in juli verbruikt?",
        Facts = ["het eerste feit", "het tweede feit", "het derde feit"],
    };

    /// <summary>
    /// Een respons van de dienst met deze tekst als inhoud van de boodschap.
    /// </summary>
    /// <param name="inhoud">Wat het model antwoordt, als gewone tekst.</param>
    /// <param name="stop">De <c>finish_reason</c>.</param>
    /// <returns>Het lichaam van de respons.</returns>
    /// <remarks>
    /// <strong>Het escapen gebeurt hier en niet op elke aanroepplaats.</strong> Dat is §46.12.8 in
    /// praktijk gebracht: een test waarin het verschil tussen het teken en de escape onzichtbaar is,
    /// is niet te reviewen. Een aanroeper schrijft dus <c>{"kies": 1}</c> zoals het model het zou
    /// schrijven, en de serializer maakt daar een JSON-tekenreeks van.
    /// </remarks>
    private static string Antwoord(string inhoud, string stop = "stop") =>
        "{\"choices\":[{\"finish_reason\":\"" + stop + "\",\"message\":{\"role\":\"assistant\""
        + ",\"content\":" + JsonSerializer.Serialize(inhoud) + "}}]}";

    private static (AzureOpenAiChooser Kiezer, Vasteafhandelaar Afhandelaar, Testlogger<AzureOpenAiChooser> Logger)
        Opzet(
            HttpStatusCode status = HttpStatusCode.OK,
            string lichaam = "{}",
            TimeSpan? wachten = null,
            int seconden = 20)
    {
        var afhandelaar = new Vasteafhandelaar(status, lichaam, wachten);
        var logger = new Testlogger<AzureOpenAiChooser>();

        var kiezer = new AzureOpenAiChooser(
            new Vasteclientfabriek(afhandelaar),
            new Vastecredential(),
            Options.Create(new FirstLineOptions
            {
                Endpoint = Endpoint,
                Deployment = "gpt-4o-mini",
                Enabled = true,
                TimeoutSeconds = seconden,
            }),
            logger);

        return (kiezer, afhandelaar, logger);
    }

    // ── Wat er de deur uit gaat ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HetVerzoekDraagtEenBearertokenEnGeenSleutel()
    {
        var (kiezer, afhandelaar, _) = Opzet(lichaam: Antwoord("{\"kies\": 1}"));

        await kiezer.ChooseAsync(Drie);

        Assert.StartsWith("Bearer ", afhandelaar.Autorisatie, StringComparison.Ordinal);

        // api-key is de header van de marketingsite. Die staat hier niet, en dat is de hele reden dat
        // dit project een eigen aanroep heeft in plaats van die code te hergebruiken.
        Assert.DoesNotContain("api-key", afhandelaar.Autorisatie ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HetVerzoekGaatNaarDeDeploymentUitDeInstellingen()
    {
        var (kiezer, afhandelaar, _) = Opzet(lichaam: Antwoord("{\"kies\": 1}"));

        await kiezer.ChooseAsync(Drie);

        Assert.Equal(
            Endpoint + "openai/deployments/gpt-4o-mini/chat/completions?api-version=2024-10-21",
            afhandelaar.Adres?.ToString());
    }

    [Fact]
    public async Task HetLichaamDraagtDeVraagDeFeitenEnEenJsonvorm()
    {
        var (kiezer, afhandelaar, _) = Opzet(lichaam: Antwoord("{\"kies\": 1}"));

        await kiezer.ChooseAsync(Drie);

        var lichaam = afhandelaar.Verzoeklichaam ?? string.Empty;

        Assert.Contains("json_object", lichaam, StringComparison.Ordinal);
        Assert.Contains("\"temperature\":0", lichaam, StringComparison.Ordinal);
        Assert.Contains("Hoeveel uren heb ik in juli verbruikt?", lichaam, StringComparison.Ordinal);
        Assert.Contains("1. het eerste feit", lichaam, StringComparison.Ordinal);
        Assert.Contains("3. het derde feit", lichaam, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErGaatNietsAndersDeDeurUitDanDeVraagEnDeFeiten()
    {
        // Dit is de meting onder §47.6: de volledige opsomming van wat er over een klant het proces
        // verlaat. Er is geen klantslug, geen e-mailadres en geen contract op FirstLineQuestion, dus
        // de manier om dit te meten is dat de tekst van het verzoek uit precies twee bronnen bestaat:
        // de vaste opdracht, en de vraag met de genummerde feiten.
        var (kiezer, afhandelaar, _) = Opzet(lichaam: Antwoord("{\"kies\": 1}"));

        await kiezer.ChooseAsync(Drie);

        var lichaam = afhandelaar.Verzoeklichaam ?? string.Empty;

        foreach (var vreemd in new[] { "bakker", "vandijk", "customerId", "klantslug", "@soratus.com" })
        {
            Assert.DoesNotContain(vreemd, lichaam, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ZonderFeitenWordtErNietsGevraagd()
    {
        // Niets te kiezen, dus niet vragen. Eerlijker dan vragen — hetzelfde besluit als bij de
        // kostencollector op de 1e van de maand — en het scheelt een aanroep die alleen maar een
        // overdracht kan opleveren.
        var (kiezer, afhandelaar, _) = Opzet();

        var keuze = await kiezer.ChooseAsync(
            new FirstLineQuestion { Text = "Hoe staat het ervoor?", Facts = [] });

        Assert.Equal(0, afhandelaar.Aanroepen);
        Assert.Equal(FirstLineHandoff.OutsideTheData, keuze?.Handoff);
    }

    [Fact]
    public async Task ZonderEndpointWordtErNietsGevraagd()
    {
        var afhandelaar = new Vasteafhandelaar(HttpStatusCode.OK, "{}");

        var kiezer = new AzureOpenAiChooser(
            new Vasteclientfabriek(afhandelaar),
            new Vastecredential(),
            Options.Create(new FirstLineOptions { Enabled = true }),
            new Testlogger<AzureOpenAiChooser>());

        Assert.Null(await kiezer.ChooseAsync(Drie));
        Assert.Equal(0, afhandelaar.Aanroepen);
    }

    // ── Wat er terugkomt ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EenGekozenNummerWordtEenPlaats()
    {
        var (kiezer, _, _) = Opzet(lichaam: Antwoord("{\"kies\": 2}"));

        var keuze = await kiezer.ChooseAsync(Drie);

        Assert.Equal(1, keuze?.Index);
    }

    [Fact]
    public async Task EenOverdrachtKomtDoorAlsOverdracht()
    {
        var (kiezer, _, _) = Opzet(lichaam: Antwoord("{\"overdracht\": \"geenFeit\"}"));

        var keuze = await kiezer.ChooseAsync(Drie);

        Assert.Equal(FirstLineHandoff.NeedsAHuman, keuze?.Handoff);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task EenFoutstatusLevertNietsOpEnGeenHerhaling(HttpStatusCode status)
    {
        // Geen backoff en geen tweede poging, anders dan bij AzureCostClient: daar wacht niemand, hier
        // wacht een mens op een pagina en staat zijn vraag al in de draad. Een 429 is dus geen reden om
        // te wachten maar om te escaleren.
        var (kiezer, afhandelaar, _) = Opzet(status, "{\"error\":{\"message\":\"nee\"}}");

        Assert.Null(await kiezer.ChooseAsync(Drie));
        Assert.Equal(1, afhandelaar.Aanroepen);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("content_filter")]
    public async Task EenAfgekaptOfGefilterdAntwoordIsGeenAntwoord(string stop)
    {
        // Bij "length" staat er een halve JSON, en die is soms nog te parseren — dan zou een half
        // nummer een heel feit aanwijzen.
        var (kiezer, _, _) = Opzet(lichaam: Antwoord("{\"kies\": 1}", stop));

        Assert.Null(await kiezer.ChooseAsync(Drie));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":null}}]}")]
    public async Task EenAntwoordZonderInhoudLevertNietsOp(string lichaam)
    {
        var (kiezer, _, _) = Opzet(lichaam: lichaam);

        Assert.Null(await kiezer.ChooseAsync(Drie));
    }

    [Fact]
    public async Task EenLichaamDatGeenJsonIsLevertNietsOpEnGeenUitzondering()
    {
        var (kiezer, _, _) = Opzet(lichaam: "<html>502 Bad Gateway</html>");

        Assert.Null(await kiezer.ChooseAsync(Drie));
    }

    // ── Wachten en afbreken ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EenTijdslimietLevertNietsOpEnGeenUitzondering()
    {
        // Aan de andere kant van deze wachttijd zit een mens die naar een ladende pagina kijkt. Zijn
        // vraag staat al in de draad, dus een tijdslimiet kost hem een AI-antwoord en niet zijn vraag.
        var (kiezer, _, logger) = Opzet(wachten: TimeSpan.FromSeconds(30), seconden: 1);

        Assert.Null(await kiezer.ChooseAsync(Drie));
        Assert.Contains(logger.Regels, regel => regel.Contains("langer dan 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EenKlantDieZijnTabbladSluitLevertEenAfbreking()
    {
        // OperationCanceledException gaat wél door: SupportDesk laat hem met opzet door, en er valt
        // niets weg door niets te doen — de vraag is vastgelegd vóór deze aanroep.
        var (kiezer, _, _) = Opzet(wachten: TimeSpan.FromSeconds(30));

        using var afgebroken = new CancellationTokenSource();
        var bezig = kiezer.ChooseAsync(Drie, afgebroken.Token);

        await afgebroken.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bezig);
    }

    // ── Wat er in een logregel mag staan ────────────────────────────────────────────────────────

    [Fact]
    public async Task ErKomtGeenKlanttekstInEenLogregel()
    {
        // Punt 13 en 14 in een nieuwe richting. Het foutlichaam van een externe dienst kan onze eigen
        // prompt terugkaatsen, en een logregel van dit portaal komt op een operatorscherm. Wat er wél
        // in mag: de statuscode, de deployment, het aantal feiten en de gekozen plaats.
        var (kiezer, _, logger) = Opzet(
            HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"content filter op: Hoeveel uren heb ik in juli verbruikt?\"}}");

        await kiezer.ChooseAsync(Drie);

        Assert.NotEmpty(logger.Regels);

        foreach (var regel in logger.Regels)
        {
            Assert.DoesNotContain("juli verbruikt", regel, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("het eerste feit", regel, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("content filter op", regel, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(logger.Regels, regel => regel.Contains("400", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeLogregelNoemtDeDeploymentEnDeGekozenPlaats()
    {
        // §46.9: wie welk model heeft gedraaid hoort in de logregel, bij de operator, en niet op een
        // bericht van een klant.
        var (kiezer, _, logger) = Opzet(lichaam: Antwoord("{\"kies\": 3}"));

        await kiezer.ChooseAsync(Drie);

        Assert.Contains(
            logger.Regels,
            regel => regel.Contains("gpt-4o-mini", StringComparison.Ordinal)
                && regel.Contains("plaats 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeClientKomtUitDeFabriekMetEenEigenNaam()
    {
        // Een fabriek en geen geïnjecteerde HttpClient, om dezelfde reden als bij AzureCostClient en
        // DevOpsSprintClient: zo kan deze klasse niet stil een singleton worden die jaren dezelfde
        // handler vasthoudt en een DNS-wijziging van openai.azure.com niet meer volgt.
        var afhandelaar = new Vasteafhandelaar(HttpStatusCode.OK, Antwoord("{\"kies\": 1}"));
        var fabriek = new Vasteclientfabriek(afhandelaar);

        var kiezer = new AzureOpenAiChooser(
            fabriek,
            new Vastecredential(),
            Options.Create(new FirstLineOptions
            {
                Endpoint = Endpoint,
                Deployment = "gpt-4o-mini",
                Enabled = true,
            }),
            new Testlogger<AzureOpenAiChooser>());

        await kiezer.ChooseAsync(Drie);

        Assert.Equal(AzureOpenAiChooser.HttpClientName, fabriek.Naam);
    }
}
