using Soratus.Portal.Sprints;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De controle op het DevOps-bord van een klant (§3.4).
/// </summary>
/// <remarks>
/// <para><strong>Twee kanten, en dat is een eis en geen dubbele controle.</strong>
/// <see cref="DevOpsScope.Validate"/> wordt door de schrijfkant gebruikt en
/// <see cref="DevOpsScope.TryParse"/> door de collector en het scherm. Zouden ze uiteenlopen, dan staat er
/// "wordt opgehaald" bij een klant die niet wordt opgehaald, of weigert het formulier een bord waar de
/// collector prima mee uit de voeten kan. Dat is gat 1 uit punt 41 — daar met een mutatie gevonden en niet
/// met een test — en daarom staan hier tests op beide kanten, in beide richtingen.</para>
///
/// <para>Puur, dus zonder klok, zonder opslag en zonder HTTP. Dat is de reden dat deze regels te meten
/// zijn: er is niets in de weg tussen de invoer en het oordeel.</para>
/// </remarks>
public class DevopsbordTests
{
    /// <summary>Het bord van de eerste echte klant, in de exacte vorm.</summary>
    private const string Echt = "soratus/MBVApp4 MAUI/MBVApp4 MAUI Team";

    [Fact]
    public void HetGemetenBordVanMbvIsGeldig()
    {
        // De vorm die op 22 augustus 2026 werkelijk is gemeten: organisatie soratus, project
        // "MBVApp4 MAUI", team "MBVApp4 MAUI Team". Met spaties in twee van de drie segmenten, en dat is
        // precies het geval waarop een te strenge controle stukloopt.
        Assert.Null(DevOpsScope.Validate(Echt));
        Assert.True(DevOpsScope.TryParse(Echt, out var bord));
        Assert.NotNull(bord);
        Assert.Equal("soratus", bord.Organization);
        Assert.Equal("MBVApp4 MAUI", bord.Project);
        Assert.Equal("MBVApp4 MAUI Team", bord.Team);
    }

