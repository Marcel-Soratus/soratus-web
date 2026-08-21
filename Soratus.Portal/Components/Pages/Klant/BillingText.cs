using System.Globalization;
using Soratus.Portal.Components.Shared;
using Soratus.Portal.Data;

namespace Soratus.Portal.Components.Pages.Klant;

/// <summary>
/// De woorden, de getalvormen en de paden die het facturatiescherm (§3.7) gebruikt.
/// </summary>
/// <remarks>
/// <para>Presentatie en geen rekenwerk. Elk bedrag komt uit <see cref="MonthlyCharge"/> of uit een
/// viewmodel; hier wordt het alleen in de juiste vorm gezet. Dezelfde afspraak en dezelfde plek als
/// <see cref="HourText"/> en <see cref="Pages.ContractText"/>.</para>
///
/// <para><strong>De getalvormen komen uit <see cref="Pages.ContractText"/> en worden hier niet
/// nagebouwd.</strong> Het uurtarief op de contractkaart, het uurtarief in de tooltip van het
/// urenscherm en het uurtarief in deze berekening zijn hetzelfde getal uit hetzelfde contract; drie
/// opmaakfuncties zouden betekenen dat het op drie schermen anders staat.</para>
///
/// <para><strong>Nergens een <c>?? 0</c>.</strong> Elke methode die een <c>decimal?</c> aanneemt geeft
/// bij <c>null</c> een streepje terug en nooit <c>€ 0,00</c>. Dit is de laatste laag waar dat verschil
/// stil kan sneuvelen — hier wordt er verder niets meer met het getal gedaan, dus een <c>?? 0</c> hier
/// zou nergens meer opvallen en wél op de factuur staan. Zie punt 15 en
/// <see cref="AzureCostState"/>.</para>
/// </remarks>
internal static class BillingText
{
    /// <summary>De naam van het queryveld met het jaartal.</summary>
    /// <remarks>
    /// Dezelfde naam als op het urenscherm (<see cref="HourText.YearQuery"/>), en dat is geen toeval:
    /// een operator die van het urenscherm naar de facturatie van hetzelfde jaar gaat, hoort niet in
    /// een ander jaar te landen omdat het queryveld anders heet.
    /// </remarks>
    public const string YearQuery = HourText.YearQuery;

    /// <summary>De maand waarvan de uitsplitsing per dienst open staat. Operator-only.</summary>
    public const string MonthQuery = HourText.MonthQuery;

    /// <summary>Het streepje dat op de plek van een ontbrekende waarde staat.</summary>
    /// <remarks>
    /// Hetzelfde streepje als op het urenscherm en de contractkaart. Op dit scherm draagt het meer
    /// gewicht dan daar: hier betekent het "dit bedrag is niet gemeten", en dat is de mededeling waar
    /// dit hele onderdeel om gaat. Er staat daarom bij elke tabel een regel die dat uitspreekt; zie
    /// <see cref="Views.BillingNotice.OperatorAmountUnknown"/>.
    /// </remarks>
    public const string Dash = HourText.Dash;

    /// <summary>
    /// De ondergrens waaronder een bedrag als "kleiner dan een cent" wordt getoond.
    /// </summary>
    /// <remarks>
    /// Een halve cent: daaronder rondt <c>0.00</c> af op nul. Gemeten waarden die hieronder vallen zijn
    /// niet theoretisch — <c>Key Vault</c> kostte over een hele maand € 0,000242 en <c>Bandwidth</c>
    /// exact € 0. Die twee als hetzelfde tonen zou zeggen dat Key Vault niets kost.
    /// </remarks>
    private const decimal Cent = 0.005m;

