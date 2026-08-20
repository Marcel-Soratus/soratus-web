using Microsoft.Extensions.Logging;
using Soratus.Agents.Contracts;
using Soratus.Agents.Telemetry.Tests.Hulpmiddelen;
using ContractLogLevel = Soratus.Agents.Contracts.LogLevel;

namespace Soratus.Agents.Telemetry.Tests;

/// <summary>
/// Framework-categorieën komen pas vanaf <c>warn</c> in het contract; agentcategorieën vanaf
/// <c>info</c>.
/// </summary>
/// <remarks>
/// <para><strong>Aanleiding, gemeten.</strong> <c>Microsoft.Hosting.Lifetime</c> schreef
/// <c>"Content root path: D:\SORATUS\Website\..."</c> in <c>msg</c>, en <c>msg</c> wordt door de
/// klant gelezen. Dat is een absoluut bestandspad, op één regel, dus de knip op de regelovergang
/// helpt er niet tegen. Het kwam ook niet van een agentbouwer — het staat er bij élke agent die met
/// een gewone host start.</para>
///
/// <para><strong>Waarom dit niets kost.</strong> "Application started" verdwijnt hiermee, maar dat
/// feit staat beter gemodelleerd in het registratiedocument als <c>startedAt</c> en
/// <c>lifecycle</c>. Een feit in een veld verslaat een regel die je moet zien langskomen.</para>
///
/// <para><strong>Waarom warn en error blijven.</strong> Dan gaat een framework-melding over echt
/// gedrag, zoals <c>HttpsRedirectionMiddleware — Failed to determine the https port</c>. Onschadelijk,
/// maar echt, en een operator hoort hem te kunnen vinden.</para>
///
/// <para>De toets is de categorie en niet de inhoud. Dat is dezelfde keuze als bij de knip op
/// <c>msg</c>: een patroon in de tekst zou vandaag op <c>D:\</c> letten en morgen <c>/srv/</c>
/// missen.</para>
/// </remarks>
public class FrameworkfilterTests
{
    [Theory]
    [InlineData("Microsoft.Hosting.Lifetime")]
    [InlineData("Microsoft.Extensions.Hosting")]
    [InlineData("Microsoft")]
    [InlineData("System.Net.Http.HttpClient")]
    [InlineData("System")]
    [InlineData("Azure.Identity")]
    [InlineData("Azure.Core")]
    [InlineData("Azure")]
    public async Task EenFrameworkcategorieKomtOpInfoNietDoor(string categorie)
    {
        OpvangendeSink sink = await Proefagent.LogAsync(
            categorie,
            logger => logger.LogInformation("Content root path: D:\\SORATUS\\Website\\"));

        Assert.Empty(sink.Logs);
    }

    [Fact]
    public async Task EenFrameworkcategorieKomtOpWarnWelDoor()
    {
        OpvangendeSink sink = await Proefagent.LogAsync(
            "Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware",
            logger => logger.LogWarning("Failed to determine the https port for redirect."));

        LogRecord regel = Assert.Single(sink.Logs);

        Assert.Equal(ContractLogLevel.Warn, regel.Level);
        Assert.Equal("Failed to determine the https port for redirect.", regel.Message);
    }

    [Fact]
    public async Task EenFrameworkcategorieKomtOpErrorWelDoor()
    {
        OpvangendeSink sink = await Proefagent.LogAsync(
            "Microsoft.Hosting.Lifetime",
            logger => logger.LogError("De host kon niet starten."));

        Assert.Equal(ContractLogLevel.Error, Assert.Single(sink.Logs).Level);
    }

    [Fact]
    public async Task EenAgentcategorieKomtOpInfoDoor()
    {
        OpvangendeSink sink = await Proefagent.LogAsync(
            "Bakker.VoorraadSync",
            logger => logger.LogInformation("Factuur INV-2291 verwerkt."));

        Assert.Equal("Factuur INV-2291 verwerkt.", Assert.Single(sink.Logs).Message);
    }

    [Theory]
    [InlineData("MicrosoftKoppeling.Mailtriage")]
    [InlineData("AzureKoppeling.Facturen")]
    [InlineData("SystemenBeheer.Voorraad")]
    public async Task EenAgentcategorieDieOpEenFrameworknaamLijktKomtWelDoor(string categorie)
    {
        // Het voorvoegsel wordt met het punt erbij getoetst, dus een agentbouwer die zijn koppeling
        // AzureKoppeling noemt wordt niet per ongeluk het zwijgen opgelegd. Het criterium is welke
        // bibliotheek er logt, niet hoe de naam klinkt.
        OpvangendeSink sink = await Proefagent.LogAsync(
            categorie,
            logger => logger.LogInformation("Twaalf berichten opgehaald."));

        Assert.Equal("Twaalf berichten opgehaald.", Assert.Single(sink.Logs).Message);
    }

    [Fact]
    public async Task EenAzurecategorieKomtOpWarnWelDoor()
    {
        OpvangendeSink sink = await Proefagent.LogAsync(
            "Azure.Identity",
            logger => logger.LogWarning("Het vernieuwen van het token is mislukt."));

        Assert.Equal(ContractLogLevel.Warn, Assert.Single(sink.Logs).Level);
    }

    [Theory]
    [InlineData(Microsoft.Extensions.Logging.LogLevel.Debug)]
    [InlineData(Microsoft.Extensions.Logging.LogLevel.Trace)]
    public async Task DebugEnTraceKomenVanGeenEnkeleCategorieDoor(
        Microsoft.Extensions.Logging.LogLevel niveau)
    {
        OpvangendeSink sink = await Proefagent.LogAsync(
            "Bakker.VoorraadSync",
            logger => logger.Log(niveau, "Waarde van teller: {Teller}", 42));

        Assert.Empty(sink.Logs);
    }

    [Fact]
    public async Task DeHostZelfSchrijftGeenOpstartregelsMeerInHetContract()
    {
        // Het geval waarmee dit gevonden is: een lopende host levert nu nul logregels op, in plaats
        // van "Application started", "Hosting environment" en "Content root path".
        OpvangendeSink sink = await Proefagent.DraaiAsync(static _ => Task.CompletedTask);

        Assert.Empty(sink.Logs);
    }
}
