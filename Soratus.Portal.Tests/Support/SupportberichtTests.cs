using Soratus.Agents.Contracts;
using Soratus.Portal.Support;

namespace Soratus.Portal.Tests.Support;

/// <summary>
/// Wat er in een bericht kan sluipen dat er niet in hoort, en wat daarvan is gesloten — in beide
/// richtingen.
/// </summary>
/// <remarks>
/// Punt 13 en punt 14 van de fase-0-afwijkingen gaan over deze klasse fout: tekst die door onze eigen
/// systemen is geschreven en bij een klant belandt. Een supportdraad heeft die richting én de
/// omgekeerde, en dit bestand meet ze beide.
/// </remarks>
public class SupportberichtTests
{
    [Fact]
    public void EenTweedeAlineaBlijftStaanEnWordtNietAfgeknipt()
    {
        // Hier wijkt deze map bewust af van punt 13. MessageTruncation.Cut knipt op de eerste
        // regelovergang omdat een logregel één zin hoort te zijn; een antwoord aan een klant is proza
        // met alinea's, en die knip zou de tweede alinea van een operator stil weggooien.
        //
        // Dit is de mutatiegevoelige test van dit bestand: wie Shorten door Cut vervangt, maakt hem
        // rood.
        var bericht = "Het loopt vast op een locatiecode.\n\nWe pakken het morgen op.";

        Assert.Equal(bericht, SupportBody.Clean(bericht));
        Assert.Contains("morgen", SupportBody.Clean(bericht), StringComparison.Ordinal);
    }

    [Fact]
    public void RegelovergangenWordenGenormaliseerdEnEenLegeRegelBlijftEen()
    {
        Assert.Equal("a\nb", SupportBody.Clean("a\r\nb"));
        Assert.Equal("a\nb", SupportBody.Clean("a\rb"));

        // Eén lege regel mag; een bericht dat met tweehonderd lege regels begint duwt de rest van de
        // draad uit beeld, en dat is geen opmaak maar een bijwerking.
        Assert.Equal("a\n\nb", SupportBody.Clean("a\n\n\n\n\n\nb"));
        Assert.Equal("a\n\nb", SupportBody.Clean("a\r\n\r\n\r\n\r\nb"));
    }

    [Fact]
    public void TekensDieDeLeesrichtingOmkerenOverlevenHetNiet()
    {
        // De enige regel in dit bestand die werkelijk over veiligheid gaat. Met een right-to-left
        // override (U+202E) loopt de omkering door tot het einde van het tekstblok: een klant kan er
        // de weergave van onze regels eronder mee beinvloeden, en een pad of een naam kan er anders
        // uitzien dan hij is. Dat is de klasse waar "Trojan Source" over gaat.
        //
        // De tekens staan als escape-reeks en niet als letterlijk teken, om dezelfde reden als in
        // SupportBody: een testbestand met een RTL-override erin leest zelf verkeerd in een editor en
        // in een pull request, en dan is de test die het teken vangt de test die niemand kan nakijken.
        var vuil = "Betaald \u202Etxt.exe\u202C nu";
        var schoon = SupportBody.Clean(vuil);

        Assert.DoesNotContain('\u202E', schoon);
        Assert.DoesNotContain('\u202C', schoon);
        Assert.Contains("txt.exe", schoon, StringComparison.Ordinal);

        foreach (var teken in Omkeerders)
        {
            Assert.Equal("ab", SupportBody.Clean($"a{teken}b"));
        }
    }

    /// <summary>De tekens die de leesrichting kunnen omkeren of isoleren.</summary>
    private static readonly char[] Omkeerders =
    [
        '\u061C',
        '\u200E',
        '\u200F',
        '\u202A',
        '\u202B',
        '\u202C',
        '\u202D',
        '\u202E',
        '\u2066',
        '\u2067',
        '\u2068',
        '\u2069',
    ];

    [Fact]
    public void OnzichtbareBreedtelozeTekensOverlevenHetNietEnEenEmojiWel()
    {
        // Een breedteloze ruimte doet in proza niets en kan een woordgrenscontrole omzeilen -- en dit
        // portaal heeft controles die op woordgrenzen zoeken (KlantVangnetTests).
        Assert.Equal("fiatteren", SupportBody.Clean("fiat\u200Bteren"));
        Assert.Equal("ab", SupportBody.Clean("a\uFEFFb"));

        // ZWJ blijft, want die houdt een samengestelde emoji een teken. Hij is onzichtbaar maar niet
        // misleidend: hij keert geen leesrichting om en verbergt geen woordgrens die iets betekent.
        var gezin = "\uD83D\uDC68\u200D\uD83D\uDC69\u200D\uD83D\uDC67";

        Assert.Equal(gezin, SupportBody.Clean(gezin));
    }

