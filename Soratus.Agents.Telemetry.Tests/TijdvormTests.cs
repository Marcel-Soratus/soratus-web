using System.Text.Json;
using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.Tests;

/// <summary>
/// De canonieke tijdvorm uit <see cref="TimestampNormalization"/>: altijd UTC, altijd zeven
/// decimalen, altijd een afsluitende <c>Z</c>, altijd 28 tekens.
/// </summary>
/// <remarks>
/// <para><strong>Waarom dit getest wordt en niet alleen geasserteerd.</strong> De assertie
/// (<see cref="TimestampNormalization.AssertCanonical"/>) bewijst dat een gegeven set opties de
/// vorm oplevert. Wat zij niet kan bewijzen is dat zij zélf afgaat als de vorm wegvalt: een
/// assertie die niets afkeurt is niet van een groene assertie te onderscheiden. Dat is precies wat
/// hier gemeten wordt — <see cref="SlaatAlarmZonderRegistratie"/> en
/// <see cref="SlaatAlarmBijEenVormDieVerkeerdSorteert"/> voeren de assertie foute opties toe en
/// eisen dat hij werpt.</para>
///
/// <para><strong>De eigenschap is niet "de vorm" maar "tekst sorteren = tijd sorteren".</strong>
/// Cosmos bewaart deze velden als tekst en <c>ORDER BY</c> vergelijkt lexicografisch. De vorm is
/// het middel; de sorteergelijkheid is de eis. Vandaar dat
/// <see cref="TekstSorterenIsHetzelfdeAlsTijdSorteren"/> geen vormcontrole is maar een vergelijking
/// van twee ordeningen, en vandaar dat
/// <see cref="EenVormDieErGoedUitzietKanNogAltijdVerkeerdSorteren"/> twee formaten uitoefent die aan
/// élke vormeis voldoen — altijd UTC, altijd een afsluitende <c>Z</c> — en toch verkeerd sorteren.
/// </para>
///
/// <para><strong>Waarom die twee tests op het formaat zitten en niet op de assertie.</strong> In
/// <see cref="TimestampNormalization.AssertCanonical"/> staan de exacte-tekstproeven vóór de
/// volgordecontrole, en die exacte proeven zijn strikt sterker: wat ze doorlaat ís canoniek. De
/// volgordecontrole is dus een achtervang die in de praktijk niet als eerste afgaat, en
/// <see cref="SlaatAlarmBijEenVormDieVerkeerdSorteert"/> eist daarom alleen dát er geworpen wordt en
/// niet welke melding het is. Dat de achtervang zelf werkt is met een mutatietest gemeten: met de
/// exacte proeven tijdelijk uitgeschakeld gaat hij voor beide formaten af. Wat er hierboven aan
/// dekking overblijft is de eigenschap zelf, en die staat op het formaat.</para>
///
/// <para>Deze tests staan in dit project en niet in een eigen testproject voor
/// <c>Soratus.Agents.Contracts</c>: dat project bestaat niet, dit is het enige testproject dat naar
/// de contracten verwijst zonder het portaal mee te slepen, en <see cref="MsgKnipTests"/> staat hier
/// om dezelfde reden. De proef op de schrijfkant van het portaal kan hier per definitie niet staan;
/// die staat in <c>Soratus.Portal.Tests/Portaalgegevens/PortaaltijdvormTests.cs</c>.</para>
/// </remarks>
public class TijdvormTests
{
    /// <summary>De opties zoals elke schrijver ze samenstelt: web-defaults plus de normalisatie.</summary>
    private static JsonSerializerOptions Genormaliseerd()
    {
        var opties = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        TimestampNormalization.Register(opties);
        return opties;
    }

    /// <summary>De tekst zoals die in het document belandt, zonder de aanhalingstekens.</summary>
    private static string Tekst<T>(JsonSerializerOptions opties, T waarde) =>
        JsonSerializer.Serialize(waarde, opties).Trim('"');

