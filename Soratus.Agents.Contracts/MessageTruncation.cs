using System.Buffers;
using System.Globalization;
using System.Text;

namespace Soratus.Agents.Contracts;

/// <summary>
/// Dwingt de contractregel op <see cref="LogRecord.Message"/> af: één zin, leesbaar voor wie de
/// code niet kent.
/// </summary>
/// <remarks>
/// <para><c>msg</c> wordt door de klant gelezen en <c>extra</c> niet. Dat is de enige echte grens
/// op deze twee velden, en tot voor kort was het een afspraak. Een verificatie over negentien
/// agents vond een <c>payload.dump</c> met zestien regels stacktrace in <c>msg</c> — bronpaden,
/// klasse- en methodenamen, zichtbaar voor een klant.</para>
///
/// <para><strong>De knip valt op de eerste regelovergang.</strong> Dat is even mechanisch als een
/// lengtegrens — geen inhoudsheuristiek, nooit "is dit een stacktrace" — maar het volgt
/// rechtstreeks uit de contractregel die er al staat: één zin. Een zin bevat geen
/// regelafbreking.</para>
///
/// <para>Een lengtegrens was het eerste idee en is gemeten onbruikbaar. Over de 93 klantzichtbare
/// logregels in de opslag: één regel had meer dan één regel in <c>msg</c>, één regel had verdachte
/// inhoud, en het aantal regels met verdachte inhoud in alleen de <em>eerste</em> regel was nul.
/// De langste legitieme eerste regel was 1417 tekens. Elke grens tussen 200 en 500 verminkt dus
/// geldig Nederlands proza middenin, en elke grens boven 1417 laat de stacktrace er deels doorheen.
/// Dat middengebied is het gevaarlijkst, want het lijkt de veilige ruime keuze. Op de knip op de
/// regelovergang verdwijnen alle zestien stacktrace-regels en blijven de andere 92 regels
/// onaangeraakt: nul valse positieven, één ware positief.</para>
///
/// <para><strong>Waarom deze functie hier staat en niet in de telemetriebibliotheek.</strong> De
/// knip hoort op twee plekken te gebeuren. Bij het schrijven, waar de tekst ontstaat; en bij het
/// projecteren naar de klant in het portaal, want dat dekt wat de schrijfkant niet kan — de dertig
/// dagen documenten die er al staan, een agent op een oudere bibliotheekversie, en een agent die de
/// bibliotheek helemaal niet gebruikt. Zouden die twee elk hun eigen knip schrijven, dan bestaan er
/// twee definities van "één zin" en gaan die divergeren. Dit project heeft bewust geen
/// afhankelijkheden en wordt door beide kanten gebruikt, dus dit is de plek waar één definitie kan
/// staan.</para>
/// </remarks>
public static class MessageTruncation
{
    /// <summary>
    /// De gereserveerde sleutel in <see cref="LogRecord.Extra"/> waaronder alles ná de eerste
    /// regelovergang belandt.
    /// </summary>
    /// <remarks>
    /// Gereserveerd: een agentbouwer die deze naam zelf gebruikt wordt overschreven. Bewust zonder
    /// liggend streepje ervoor, anders dan de sleutels die de telemetriebibliotheek toevoegt
    /// (<c>_exception</c>, <c>_category</c>). Die prefix betekent "door de bibliotheek
    /// toegevoegd"; dit is een contractveld dat het portaal expliciet rendert en dat ook door het
    /// seed-gereedschap wordt geschreven, buiten die bibliotheek om.
    /// </remarks>
    public const string OverflowKey = "msgOverflow";

    /// <summary>
    /// De markering die achter de overgebleven eerste regel komt.
    /// </summary>
    /// <remarks>
    /// Vaste tekst en vaste lengte, zodat elke schrijver hem letterlijk kan nabouwen. Hij noemt
    /// <c>extra</c> niet: een klant kan dat veld niet zien, en een verwijzing naar iets waar hij
    /// niet bij komt is geen mededeling maar een raadsel. Wat hij wél moet weten is dat er meer
    /// was — anders leest het alsof de agent halverwege is gestopt.
    /// </remarks>
    public const string Marker = " … (ingekort)";

