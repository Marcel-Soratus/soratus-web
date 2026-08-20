using Microsoft.Extensions.Options;
using Soratus.Portal.Data;
using Soratus.Portal.Security;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Portaalgegevens;

/// <summary>
/// De klantenlijst is een momentopname: hij komt uit de configuratie tot de opslag is gelezen, en
/// wordt dan in één keer vervangen.
/// </summary>
/// <remarks>
/// <para>Dit is de autorisatiebron van het hele portaal. Wie erin staat kan aanmelden en bij zijn
/// eigen omgeving; wie eruit valt kan dat niet. Elke test hier gaat daarom over één van twee vragen:
/// <em>staat er iemand in die er niet in hoort</em> (een lek) en <em>valt er iemand uit die er wel in
/// hoort</em> (een buitensluiting). Het tweede is in dit ontwerp het waarschijnlijkere van de twee,
/// want de lijst wordt <strong>vervangen</strong> en niet samengevoegd.</para>
///
/// <para>De klasse is <c>internal</c> en wordt hier direct aangeroepen via de
/// <c>InternalsVisibleTo</c> uit <c>Soratus.Portal.csproj</c>. <see cref="CustomerDirectory.Replace"/>
/// is ook <c>internal</c> — dat hoort zo, een publieke methode waarmee je de autorisatiebron kunt
/// vervangen is een achterdeur — dus dit is de enige plek buiten het portaal waar hij te bereiken is.
/// </para>
/// </remarks>
public class KlantenlijstMomentopnameTests
{
    // ── De momentopname uit de configuratie ─────────────────────────────────────────────────────

    [Fact]
    public void VoorDatErIetsIsGelezenStaanDeKlantenUitDeConfiguratieErAl()
    {
        // Het portaal kent zijn klanten dus vóórdat er één query is gelopen. Zou deze lijst pas na
        // een lezing bestaan, dan hangt het aanmelden aan de bereikbaarheid van Cosmos.
        var lijst = Klantenlijst(Klant("bakker", "Bakker Logistiek"), Klant("vandijk", "Van Dijk"));

        Assert.Equal(["bakker", "vandijk"], lijst.All.Select(k => k.Id));
        Assert.False(lijst.LoadedFromStore);
    }

    [Fact]
    public void EenKlantZonderIdWordtOvergeslagenEnLaatDeRestStaan()
    {
        // Eén verkeerd geplakte regel in de app-instellingen mag het portaal niet omleggen.
        var lijst = Klantenlijst(Klant(string.Empty, "Zonder id"), Klant("bakker", "Bakker Logistiek"));

        Assert.Equal(["bakker"], lijst.All.Select(k => k.Id));
    }

    [Fact]
    public void EenTweedeKlantMetDezelfdeSlugWordtGenegeerd()
    {
        // Twee records met dezelfde slug zouden twee klanten met één URL zijn. De eerste wint, en
        // dat is de enige keuze die niet van de leesvolgorde van een dictionary afhangt.
        var lijst = Klantenlijst(Klant("bakker", "Bakker Logistiek"), Klant("BAKKER", "Iets anders"));

        Assert.Equal(["bakker"], lijst.All.Select(k => k.Id));
        Assert.Equal("Bakker Logistiek", lijst.Find("bakker")!.Name);
    }

    [Fact]
    public void EenKlantZonderNaamHeetNaarZijnSlug()
    {
        var lijst = Klantenlijst(Klant("bakker", name: null));

        Assert.Equal("bakker", lijst.Find("bakker")!.Name);
    }

    // ── De omschakeling naar de opslag ──────────────────────────────────────────────────────────

    [Fact]
    public void NaHetLezenVanDeOpslagIsDatDeLijst()
    {
        var lijst = Klantenlijst(Klant("bakker", "Bakker Logistiek"));

        lijst.Replace(
            [Document("meijer", "Meijer Advocaten")],
            [Toegang("meijer", "partner@meijer.nl", PortalAccessRoles.Administrator)]);

        Assert.True(lijst.LoadedFromStore);
        Assert.Equal(["meijer"], lijst.All.Select(k => k.Id));
        Assert.Equal("partner@meijer.nl", lijst.Find("meijer")!.Access.Single().Email);
        Assert.Equal(PortalAccessRoles.Administrator, lijst.Find("meijer")!.Access.Single().Role);
    }

    [Fact]
    public void DeConfiguratielijstWordtVervangenEnNietSamengevoegd()
    {
        // Zouden de twee worden samengevoegd, dan komt een klant die iemand bewust heeft verwijderd
        // bij elke herstart terug — en dan is de configuratie een stille tweede bron gebleven.
        var lijst = Klantenlijst(Klant("bakker", "Bakker Logistiek"), Klant("kroon", "Kroon Techniek"));

        lijst.Replace([Document("bakker", "Bakker Logistiek")], []);

        Assert.Null(lijst.Find("kroon"));
        Assert.Equal(["bakker"], lijst.All.Select(k => k.Id));
    }

    [Fact]
    public void EenLegeLezingLaatDeVorigeLijstStaan()
    {
        // Dit is de buitensluiting waar het hele ontwerp om draait: "er is geen moment waarop het
        // portaal geen klanten kent". Een lezing die niets oplevert is niet hetzelfde als een lijst
        // zonder klanten. Het overkomt je bij een verse container waarin de migratie nog niet heeft
        // gelopen of uit staat (PortalData:Bootstrap = false), en de uitkomst is dat niemand meer
        // kan aanmelden — inclusief de operator die het zou moeten repareren.
        var lijst = Klantenlijst(Klant("bakker", "Bakker Logistiek"));

        lijst.Replace([], []);

        Assert.NotEmpty(lijst.All);
        Assert.NotNull(lijst.Find("bakker"));
    }

