namespace Soratus.Portal.Data;

/// <summary>
/// Waarom er voor een maand geen door te belasten totaal is (§3.7).
/// </summary>
/// <remarks>
/// <para><strong>Vlaggen en geen enkele waarde, want er kunnen er twee tegelijk gelden.</strong> Een
/// klant zonder contract heeft geen bundel én geen opslagpercentage, en een lezer die alleen de
/// eerste reden ziet gaat die oplossen en houdt dan een totaal dat nog steeds ontbreekt.</para>
///
/// <para>Dit type bestaat zodat de melding op het scherm niet uit een reeks vergelijkingen in de
/// Razor komt. Dezelfde afspraak als bij de teksten in <see cref="Views.HoursNotice"/>: het
/// viewmodel draagt wat er te melden valt, de markup zet het neer.</para>
/// </remarks>
[Flags]
public enum MonthlyChargeGap
{
    /// <summary>Er is niets dat het totaal in de weg staat.</summary>
    None = 0,

    /// <summary>
    /// Het Azure-verbruik van deze maand is niet bekend.
    /// </summary>
    /// <remarks>
    /// Dat kan drie dingen zijn en het scherm hoort te zeggen welke: er is nooit gemeten, de meting
    /// is mislukt, of de meting gaf nul regels. Zie <see cref="AzureCostState"/>. Voor het totaal
    /// maakt het geen verschil — geen van de drie is een bedrag.
    /// </remarks>
    AzureUnknown = 1,

    /// <summary>
    /// Er is geen beheeropslag afgesproken, dus het door te belasten Azure-bedrag is niet te bepalen.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit is besluit 15 op de plek waar hij geld kost.</strong> Nul procent opslag is
    /// een afspraak; geen opslag ingevuld is een afspraak die nog moet komen. Een niet-nullable
    /// <c>decimal</c> zou de tweede stil als de eerste doorrekenen en dan is het door te belasten
    /// bedrag gelijk aan de inkoop — onze marge weg, zonder dat er iets aan het getal te zien is. Zie
    /// <see cref="ContractDocument.AzureSurchargePercentage"/>, dat om precies deze maand
    /// <c>decimal?</c> is.</para>
    /// </remarks>
    NoSurchargeAgreed = 2,

    /// <summary>
    /// Er is geen urenbundel vastgelegd, dus er is niet te bepalen hoeveel uren boven bundel liggen.
    /// </summary>
    /// <remarks>
    /// De vierde urenstand (punt 19) die in de facturatie doorwerkt: zonder bundel is er geen
    /// overschrijding, en "nul uur boven bundel" zou zeggen dat een klant binnen een afspraak valt
    /// die niet bestaat. Zie <see cref="HourBalance.OverBundleHours"/>.
    /// </remarks>
    NoBundleAgreed = 4,

    /// <summary>
    /// Er zijn uren boven bundel, maar er is geen uurtarief afgesproken.
    /// </summary>
    NoRateAgreed = 8,
}

/// <summary>
/// Wat één maand kost en wat er doorbelast wordt: Azure plus opslag, plus de uren boven bundel (§3.7).
/// </summary>
/// <remarks>
/// <para><strong>Elk bedrag is <c>decimal?</c> en geen enkele <c>null</c> wordt onderweg nul.</strong>
/// Dat is de regel van besluit 15, en dit type is de plek waar hij het meest kost: hier komen vier
/// getallen samen die elk om een eigen reden kunnen ontbreken, en een <c>?? 0m</c> op één ervan
/// levert een totaal op dat te laag is en er geloofwaardig uitziet.</para>
///
/// <para><strong>Eén onbekende maakt het totaal onbekend.</strong> Dat is een keuze en het is de
/// strengste van de mogelijke keuzes. Het alternatief — de bekende delen optellen en de onbekende
/// weglaten — levert een bedrag op dat een mens niet van een compleet bedrag kan onderscheiden. §3.7
/// zet Azure en de uren boven bundel bovendien uitdrukkelijk "op één totaal", en een totaal waarvan
/// de helft ontbreekt is dat niet.</para>
///
/// <para><strong>De getallen op het scherm tellen op, door constructie.</strong> Het afronden gebeurt
/// op de bedragen die een mens natelt: het subtotaal, de opslag, het uurbedrag. Het door te belasten
/// Azure-bedrag is de som van de eerste twee <em>afgeronde</em> bedragen en niet een eigen afronding
/// van het product, en het totaal is de som van de afgeronde delen. Dat kan een cent afwijken van de
/// exacte uitkomst, en dat is de goede kant van de ruil: een kolom die niet optelt maakt een lezer
/// terecht wantrouwig over het hele scherm.</para>
///
/// <para>Puur, en zonder klok — zie <see cref="MonthlyChargeCalculator"/>.</para>
/// </remarks>
public sealed record MonthlyCharge
{
    /// <summary>De maand als <c>yyyy-MM</c>.</summary>
    public required string Month { get; init; }

