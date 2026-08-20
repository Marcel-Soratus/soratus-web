using Soratus.Portal.Components.Shared;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// De tooltip bij een logbericht: afgekapt op 400 tekens, en nooit midden in een teken.
/// </summary>
/// <remarks>
/// <para>In de data zit een logregel van ruim 3400 tekens. Die als <c>title</c> tonen levert een
/// blok tekst op dat het halve scherm bedekt en niet te scrollen is, dus wordt hij afgekapt. Wie
/// meer wil, klapt de regel uit — daar staat hij volledig en scrollbaar.</para>
///
/// <para><strong>Het surrogaatpaar is de reden dat dit een eigen klasse is en geen inline
/// uitdrukking.</strong> <c>message[..400]</c> knipt op UTF-16-eenheden, en een emoji is er twee.
/// Valt de grens ertussen, dan blijft er een losse high surrogate in het attribuut staan: dat is
/// geen geldig teken, en wat een browser ermee doet is niet afgesproken. Zichtbaar zodra iemand een
/// emoji in een productnaam heeft — en dat is niet zeldzaam in een factuurregel.</para>
/// </remarks>
public class LogTextTests
{
    [Fact]
    public void EenKortBerichtKomtOngewijzigdInDeTooltip()
    {
        const string bericht = "De bron antwoordde niet binnen 30 seconden.";

        Assert.Equal(bericht, LogText.Title(bericht));
        Assert.Equal(bericht, LogText.Title(bericht, "klik de regel open"));
    }

    [Fact]
    public void EenLeegBerichtLevertEenLegeTooltipEnGeenNull()
    {
        // Een title="" is geen tooltip; een title="…" op niets zou beweren dat er meer was.
        Assert.Equal(string.Empty, LogText.Title(null));
        Assert.Equal(string.Empty, LogText.Title(string.Empty));
    }

    [Fact]
    public void EenBerichtVanPreciesDeGrensWordtNietAfgekapt()
    {
        // De grens zelf hoort er nog helemaal bij te horen: 400 tekens zijn 400 tekens, geen 399
        // met een puntje. Een off-by-one hier is onzichtbaar tot iemand hem meet.
        var bericht = new string('a', LogText.MaxTitleLength);

        Assert.Equal(bericht, LogText.Title(bericht));
        Assert.DoesNotContain('…', LogText.Title(bericht));
    }

    [Fact]
    public void EenLangerBerichtWordtAfgekaptMetEenUitleg()
    {
        var bericht = new string('a', LogText.MaxTitleLength + 1);

        var tooltip = LogText.Title(bericht, "klik de regel open voor de volledige tekst");

        Assert.StartsWith(new string('a', LogText.MaxTitleLength), tooltip, StringComparison.Ordinal);
        Assert.Contains('…', tooltip);
        Assert.Contains("klik de regel open", tooltip, StringComparison.Ordinal);
        Assert.True(
            tooltip.Length < bericht.Length + 64,
            "De tooltip is bijna zo lang als het bericht; dan is er niets afgekapt.");
    }

    [Fact]
    public void ZonderUitlegStaatErAlleenEenBeletselteken()
    {
        var tooltip = LogText.Title(new string('a', LogText.MaxTitleLength + 1));

        Assert.EndsWith("…", tooltip, StringComparison.Ordinal);
        Assert.DoesNotContain('(', tooltip);
    }

    [Fact]
    public void EenEmojiPreciesOpDeGrensBlijftHeelInPlaatsVanHalf()
    {
        // 399 gewone tekens en dan een emoji: de grens valt tussen de twee helften van dat teken.
        // Een kale [..400] laat dan een losse high surrogate achter in het title-attribuut.
        var bericht = new string('a', LogText.MaxTitleLength - 1) + "🧾" + "en dan nog wat tekst";

        var tooltip = LogText.Title(bericht);

        Assert.True(
            LosseSurrogaten(tooltip) == 0,
            $"De tooltip bevat {LosseSurrogaten(tooltip)} losse surrogaat/surrogaten. " +
            "Dat is geen geldig teken en wat een browser ermee doet is niet afgesproken. Kap af " +
            "op een tekengrens: kijk of het laatste teken een high surrogate is en laat het dan " +
            "liggen.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GeenEnkeleStandVanEenEmojiRondDeGrensLevertEenHalfTeken(int verschuiving)
    {
        // Vier standen: de emoji begint net vóór, precies op, en net ná de grens. Eén test op één
        // stand zou de goede kunnen treffen door geluk.
        var bericht = new string('a', LogText.MaxTitleLength - 2 + verschuiving)
            + "🧾🧾🧾"
            + new string('b', 50);

        var tooltip = LogText.Title(bericht);

        Assert.Equal(0, LosseSurrogaten(tooltip));
    }

    /// <summary>Hoeveel UTF-16-eenheden in deze tekst geen geldig paar vormen.</summary>
    private static int LosseSurrogaten(string tekst)
    {
        var aantal = 0;

        for (var i = 0; i < tekst.Length; i++)
        {
            if (char.IsHighSurrogate(tekst[i]))
            {
                if (i + 1 < tekst.Length && char.IsLowSurrogate(tekst[i + 1]))
                {
                    i++;
                    continue;
                }

                aantal++;
            }
            else if (char.IsLowSurrogate(tekst[i]))
            {
                aantal++;
            }
        }

        return aantal;
    }
}