    // ── Bedragen ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Een bedrag dat er niet hoeft te zijn.
    /// </summary>
    /// <param name="amount">Het bedrag, of <c>null</c>.</param>
    /// <returns>Bijvoorbeeld <c>€ 37,46</c>, of <c>&lt; € 0,01</c>, of <see cref="Dash"/>.</returns>
    /// <remarks>
    /// <para><strong>Drie uitkomsten en niet twee, en het middelste geval is gemeten.</strong> Een
    /// bedrag boven een halve cent staat er gewoon. <c>null</c> geeft een streepje: dat is "niet
    /// gemeten" en het is met opzet niet van "nul" te onderscheiden door erop te turen — de uitleg
    /// staat als regel onder de tabel, waar hij één keer staat in plaats van in twaalf tooltips.</para>
    ///
    /// <para>En een bedrag dat wél is gemeten maar kleiner is dan een cent, staat er als
    /// <c>&lt; € 0,01</c>. Zou dat <c>€ 0,00</c> worden, dan staat er een dienst in de uitsplitsing die
    /// niets kost, en dat is dezelfde onwaarheid als € 0,00 voor een onbekend bedrag — alleen kleiner.
    /// Een exacte nul komt wél voor (<c>Bandwidth</c>) en die krijgt <c>€ 0,00</c>, want dat is waar.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>De armen matchen met <c>{ }</c> en niet met <c>var</c>, en dat is hier geen stijl. In een
    /// <c>switch</c> over een <c>decimal?</c> vangt <c>var value</c> de <em>nullable</em> op, niet de
    /// waarde erin — dus zou een <c>?? 0m</c> of een <c>.Value</c> nodig zijn om er verder mee te
    /// rekenen, en dat is precies de plek waar besluit 15 sneuvelt: een <c>?? 0m</c> hier zou een
    /// onbekend bedrag als <c>€ 0,00</c> op het scherm zetten. Met <c>{ } value</c> is de waarde
    /// binnen de arm bewijsbaar niet-null en blijft <c>null</c> de enige arm die een streepje geeft.
    /// De nullable loopt dus door tot hier en verliest zijn "onbekend" nergens onderweg.</para>
    /// </remarks>
    public static string Amount(decimal? amount) => amount switch
    {
        null => Dash,
        0m => "€ 0,00",
        { } value when value > 0m && value < Cent => "< € 0,01",
        { } value when value < 0m && value > -Cent => "> -€ 0,01",
        { } value => $"€ {ContractText.Amount(value)}",
    };

    /// <summary>
    /// Een opslagpercentage, of een streepje als er niets is afgesproken.
    /// </summary>
    /// <param name="percentage">Het percentage, of <c>null</c>.</param>
    /// <returns>Bijvoorbeeld <c>8,75 %</c>, of <see cref="Dash"/>.</returns>
    /// <remarks>
    /// <strong><c>0 %</c> en een streepje zijn hier verschillende mededelingen.</strong> Nul procent
    /// opslag is een afspraak die we hebben gemaakt; geen opslag ingevuld is een afspraak die nog moet
    /// komen. Dat is besluit 15 op het veld waar het volgens dat besluit het gevaarlijkste is, en dit is
    /// de plek waar het verschil op het scherm terechtkomt.
    /// </remarks>
    public static string Percentage(decimal? percentage) =>
        percentage is { } value ? $"{ContractText.Number(value)} %" : Dash;

    /// <summary>
    /// De rekensom onder het door te belasten Azure-bedrag, voor de tooltip (operator-only).
    /// </summary>
    /// <param name="subtotal">Het subtotaal, of <c>null</c>.</param>
    /// <param name="percentage">Het opslagpercentage, or <c>null</c>.</param>
    /// <param name="surcharge">Het opslagbedrag, of <c>null</c>.</param>
    /// <param name="charged">Het door te belasten bedrag, of <c>null</c>.</param>
    /// <returns>Bijvoorbeeld <c>€ 37,46 + 8,75 % (€ 3,28) = € 40,74</c>, of <c>null</c>.</returns>
    /// <remarks>
    /// <c>null</c> zodra er iets ontbreekt, en dan staat er geen tooltip in plaats van een som met een
    /// streepje erin. Dezelfde keuze als bij <see cref="HourText.OverBundleCost"/>: de som staat er
    /// zichtbaar bij en niet alleen de uitkomst, want dit bedrag komt op een factuur en wie het niet
    /// kan navertellen kan het niet controleren.
    /// </remarks>
    public static string? ChargedSum(
        decimal? subtotal,
        decimal? percentage,
        decimal? surcharge,
        decimal? charged) =>
        subtotal is { } basis && percentage is { } pct && surcharge is { } margin && charged is { } total
            ? $"€ {ContractText.Amount(basis)} + {ContractText.Number(pct)} % "
              + $"(€ {ContractText.Amount(margin)}) = € {ContractText.Amount(total)}"
            : null;

    /// <summary>
    /// De rekensom onder het uurbedrag, voor de tooltip.
    /// </summary>
    /// <param name="overBundle">De uren boven bundel, of <c>null</c>.</param>
    /// <param name="rate">Het uurtarief, of <c>null</c>.</param>
    /// <returns>Bijvoorbeeld <c>4 u × € 137,50 = € 550,00</c>, of <c>null</c>.</returns>
    /// <remarks>
    /// Via <see cref="HourText.OverBundleCost"/> en niet met een eigen som: het bedrag voor uren boven
    /// bundel staat ook in de tooltip van het urenscherm, en twee sommen over hetzelfde bedrag kunnen
    /// uiteenlopen. <c>isInternal</c> staat hier vast op <c>false</c> omdat het facturatiescherm de
    /// interne klant al eerder afvangt: daar staat "niet doorbelast" in plaats van een bedrag.
    /// </remarks>
    public static string? HoursSum(decimal? overBundle, decimal? rate) =>
        HourText.OverBundleCost(overBundle, rate, isInternal: false);

