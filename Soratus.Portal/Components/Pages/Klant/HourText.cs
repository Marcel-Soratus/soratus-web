using System.Globalization;
using Soratus.Portal.Data;

namespace Soratus.Portal.Components.Pages.Klant;

/// <summary>
/// De woorden, de getalvormen en de paden die het urenscherm (§3.6) gebruikt.
/// </summary>
/// <remarks>
/// <para>Dit is presentatie en geen rekenwerk. Elk getal komt uit <see cref="HourBalance"/> of uit
/// een viewmodel; hier wordt het alleen in de juiste vorm gezet. Dezelfde afspraak en dezelfde plek
/// als <see cref="AgentText"/> en <see cref="Pages.ContractText"/>: een klasse in de paginamap, want
/// het is geen component en alleen deze schermen gebruiken hem.</para>
///
/// <para><strong>De getalvormen komen uit <see cref="Pages.ContractText"/> en worden hier niet
/// nagebouwd.</strong> Dat is geen luiheid maar de enige manier waarop "12,5" op de contractkaart en
/// "12,5 u" op het urenscherm hetzelfde getal blijven. Het urenscherm leest de bundel en het
/// uurtarief uit hetzelfde contract, dus twee opmaakfuncties zouden betekenen dat de bundel op het
/// ene scherm "12" en op het andere "12,00" heet.</para>
///
/// <para><strong>Nergens een <c>?? 0</c>.</strong> Elke methode die een <c>decimal?</c> aanneemt
/// geeft bij <c>null</c> een streepje of niets terug, en nooit een nul. Zie punt 15 van
/// <c>docs/agent-portal/fase-0-afwijkingen.md</c>: bij een saldo is het verschil tussen "niet
/// afgesproken" en "nul" precies het verschil dat geld kost, en dit is de laag waar dat verschil
/// stil kan sneuvelen omdat er verder niets meer mee wordt gerekend.</para>
/// </remarks>
internal static class HourText
{
    /// <summary>De naam van het queryveld waarmee de specificatie op één maand wordt gefilterd.</summary>
    public const string MonthQuery = "maand";

    /// <summary>De naam van het queryveld dat de hele historie openklapt (§3.6, "Alle maanden").</summary>
    public const string AllQuery = "alle";

    /// <summary>De naam van het queryveld met het jaartal.</summary>
    public const string YearQuery = "jaar";

    /// <summary>De naam van het queryveld met de regel die beoordeeld wordt. Operator-only.</summary>
    public const string JudgeQuery = "beoordeel";

    /// <summary>De naam van het queryveld met de beoordeling zelf. Operator-only.</summary>
    public const string ActionQuery = "actie";

    /// <summary>De waarde van <see cref="ActionQuery"/> waarmee een regel wordt gefiatteerd.</summary>
    public const string ApproveAction = "fiatteren";

    /// <summary>De waarde van <see cref="ActionQuery"/> waarmee een regel wordt afgewezen.</summary>
    public const string RejectAction = "afwijzen";

    /// <summary>Het streepje dat op de plek van een ontbrekende waarde staat.</summary>
    /// <remarks>
    /// Een streepje en geen lege cel: een lege cel laat de lezer denken dat het scherm niet klaar is
    /// met laden. Dezelfde keuze als in de toegangslijst van §3.5.
    /// </remarks>
    public const string Dash = "—";

    // ── Uren en saldi ───────────────────────────────────────────────────────────────────────────

    /// <summary>Een aantal uren, met de eenheid erachter.</summary>
    /// <param name="hours">Het aantal.</param>
    /// <returns>Bijvoorbeeld <c>12,5 u</c>.</returns>
    public static string Hours(decimal hours) => $"{ContractText.Number(hours)} u";

    /// <summary>
    /// Een aantal uren dat er niet hoeft te zijn.
    /// </summary>
    /// <param name="hours">Het aantal, of <c>null</c>.</param>
    /// <returns>Het aantal met eenheid, of <see cref="Dash"/>.</returns>
    /// <remarks>
    /// Hier zit punt 15. Een <c>null</c>-bundel is geen bundel van nul uur, en een klant die "0 u"
    /// in zijn bundelkolom leest, leest een afspraak die niemand heeft gemaakt. Het streepje zegt
    /// het enige dat waar is: er staat niets.
    /// </remarks>
    public static string Hours(decimal? hours) => hours is { } value ? Hours(value) : Dash;

