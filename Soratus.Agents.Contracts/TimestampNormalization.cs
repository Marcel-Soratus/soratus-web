using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soratus.Agents.Contracts;

/// <summary>
/// Dwingt af dat elk tijdstempel in dezelfde canonieke vorm de opslag in gaat:
/// <c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>, altijd UTC, altijd zeven decimalen, altijd 28 tekens.
/// </summary>
/// <remarks>
/// <para><strong>Waarom dit geen schoonheidsprijs is.</strong> Cosmos bewaart een tijdstempel als
/// tekst en <c>ORDER BY</c> vergelijkt die tekst lexicografisch. Zolang elk document dezelfde vorm
/// heeft, is lexicografisch sorteren gelijk aan chronologisch sorteren. Zodra er twee vormen door
/// elkaar staan, is het dat niet meer — en dan sorteert een lijst stil verkeerd. Niet met een fout,
/// maar met een verkeerde volgorde die eruitziet als een goede. Lokaal valt het niet op, want in C#
/// vergelijkt <c>DateTimeOffset</c> wél correct.</para>
///
/// <para><strong>De vorm die van niets vaststaat, gemeten.</strong> Met de standaardopties van
/// <c>System.Text.Json</c> schrijft een <c>DateTimeOffset</c> de offset uit en laat hij nullen aan
/// het eind van de decimalen weg. Vier momenten uit één werkdag kwamen er zo uit:</para>
///
/// <code>
/// 2026-08-20T17:04:05+02:00          (= 15:04:05 UTC, het vroegste moment)
/// 2026-08-20T15:04:05+00:00
/// 2026-08-20T15:04:05.678+00:00
/// 2026-08-20T15:13:19.9449045+00:00
/// </code>
///
/// <para>Op tekst gesorteerd komt de eerste regel als láátste te staan: <c>1</c> is groter dan
/// <c>5</c> op positie twaalf. En zelfs zonder offsets loopt het mis, want een ontbrekend
/// decimaaldeel sorteert ná een aanwezig decimaaldeel: <c>…:05Z</c> komt na <c>…:05.678Z</c>, omdat
/// <c>Z</c> groter is dan <c>.</c>.</para>
///
/// <para><strong>Waarom een ander type dit niet oplost.</strong> De verleiding is om
/// <c>DateTimeOffset</c> te vervangen door <c>DateTime</c>, want die schrijft een <c>Z</c>. Dat is
/// gemeten en het lost de helft op die je ziet, niet de helft die bijt. Drie momenten als
/// <c>DateTime</c> met <c>Kind.Utc</c>:</para>
///
/// <code>
/// 2026-08-20T15:04:05Z            (20 tekens)
/// 2026-08-20T15:04:05.678Z        (24 tekens)
/// 2026-08-20T15:04:05.0000001Z    (28 tekens)
/// </code>
///
/// <para>Op tekst gesorteerd staat het vroegste moment weer achteraan. De variabele precisie blijft
/// dus staan, en er komen twee vormen bíj: <c>Kind.Unspecified</c> schrijft helemaal geen zone
/// (<c>2026-08-20T15:04:05</c>) en <c>Kind.Local</c> schrijft alsnog een offset. Bovendien is
/// <c>Kind</c> geen deel van de waarde: een <c>DateTime</c> die zijn kind onderweg verliest is
/// stilletjes twee uur verschoven, en een verkeerd móment is erger dan een verkeerde volgorde.
/// <c>DateTimeOffset</c> draagt altijd een ondubbelzinnig moment. Het type is dus níet het probleem;
/// de niet-vastgepinde uitvoervorm is het. Die pinnen we hier vast, en het type blijft
/// <c>DateTimeOffset</c>.</para>
///
/// <para><strong>Waarom dit in dit project staat.</strong> Dezelfde reden als bij
/// <see cref="MessageTruncation"/>: de regel moet op meer dan één schrijfplek gelden — de
/// telemetriebibliotheek die agents gebruiken, de schrijfkant van het portaal, en het
/// seed-gereedschap. Toen deze converter alleen in de telemetriebibliotheek stond en daar
/// <c>internal</c> was, ontstond de eerste kopie in het seed-gereedschap, met in de eigen
/// documentatie de toegift dat de assertie erop niet kan zien of de twee nog gelijk zijn. Een derde
/// exemplaar in het portaal zou de reeks afmaken. Eén implementatie op de plek die beide kanten al
/// delen is de enige vorm waarin ze niet uiteen kunnen lopen.</para>
/// </remarks>
public static class TimestampNormalization
{
    /// <summary>
    /// De enige vorm waarin een tijdstempel de opslag in mag: UTC, zeven decimalen, afsluitende
    /// <c>Z</c>.
    /// </summary>
    /// <remarks>
    /// Verander dit formaat niet zonder alle bestaande documenten mee te verhuizen. Een container
    /// met twee vormen erin sorteert erger dan een container met één verkeerde vorm, want dan lijkt
    /// het te kloppen.
    /// </remarks>
    public const string UtcFormat = "yyyy-MM-ddTHH:mm:ss.fffffff'Z'";

