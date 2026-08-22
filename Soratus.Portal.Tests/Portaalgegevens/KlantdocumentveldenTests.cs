using System.Reflection;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Portaalgegevens;

/// <summary>
/// Dat elk bewerkbaar veld van het klantdocument door élk schrijfpad wordt gezet.
/// </summary>
/// <remarks>
/// <para><strong>Deze tests bestaan niet voor een veld maar voor het vólgende veld.</strong> Punt 41
/// noteert als gat 4 dat het weghalen van <c>AzureScope</c> uit de bewerking die het contractscherm
/// verstuurt <em>niets rood maakte</em>. Het gevolg is stil en het is precies de vorm die dit portaal al
/// een keer heeft platgelegd: <see cref="IPortalDataStore.SaveCustomerAsync"/> vervangt het hele
/// klantdocument, dus een veld dat de bewerking niet draagt wordt bij het eerste bewaren leeggemaakt — en
/// dan zet een operator die de klantnaam verbetert de kostenmeting of de sprintweergave van die klant uit,
/// waarna het scherm netjes meldt dat er "niets is ingericht". Dat is een storing die zich voordoet als
/// werkende functionaliteit, met een tekst die iemand heeft geschreven om waar te zijn.</para>
///
/// <para><strong>Er zijn drie schrijfpaden en niet twee, en dat is nagemeten.</strong>
/// <see cref="Schrijfpaden"/> zoekt élke <c>new CustomerDocument</c> in de productiecode; op 22 augustus
/// 2026 waren dat er drie: het aanmaken (<c>CreateCustomerAsync</c>), het bewaren
/// (<c>SaveCustomerAsync</c>) en de eenmalige migratie uit <c>appsettings.json</c>. Die derde is met opzet
/// anders en <see cref="DeMigratieVerzintGeenKoppelingen"/> legt vast waarom: een klant uit de
/// configuratie heeft geen scope en geen bord, en er een verzinnen is precies de fout waartegen die twee
/// velden bestaan.</para>
///
/// <para><strong>Waarom een broncodetest en niet een rondgang door de echte mapping.</strong> Dat laatste
/// zou sterker zijn, en het kan vandaag niet: de mapping staat als objectinitialisatie ín
/// <c>CreateCustomerAsync</c> en <c>SaveCustomerAsync</c>, en die twee praten met Cosmos. Punt 41 stelt
/// voor haar eruit te halen zoals <c>ToDocument</c> dat elders doet; dat is de betere oplossing en het is
/// een wijziging in een bestand met meer schrijvers. Zolang die er niet is, is de tekst van de
/// initialisatie het enige wat te lezen valt — en een test die de tekst leest, vangt het gat wél. Wat hij
/// niet kan zien is een veld dat aan de verkeerde bron wordt toegewezen
/// (<c>DevOpsScope = Clean(edit.AzureScope)</c>); dat is de grens van deze vorm en hij staat hier
/// opgeschreven in plaats van dat iemand hem later ontdekt.</para>
/// </remarks>
public class KlantdocumentveldenTests
{
    /// <summary>
    /// De velden van het klantdocument die geen gegeven van een operator zijn maar documentmechaniek.
    /// </summary>
    /// <remarks>
    /// <para>Deze negen horen niet op een formulier en niet in een bewerking: de eerste vier zijn de
    /// sleutels en de soort, en de laatste vijf zijn het spoor dat de opslag zelf zet. Ze staan hier als
    /// lijst en niet als naamconventie, want een naamconventie die je moet raden is een lijst die niemand
    /// heeft opgeschreven.</para>
    /// </remarks>
    private static readonly string[] Mechaniek =
    [
        nameof(CustomerDocument.Id),
        nameof(CustomerDocument.PartitionKey),
        nameof(CustomerDocument.Kind),
        nameof(CustomerDocument.CustomerId),
        nameof(CustomerDocument.CreatedAt),
        nameof(CustomerDocument.CreatedBy),
        nameof(CustomerDocument.ChangedAt),
        nameof(CustomerDocument.ChangedBy),
        nameof(CustomerDocument.ETag),
    ];