    /// <summary>Het maandlabel, bijvoorbeeld <c>augustus 2026</c>.</summary>
    public required string MonthLabel { get; init; }

    /// <summary>Wat er van het Azure-verbruik bekend is.</summary>
    public required AzureCostState AzureState { get; init; }

    /// <summary>
    /// Het Azure-subtotaal, afgerond op centen, of <c>null</c> als het niet bekend is.
    /// </summary>
    /// <remarks>Onafgerond staat hij op <see cref="AzureCostReading.Subtotal"/>.</remarks>
    public decimal? AzureSubtotal { get; init; }

    /// <summary>
    /// Het afgesproken opslagpercentage, of <c>null</c> als er niets is afgesproken. Operator-only.
    /// </summary>
    public decimal? SurchargePercentage { get; init; }

    /// <summary>Het opslagbedrag, of <c>null</c>. Operator-only.</summary>
    public decimal? SurchargeAmount { get; init; }

    /// <summary>
    /// Het door te belasten Azure-bedrag: subtotaal plus opslag, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Dit bedrag mag de klant zien (§2: "Facturatie: bedragen en status — ja"); de opbouw eronder
    /// niet. Zie <see cref="Views.CustomerChargeRow"/>.
    /// </remarks>
    public decimal? AzureCharged { get; init; }

    /// <summary>De uren boven de bundel, of <c>null</c> als er geen bundel is vastgelegd.</summary>
    public decimal? OverBundleHours { get; init; }

    /// <summary>Het uurtarief buiten de bundel, of <c>null</c>.</summary>
    public decimal? HourlyRate { get; init; }

    /// <summary>
    /// Wat de uren boven bundel kosten, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <strong>Nul uren boven bundel kost nul euro, ook zonder afgesproken tarief.</strong> Dat is
    /// geen slordigheid maar het enige juiste antwoord: bij een klant die binnen zijn bundel blijft
    /// valt er niets te factureren, en dan is het ontbreken van een tarief geen belemmering. Het
    /// tarief is pas nodig zodra er iets boven de bundel staat, en dan is het ontbreken ervan wél een
    /// blokkade — zie <see cref="MonthlyChargeGap.NoRateAgreed"/>.
    /// </remarks>
    public decimal? HoursAmount { get; init; }

    /// <summary>
    /// Het totaal dat achteraf wordt gefactureerd, of <c>null</c> als er iets onbekend is.
    /// </summary>
    /// <remarks>
    /// §3.7: Azure en de extra uren staan "op één totaal, achteraf gefactureerd". Dit is dat totaal.
    /// <c>null</c> betekent dat het er niet is; zie <see cref="Gap"/> voor waarom.
    /// </remarks>
    public decimal? Total { get; init; }

    /// <summary>Waarom er geen totaal is. <see cref="MonthlyChargeGap.None"/> als er wel een is.</summary>
    public required MonthlyChargeGap Gap { get; init; }

    /// <summary>
    /// Of dit de interne beheerklant is, die niet wordt doorbelast (§4).
    /// </summary>
    /// <remarks>
    /// Bij <c>true</c> zijn alle bedragen behalve <see cref="AzureSubtotal"/> <c>null</c>, en niet
    /// nul. Het verbruik is een feit en hoort gemeten te blijven — de beheeragents draaien ergens en
    /// dat kost geld — maar er is niets om door te belasten, en € 0,00 zou zeggen dat we een factuur
    /// van nul sturen. Dezelfde vorm als
    /// <see cref="Components.Pages.ContractText.Rate(decimal?, bool)"/>, dat om dezelfde reden
    /// "intern — niet doorbelast" zegt in plaats van een bedrag.
    /// </remarks>
    public required bool IsInternal { get; init; }