    // ── De vorm zelf ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenTijdMetOffsetWordtNaarUtcGerekend()
    {
        // 17:14 in +02:00 is 15:14 UTC. Ging dit mis, dan stond er een verkeerd móment in de opslag
        // en niet alleen een verkeerde volgorde — dat is de ernstiger van de twee.
        var moment = new DateTimeOffset(2026, 8, 19, 17, 14, 0, TimeSpan.FromHours(2));

        Assert.Equal("2026-08-19T15:14:00.0000000Z", Tekst(Genormaliseerd(), moment));
    }

    [Fact]
    public void HetzelfdeMomentInTweeOffsetsKrijgtEenEnDezelfdeSpelling()
    {
        // Dit is de kern. Twee spellingen van één moment maken sorteren op tekst een kansspel.
        var opties = Genormaliseerd();

        Assert.Equal(
            Tekst(opties, new DateTimeOffset(2026, 8, 19, 15, 14, 0, TimeSpan.Zero)),
            Tekst(opties, new DateTimeOffset(2026, 8, 19, 17, 14, 0, TimeSpan.FromHours(2))));
    }

    [Theory]
    // Geen decimalen, drie decimalen, zeven decimalen: met de standaardopties zijn dit drie
    // verschillende lengtes (20, 24 en 28 tekens) en dus drie ordeningen.
    [InlineData(0, "2026-08-19T15:04:05.0000000Z")]
    [InlineData(6780000, "2026-08-19T15:04:05.6780000Z")]
    [InlineData(9449045, "2026-08-19T15:04:05.9449045Z")]
    public void ElkePrecisieKrijgtDezelfdeBreedte(int ticks, string verwacht)
    {
        var moment = new DateTimeOffset(2026, 8, 19, 15, 4, 5, TimeSpan.Zero).AddTicks(ticks);

        string tekst = Tekst(Genormaliseerd(), moment);

        Assert.Equal(verwacht, tekst);
        Assert.Equal(TimestampNormalization.Width, tekst.Length);
    }

    [Fact]
    public void EenNullableTijdGaatLangsDezelfdeConverter()
    {
        // Apart, want de portaaldocumenten gebruiken DateTimeOffset? (changedAt) en dat een
        // converter voor DateTimeOffset ook DateTimeOffset? dekt is gedrag van System.Text.Json en
        // geen belofte van onze kant. Wordt dat ooit anders, dan is changedAt de enige die valt.
        DateTimeOffset? moment = new DateTimeOffset(2026, 8, 19, 17, 14, 0, TimeSpan.FromHours(2));

        Assert.Equal("2026-08-19T15:14:00.0000000Z", Tekst(Genormaliseerd(), moment));
    }

    [Fact]
    public void EenLegeNullableTijdBlijftNull()
    {
        // De converter mag geen tijdstempel verzinnen waar er geen is: een klant die nooit is
        // gewijzigd heeft geen changedAt, en "1 januari 0001" is geen betere waarde dan niets.
        Assert.Equal("null", JsonSerializer.Serialize((DateTimeOffset?)null, Genormaliseerd()));
    }

    [Fact]
    public void EenDateTimeInUtcKrijgtDezelfdeVorm()
    {
        // DateTime komt niet in de contracttypen voor, maar wel in de structured-logging-state die
        // een agent meestuurt — en die gaat door dezelfde serializer naar hetzelfde document.
        var moment = new DateTime(2026, 8, 19, 15, 13, 19, 944, DateTimeKind.Utc).AddTicks(9045);

        Assert.Equal("2026-08-19T15:13:19.9449045Z", Tekst(Genormaliseerd(), moment));
    }

    [Fact]
    public void ToCanonicalGeeftDezelfdeTekstAlsDeSerializer()
    {
        // ToCanonical bestaat voor de plekken waar geen serializer tussen zit: een tijdstempel in
        // een queryparameter of in een documentsleutel moet dezelfde vorm hebben als het veld
        // waarmee hij vergeleken wordt. Zouden die twee uiteenlopen, dan vergelijkt een WHERE-clause
        // twee spellingen en levert nul rijen op in plaats van een fout.
        var opties = Genormaliseerd();

        foreach (var moment in Momenten())
        {
            Assert.Equal(Tekst(opties, moment), TimestampNormalization.ToCanonical(moment));
        }
    }

    // ── De eigenschap waar het om gaat ──────────────────────────────────────────────────────────