    [Fact]
    public void HetPadIsDeTekenreeksDieDeDeurUitGaat()
    {
        // De drie voorvoegsels komen uit één ontleding en niet uit drie. Dit is de eigenschap die de
        // tweede waarheid uitsluit: het scherm toont Path en de client neemt er prefixen van, dus ze
        // kunnen niet uiteenlopen zonder dat deze drie regels uiteenlopen.
        Assert.True(DevOpsScope.TryParse(Echt, out var bord));

        Assert.Equal("/soratus", bord!.OrganizationPath);
        Assert.Equal("/soratus/MBVApp4 MAUI", bord.ProjectPath);
        Assert.Equal("/soratus/MBVApp4 MAUI/MBVApp4 MAUI Team", bord.Path);
        Assert.StartsWith(bord.OrganizationPath, bord.ProjectPath, StringComparison.Ordinal);
        Assert.StartsWith(bord.ProjectPath, bord.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void ToStringIsHetPadEnNietDeRecordvorm()
    {
        // Een scope die in een logregel of in een melding terechtkomt hoort dezelfde tekenreeks te zijn
        // als die naar de API gaat. De standaard-ToString van een record zou hier
        // "DevOpsScope { Organization = …, Project = … }" neerzetten, en dan staat er in het log iets wat
        // niet is bevraagd. Dezelfde regel als bij AzureScope.
        Assert.True(DevOpsScope.TryParse(Echt, out var bord));

        Assert.Equal(bord!.Path, bord.ToString());
        Assert.DoesNotContain("Organization", bord.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LeegIsToegestaanEnBetekentNietIngericht(string? invoer)
    {
        // Punt 15 op de plek waar hij de sprintweergave raakt. Een klant zonder bord is een geldige
        // toestand: hij wordt niet bevraagd, er komt geen document, en het scherm zegt dát er niets is
        // ingericht in plaats van een leeg sprintoverzicht te tonen dat op "geen werk" lijkt.
        //
        // De twee kanten geven hier bewust een ánder antwoord: Validate zegt "niets aan de hand" en
        // TryParse zegt "er is geen bord". Dat is geen tegenspraak maar het onderscheid dat de aanroeper
        // nodig heeft — het formulier mag leeg accepteren, de collector heeft niets te bevragen.
        Assert.Null(DevOpsScope.Validate(invoer));
        Assert.False(DevOpsScope.TryParse(invoer, out var bord));
        Assert.Null(bord);
    }

    [Theory]
    [InlineData("soratus")]
    [InlineData("soratus/MBVApp4 MAUI")]
    [InlineData("soratus/MBVApp4 MAUI/MBVApp4 MAUI Team/extra")]
    [InlineData("soratus//MBVApp4 MAUI Team")]
    [InlineData("/soratus/MBVApp4 MAUI/")]
    public void EenVormDieGeenDrieSegmentenIsWordtGeweigerd(string invoer)
    {
        // Twee segmenten zijn niet genoeg en vier zijn te veel. Het team hoort erbij omdat een sprint een
        // teambegrip is: iteraties worden aan een team toegewezen en @currentIteration is een
        // teaminstelling. Zonder team is er geen sprint om te tonen.
        //
        // De laatste twee gevallen zijn de interessante: een lege middenterm en een afsluitende schuine
        // streep leveren na het trimmen twee segmenten op, en dat hoort net zo hard te vallen als één.
        Assert.NotNull(DevOpsScope.Validate(invoer));
        Assert.False(DevOpsScope.TryParse(invoer, out _));
    }

    [Fact]
    public void EenGeplakteUrlMetSchuineStrepenEromheenWordtGelezen()
    {
        // De invoerweg is overtypen of kopiëren uit de adresregel, en dan komt er een schuine streep mee.
        // Die weghalen is geen normalisatie van de námen — die blijven van de operator — maar van de
        // scheidingstekens, en dat scheelt een melding waar een mens niets aan heeft.
        Assert.True(DevOpsScope.TryParse("/soratus/MBVApp4 MAUI/MBVApp4 MAUI Team/", out var bord));
        Assert.Equal(Echt, bord!.Path.TrimStart('/'));
    }

    [Fact]
    public void WitruimteRondEenSegmentWordtWeggehaaldEnDeSchrijfwijzeBlijft()
    {
        // "soratus / MBVApp4 MAUI / …" is wat een mens typt, en een naam met een spatie ervoor bestaat
        // niet. Maar de hoofdletters blijven: deze tekenreeks komt op het operatorscherm terug als
        // "bevraagd: …", en daar hoort te staan wat er is ingevuld en niet wat wij ervan hebben gemaakt.
        // Dezelfde keuze en dezelfde reden als bij de resourcegroepnaam in AzureScope.
        Assert.True(
            DevOpsScope.TryParse(" soratus / MBVApp4 MAUI / MBVApp4 MAUI Team ", out var bord));

        Assert.Equal("MBVApp4 MAUI", bord!.Project);
        Assert.Equal("MBVApp4 MAUI Team", bord.Team);
    }

    [Theory]
    [InlineData("SORATUS/Project/Team", "SORATUS")]
    [InlineData("soratus/PROJECT/TEAM", "soratus")]
    public void DeSchrijfwijzeWordtNietGenormaliseerd(string invoer, string organisatie)
    {
        // Er is hier niets vast te normaliseren, en dat is het verschil met AzureScope. Daar zijn
        // /subscriptions/ en /resourceGroups/ van Azure en niet van de operator, dus die worden
        // genormaliseerd en dan zijn twee scopes te vergelijken. Deze scope bestaat uitsluitend uit namen
        // die van de klant zijn, dus er valt niets te normaliseren zonder de invoer te veranderen.
        Assert.True(DevOpsScope.TryParse(invoer, out var bord));
        Assert.Equal(organisatie, bord!.Organization);
    }

    [Theory]
    [InlineData("sor_atus/Project/Team")]
    [InlineData("-soratus/Project/Team")]
    [InlineData("soratus-/Project/Team")]
    [InlineData("sor atus/Project/Team")]
    public void EenOrganisatienaamVolgtDeRegelsVanDevOps(string invoer)
    {
        // Strenger dan een project- of teamnaam, en dat komt uit de documentatie: een organisatienaam
        // bestaat uit letters, cijfers en koppelstreepjes en begint en eindigt op een letter of cijfer.
        // Hij is een deel van het adres en geen naam die een mens per project verzint.
        var melding = DevOpsScope.Validate(invoer);

        Assert.NotNull(melding);
        Assert.Contains("organisatie", melding, StringComparison.OrdinalIgnoreCase);
        Assert.False(DevOpsScope.TryParse(invoer, out _));
    }

    [Theory]
    [InlineData("soratus/Pro?ject/Team", "project")]
    [InlineData("soratus/Project/Te#am", "team")]
    [InlineData("soratus/Pro%ject/Team", "project")]
    [InlineData("soratus/Project/Te&am", "team")]
    public void EenTekenDatEenUrlZouBrekenWordtGeweigerdEnDeMeldingNoemtHetSegment(
        string invoer,
        string soort)
    {
        // Dit is waar deze controle strenger is dan zijn tweelingbroer, en het volgt uit de vorm: deze
        // scope wordt als tekst in een URL gezet. Een naam met een ? erin zou de rest van het pad in een
        // querystring veranderen, en dan is het bevraagde adres een ánder adres dan wat er op het scherm
        // staat als "bevraagd: …". Dat is precies het gat dat de scope zou moeten dichten.
        var melding = DevOpsScope.Validate(invoer);

        Assert.NotNull(melding);
        Assert.Contains(soort, melding, StringComparison.OrdinalIgnoreCase);
        Assert.False(DevOpsScope.TryParse(invoer, out _));
    }

    [Fact]
    public void EenProjectnaamMetEenAccentIsGeldig()
    {
        // Unicodeletters zijn toegestaan: een project met een accent in de naam bestaat. Er wordt dus niet
        // op ASCII gecontroleerd maar op wat er verboden is — dezelfde keuze als bij een
        // resourcegroepnaam, waar "café" geldig is. Een controle die alleen ASCII toelaat, weigert een
        // bord dat werkt.
        Assert.Null(DevOpsScope.Validate("soratus/Café Systemen/Café Team"));
        Assert.True(DevOpsScope.TryParse("soratus/Café Systemen/Café Team", out var bord));
        Assert.Equal("Café Systemen", bord!.Project);
    }

    [Theory]
    [InlineData("soratus/_Project/Team")]
    [InlineData("soratus/Project./Team")]
    [InlineData("soratus/Project/.Team")]
    public void EenNaamMetEenVerbodenBeginOfEindeWordtGeweigerd(string invoer)
    {
        // Uit de naamregels van Azure DevOps: niet beginnen met een onderstrepingsteken, niet beginnen of
        // eindigen op een punt. Een naam die deze regels schendt bestaat niet, dus een bord dat hem noemt
        // kan alleen een 404 opleveren — en dan is "niet ingericht" niet meer van "verkeerd ingericht" te
        // onderscheiden.
        Assert.NotNull(DevOpsScope.Validate(invoer));
        Assert.False(DevOpsScope.TryParse(invoer, out _));
    }

    [Fact]
    public void EenTeLangeNaamWordtGeweigerdMetDeGrensInDeMelding()
    {
        var telang = "soratus/" + new string('p', DevOpsScope.MaximumNameLength + 1) + "/Team";
        var melding = DevOpsScope.Validate(telang);

        Assert.NotNull(melding);
        Assert.Contains(
            DevOpsScope.MaximumNameLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            melding,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EenGeplakteLapTekstKomtNietHeelInDeMelding()
    {
        // Deze grens is er om de meldingen bruikbaar te houden. Zonder hem zou een per ongeluk geplakte
        // lap tekst als geheel in een foutmelding op het scherm belanden — dezelfde reden als bij
        // AzureScope.MaximumScopeLength.
        var lap = new string('x', DevOpsScope.MaximumScopeLength + 200);
        var melding = DevOpsScope.Validate(lap);

        Assert.NotNull(melding);
        Assert.DoesNotContain(lap, melding, StringComparison.Ordinal);
    }

    [Fact]
    public void EenWeergavetekstInHetVerkeerdeVeldWordtGeweigerd()
    {
        // Het geval waar dit type voor bestaat: iemand plakt de omgevingstekst in het bordveld. Die hoort
        // niet in de opslag te komen, want wat er in een document staat wordt door de collector bevraagd.
        Assert.NotNull(DevOpsScope.Validate("sub-soratus-acme · rg-acme-prod"));
        Assert.False(DevOpsScope.TryParse("sub-soratus-acme · rg-acme-prod", out _));
    }

    [Fact]
    public void EenAzureScopeInHetBordveldWordtGeweigerd()
    {
        // De twee velden staan naast elkaar op hetzelfde formulier en hebben beide een pad-achtige vorm.
        // Ze verwisselen is dus een echte fout, en hij hoort te vallen: een ARM-pad heeft vier segmenten
        // en een bord drie, en /subscriptions/ is geen organisatienaam.
        const string arm =
            "/subscriptions/501a66d2-de54-4d4f-9f7c-1fbb55bec17f/resourceGroups/MBV";

        Assert.NotNull(DevOpsScope.Validate(arm));
        Assert.False(DevOpsScope.TryParse(arm, out _));
    }

    [Theory]
    [InlineData(Echt)]
    [InlineData("soratus/Project/Team")]
    [InlineData("a/b/c")]
    [InlineData("soratus/Pro?ject/Team")]
    [InlineData("soratus/MBVApp4 MAUI")]
    [InlineData("sub-soratus-acme · rg-acme-prod")]
    [InlineData("")]
    [InlineData(null)]
    public void DeControleEnDeOntledingZijnHetOverElkeInvoerEens(string? invoer)
    {
        // Gat 1 van punt 41, hier in beide richtingen vastgezet. Validate en TryParse worden door
        // verschillende kanten gebruikt — de formulieren valideren, de collector en het scherm ontleden —
        // en zouden ze uiteenlopen, dan is er een pad waarop het scherm een bord goedkeurt dat de
        // collector weigert. Deze test bewijst niet dat de regels goed zijn; hij bewijst dat het er één
        // stel is.
        //
        // Leeg is de uitzondering en die staat er met opzet in: daar geven ze bewust een ander antwoord,
        // en de assertie hieronder houdt daar rekening mee in plaats van het geval weg te laten. Een
        // theorie die het lastige geval overslaat is een theorie die niets zegt over het lastige geval.
        var geldig = DevOpsScope.Validate(invoer) is null;
        var ontleed = DevOpsScope.TryParse(invoer, out var bord);
        var leeg = string.IsNullOrWhiteSpace(invoer);

        Assert.Equal(geldig && !leeg, ontleed);
        Assert.Equal(ontleed, bord is not null);
    }
}