    /// <summary>
    /// Ruime hygiënegrens op de lengte van <c>msg</c>, tegen één absurd lange ononderbroken regel.
    /// </summary>
    /// <remarks>
    /// Dit is <em>niet</em> het mechanisme dat interne details uit <c>msg</c> houdt; dat doet de
    /// knip op de regelovergang. Deze grens staat ruim boven de 1417 tekens van de langste gemeten
    /// legitieme regel en gaat in de praktijk nooit af.
    /// </remarks>
    public const int DefaultMaxLength = 8_000;

    /// <summary>
    /// De ondergrens die een eigen <paramref name="maxLength"/> mag hebben. Daaronder past
    /// <see cref="Marker"/> zelf niet meer en levert knippen onleesbare tekst op.
    /// </summary>
    public const int MinimumLength = 64;

    private static readonly SearchValues<char> LineBreaks = SearchValues.Create("\r\n");

    /// <summary>
    /// Houdt van <paramref name="message"/> de eerste regel over en geeft de rest apart terug.
    /// </summary>
    /// <param name="message">Het bericht zoals de agentbouwer het schreef.</param>
    /// <param name="maxLength">
    /// Hygiënegrens tegen één absurd lange ononderbroken regel. De regelovergang doet het
    /// eigenlijke werk.
    /// </param>
    /// <returns>
    /// <c>Message</c> is het bericht zoals het aan een klant getoond mag worden — nooit langer dan
    /// <paramref name="maxLength"/>. <c>Overflow</c> is alles wat is weggeknipt, of <c>null</c> als
    /// er niets is weggeknipt.
    /// </returns>
    /// <remarks>
    /// De overloop komt apart terug in plaats van dat deze functie hem ergens neerzet, omdat de twee
    /// aanroepers er verschillende dingen mee moeten. De schrijfkant zet hem onder
    /// <see cref="OverflowKey"/> in <see cref="LogRecord.Extra"/>; het klantpad in het portaal
    /// negeert hem, want het klanttype heeft geen veld voor vrije JSON. Zou deze functie aannemen
    /// dat er een plek is om de overloop te zetten, dan zou het klantpad hem niet kunnen gebruiken.
    /// </remarks>
    public static (string Message, string? Overflow) Cut(string? message, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(message))
        {
            return ("(geen bericht)", null);
        }

        // De eerste regelovergang, in welke vorm dan ook: \n, \r\n of een losse \r.
        int cut = message.AsSpan().IndexOfAny(LineBreaks);
        if (cut < 0)
        {
            cut = message.Length;
        }

        // Hygiëne: is de eerste regel zelf absurd lang, knip dan alsnog — op een grafeemgrens, met
        // de markering al van het budget af zodat het bericht de grens niet kan overschrijden.
        if (cut > maxLength)
        {
            cut = SafeCutIndex(message, maxLength - Marker.Length);
        }

        if (cut >= message.Length)
        {
            return (message, null);
        }

        // De regelovergang waarop geknipt is hoort in geen van beide helften.
        string head = message[..cut].TrimEnd();
        string overflow = message[cut..].TrimStart('\r', '\n');