    [Fact]
    public void TekstSorterenIsHetzelfdeAlsTijdSorteren()
    {
        var opties = Genormaliseerd();

        string[] opTekst = [.. Momenten().Select(m => Tekst(opties, m)).Order(StringComparer.Ordinal)];
        string[] opTijd = [.. Momenten().Order().Select(m => Tekst(opties, m))];

        Assert.Equal(opTijd, opTekst);
    }

    [Fact]
    public void ZonderNormalisatieSorteertTekstAndersDanTijd()
    {
        // De contraproef. Zonder deze test bewijst de test hierboven niets: als de standaardopties
        // óók goed sorteerden, was de hele reparatie overbodig en zou niemand het merken.
        var kaal = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        string[] opTekst = [.. Momenten().Select(m => Tekst(kaal, m)).Order(StringComparer.Ordinal)];
        string[] opTijd = [.. Momenten().Order().Select(m => Tekst(kaal, m))];

        Assert.NotEqual(opTijd, opTekst);
    }

    [Theory]
    // Een formaat waarin de dag vooraan staat: 28 tekens, afsluitende Z, altijd UTC, en toch de
    // verkeerde volgorde zodra er twee maanden in het spel zijn.
    [InlineData("dd-MM-yyyyTHH:mm:ss.fffffff'Z'")]
    // Het formaat dat je krijgt als iemand "die nullen achteraan mogen weg" vindt. Altijd UTC,
    // altijd een Z, twee breedtes door elkaar — en dan sorteert ":05Z" ná ":05.678Z".
    [InlineData("yyyy-MM-ddTHH:mm:ss.FFFFFFF'Z'")]
    public void EenVormDieErGoedUitzietKanNogAltijdVerkeerdSorteren(string formaat)
    {
        // Hier hangt de assertie aan: dit is de reden dat er een volgordecontrole in
        // AssertCanonical staat en niet alleen een lengtecontrole. Zou deze test groen zijn zonder
        // de NotEqual — dus zouden deze formaten tóch goed sorteren — dan zou die controle nooit
        // kunnen afgaan, en dat is precies wat er met de vorige reeks proefmomenten aan de hand was.
        string Vorm(DateTimeOffset moment) =>
            moment.ToUniversalTime().ToString(formaat, System.Globalization.CultureInfo.InvariantCulture);

        string[] opTekst = [.. Momenten().Select(Vorm).Order(StringComparer.Ordinal)];
        string[] opTijd = [.. Momenten().Order().Select(Vorm)];

        Assert.NotEqual(opTijd, opTekst);
    }

    /// <summary>
    /// Vijf momenten die elke foute tijdvorm die we zagen of konden bedenken laten opvallen.
    /// </summary>
    /// <remarks>
    /// <para>Dezelfde soort reeks als in <see cref="TimestampNormalization.AssertCanonical"/>, en om
    /// dezelfde reden zorgvuldig gekozen: twee momenten in dezelfde seconde met en zonder
    /// decimaaldeel (wisselende breedte), een moment in +02:00 dat tussen twee UTC-momenten valt
    /// (niet-omgerekende offset), en een moment in een andere maand (dag-vooraan). Een reeks van
    /// vier momenten uit één augustusdag sorteert op tekst toevallig hetzelfde als op tijd, óók
    /// helemaal zonder normalisatie — dat is gemeten en het is de val waar de vorige versie van deze
    /// controle in liep.</para>
    ///
    /// <para>Bewust niet-chronologisch opgeschreven, zodat een test die per ongeluk de invoerorde
    /// teruggeeft in plaats van te sorteren, opvalt.</para>
    /// </remarks>
    private static DateTimeOffset[] Momenten() =>
    [
        new DateTimeOffset(2026, 8, 19, 15, 20, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 19, 15, 4, 5, 678, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 19, 17, 14, 0, TimeSpan.FromHours(2)),
        new DateTimeOffset(2026, 8, 19, 15, 4, 5, 0, TimeSpan.Zero),
    ];

    // ── De assertie moet afgaan, en dat is het punt van een assertie ────────────────────────────