    /// <summary>Of er een totaal is dat gefactureerd kan worden.</summary>
    public bool HasTotal => Total is not null;

    /// <summary>
    /// Of het tijdvak volledig geboekt is: de maand is om en Cost Management is klaar.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit staat apart van <see cref="IsFinal"/> omdat het twee verschillende dingen
    /// zijn, en dat verschil is bij het bouwen gevonden en niet bedacht.</strong> De eerste opzet had
    /// alleen <see cref="IsFinal"/>, en die is <c>false</c> zodra er iets ontbreekt — een onvolledig
    /// tijdvak of een ontbrekende contractafspraak. Het maandoverzicht per mail moet zeggen <em>welke
    /// van de twee</em>, en met één vlag kwam er "het tijdvak is nog niet volledig" uit bij een klant
    /// zonder contract van wie de maand allang volledig gemeten was. Dat is een ware uitkomst (er gaat
    /// geen mail) met een onware reden, en dan gaat een operator wachten op een meting die er
    /// al is.</para>
    ///
    /// <para>Afgeleid uit <see cref="AzureState"/> en niet als eigen veld, zodat de twee niet uiteen
    /// kunnen lopen. <see cref="IsFinal"/> gebruikt hem ook, om dezelfde reden.</para>
    /// </remarks>
    public bool IsPeriodComplete => AzureState == AzureCostState.Measured;

    /// <summary>
    /// Of dit bedrag definitief is: de maand is volledig gemeten en er is een totaal.
    /// </summary>
    /// <remarks>
    /// <para>De vraag die de facturatie-agent stelt, in één eigenschap. <c>false</c> voor de lopende
    /// maand — die staat volgens §3.7 bovenaan als concept — en <c>false</c> voor een afgesloten maand
    /// waarvan de laatste dag nog niet is geboekt. Zie <see cref="AzureCostCompleteness"/> voor
    /// waarom die tweede toestand bestaat en gemeten is.</para>
    ///
    /// <para>Voor de interne klant is dit nooit <c>true</c>: er valt niets te factureren. Dat volgt uit
    /// <see cref="HasTotal"/> en hoeft hier geen eigen regel.</para>
    /// </remarks>
    public bool IsFinal => IsPeriodComplete && HasTotal;
}