    [Fact]
    public void EenKlantdocumentZonderSlugWordtOvergeslagen()
    {
        var lijst = Klantenlijst(Klant("bakker", "Bakker Logistiek"));

        lijst.Replace([Document(string.Empty, "Zonder slug"), Document("meijer", "Meijer")], []);

        Assert.Equal(["meijer"], lijst.All.Select(k => k.Id));
    }

    [Fact]
    public void ToegangUitDeOpslagBepaaltWieErBijMag()
    {
        var lijst = Klantenlijst(Klant("bakker", "Bakker Logistiek"));

        lijst.Replace(
            [Document("bakker", "Bakker Logistiek"), Document("meijer", "Meijer Advocaten")],
            [
                Toegang("bakker", "planning@bakkerlogistiek.nl"),
                Toegang("meijer", "partner@meijer.nl"),
            ]);

        var eigen = lijst.ForUser(Testprincipals.Klant("planning@bakkerlogistiek.nl"));

        Assert.Equal(["bakker"], eigen.Select(k => k.Id));
    }

    [Fact]
    public void EenIngetrokkenToegangIsNaHerladenWeg()
    {
        // De aanwezigheid van het document is het recht. Verdwijnt het document, dan hoort de
        // gebruiker bij de volgende momentopname niets meer te kunnen.
        var lijst = Klantenlijst(Klant("bakker", "Bakker Logistiek"));

        lijst.Replace([Document("bakker", "Bakker")], [Toegang("bakker", "planning@bakkerlogistiek.nl")]);
        Assert.NotEmpty(lijst.ForUser(Testprincipals.Klant("planning@bakkerlogistiek.nl")));

        lijst.Replace([Document("bakker", "Bakker")], []);
        Assert.Empty(lijst.ForUser(Testprincipals.Klant("planning@bakkerlogistiek.nl")));
    }

    [Fact]
    public void EenToegangZonderEmailWordtOvergeslagen()
    {
        var lijst = Klantenlijst(Klant("bakker", "Bakker Logistiek"));

        lijst.Replace([Document("bakker", "Bakker")], [Toegang("bakker", string.Empty)]);

        Assert.Empty(lijst.Find("bakker")!.Access);
    }

    // ── De opslaglocatie van de telemetrie ──────────────────────────────────────────────────────

    [Fact]
    public void EenKlantUitDeOpslagKrijgtDeStandaardEndpointAlsHijGeenEigenHeeft()
    {
        var lijst = Klantenlijst(Klant("bakker", "Bakker Logistiek"));

        lijst.Replace([Document("meijer", "Meijer Advocaten")], []);

        Assert.NotNull(lijst.Find("meijer")!.Telemetry);
    }

    [Fact]
    public void EenKlantZonderEnigeEndpointBlijftInDeLijstStaanZonderOpslag()
    {
        // Niet te lezen, maar niet verdwenen: het overzicht toont hem als "status onbekend". Dat is
        // eerlijker dan een klant die stilletjes wegvalt.
        var lijst = Klantenlijst(standaardEndpoint: null, Klant("bakker", "Bakker Logistiek"));

        lijst.Replace([Document("meijer", "Meijer Advocaten")], []);

        Assert.NotNull(lijst.Find("meijer"));
        Assert.Null(lijst.Find("meijer")!.Telemetry);
    }

    [Fact]
    public void EenEigenEndpointUitDeOpslagGaatVoorDeStandaard()
    {
        var lijst = Klantenlijst(Klant("bakker", "Bakker Logistiek"));

        lijst.Replace(
            [Document("meijer", "Meijer Advocaten") with
            {
                TelemetryEndpoint = "https://cosmos-meijer.documents.azure.com:443/",
                TelemetryDatabase = "telemetry",
            }],
            []);

        Assert.Equal(
            "https://cosmos-meijer.documents.azure.com:443/",
            lijst.Find("meijer")!.Telemetry!.AccountEndpoint);
    }

    // ── Hulpmiddelen ────────────────────────────────────────────────────────────────────────────

    private static CustomerDirectory Klantenlijst(params CustomerRecord[] klanten) =>
        Klantenlijst(Autorisatiebron.StandaardEndpoint, klanten);

    private static CustomerDirectory Klantenlijst(
        string? standaardEndpoint,
        params CustomerRecord[] klanten) =>
        new(
            Options.Create(new PortalCustomerOptions { Customers = [.. klanten] }),
            Options.Create(new PortalTelemetryOptions
            {
                AccountEndpoint = standaardEndpoint,
                Database = "telemetry",
            }));

    private static CustomerRecord Klant(string id, string? name) => new()
    {
        Id = id,
        Name = name ?? string.Empty,
    };

    private static CustomerDocument Document(string slug, string name) => new()
    {
        Id = PortalDocumentIds.Customer,
        PartitionKey = slug,
        CustomerId = slug,
        Name = name,
    };

    private static AccessDocument Toegang(
        string slug,
        string email,
        string role = PortalAccessRoles.Reader) => new()
    {
        Id = PortalDocumentIds.Access(email),
        PartitionKey = slug,
        CustomerId = slug,
        Email = email,
        Role = role,
    };
}
