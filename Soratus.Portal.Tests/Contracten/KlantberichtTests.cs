using Soratus.Agents.Contracts;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De klantprojectie van <c>msg</c> gebruikt de gedeelde knipregel, en houdt zich aan de invariant
/// die daarbij hoort.
/// </summary>
/// <remarks>
/// <para><strong>Wat hier met opzet níet staat.</strong> Hoe de knip werkt — welke vormen van een
/// regelovergang tellen, dat een afsluitende enter geen overloop is, dat de backstop op een
/// grafeemgrens knipt — is vastgelegd in <see cref="MessageTruncation"/> en getest aan de kant van
/// de bibliotheek. Dat hier nog eens overdoen zou een tweede plek maken die dezelfde regel
/// vastlegt, en dat is precies het patroon dat net is opgeruimd: er stonden drie kopieën van deze
/// regel, ze waren met opzet identiek opgeschreven, en ze liepen binnen één dag uiteen.</para>
///
/// <para><strong>Waar ze uiteenliepen is wél iets voor deze tests.</strong> Niet op de knipregel
/// maar op de invariant: <c>MessageTruncation.Cut</c> houdt bericht plus markering onder de 8000, de
/// kopie in de klantprojectie knipte op 8000 en plakte de markering daarná — tot 8013. Geen lek,
/// dertien tekens, en alleen bereikbaar bij een eerste regel langer dan 8000 tekens. Maar
/// <c>AssertContract</c> toetste precies die invariant en kon de twee kopieën structureel niet
/// bereiken, dus stond er een guard naast een regel die stil geschonden werd.</para>
///
/// <para>Deze tests leggen daarom drie dingen vast en niet meer: dat de projectie de gedeelde
/// functie gebruikt <em>en haar uitkomst overneemt</em>, dat de invariant over de grens ook op het
/// klantpad geldt, en de ene plek waar de projectie bewust afwijkt. Dat de knip inhoudelijk het
/// juiste doet, meet <see cref="Zichtbaarheid.KlantLogregelTests"/> op de gerenderde pagina.</para>
/// </remarks>
public class KlantberichtTests
{
    /// <summary>
    /// Berichten die de projectie en de gedeelde regel op hetzelfde antwoord moeten uitkomen.
    /// </summary>
    /// <remarks>
    /// Bewust over de hele breedte: een gewone zin, alle drie de vormen van een regelovergang, een
    /// afsluitende enter, een al geknipt bericht, een lange legitieme regel, en een eerste regel
    /// boven de bovengrens. Wat elk van die gevallen hóórt op te leveren staat hier niet — dat is de
    /// vraag aan de bibliotheek. Hier staat alleen dat de klant hetzelfde antwoord krijgt.
    /// </remarks>
    public static TheoryData<string> Berichten =>
    [
        "Factuur INV-2291 verwerkt.",
        "Factuur INV-2291 verwerkt.\n",
        "Factuur INV-2291 verwerkt.\r\n",
        "De bron antwoordde niet.\nat SoratusAgent.Mail.Run() in /src/Mail/Run.cs:line 12",
        "De bron antwoordde niet.\rat SoratusAgent.Mail.Run() in /src/Mail/Run.cs:line 12",
        "De bron antwoordde niet." + MessageTruncation.Marker,
        Testlogregels.LangeZin,
        new string('a', MessageTruncation.DefaultMaxLength + 400) + "\nframe",
    ];

    [Theory]
    [MemberData(nameof(Berichten))]
    public void DeKlantprojectieGeeftPreciesWatDeGedeeldeKnipregelGeeft(string bericht)
    {
        // Dit is de test op de delegatie en niet op de regel. Hij zegt: wat de klant leest is wat
        // MessageTruncation.Cut ervan maakt — niet iets wat er sterk op lijkt. Zodra iemand de
        // projectie weer eigen logica geeft, ook als die op dat moment hetzelfde doet, gaat deze
        // test rood zodra de twee uit elkaar lopen. Dat is precies wat er is gebeurd en wat niemand
        // een dag lang zag.
        Assert.Equal(MessageTruncation.Cut(bericht).Message, CustomerMessage.FirstLine(bericht));
    }

