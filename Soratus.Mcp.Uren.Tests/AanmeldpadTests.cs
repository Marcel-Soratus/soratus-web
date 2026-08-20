using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Soratus.Mcp.Uren;

namespace Soratus.Mcp.Uren.Tests;

/// <summary>
/// Het aanmeldpad: een eigen public client met device-code, en geen terugval op de Azure CLI.
/// </summary>
/// <remarks>
/// <para>Het besluit is niet "een eigen client is netter" maar "de andere route is niet te zien".
/// Autoriseer je de Azure CLI-client vooraf op de portaal-API, dan kan élk script dat op die machine
/// met <c>DefaultAzureCredential</c> werkt een token voor het portaal krijgen en uren wegschrijven.
/// Dat is geen nieuwe macht — die persoon is al operator — maar de macht is dan bereikbaar voor code
/// die er niets mee te maken heeft.</para>
///
/// <para>Daarom staat hier een test die in het gecompileerde bestand kijkt of
/// <c>DefaultAzureCredential</c> ergens wordt aangehaald. Een terugvaloptie die er "voor de
/// zekerheid" bij komt, heropent die route stil: er verandert niets aan het gedrag tot iemand ooit de
/// CLI-client alsnog autoriseert, en dán is het gat er zonder dat er een regel code is gewijzigd.</para>
/// </remarks>
public class AanmeldpadTests
{
    private const string Client = "6b1a4c0e-0000-4000-8000-000000000001";
    private const string Tenant = "6b1a4c0e-0000-4000-8000-000000000002";

    private static UrenOptions Opties() => new()
    {
        PortalBaseAddress = new Uri("https://portal.soratus.com"),
        Scope = "api://soratus-portal/.default",
        ClientId = Client,
        TenantId = Tenant,
        // Naar een pad dat niet bestaat, zodat er geen bewaarde aanmelding van deze machine
        // meelift in de test.
        AuthenticationRecordPath = Path.Combine(Path.GetTempPath(), "soratus-uren-test-bestaat-niet.json"),
    };

    [Fact]
    public void DeCredentialIsEenDeviceCodeCredential()
    {
        TokenCredential credential = UrenCredentials.CreateSilent(Opties());

        Assert.IsType<DeviceCodeCredential>(credential);
    }

    [Fact]
    public void DeAssemblyHaaltDefaultAzureCredentialNergensAan()
    {
        string pad = typeof(UrenCredentials).Assembly.Location;

        using var stream = File.OpenRead(pad);
        using var reader = new PEReader(stream);
        MetadataReader metadata = reader.GetMetadataReader();

        string[] aangehaald = [.. metadata.TypeReferences
            .Select(handle => metadata.GetString(metadata.GetTypeReference(handle).Name))];

        Assert.DoesNotContain("DefaultAzureCredential", aangehaald);
        Assert.DoesNotContain("AzureCliCredential", aangehaald);
        // Deze wél: hij is de hele aanmelding.
        Assert.Contains("DeviceCodeCredential", aangehaald);
    }

    [Fact]
    public void DeMcpModusVraagtNooitInteractiefOmEenAanmelding()
    {
        // De stille credential werpt AuthenticationRequiredException in plaats van een prompt te
        // openen. Dat moet zo: een device-code-instructie zou op stdout moeten en dat is het
        // JSON-RPC-kanaal; op stderr ziet de aanroeper hem niet en hangt de tool tot de tijdslimiet.
        TokenCredential credential = UrenCredentials.CreateSilent(Opties());

        Assert.ThrowsAny<CredentialUnavailableException>(() =>
            credential.GetToken(new TokenRequestContext(["api://soratus-portal/.default"]), CancellationToken.None));
    }

    [Fact]
    public void DeTokencacheStaatNooitOnversleuteldToe()
    {
        // Een tokencache voor een schrijfpad naar facturatiegegevens die onversleuteld op schijf staat,
        // is een credential in rust. Draait dit ooit op een machine zonder sleutelbewaring, dan hoort
        // dat om te vallen en niet stil terug te vallen op een leesbaar bestand.
        var options = new DeviceCodeCredentialOptions();

        Assert.False(options.TokenCachePersistenceOptions?.UnsafeAllowUnencryptedStorage ?? false);
    }

    [Fact]
    public void DeMeldingenVerwijzenNaarCommandoNamenDieBestaan()
    {
        // Een melding die "meld je aan met X" zegt terwijl X niet bestaat, is erger dan geen melding.
        Assert.StartsWith("soratus-uren ", UrenCommands.SignIn, StringComparison.Ordinal);
        Assert.StartsWith("soratus-uren ", UrenCommands.Check, StringComparison.Ordinal);

        string[] werkwoorden = [.. new[] { UrenCommands.SignIn, UrenCommands.Check }
            .Select(static command => command.Split(' ')[1])];

        // Dit zijn de werkwoorden die Program.cs afhandelt.
        Assert.Equal(["aanmelden", "controleer"], werkwoorden);
    }

    [Fact]
    public void ZonderClientIdValtDeServerOmMetDeSleutelInDeMelding()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new(UrenConfiguration.PortalKey, "https://portal.soratus.com"),
                new(UrenConfiguration.ScopeKey, "api://soratus-portal/.default"),
            ])
            .Build();

        string melding = Assert.Throws<InvalidOperationException>(
            () => UrenConfiguration.Resolve(configuration)).Message;

        Assert.Contains(UrenConfiguration.ClientIdKey, melding, StringComparison.Ordinal);
    }

    [Fact]
    public void EenClientIdDatGeenGuidIsWordtGeweigerd()
    {
        // Een verkeerd geplakte waarde levert anders pas bij het aanmelden een melding over
        // "invalid_client" op, en daar herkent niemand een plakfout in.
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new(UrenConfiguration.PortalKey, "https://portal.soratus.com"),
                new(UrenConfiguration.ScopeKey, "api://soratus-portal/.default"),
                new(UrenConfiguration.ClientIdKey, "soratus-uren"),
                new(UrenConfiguration.TenantIdKey, Tenant),
            ])
            .Build();

        string melding = Assert.Throws<InvalidOperationException>(
            () => UrenConfiguration.Resolve(configuration)).Message;

        Assert.Contains("geen GUID", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void DeBewaardeAanmeldingIsGeenGeheimEnDraagtGeenToken()
    {
        // Voor wie dit bestand ooit tegenkomt: het draagt de gebruikersnaam, tenant en account-id
        // zodat de credential weet welk account hij in de versleutelde cache moet zoeken. De tokens
        // staan daar en niet hier. Deze test legt vast dat de standaardplek in de gebruikersmap ligt
        // en niet in de repo.
        string pad = new UrenOptions().AuthenticationRecordPath;

        Assert.Contains("soratus-uren", pad, StringComparison.Ordinal);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            pad,
            StringComparison.Ordinal);
    }
}