    /// <summary>
    /// De lengte die elke waarde in <see cref="UtcFormat"/> heeft.
    /// </summary>
    /// <remarks>
    /// Vaste breedte is geen gevolg maar de eis zelf: lexicografisch vergelijken van tekst van
    /// gelijke lengte is hetzelfde als chronologisch vergelijken. Zodra één waarde korter is, is
    /// dat niet meer waar.
    /// </remarks>
    public const int Width = 28;

    /// <summary>
    /// Zet een moment om naar de canonieke tekst, ongeacht de offset waarin het is aangeleverd.
    /// </summary>
    /// <param name="moment">Het moment.</param>
    /// <returns>De tekst van <see cref="Width"/> tekens in <see cref="UtcFormat"/>.</returns>
    /// <remarks>
    /// Dit is het lijf van de converter, apart bereikbaar voor de plekken waar geen serializer
    /// tussen zit: een tijdstempel in een queryparameter of in een documentsleutel moet dezelfde
    /// vorm hebben als het veld waarmee hij vergeleken wordt. Zelf <c>ToString(UtcFormat)</c>
    /// aanroepen is precies waar het misgaat — dan is <c>ToUniversalTime()</c> of
    /// <c>InvariantCulture</c> het ding dat iemand vergeet.
    /// </remarks>
    public static string ToCanonical(DateTimeOffset moment) =>
        moment.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Hangt de normalisatie aan <paramref name="options"/>.
    /// </summary>
    /// <param name="options">De opties waarmee naar de opslag wordt geschreven.</param>
    /// <exception cref="ArgumentNullException">Als <paramref name="options"/> <c>null</c> is.</exception>
    /// <exception cref="InvalidOperationException">Als <paramref name="options"/> al bevroren is.</exception>
    /// <remarks>
    /// Eén aanroep voor beide typen, en niet twee publieke convertertypen om zelf te registreren.
    /// De fout die zich aanbiedt is niet "de converter is verkeerd" maar "er is er één van de twee
    /// geregistreerd": <c>DateTime</c> komt niet in de contracttypen voor, maar wel in de
    /// structured-logging-state die een agent meestuurt, en die state gaat door dezelfde serializer
    /// naar hetzelfde document. Zo'n halve registratie is hier onmogelijk te maken door er één
    /// aanroep van te maken.
    /// </remarks>
    public static void Register(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Converters.Add(new UtcDateTimeOffsetConverter());
        options.Converters.Add(new UtcDateTimeConverter());
    }