    [Fact]
    public void EenGekniptKlantberichtBlijftMetMarkeringBinnenDeBovengrens()
    {
        // Dít is het geval dat een dag lang stil geschonden werd: de kop op 8000 knippen en de
        // markering daarná plakken levert 8013 op. De markering hoort van het budget af te gaan en
        // er niet bovenop te komen, anders overschrijdt juist het bericht dat op de grens wordt
        // geknipt de grens waarvoor de grens er is.
        var bericht = new string('a', MessageTruncation.DefaultMaxLength + 500) + "\nen nog een regel";

        var geknipt = CustomerMessage.FirstLine(bericht);

        // Eerst vaststellen dát er is geknipt. Zonder deze regel zou de test kunnen slagen doordat
        // er niets gebeurde in plaats van doordat de grens klopt.
        Assert.EndsWith(MessageTruncation.Marker, geknipt, StringComparison.Ordinal);

        Assert.True(
            geknipt.Length <= MessageTruncation.DefaultMaxLength,
            $"Een geknipt klantbericht is {geknipt.Length} tekens en de grens is " +
            $"{MessageTruncation.DefaultMaxLength}. Dat is de fout waarmee de kopie in " +
            "CustomerMessage uiteenliep met MessageTruncation.Cut: knippen op de grens en de " +
            "markering erná plakken. Ga niet in de projectie rekenen — laat het aan " +
            "MessageTruncation.Cut, dat de markering van het budget afhaalt.");
    }

    [Fact]
    public void DeProjectieRekentZelfNietMeerAanDeGrens()
    {
        // De invariant hierboven zegt dat de uitkomst klopt; deze zegt dat hij om de juiste reden
        // klopt. Zou de projectie zelf gaan rekenen en toevallig hetzelfde getal halen, dan is de
        // volgende afwijking weer onzichtbaar tot iemand hem meet.
        var lang = new string('b', MessageTruncation.DefaultMaxLength * 2) + "\nframe";

        Assert.Equal(MessageTruncation.Cut(lang).Message, CustomerMessage.FirstLine(lang));
        Assert.Equal(
            MessageTruncation.Cut(lang, MessageTruncation.DefaultMaxLength).Message,
            CustomerMessage.FirstLine(lang));
    }

    [Fact]
    public void EenLeegKlantberichtBlijftLeegEnWordtNietVerkleedAlsGeldigDocument()
    {
        // De ene plek waar de projectie bewust afwijkt van de gedeelde regel, en dus de ene plek
        // waar hier iets over te zeggen valt.
        //
        // Bij het wegschrijven is "(geen bericht)" een correctie op de bron: dan staat er in het
        // document dat de agent niets meegaf, in plaats van dat het veld ontbreekt. Bij het lezen
        // van een document dat er al staat zou diezelfde tekst een document dat niet aan het
        // contract voldoet verkleden als een document dat dat wel doet — en dan is op het scherm
        // niet meer te zien dat er iets mis is met de bron.
        Assert.Equal("(geen bericht)", MessageTruncation.Cut(string.Empty).Message);

        Assert.Equal(string.Empty, CustomerMessage.FirstLine(string.Empty));
    }

    [Fact]
    public void DeProjectieLaatGeenStacktraceDoorEnHoudtDeEersteZinHeel()
    {
        // Waar het allemaal om begon, op het niveau van de projectie in plaats van de pagina. Beide
        // kanten in één test, want ze zijn los van elkaar makkelijk te halen: alles weggooien laat
        // ook geen stacktrace door, en niets doen houdt ook de eerste zin heel.
        var geknipt = CustomerMessage.FirstLine(Testlogregels.BerichtMetStacktrace().Message);

        Assert.StartsWith(
            "De voorraadregel kon niet worden weggeschreven.",
            geknipt,
            StringComparison.Ordinal);
        Assert.DoesNotContain(Testlogregels.Bronpad, geknipt, StringComparison.Ordinal);
        Assert.DoesNotContain(Testlogregels.Stacktrace, geknipt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1417)]
    [InlineData(1615)]
    public void EenLangeLegitiemeRegelKomtOngewijzigdDoorDeProjectie(int lengte)
    {
        // 1417 is de langste legitieme eerste regel die over de klantzichtbare logregels is gemeten;
        // 1615 is de echte validation.summary van bakker-voorraad-sync. Beide zijn één doorlopende
        // zin, dus er is niets om op te knippen, en beide moeten ongewijzigd doorkomen. Dit is de
        // rem op "voor de zekerheid ook op lengte afkappen": een grens in het middengebied verminkt
        // deze berichten en laat een stacktrace nog deels door.
        var zin = Testlogregels.LangeZin[..Math.Min(lengte, Testlogregels.LangeZin.Length)]
            + new string('c', Math.Max(0, lengte - Testlogregels.LangeZin.Length));

        var geknipt = CustomerMessage.FirstLine(zin);

        Assert.Equal(lengte, geknipt.Length);
        Assert.Equal(zin, geknipt);
        Assert.DoesNotContain(MessageTruncation.Marker, geknipt, StringComparison.Ordinal);
    }
}