    // ── Toestanden ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wat er van een maand bekend is, in één woord (operator-only).
    /// </summary>
    /// <param name="state">De toestand.</param>
    /// <returns>Het label.</returns>
    /// <remarks>
    /// <para><strong>Tekst en geen statusbadge.</strong> §1 houdt groen, amber en rood voor <em>status</em>
    /// — draait het of niet — en al het andere neutraal grijs. Dit is geen status maar een
    /// kennistoestand: "we weten het niet" is geen storing en "volledig gemeten" is geen gezondheid. Een
    /// badge in statuskleuren zou die twee vocabulaires door elkaar halen, en dan betekent amber op het
    /// ene scherm iets anders dan op het andere.</para>
    ///
    /// <para>Alleen voor de operator. De klant ziet aan het streepje of er een bedrag is en leest
    /// eronder wat dat betekent; de <em>reden</em> waarom Cost Management niets gaf is onze
    /// bedrijfsvoering.</para>
    /// </remarks>
    public static string StateLabel(AzureCostState state) => state switch
    {
        AzureCostState.Measured => "volledig gemeten",
        AzureCostState.Partial => "loopt nog",
        AzureCostState.NoLines => "geen regels",
        _ => "onbekend",
    };

    /// <summary>
    /// De tooltip bij <see cref="StateLabel"/>: wat die toestand betekent en wat hij niet betekent.
    /// </summary>
    /// <param name="state">De toestand.</param>
    /// <returns>De tooltip.</returns>
    /// <remarks>
    /// De tekst bij <see cref="AzureCostState.NoLines"/> is de belangrijkste van dit scherm en de enige
    /// die drie mogelijkheden noemt. Dat is niet omslachtig: die drie zijn gemeten, ze geven hetzelfde
    /// HTTP-antwoord, en de code kan ze niet uit elkaar halen. Wie dit leest kan dat wel — door naar de
    /// bevraagde omgeving eronder te kijken.
    /// </remarks>
    public static string StateTitle(AzureCostState state) => state switch
    {
        AzureCostState.Measured =>
            "De maand is afgelopen en volledig geboekt. Dit bedrag verandert niet meer.",
        AzureCostState.Partial =>
            "Er zijn bedragen, maar de maand is nog niet volledig geboekt. Dit bedrag is een "
            + "ondergrens en loopt nog op.",
        AzureCostState.NoLines =>
            "Cost Management gaf geen enkele regel terug. Dat kan drie dingen betekenen en het "
            + "antwoord is voor alle drie hetzelfde: er is werkelijk niets verbruikt, de periode is "
            + "nog niet geboekt, of de bevraagde omgeving bestaat niet. Daarom staat er geen bedrag en "
            + "geen nul.",
        _ =>
            "Er is voor deze maand geen geslaagde meting. Dat is niet hetzelfde als nul euro, dus er "
            + "staat geen bedrag.",
    };

    /// <summary>
    /// Waarom er geen totaal is, in gewone taal (operator-only).
    /// </summary>
    /// <param name="gap">De gaten.</param>
    /// <returns>De melding, of <c>null</c> als er niets in de weg staat.</returns>
    /// <remarks>
    /// <para>Alle gaten die gelden, en niet alleen de eerste. Een klant zonder contract mist er drie, en
    /// een operator die er één ziet gaat die oplossen en houdt dan een totaal dat nog steeds ontbreekt.
    /// Zie <see cref="MonthlyChargeGap"/>.</para>
    ///
    /// <para>Deze tekst noemt de beheeropslag met naam en staat daarom uitsluitend op de
    /// operatorweergave. De klantvariant staat als voorgeformuleerde tekst op
    /// <see cref="Views.CustomerChargeRow.TotalNotice"/> — juist omdat de klantrij de vlaggen niet
    /// draagt en er dus geen uitdrukking bestaat die dit hier per ongeluk oplevert.</para>
    /// </remarks>
    public static string? GapReason(MonthlyChargeGap gap)
    {
        if (gap == MonthlyChargeGap.None)
        {
            return null;
        }

        var parts = new List<string>(4);

        if (gap.HasFlag(MonthlyChargeGap.AzureUnknown))
        {
            parts.Add("het Azure-verbruik is niet gemeten");
        }

        if (gap.HasFlag(MonthlyChargeGap.NoSurchargeAgreed))
        {
            parts.Add("er is geen beheeropslag afgesproken");
        }

        if (gap.HasFlag(MonthlyChargeGap.NoBundleAgreed))
        {
            parts.Add("er is geen urenbundel vastgelegd");
        }

        if (gap.HasFlag(MonthlyChargeGap.NoRateAgreed))
        {
            parts.Add("er staan uren boven bundel maar er is geen uurtarief afgesproken");
        }

        return $"Geen totaal: {string.Join("; ", parts)}.";
    }