        // Een afsluitende regelovergang is geen overloop. Zonder deze regel zou elk bericht dat
        // toevallig op een newline eindigt een misleidende markering krijgen.
        return overflow.Length == 0 ? (head, null) : (head + Marker, overflow);
    }

    /// <summary>
    /// Geeft de grootste knipplek die binnen <paramref name="budget"/> past en op een grafeemgrens
    /// ligt.
    /// </summary>
    /// <remarks>
    /// Op tekens knippen is niet genoeg. Halverwege een surrogaatpaar knippen levert ongeldige
    /// UTF-16 op, en halverwege een samengestelde glyph — een letter met een combineerteken, een
    /// vlag, een emoji met ZWJ — levert een ander teken dan er stond. Een afgekapte string die
    /// ongeldig is, is erger dan een lange: die breekt de serialisatie of de weergave in plaats van
    /// alleen te ergeren.
    /// </remarks>
    private static int SafeCutIndex(string value, int budget)
    {
        if (budget <= 0)
        {
            return 0;
        }

        int boundary = 0;
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(value);

        while (elements.MoveNext())
        {
            int start = elements.ElementIndex;
            if (start > budget)
            {
                return boundary;
            }

            boundary = start;
        }

        return boundary;
    }

    /// <summary>
    /// Controleert dat de knipregel doet wat hij belooft. Bedoeld om bij het opstarten aan te
    /// roepen.
    /// </summary>
    /// <remarks>
    /// Er staan echte tests op deze functie in <c>Soratus.Agents.Telemetry.Tests</c>. Deze assertie
    /// staat daar niet in de weg maar naast: hij loopt bij elke start van elke agent en van het
    /// portaal, en dekt daarmee het geval dat iemand de bibliotheek verbouwt zonder de tests te
    /// draaien.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Als een van de garanties niet meer geldt.</exception>
    public static void AssertContract()
    {
        // 1. Eén regel blijft ongemoeid, ook als hij lang is. Dit is het geval waarop een
        //    lengtegrens stukliep.
        string prose = new('a', 1_417);
        Require(Cut(prose) is (_, null), "een lange regel van één zin wordt onterecht geknipt");
        Require(Cut(prose).Message.Length == 1_417, "een lange regel van één zin wordt verminkt");

        // 2. Een afsluitende regelovergang is geen overloop.
        Require(
            Cut("Factuur INV-2291 verwerkt.\r\n") is ("Factuur INV-2291 verwerkt.", null),
            "een afsluitende regelovergang levert een onterechte markering op");

        // 3. Een stacktrace ná de eerste regel verdwijnt en blijft volledig in de overloop.
        const string frame = "   at Soratus.Sync.Validators.StockLineValidator.Validate(StockLine line)";
        (string Message, string? Overflow) dump = Cut("De voorraadregels zijn afgekeurd.\n" + frame);
        Require(
            dump.Message == "De voorraadregels zijn afgekeurd." + Marker,
            "de eerste regel overleeft de knip niet ongeschonden");
        Require(!dump.Message.Contains("   at ", StringComparison.Ordinal), "er staat nog stacktrace in msg");
        Require(dump.Overflow == frame, "de overloop is niet volledig of niet onveranderd");

        // 4. Slaat de hygiënegrens toe, dan blijft het bericht binnen de grens en is er niet in een
        //    surrogaatpaar of een samengestelde glyph geknipt.
        (string Message, string? Overflow) wide = Cut(string.Concat(Enumerable.Repeat("\U0001D11E", 6_000)));
        Require(wide.Message.Length <= DefaultMaxLength, "een geknipt bericht past niet binnen de hygiënegrens");
        Require(wide.Overflow is not null, "de hygiënegrens levert geen overloop op");
        Require(
            Rune.DecodeLastFromUtf16(HeadOf(wide.Message), out _, out _) == OperationStatus.Done,
            "er is in een surrogaatpaar geknipt");
        Require(
            HeadOf(Cut(string.Concat(Enumerable.Repeat("é", 6_000))).Message).Length % 2 == 0,
            "er is in een samengestelde glyph geknipt");
    }

    /// <summary>Haalt het overgebleven tekstdeel terug uit een bericht met markering.</summary>
    private static string HeadOf(string message) =>
        message.EndsWith(Marker, StringComparison.Ordinal) ? message[..^Marker.Length] : message;

    private static void Require(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"De knipregel op msg is stuk: {what}. Daarmee kan een stacktrace of interne " +
                "context in een veld belanden dat de klant leest.");
        }
    }
}