    /// <summary>
    /// Bewijst dat <paramref name="options"/> tijdstempels canoniek wegschrijft. Bedoeld om aan te
    /// roepen vlak nadat de opties zijn samengesteld, vóór de eerste schrijfactie.
    /// </summary>
    /// <param name="options">Precies de opties waarmee straks geschreven wordt.</param>
    /// <exception cref="ArgumentNullException">Als <paramref name="options"/> <c>null</c> is.</exception>
    /// <exception cref="InvalidOperationException">Als een tijd niet canoniek wordt geschreven.</exception>
    /// <remarks>
    /// <para><strong>Waarom een assertie naast de tests.</strong> Een converter is geen slot. Wie
    /// een eigen <c>JsonSerializerOptions</c> samenstelt en <see cref="Register"/> vergeet, krijgt
    /// geen fout maar de verkeerde vorm — en die is aan het document niet af te zien. Dat is niet
    /// hypothetisch: precies zo schreef de schrijfkant van het portaal
    /// <c>2026-08-20T15:04:05.678+00:00</c> weg. Deze assertie loopt bij het opstarten, kost vier
    /// serialisaties, en gaat af op de plek waar de opties gemaakt worden in plaats van bij de
    /// eerste lijst die verkeerd sorteert.</para>
    ///
    /// <para><strong>De opties gaan als parameter mee en dat is het hele punt.</strong> Een
    /// assertie over een eigen, elders opgebouwde set opties bewijst iets over die set en niets
    /// over wat de aanroeper gebruikt. Alleen de opties die daadwerkelijk naar de opslag schrijven
    /// zeggen iets. Vandaar ook dat er geen parameterloze variant bestaat.</para>
    ///
    /// <para><strong>Er wordt ook op volgorde getoetst, niet alleen op vorm.</strong> Een controle
    /// op lengte en een afsluitende <c>Z</c> laat een formaat als <c>dd-MM-yyyy…</c> ongemoeid
    /// doorlopen: 28 tekens, eindigt op <c>Z</c>, sorteert volstrekt verkeerd. De eigenschap waar
    /// het om gaat is dat tekstvergelijking dezelfde volgorde geeft als tijdvergelijking, dus dat
    /// wordt hier ook letterlijk gemeten.</para>
    ///
    /// <para><strong>De proefmomenten zijn gekozen en niet gegrepen, want de eerste keer waren ze
    /// gegrepen.</strong> Deze volgordecontrole stond er met vier momenten uit één werkdag in
    /// augustus, en die vier sorteerden op tekst toevallig hetzelfde als op tijd — óók zonder enige
    /// normalisatie. De controle stond er dus, las als dekking, en kon niet afgaan. Gemeten met de
    /// standaardopties van <c>System.Text.Json</c>:</para>
    ///
    /// <code>
    /// 2026-08-19T15:04:05+00:00        ← oude proef: op tekst en op tijd dezelfde volgorde
    /// 2026-08-19T15:13:19.9449045+00:00
    /// 2026-08-19T17:14:00+02:00
    /// 2026-08-20T00:00:00+00:00
    /// </code>
    ///
    /// <para>De reeks hieronder is zo gekozen dat elk van de drie foute vormen die we in het wild
    /// zagen of konden bedenken de volgorde wél omgooit, en dat is per vorm gemeten. Twee momenten
    /// in dezelfde seconde, één met en één zonder decimaaldeel, betrappen een formaat met
    /// wisselende breedte (<c>…:05Z</c> sorteert ná <c>…:05.678Z</c>). Een moment in +02:00 dat
    /// tússen twee UTC-momenten valt betrapt een niet-omgerekende offset. Een moment in een andere
    /// maand betrapt een formaat waarin de dag vooraan staat. Wie hier een moment weghaalt, haalt
    /// een van die drie gevallen weg.</para>
    ///
    /// <para><strong>Wat de proef bewust niet bevat.</strong> Een <c>DateTime</c> met
    /// <c>Kind.Unspecified</c> of <c>Kind.Local</c>. De uitkomst daarvan hangt van de tijdzone van
    /// de machine af, en een assertie die op de bouwserver iets anders zegt dan op een laptop is
    /// geen assertie. Zie de opmerking bij <see cref="UtcDateTimeConverter"/> voor wat er met die
    /// twee gebeurt.</para>
    /// </remarks>
    public static void AssertCanonical(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Bewust de vormen uit het foute document: een moment in +02:00, hetzelfde moment in UTC
        // zonder decimalen, en een moment met alle zeven decimalen gevuld. De eerste twee zijn
        // hetzelfde punt in de tijd en moeten dus dezelfde tekst opleveren — zolang dat niet zo is,
        // bestaan er twee spellingen van één moment en is sorteren op tekst kansspel.
        var offset = new DateTimeOffset(2026, 8, 19, 17, 14, 0, TimeSpan.FromHours(2));
        var utc = new DateTimeOffset(2026, 8, 19, 15, 14, 0, TimeSpan.Zero);
        var precise = new DateTimeOffset(2026, 8, 19, 15, 13, 19, 944, TimeSpan.Zero).AddTicks(9045);

        Require(options, offset, "2026-08-19T15:14:00.0000000Z", "een tijd met offset");
        Require(options, utc, "2026-08-19T15:14:00.0000000Z", "een tijd in UTC zonder decimalen");

        // Het nullable pad apart, want de portaaldocumenten gebruiken het (changedAt) en een
        // converter voor DateTimeOffset dekt DateTimeOffset? alleen doordat de serializer dat zelf
        // doorgeeft. Dat is gedrag van de bibliotheek en geen belofte van onze kant.
        Require<DateTimeOffset?>(options, precise, "2026-08-19T15:13:19.9449045Z", "een nullable tijd");

        // En het DateTime-pad, dat via de logstate van een agent in hetzelfde document komt. Twee
        // waarden en niet één, en dat is geen grondigheid maar een reparatie: hier stond alleen de
        // waarde met zeven gevulde decimalen, en juist díe schrijft System.Text.Json van zichzelf al
        // als "2026-08-19T15:13:19.9449045Z" — 28 tekens, afsluitende Z, canoniek. Deze assertie kon
        // dus niet zien dat de DateTime-converter ontbrak. Een waarde waarvan het decimaaldeel
        // eindigt op nullen wél: die trimt de standaard tot ".944Z" (24 tekens).
        Require(
            options,
            new DateTime(2026, 8, 19, 15, 13, 19, 944, DateTimeKind.Utc).AddTicks(9045),
            "2026-08-19T15:13:19.9449045Z",
            "een DateTime in UTC met zeven decimalen");

        Require(
            options,
            new DateTime(2026, 8, 19, 15, 13, 19, 944, DateTimeKind.Utc),
            "2026-08-19T15:13:19.9440000Z",
            "een DateTime in UTC met afsluitende nullen");

        // De eigenschap zelf: tekst sorteren moet hetzelfde zijn als tijd sorteren. Dit is wat de
        // vorm moet opleveren; de vorm zelf is slechts het middel. Zie de opmerking bij deze methode
        // voor waarom het precies deze vijf momenten zijn en niet vier willekeurige uit één dag.
        DateTimeOffset[] moments =
        [
            // Dezelfde seconde, met en zonder decimaaldeel: betrapt een wisselende breedte.
            new DateTimeOffset(2026, 8, 19, 15, 4, 5, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 15, 4, 5, 678, TimeSpan.Zero),

            // 15:14 UTC, opgeschreven als 17:14+02:00, valt tussen de twee UTC-momenten om hem
            // heen: betrapt een offset die niet is omgerekend.
            offset,
            new DateTimeOffset(2026, 8, 19, 15, 20, 0, TimeSpan.Zero),

            // Een andere maand: betrapt een formaat waarin de dag vooraan staat.
            new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
        ];

        string[] asText = [.. moments.Select(moment => Text(options, moment)).Order(StringComparer.Ordinal)];
        string[] asTime = [.. moments.Order().Select(moment => Text(options, moment))];

        if (Array.Exists(asText, text => text.Length != Width))
        {
            throw new InvalidOperationException(
                $"Niet elk tijdstempel is {Width} tekens lang: " + string.Join(", ", asText) +
                ". Tekst van ongelijke lengte vergelijkt Cosmos anders dan je verwacht — een " +
                "ontbrekend decimaaldeel sorteert ná een aanwezig decimaaldeel.");
        }

        if (!asText.SequenceEqual(asTime, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Tijdstempels worden in een vorm geschreven waarin lexicografisch sorteren niet " +
                "gelijk is aan chronologisch sorteren. Cosmos vergelijkt deze velden als tekst, dus " +
                "elke ORDER BY erop geeft een verkeerde volgorde zonder een fout te melden. Op tekst: " +
                string.Join(", ", asText) + " — chronologisch: " + string.Join(", ", asTime) + ".");
        }
    }