    /// <summary>De velden die een operator vastlegt en die dus door elk schrijfpad moeten komen.</summary>
    private static string[] Bewerkbaar() =>
    [
        .. typeof(CustomerDocument)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(eigenschap => eigenschap.Name)
            .Except(Mechaniek, StringComparer.Ordinal)
            .OrderBy(naam => naam, StringComparer.Ordinal),
    ];

    /// <summary>De bewerkbare velden als theoriegegevens.</summary>
    public static TheoryData<string> Velden
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var veld in Bewerkbaar())
            {
                data.Add(veld);
            }

            return data;
        }
    }

    [Fact]
    public void DeBewerkbareVeldenVanHetKlantdocumentStaanVast()
    {
        // Zonder deze lijst kunnen de theorieën hieronder stil leeg raken: gaat Mechaniek ooit een veld
        // te veel bevatten, dan meten ze niets meer terwijl ze groen blijven. Dat is exact hoe een
        // theorie die per veld besluit of hij iets meet alles kan overslaan en toch groen zijn —
        // dezelfde constructie als de vastgelegde paginalijsten in Zichtbaarheid.
        //
        // En het is meer dan een vangnet voor de test zelf: een veld toevoegen aan het klantdocument is
        // een beslissing die door vier lagen heen moet, en die hoort iemand bewust te nemen in plaats
        // van hem stilzwijgend mee te laten liften.
        Assert.Equal(
            [
                "AzureScope",
                "DevOpsScope",
                "Environment",
                "EnvironmentDetail",
                "IsInternal",
                "Name",
                "TelemetryDatabase",
                "TelemetryEndpoint",
            ],
            Bewerkbaar());
    }

    [Theory]
    [MemberData(nameof(Velden))]
    public void ElkBewerkbaarVeldStaatOpDeBewerkingEnOpHetAanmaakverzoek(string veld)
    {
        // De reflectiehelft, en hij vangt het goedkoopste geval: een veld op het document dat de
        // bewerking niet draagt. Dan is het bij het eerste bewaren weg, en er is niets dat het merkt —
        // de opslag schrijft braaf een null waar het formulier niets kon leveren.
        Assert.NotNull(typeof(CustomerEdit).GetProperty(veld));
        Assert.NotNull(typeof(NewCustomerRequest).GetProperty(veld));
    }

    [Theory]
    [MemberData(nameof(Velden))]
    public void ElkBewerkbaarVeldWordtDoorHetAanmakenEnHetBewarenGezet(string veld)
    {
        var paden = Schrijfpaden();

        Assert.True(
            paden.Aanmaken.Contains($"{veld} =", StringComparison.Ordinal),
            $"Het aanmaken van een klant zet '{veld}' niet.\n\n" +
            "CreateCustomerAsync bouwt het klantdocument in één objectinitialisatie. Een veld dat daar " +
            "niet staat wordt bij het aanmaken nooit vastgelegd, en dan vult een operator hem in op het " +
            "aanmaakformulier zonder dat er iets wordt bewaard.\n\n" +
            "De initialisatie die is gelezen:\n" + paden.Aanmaken);

        Assert.True(
            paden.Bewaren.Contains($"{veld} =", StringComparison.Ordinal),
            $"Het bewaren van een klant zet '{veld}' niet.\n\n" +
            "SaveCustomerAsync vervangt het héle klantdocument, dus een veld dat daar niet staat wordt " +
            "bij het eerste bewaren leeggemaakt. Het gevolg is stil: een operator die de klantnaam " +
            "verbetert zet daarmee de kostenmeting of de sprintweergave van die klant uit, en het scherm " +
            "meldt netjes dat er niets is ingericht. Dat is gat 4 van punt 41.\n\n" +
            "De initialisatie die is gelezen:\n" + paden.Bewaren);
    }

    [Fact]
    public void DeMigratieVerzintGeenKoppelingen()
    {
        // Het derde schrijfpad, en het is met opzet anders. De eenmalige migratie uit appsettings.json
        // bouwt een klantdocument uit een CustomerRecord, en dat type heeft geen Azure-scope en geen
        // DevOps-bord. Er een verzinnen — of uit envFull raden — is precies de fout waartegen die twee
        // velden bestaan: een tikfout in een resourcegroepnaam levert bij Cost Management een geslaagd
        // leeg antwoord op, en dat wordt € 0,00 op een factuur.
        //
        // Deze test staat er zodat het verschil een besluit blijft. Zet iemand die velden daar wél, dan
        // is dat een beslissing over het raden van meetscopes en die hoort rood te worden.
        var migratie = Schrijfpaden().Migratie;

        Assert.DoesNotContain(nameof(CustomerDocument.AzureScope), migratie, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(CustomerDocument.DevOpsScope), migratie, StringComparison.Ordinal);
    }

    [Fact]
    public void ErZijnPreciesDrieSchrijfpadenNaarEenKlantdocument()
    {
        // De onmisbare tegenhanger: de drie tests hierboven kijken naar drie blokken, en dat is alleen
        // iets waard als er niet stil een vierde bij komt. Een vierde schrijver zou een eigen mapping
        // hebben met zijn eigen vergeten veld, en geen van de tests hierboven zou hem zien.
        var bron = Bestand();
        var aantal = Voorkomens(bron, "new CustomerDocument").Count;

        Assert.True(
            aantal == 3,
            $"Er staan {aantal} plekken in CosmosPortalDataStore.cs die een CustomerDocument bouwen, " +
            "en deze tests kennen er drie: aanmaken, bewaren en de migratie uit appsettings.json.\n\n" +
            "Komt er een vierde schrijver, dan heeft die een eigen mapping — en dus een eigen veld dat " +
            "iemand kan vergeten. Voeg hem toe aan Schrijfpaden() met de reden erbij, of leg uit waarom " +
            "hij geen bewerkbaar veld hoeft te zetten.");
    }

    [Fact]
    public void HetGereedschapLeestDeInitialisatiesEcht()
    {
        // De onmisbare tegenhanger van elke broncodetest: die kijkt of er iets in een tekst staat, en
        // dat is alleen iets waard als er tekst te lezen valt. Een leesfunctie die drie lege
        // tekenreeksen teruggeeft maakt ElkBewerkbaarVeldWordtDoorHetAanmakenEnHetBewarenGezet rood in
        // plaats van groen — dus dat geval is gedekt — maar DeMigratieVerzintGeenKoppelingen zou er
        // stilletjes groen op staan. Dat is het valse groen dat dit portaal al eerder heeft gehad.
        var paden = Schrijfpaden();

        Assert.Contains("Name =", paden.Aanmaken, StringComparison.Ordinal);
        Assert.Contains("Name =", paden.Bewaren, StringComparison.Ordinal);
        Assert.Contains("Name = record.Name", paden.Migratie, StringComparison.Ordinal);
    }

    /// <summary>De drie objectinitialisaties die een klantdocument bouwen.</summary>
    /// <param name="Aanmaken">Uit <c>CreateCustomerAsync</c>.</param>
    /// <param name="Bewaren">Uit <c>SaveCustomerAsync</c>.</param>
    /// <param name="Migratie">Uit de eenmalige migratie uit <c>appsettings.json</c>.</param>
    private readonly record struct Paden(string Aanmaken, string Bewaren, string Migratie);

    /// <summary>
    /// Leest de drie initialisaties uit de productiecode.
    /// </summary>
    /// <returns>De drie blokken.</returns>
    /// <remarks>
    /// <para>De blokken worden op accolades geteld en niet op regelnummers of op indentatie: dat eerste
    /// verschuift bij elke wijziging en dat tweede bij een <c>batch.CreateItem(new CustomerDocument</c>
    /// die één niveau dieper staat. Wie ze uit elkaar houdt is de <em>inhoud</em> — het aanmaken zet
    /// <c>CreatedBy = scope.Actor</c>, het bewaren zet <c>ChangedBy</c>, en de migratie noemt
    /// <c>record</c>.</para>
    ///
    /// <para>Er wordt op inhoud gesorteerd en niet op volgorde in het bestand, want die volgorde is geen
    /// afspraak. Zou een van de drie kenmerken verdwijnen, dan werpt deze methode met een melding die
    /// zegt wat er is gevonden — een test die op een verkeerd blok kijkt is erger dan een test die valt.
    /// </para>
    /// </remarks>
    private static Paden Schrijfpaden()
    {
        var bron = Bestand();
        var blokken = Voorkomens(bron, "new CustomerDocument");

        var aanmaken = blokken.SingleOrDefault(
            blok => blok.Contains("CreatedBy = scope.Actor,", StringComparison.Ordinal));

        var bewaren = blokken.SingleOrDefault(
            blok => blok.Contains("ChangedBy = scope.Actor,", StringComparison.Ordinal));

        var migratie = blokken.SingleOrDefault(
            blok => blok.Contains("record.Name", StringComparison.Ordinal));

        return aanmaken is null || bewaren is null || migratie is null
            ? throw new InvalidOperationException(
                "De drie schrijfpaden naar een klantdocument zijn niet alle drie te herkennen in "
                + "CosmosPortalDataStore.cs. Ze worden onderscheiden op hun inhoud — 'CreatedBy = "
                + "scope.Actor,' voor het aanmaken, 'ChangedBy = scope.Actor,' voor het bewaren en "
                + $"'record.Name' voor de migratie. Er zijn {blokken.Count} initialisatie(s) gevonden. "
                + "Is een van die kenmerken hernoemd, dan hoort deze methode mee te veranderen; laat hem "
                + "niet op een verkeerd blok kijken.")
            : new Paden(aanmaken, bewaren, migratie);
    }

    /// <summary>De broncode van de opslaglaag.</summary>
    /// <returns>De volledige tekst van het bestand.</returns>
    private static string Bestand()
    {
        var pad = Path.Combine(
            Broncode.Portaalproject.FullName,
            "Data",
            "CosmosPortalDataStore.cs");

        return File.Exists(pad)
            ? File.ReadAllText(pad)
            : throw new FileNotFoundException(
                "CosmosPortalDataStore.cs is niet gevonden. Verhuist dat bestand, dan hoort deze test "
                + "mee te verhuizen — hij bewaakt de enige plek waar een klantdocument wordt opgebouwd.",
                pad);
    }

    /// <summary>
    /// Elk blok dat op een aanduiding volgt, tot en met de sluitende accolade.
    /// </summary>
    /// <param name="bron">De broncode.</param>
    /// <param name="aanduiding">Waar een blok mee begint, bijvoorbeeld <c>new CustomerDocument</c>.</param>
    /// <returns>De blokken, in de volgorde van het bestand.</returns>
    /// <remarks>
    /// Accolades tellen vanaf de eerste <c>{</c> na de aanduiding. Dat is geen volledige parser en het
    /// hoeft het niet te zijn: een objectinitialisatie met een accolade in een tekenreeks komt hier niet
    /// voor, en zou er ooit een komen, dan valt deze methode op met een blok dat halverwege ophoudt in
    /// plaats van stil het verkeerde te lezen.
    /// </remarks>
    private static IReadOnlyList<string> Voorkomens(string bron, string aanduiding)
    {
        var blokken = new List<string>();

        for (var index = bron.IndexOf(aanduiding, StringComparison.Ordinal);
             index >= 0;
             index = bron.IndexOf(aanduiding, index + aanduiding.Length, StringComparison.Ordinal))
        {
            var start = bron.IndexOf('{', index + aanduiding.Length);

            if (start < 0)
            {
                continue;
            }

            var diepte = 0;

            for (var einde = start; einde < bron.Length; einde++)
            {
                if (bron[einde] == '{')
                {
                    diepte++;
                }
                else if (bron[einde] == '}')
                {
                    diepte--;

                    if (diepte == 0)
                    {
                        blokken.Add(bron[start..(einde + 1)]);
                        break;
                    }
                }
            }
        }

        return blokken;
    }
}