    /// <summary>
    /// Een saldo, met een teken ervoor.
    /// </summary>
    /// <param name="balance">Het saldo, of <c>null</c> als er geen bundel is vastgelegd.</param>
    /// <returns>Bijvoorbeeld <c>+2,5 u</c> of <c>-4 u</c>, of <see cref="Dash"/>.</returns>
    /// <remarks>
    /// <para>Het plusteken staat er expliciet bij, want zonder teken is "2,5 u" in een saldokolom
    /// niet te onderscheiden van "-2,5 u" op een smal scherm waar het minteken tegen de vorige kolom
    /// aan staat. Het minteken komt uit het getal zelf, dus het is een gewoon koppelteken en geen
    /// typografisch minteken: dit getal wordt gekopieerd naar een mail over een factuur.</para>
    ///
    /// <para>Exact nul krijgt een plus. Dat is geen afronding maar de betekenis: de bundel is precies
    /// op, en dat valt binnen de afspraak (zie <see cref="HourBalance.Balance"/>).</para>
    /// </remarks>
    public static string Balance(decimal? balance) => balance is { } value
        ? value >= 0m ? $"+{Hours(value)}" : Hours(value)
        : Dash;

    /// <summary>
    /// Wat er in de tooltip van het maandtotaal staat: waaruit dat getal is opgebouwd (§3.6).
    /// </summary>
    /// <param name="balance">De maandstand.</param>
    /// <returns>De tooltip.</returns>
    /// <remarks>
    /// <para><strong>Dit is de tooltip die §3.6 vraagt, en de correctie staat erin als bijdrage en
    /// niet als afwijking.</strong> §3.6 vraagt om een melding dat er "handmatig gecorrigeerd" is,
    /// en de mockup zet daar het verschil tussen twee getallen in — een override tegenover de som van
    /// de specificatie. Die twee getallen bestaan hier niet: een correctie ís een regel in de
    /// specificatie (punt 16), dus het totaal is en blijft de som. Wat er dan te melden valt is
    /// hoeveel van dat totaal uit correcties komt, en dat is
    /// <see cref="HourBalance.CorrectionHours"/>.</para>
    ///
    /// <para>Voor beide rollen dezelfde tekst. Een klant hoort te weten dat er in zijn maand met de
    /// hand is bijgesteld — de correctierij staat immers ook op zijn scherm, anders telt zijn
    /// specificatie niet op tot zijn maandtotaal.</para>
    /// </remarks>
    public static string MonthTitle(HourBalance balance)
    {
        ArgumentNullException.ThrowIfNull(balance);

        var basis = $"{Hours(balance.Booked)} uit {Rows(balance.EntryCount)}";

        return balance.HasCorrection
            ? $"{basis}, waarvan {Hours(balance.CorrectionHours)} handmatig gecorrigeerd"
            : basis;
    }

    /// <summary>Een aantal regels, in woorden.</summary>
    /// <param name="count">Het aantal.</param>
    /// <returns>Bijvoorbeeld <c>4 regels</c>.</returns>
    public static string Rows(int count) => count == 1 ? "1 regel" : $"{count} regels";

    /// <summary>
    /// Wat de uren boven de bundel gaan kosten, of <c>null</c> als daar niets over te zeggen is.
    /// </summary>
    /// <param name="overBundle">De uren boven bundel, of <c>null</c>.</param>
    /// <param name="rate">Het uurtarief buiten de bundel, of <c>null</c>.</param>
    /// <param name="isInternal">Of dit de interne beheerklant is.</param>
    /// <returns>Bijvoorbeeld <c>4 u × € 137,50 = € 550,00</c>, of <c>null</c>.</returns>
    /// <remarks>
    /// <para>Vier keer <c>null</c> in plaats van een bedrag, en elke keer om een andere reden die
    /// niet als nul mag verschijnen: er is geen bundel (dus geen overschrijding te berekenen), er is
    /// geen overschrijding, er is geen tarief afgesproken, of het is de interne beheerklant en er
    /// wordt niets doorbelast. Een <c>€ 0,00</c> zou in alle vier de gevallen liegen.</para>
    ///
    /// <para>De rekensom staat er zichtbaar bij en niet alleen de uitkomst. Dit bedrag komt op een
    /// factuur; wie het niet kan navertellen kan het niet controleren.</para>
    /// </remarks>
    public static string? OverBundleCost(decimal? overBundle, decimal? rate, bool isInternal)
    {
        if (isInternal || overBundle is not { } hours || hours <= 0m || rate is not { } price)
        {
            return null;
        }

        return $"{Hours(hours)} × € {ContractText.Amount(price)} = € {ContractText.Amount(hours * price)}";
    }