    private static void Require<T>(JsonSerializerOptions options, T value, string expected, string what)
    {
        string actual = Text(options, value);
        if (actual == expected)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Tijdstempels gaan niet canoniek de opslag in: {what} werd '{actual}' en verwacht was " +
            $"'{expected}'. Cosmos vergelijkt deze velden als tekst, dus met twee vormen door elkaar " +
            "sorteert elke lijst die erop sorteert stil verkeerd. Registreer de normalisatie met " +
            $"{nameof(TimestampNormalization)}.{nameof(Register)} op precies de opties waarmee " +
            "geschreven wordt.");
    }

    /// <summary>De tekst zoals die in het document zou belanden, zonder de aanhalingstekens.</summary>
    private static string Text<T>(JsonSerializerOptions options, T value) =>
        JsonSerializer.Serialize(value, options).Trim('"');

    /// <summary>
    /// Schrijft elke <c>DateTimeOffset</c> als UTC in <see cref="UtcFormat"/>, ongeacht de offset
    /// waarin hij is aangeleverd.
    /// </summary>
    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(
                reader.GetString() ?? string.Empty,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(ToCanonical(value));
    }

    /// <summary>
    /// Hetzelfde voor <c>DateTime</c>. Die komt niet in de contracttypen voor, maar wel in de
    /// structured-logging-state die een agent meestuurt, en daar moet dezelfde vorm gelden.
    /// </summary>
    /// <remarks>
    /// <c>ToUniversalTime()</c> rekent een <c>DateTime</c> met <c>Kind.Unspecified</c> om alsóf hij
    /// lokale tijd is, terwijl de leeskant hem als UTC opvat. Op een machine in UTC — zoals de
    /// container waarin een agent draait — valt dat samen; op een laptop in Nederland verschuift het
    /// moment twee uur. Dat is met deze verhuizing bewust niet veranderd: het gedrag is ouder dan
    /// deze reparatie, het raakt alleen vrije logvelden van een agent en geen contractveld, en een
    /// stille gedragswijziging op een schrijfpad hoort niet mee te liften op een sorteerreparatie.
    /// Het staat als los punt opgeschreven.
    /// </remarks>
    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.Parse(
                reader.GetString() ?? string.Empty,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture));
    }
}