    /// <summary>
    /// Tot welke dag een meting loopt, voor de tooltip (operator-only).
    /// </summary>
    /// <param name="coversThrough">De laatste gedekte dag, of <c>null</c>.</param>
    /// <param name="measuredAt">Wanneer er is gemeten, of <c>null</c>.</param>
    /// <returns>De tooltip, of <c>null</c> als er niets te melden is.</returns>
    /// <remarks>
    /// Twee gegevens die bij elkaar horen en apart niets zeggen: "gemeten tot en met de 20e" zonder het
    /// meetmoment laat open of dat gisteren of vorige maand is, en het meetmoment zonder de gedekte dag
    /// laat open hoeveel van de maand erin zit. De gemeten vertraging tussen die twee is zeven tot tien
    /// uur; zie <see cref="AzureCostCompleteness"/>.
    /// </remarks>
    public static string? Coverage(DateOnly? coversThrough, DateTimeOffset? measuredAt)
    {
        if (coversThrough is not { } through)
        {
            return measuredAt is { } onlyMoment
                ? $"opgehaald op {TimeFormat.Absolute(onlyMoment)}, zonder bedragen"
                : null;
        }

        var day = ContractText.Date(through) ?? Dash;

        return measuredAt is { } moment
            ? $"bedragen tot en met {day}, opgehaald op {TimeFormat.Absolute(moment)}"
            : $"bedragen tot en met {day}";
    }

    // ── Paden ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Het pad van het facturatiescherm van deze klant, zonder query.</summary>
    /// <param name="slug">De klantslug.</param>
    /// <returns>Bijvoorbeeld <c>/klant/bakker/facturatie</c>.</returns>
    /// <remarks>
    /// De slug wordt geëscaped, om dezelfde reden als bij <see cref="HourText.Path"/>: hij komt hier
    /// binnen als tekst uit een viewmodel, en een pad dat op het formaat van zijn invoer vertrouwt
    /// breekt stil zodra dat formaat verandert.
    /// </remarks>
    public static string Path(string slug) => $"/klant/{Uri.EscapeDataString(slug)}/facturatie";

    /// <summary>Het facturatieoverzicht van één jaar.</summary>
    /// <param name="slug">De klantslug.</param>
    /// <param name="year">Het jaartal.</param>
    /// <returns>Bijvoorbeeld <c>/klant/bakker/facturatie?jaar=2026</c>.</returns>
    public static string YearPath(string slug, int year) =>
        string.Create(CultureInfo.InvariantCulture, $"{Path(slug)}?{YearQuery}={year:D4}");

    /// <summary>
    /// Het facturatieoverzicht met de uitsplitsing van één maand open (operator-only).
    /// </summary>
    /// <param name="slug">De klantslug.</param>
    /// <param name="year">Het jaartal.</param>
    /// <param name="month">De maand als <c>yyyy-MM</c>.</param>
    /// <returns>Bijvoorbeeld <c>/klant/bakker/facturatie?jaar=2026&amp;maand=2026-08</c>.</returns>
    /// <remarks>
    /// Het jaartal staat erbij, ook al zit het al in de maand. Dat is hier het omgekeerde van de keuze
    /// op het urenscherm (zie <see cref="HourText.MonthPath"/>), en met reden: daar <em>filtert</em> de
    /// maand de specificatie en bepaalt hij dus het jaar, hier <em>klapt</em> hij één rij open binnen
    /// een jaar dat blijft staan. Zou het jaar wegvallen, dan springt het overzicht naar het jaar van
    /// die maand zodra iemand een rij openklapt, en dat is een andere pagina dan waar hij op stond.
    /// </remarks>
    public static string MonthPath(string slug, int year, string month) =>
        $"{YearPath(slug, year)}&{MonthQuery}={Uri.EscapeDataString(month)}";
}