    /// <summary>
    /// De dag waarop een urenregel is vastgelegd.
    /// </summary>
    /// <param name="recordedOn">De dag.</param>
    /// <returns>Bijvoorbeeld <c>12-08-2026</c>.</returns>
    /// <remarks>
    /// <para>Via <see cref="Pages.ContractText.Date(DateOnly?)"/> en niet met een eigen patroon: de
    /// ingangsdatum van een contract en de dag van een urenregel horen op hetzelfde scherm van
    /// dezelfde klant in dezelfde vorm te staan.</para>
    ///
    /// <para>Niet nullable, en er is dus ook geen streepje. <c>RecordedOn</c> is een
    /// <see cref="DateOnly"/> die altijd bestaat: dit is de dag waarop de regel in de administratie
    /// is gezet, en die kent het document altijd. De kolom heet daarom <em>Geboekt</em> en niet
    /// "Datum" — dat laatste zou beloven dat het de dag is waarop het werk is gedaan, en die kent
    /// een urenregel niet.</para>
    /// </remarks>
    public static string RecordedOn(DateOnly recordedOn) =>
        ContractText.Date(recordedOn) ?? Dash;

    /// <summary>
    /// Het <c>datetime</c>-attribuut van de <c>&lt;time&gt;</c> bij die dag.
    /// </summary>
    /// <param name="recordedOn">De dag.</param>
    /// <returns><c>yyyy-MM-dd</c>.</returns>
    /// <remarks>
    /// Een kalenderdatum en geen moment, dus zonder zone en zonder tijd. Dat is machineleesbaar en
    /// het beweert niets over een tijdstip; punt 7 van de afwijkingennotitie houdt het
    /// <c>datetime</c>-attribuut van een <em>moment</em> in UTC, en een dag is geen moment.
    /// </remarks>
    public static string Iso(DateOnly recordedOn) =>
        recordedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// De meta-regel onder de omschrijving: de categorie, en bij een weergave over meer maanden ook
    /// de maand.
    /// </summary>
    /// <param name="category">De categorie.</param>
    /// <param name="monthLabel">Het maandlabel.</param>
    /// <param name="withMonth">Of de maand erbij hoort te staan.</param>
    /// <returns>Bijvoorbeeld <c>Ontwikkeling · augustus 2026</c>.</returns>
    /// <remarks>
    /// De maand staat er alleen bij als de specificatie meer dan één maand kan bevatten. In de
    /// standaardweergave — één maand, en die maand staat in de tabel erboven — zou hij op elke rij
    /// hetzelfde zeggen, en dat is de witruimte zonder doel die §1 verbiedt.
    /// </remarks>
    public static string EntryMeta(string category, string monthLabel, bool withMonth) =>
        withMonth ? $"{category} · {monthLabel}" : category;

    /// <summary>
    /// Hoeveel uren er in een maand nog te fiatteren liggen, in woorden (§3.6, operator-only).
    /// </summary>
    /// <param name="hours">Het aantal uren.</param>
    /// <param name="count">Uit hoeveel regels ze komen.</param>
    /// <returns>Bijvoorbeeld <c>+ 3 u · 2 regels</c>.</returns>
    /// <remarks>
    /// §3.6 schrijft "+ x u te fiatteren". Het woord "te fiatteren" staat hier niet in de waarde
    /// maar in de kolomkop, zodat het niet op elke rij herhaald wordt. Het plusteken blijft, want
    /// dat is wat deze uren met het maandtotaal zouden doen.
    /// </remarks>
    public static string Pending(decimal hours, int count) =>
        $"+ {Hours(hours)} · {Rows(count)}";