    [Fact]
    public void BesturingstekensVerdwijnenEnEenTabWordtEenSpatie()
    {
        // Een tab wordt een spatie en niet weggehaald: anders plakken twee woorden aan elkaar. Een tab
        // in een bubbel is opmaak, en een bubbel heeft geen opmaak om weg te geven.
        Assert.Equal("a b", SupportBody.Clean("a\u0009b"));

        // char.IsControl dekt C0 en C1, dus NEL (U+0085) valt hieronder en hoeft niet apart.
        Assert.Equal("ab", SupportBody.Clean("a\u0000b"));
        Assert.Equal("ab", SupportBody.Clean("a\u000Bb"));
        Assert.Equal("ab", SupportBody.Clean("a\u000Cb"));
        Assert.Equal("ab", SupportBody.Clean("a\u001Bb"));
        Assert.Equal("ab", SupportBody.Clean("a\u007Fb"));
        Assert.Equal("ab", SupportBody.Clean("a\u0085b"));

        // En de regelscheiders die geen \n zijn en die IndexOfAny("\u000D\u000A") niet ziet.
        Assert.Equal("ab", SupportBody.Clean("a\u2028b"));
        Assert.Equal("ab", SupportBody.Clean("a\u2029b"));
    }

    [Fact]
    public void EenTeLangBerichtWordtGeweigerdEnNietStilAfgekapt()
    {
        // Dezelfde keuze en dezelfde reden als bij HourLimits.ValidateNote: hier zit een mens aan het
        // toetsenbord, en stil afkappen zou zijn laatste alinea weggooien zonder dat hij het merkt.
        // Bij een supportbericht is dat erger dan bij een urenregel — wat eraf valt kan de vraag zelf
        // zijn.
        var teLang = new string('a', SupportLimits.MaximumLength + 1);

        Assert.NotNull(SupportBody.Validate(teLang));
        Assert.Null(SupportBody.Validate(new string('a', SupportLimits.MaximumLength)));
        Assert.Null(SupportBody.Validate("Draait de voorraad-sync?"));
        Assert.NotNull(SupportBody.Validate("   "));
        Assert.NotNull(SupportBody.Validate(null));
    }

    [Fact]
    public void DeKnipLigtOpEenGrafeemgrens()
    {
        // Aan de weergavekant is precies dit defect al aangetroffen in een message[..400]: een losse
        // surrogaat in een attribuut zodra iemand een emoji in een productnaam had. Deze knip gaat
        // via MessageTruncation.Shorten en die knipt op een grafeemgrens.
        var lang = new string('a', SupportLimits.MaximumLength - 1) + "\U0001F600" + "bbb";
        var schoon = SupportBody.Clean(lang);

        Assert.True(schoon.Length <= SupportLimits.MaximumLength);
        Assert.EndsWith(MessageTruncation.Marker, schoon, StringComparison.Ordinal);
        Assert.True(
            IsGeldigeUtf16(schoon),
            "Er staat een losse surrogaat in de geknipte tekst. Dat is geen schoonheidsfoutje: de "
            + "serializer schrijft er een vervangingsteken voor weg, en dan staat er iets anders dan "
            + "er stond — in het veld dat de klant leest.");
    }

    /// <summary>Of elke surrogaat in deze tekst deel is van een paar.</summary>
    private static bool IsGeldigeUtf16(string tekst)
    {
        for (var i = 0; i < tekst.Length; i++)
        {
            if (!char.IsSurrogate(tekst[i]))
            {
                continue;
            }

            if (i + 1 >= tekst.Length || !char.IsSurrogatePair(tekst[i], tekst[i + 1]))
            {
                return false;
            }

            i++;
        }

        return true;
    }

    [Fact]
    public void HetSchonenLaatEenGewoonNederlandsBerichtOngemoeid()
    {
        // De onmisbare tegenhanger. Zonder deze test staat elke test hierboven groen als Clean
        // eenvoudigweg alles weggooit, en dan meet geen van hen wat hij belooft.
        var bericht =
            "Onze voorraad-sync lijkt sinds gisteren stil te staan. Zie ik dat goed?\n\n"
            + "Groet, Jan — 06-12345678 (Bakker B.V., filiaal Zwolle) €";

        Assert.Equal(bericht, SupportBody.Clean(bericht));
    }
}