/// <summary>
/// Rekent het maandbedrag uit. Puur, en de enige plek waar dat gebeurt.
/// </summary>
/// <remarks>
/// <para><strong>Waarom dit één plek is.</strong> Hetzelfde argument als bij
/// <see cref="HourBalanceCalculator"/>: het scherm, het maandoverzicht per mail en de
/// facturatie-agent moeten van hetzelfde getal uitgaan. Zou het totaal in de weergave worden
/// samengesteld en in de conceptfactuur opnieuw, dan bestaan er twee definities van "wat deze maand
/// kost" — en de eerste keer dat ze verschillen is dat een factuur die niet overeenkomt met het
/// portaal waar de klant naar kijkt.</para>
///
/// <para><strong>Geen enkele parameter is een documenttype.</strong> Deze klasse kent
/// <see cref="AzureCostDocument"/> niet, <see cref="ContractDocument"/> niet en
/// <see cref="HourBalance"/> niet — ze neemt vier nullable getallen aan. Dat is niet uit netheid: het
/// maakt élke combinatie van ontbrekende gegevens in één regel testbaar, en dat is precies de
/// combinatoriek waar besluit 15 over gaat. Een berekening die documenten aanneemt, is alleen te
/// testen door documenten te bouwen, en dan test je de opbouw van het document mee.</para>
///
/// <para>Geen klok, om dezelfde reden als bij de uren: welke maand "de lopende" is, is een gegeven en
/// geen ontdekking.</para>
/// </remarks>
public static class MonthlyChargeCalculator
{
    /// <summary>
    /// Rekent het maandbedrag uit.
    /// </summary>
    /// <param name="month">De maand als <c>yyyy-MM</c>.</param>
    /// <param name="monthLabel">Het maandlabel voor het scherm.</param>
    /// <param name="azureState">Wat er van het Azure-verbruik bekend is.</param>
    /// <param name="azureSubtotal">
    /// Het onafgeronde Azure-subtotaal, of <c>null</c> als het niet bekend is. Zie
    /// <see cref="AzureCostReading.Subtotal"/> — en let op dat <c>null</c> hier de enige juiste
    /// waarde is voor een onbekend verbruik, en niet nul.
    /// </param>
    /// <param name="surchargePercentage">
    /// Het afgesproken opslagpercentage, of <c>null</c> als er niets is afgesproken.
    /// </param>
    /// <param name="overBundleHours">
    /// De uren boven bundel, of <c>null</c> als er geen bundel is vastgelegd. Uit
    /// <see cref="HourBalance.OverBundleHours"/>.
    /// </param>
    /// <param name="hourlyRate">Het uurtarief buiten de bundel, of <c>null</c>.</param>
    /// <param name="isInternal">Of dit de interne beheerklant is (§4).</param>
    /// <returns>Het maandbedrag, met de gaten erin benoemd.</returns>
    public static MonthlyCharge ForMonth(
        string month,
        string monthLabel,
        AzureCostState azureState,
        decimal? azureSubtotal,
        decimal? surchargePercentage,
        decimal? overBundleHours,
        decimal? hourlyRate,
        bool isInternal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(month);

        var subtotal = azureSubtotal is { } exact ? Cents(exact) : (decimal?)null;

        // De opslag rekent over het exacte subtotaal en niet over het afgeronde: dat is één afronding
        // in plaats van twee op elkaar. Wat er daarna wordt opgeteld zijn wél de afgeronde bedragen,
        // zodat de kolom op het scherm optelt. Zie de toelichting bij MonthlyCharge.
        var surcharge = azureSubtotal is { } basis && surchargePercentage is { } percentage
            ? Cents(basis * percentage / 100m)
            : (decimal?)null;

        var charged = isInternal || subtotal is null || surcharge is null
            ? (decimal?)null
            : subtotal + surcharge;

        // Nul uren boven bundel kost nul euro, ook zonder tarief. Zie MonthlyCharge.HoursAmount: bij
        // een klant binnen zijn bundel is er niets te factureren, en dan is een ontbrekend tarief geen
        // blokkade. Deze regel eerst, want anders valt dat geval in de tariefcontrole erna.
        var hours = isInternal ? null
            : overBundleHours is not { } over ? (decimal?)null
            : over <= 0m ? 0m
            : hourlyRate is { } rate ? Cents(over * rate)
            : null;

        var gap = MonthlyChargeGap.None;

        if (subtotal is null)
        {
            gap |= MonthlyChargeGap.AzureUnknown;
        }

        if (surchargePercentage is null)
        {
            gap |= MonthlyChargeGap.NoSurchargeAgreed;
        }

        if (overBundleHours is null)
        {
            gap |= MonthlyChargeGap.NoBundleAgreed;
        }
        else if (overBundleHours > 0m && hourlyRate is null)
        {
            gap |= MonthlyChargeGap.NoRateAgreed;
        }

        return new MonthlyCharge
        {
            Month = month,
            MonthLabel = monthLabel,
            AzureState = azureState,
            AzureSubtotal = subtotal,
            SurchargePercentage = surchargePercentage,
            SurchargeAmount = isInternal ? null : surcharge,
            AzureCharged = charged,
            OverBundleHours = overBundleHours,
            HourlyRate = hourlyRate,
            HoursAmount = hours,

            // Eén onbekende maakt het totaal onbekend. Geen "?? 0m", en geen deeltotaal: zie de
            // toelichting bij MonthlyCharge.Total.
            Total = charged is { } azure && hours is { } work ? azure + work : null,
            Gap = gap,
            IsInternal = isInternal,
        };
    }

    /// <summary>
    /// Rondt af op centen, zoals op een factuur.
    /// </summary>
    /// <param name="amount">Het bedrag.</param>
    /// <returns>Het bedrag op twee decimalen.</returns>
    /// <remarks>
    /// <para><see cref="MidpointRounding.AwayFromZero"/> en niet de standaard
    /// <see cref="MidpointRounding.ToEven"/>. Dat laatste is de standaard van <c>Math.Round</c> en het
    /// is voor statistiek de juiste keuze; voor een factuur is het de verkeerde, want daar is de
    /// afspraak dat een halve cent naar boven gaat. Het verschil is één cent en het valt op precies
    /// die facturen op waar iemand naneemt.</para>
    ///
    /// <para>De echte bedragen hebben vijftien cijfers achter de komma
    /// (<c>37,4563985414928</c>), dus deze afronding doet werkelijk iets.</para>
    /// </remarks>
    private static decimal Cents(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