    [Fact]
    public void LaatGenormaliseerdeOptiesDoor()
    {
        TimestampNormalization.AssertCanonical(Genormaliseerd());
    }

    [Fact]
    public void SlaatAlarmZonderRegistratie()
    {
        // Het scenario dat werkelijk gebeurd is: iemand stelt zijn eigen JsonSerializerOptions
        // samen en vergeet Register. Geen fout, wel de verkeerde vorm — tenzij dit werpt.
        var kaal = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var fout = Assert.Throws<InvalidOperationException>(() => TimestampNormalization.AssertCanonical(kaal));

        // De standaardopties schrijven de offset uit zoals hij is aangeleverd. De eerste proef gaat
        // over een moment in +02:00, dus dat is de offset die in de melding staat.
        Assert.Contains("+02:00", fout.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(TimestampNormalization.Register), fout.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SlaatAlarmBijEenHalveRegistratie()
    {
        // Alleen de DateTimeOffset-kant registreren is niet mogelijk via Register — dat is met opzet
        // één aanroep — maar wel via een eigen converter. Zo'n halve registratie laat het
        // DateTime-pad open, en dat pad loopt via de logstate van een agent naar hetzelfde document.
        //
        // Deze test stond hier eerst rood, en dat was geen testfout: AssertCanonical liet een
        // ontbrekende DateTime-converter door. Zijn enige DateTime-proef had zeven gevulde decimalen,
        // en juist die schrijft System.Text.Json van zichzelf al canoniek. De proef met afsluitende
        // nullen erbij sluit dat gat, en deze test is de reden dat het niet terugkomt.
        var half = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        half.Converters.Add(new AlleenOffset());

        var fout = Assert.Throws<InvalidOperationException>(() => TimestampNormalization.AssertCanonical(half));
        Assert.Contains("afsluitende nullen", fout.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SlaatAlarmBijEenVormDieVerkeerdSorteert()
    {
        // Deze converter doorstaat elke vormcontrole die je zou schrijven: 28 tekens, eindigt op Z,
        // altijd UTC, altijd zeven decimalen. Alleen staat de dag vooraan, en dan sorteert
        // lexicografisch niet meer chronologisch.
        var omgekeerd = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        omgekeerd.Converters.Add(new DagEerst());
        omgekeerd.Converters.Add(new DagEerstDateTime());

        Assert.Throws<InvalidOperationException>(() => TimestampNormalization.AssertCanonical(omgekeerd));
    }

    [Fact]
    public void SlaatAlarmBijEenVormMetWisselendeBreedte()
    {
        // De vorm die de agentkant zou krijgen als iemand "de nullen achteraan mogen weg" vindt:
        // altijd UTC, altijd een Z, en toch twee lengtes door elkaar. 15:04:05Z sorteert ná
        // 15:04:05.678Z, want Z is groter dan een punt.
        var wisselend = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        wisselend.Converters.Add(new KorteVorm());
        wisselend.Converters.Add(new KorteVormDateTime());

        Assert.Throws<InvalidOperationException>(() => TimestampNormalization.AssertCanonical(wisselend));
    }

    [Fact]
    public void WeigertOptiesDieNietBestaan()
    {
        Assert.Throws<ArgumentNullException>(() => TimestampNormalization.AssertCanonical(null!));
        Assert.Throws<ArgumentNullException>(() => TimestampNormalization.Register(null!));
    }

    // ── De opzettelijk foute converters waarmee de assertie wordt uitgeoefend ───────────────────

    private sealed class AlleenOffset : System.Text.Json.Serialization.JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(TimestampNormalization.ToCanonical(value));
    }

    private sealed class DagEerst : System.Text.Json.Serialization.JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString(
                "dd-MM-yyyyTHH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class DagEerstDateTime : System.Text.Json.Serialization.JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString(
                "dd-MM-yyyyTHH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class KorteVorm : System.Text.Json.Serialization.JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString(
                "yyyy-MM-ddTHH:mm:ss.FFFFFFF'Z'", System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class KorteVormDateTime : System.Text.Json.Serialization.JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString(
                "yyyy-MM-ddTHH:mm:ss.FFFFFFF'Z'", System.Globalization.CultureInfo.InvariantCulture));
    }
}