    // ── Paden ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Het pad van het urenscherm van deze klant, zonder query.</summary>
    /// <param name="slug">De klantslug.</param>
    /// <returns>Bijvoorbeeld <c>/klant/bakker/uren</c>.</returns>
    /// <remarks>
    /// De slug wordt geëscaped, ook al is hij volgens <c>PortalCustomerId</c> altijd veilig. Hij
    /// komt hier binnen als tekst uit een viewmodel, en een pad dat op het formaat van zijn invoer
    /// vertrouwt is een pad dat stil breekt zodra dat formaat verandert.
    /// </remarks>
    public static string Path(string slug) => $"/klant/{Uri.EscapeDataString(slug)}/uren";

    /// <summary>De standaardweergave: alleen de huidige maand (§3.6).</summary>
    /// <param name="slug">De klantslug.</param>
    /// <returns>Het pad zonder query.</returns>
    public static string CurrentMonthPath(string slug) => Path(slug);

    /// <summary>De weergave met alle maanden van een jaar en het jaartotaal (§3.6).</summary>
    /// <param name="slug">De klantslug.</param>
    /// <param name="year">Het jaartal.</param>
    /// <returns>Bijvoorbeeld <c>/klant/bakker/uren?alle=1&amp;jaar=2026</c>.</returns>
    public static string YearPath(string slug, int year) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Path(slug)}?{AllQuery}=1&{YearQuery}={year:D4}");

    /// <summary>De weergave met de specificatie gefilterd op één maand (§3.6).</summary>
    /// <param name="slug">De klantslug.</param>
    /// <param name="month">De maand als <c>yyyy-MM</c>.</param>
    /// <returns>Bijvoorbeeld <c>/klant/bakker/uren?maand=2026-07</c>.</returns>
    /// <remarks>
    /// Het jaartal zit al in de maand, dus er staat geen <c>jaar</c> naast. Twee plekken waar
    /// hetzelfde jaartal staat is één plek te veel: bij <c>?maand=2026-07&amp;jaar=2025</c> is er
    /// geen goed antwoord, en dan moet iemand kiezen welke van de twee wint.
    /// </remarks>
    public static string MonthPath(string slug, string month) =>
        $"{Path(slug)}?{MonthQuery}={Uri.EscapeDataString(month)}";

    /// <summary>
    /// Het pad naar het beoordelen van één regel (operator-only).
    /// </summary>
    /// <param name="slug">De klantslug.</param>
    /// <param name="month">De maand van de regel, als <c>yyyy-MM</c>.</param>
    /// <param name="entryId">De documentsleutel van de regel.</param>
    /// <param name="action">
    /// <see cref="ApproveAction"/> of <see cref="RejectAction"/>.
    /// </param>
    /// <returns>Het pad.</returns>
    /// <remarks>
    /// <para>De maand van de <em>regel</em> en niet de maand die op het scherm stond. Zo landt de
    /// operator na zijn besluit op de maand waarin hij iets heeft veranderd, en ziet hij het
    /// maandtotaal dat hij net heeft beïnvloed. Dat is de enige plek waar het besluit te
    /// controleren valt.</para>
    ///
    /// <para>De regel-id staat in de URL en niet in een verborgen veld. Dat is geen slordigheid: een
    /// documentsleutel is een aanduiding en geen schrijfvoorwaarde, hij is deelbaar, en hij hoort bij
    /// de vraag "welke regel beoordeel ik" — die vraag staat in de URL, en het antwoord op "hoe zag
    /// hij eruit" komt uit een verse lezing. Zie de toelichting bovenaan <c>Uren.razor</c> over de
    /// etag.</para>
    /// </remarks>
    public static string JudgePath(string slug, string month, string entryId, string action) =>
        $"{MonthPath(slug, month)}&{JudgeQuery}={Uri.EscapeDataString(entryId)}"
        + $"&{ActionQuery}={Uri.EscapeDataString(action)}";
}
