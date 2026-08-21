using System.Globalization;

namespace Soratus.Portal.Data;

/// <summary>
/// De uitkomst van de volledigheidscontrole: wat we van een maand weten, en tot welke dag.
/// </summary>
/// <param name="State">De toestand.</param>
/// <param name="CoversThrough">
/// De laatste dag waarover er bedragen zijn, of <c>null</c> als er geen dag is.
/// </param>
public readonly record struct AzureCostVerdict(AzureCostState State, DateOnly? CoversThrough);

/// <summary>
/// Stelt vast of een maand Azure-verbruik volledig geboekt is (§3.7, facturatie achteraf).
/// </summary>
/// <remarks>
/// <para><strong>Waarom dit een controle is en geen later cronmoment.</strong> Het onderzoek in
/// <c>docs/agent-portal/fase-4-haalbaarheid.md</c> §6 stelt de vraag en geeft twee antwoorden: de
/// volledigheid controleren, of later draaien. Het tweede verplaatst de gok; het eerste haalt hem
/// weg. Dit is het eerste, en het maakt bovendien het draaimoment onschadelijk: een collector die om
/// 04:00 op de 1e loopt, krijgt hier <see cref="AzureCostState.Partial"/> te horen en factureert dus
/// niet — in plaats van een factuur met een halve dag Azure erin, die aan het bedrag niet te zien is.
/// </para>
///
/// <para><strong>De regel rust op datums en niet op een percentage, en dat is een gemeten
/// keuze.</strong> Het onderzoek adviseert te kijken of het bedrag van de laatste dag "in de lijn
/// ligt van de dagen ervoor". Dat is op de resource group <c>MBV</c> gemeten en het werkt daar
/// prachtig: negentien volle dagen lagen tussen € 1,87731 en € 1,87967 — een spreiding van 0,13% —
/// en de onvolledige dag stond op 95,97% van de mediaan. Een drempel zou dus ergens tussen 96% en
/// 99,9% moeten liggen.</para>
///
/// <para>En precies daarom is hij verworpen. Die drempel is gepast op een omgeving die elke dag
/// hetzelfde kost omdat er een App Service in staat die altijd aan is. Een klant met een agent die
/// één keer per week een batch draait, of met een Azure OpenAI-verbruik dat aan zijn drukte hangt,
/// heeft een dagspreiding die veel groter is dan 4% — en dan staat de controle permanent op
/// "onvolledig" of laat hij een halve dag door, afhankelijk van welke kant je de drempel op zet. Een
/// grens die op één klant is gekalibreerd en op de volgende het omgekeerde doet, is geen grens.</para>
///
/// <para><strong>Wat er in de plaats komt is de vertraging zelf, en die is gemeten.</strong> Op
/// 21 augustus 2026 om 06:55 UTC stond de 20e op 95,97% van een volle dag en ontbrak de 21e nog
/// helemaal. De boeking loopt dus ongeveer acht uur achter — beter dan de aangenomen 24 uur, maar
/// ruim genoeg om de laatste dag van een maand op de 1e om 06:00 nog niet volledig te hebben. De
/// regel hieronder eist daarom dat de meting minstens een volle dag ná het einde van de maand is
/// gedaan; dan is er meer dan een etmaal marge op een vertraging van acht uur.</para>
///
/// <para>Geen enkele methode leest de klok — <c>observedOn</c> komt als parameter binnen. Dezelfde
/// afspraak als in <see cref="HourBalanceCalculator"/>, en om dezelfde reden: een maandgrens is
/// anders niet te testen zonder tot volgende maand te wachten.</para>
/// </remarks>
public static class AzureCostCompleteness
{
    /// <summary>
    /// Hoeveel dagen ná het einde van de maand er gemeten moet zijn voordat de maand volledig heet.
    /// </summary>
    /// <remarks>
    /// <para>Twee, en dat is één dag meer dan het lijkt te vragen. De maand eindigt op de laatste dag;
    /// de dag daarna is de dag waarop de boeking van die laatste dag nog binnenkomt. Een meting <em>op</em>
    /// die dag kan de laatste dag dus onvolledig zien — gemeten: op de 21e om 06:55 UTC stond de 20e op
    /// 95,97%. Pas de dag daarna is er meer dan een etmaal verstreken sinds het laatste uur van de maand,
    /// en de gemeten vertraging is acht uur.</para>
    ///
    /// <para>Dit is de enige getalsmatige aanname in deze klasse en hij staat daarom als constante met
    /// zijn onderbouwing erbij. Loopt de vertraging van Cost Management ooit op boven een etmaal, dan
    /// hoort dit getal op grond van een nieuwe <em>meting</em> te verschuiven en niet op grond van een
    /// vermoeden.</para>
    /// </remarks>
    public const int SettlementDays = 2;

    /// <summary>
    /// Beoordeelt een maand op grond van de dagen waarover er bedragen zijn.
    /// </summary>
    /// <param name="month">De maand als <c>yyyy-MM</c>.</param>
    /// <param name="bookedDays">
    /// De dagen waarover Cost Management bedragen gaf. Dagen buiten <paramref name="month"/> worden
    /// genegeerd; dubbele dagen mogen erin zitten.
    /// </param>
    /// <param name="observedOn">
    /// De dag waarop de meting is gedaan, in de tijdzone van de lezer. Zie
    /// <see cref="Views.PortalTimeZone"/>.
    /// </param>
    /// <returns>De toestand en de laatste gedekte dag.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="month"/> is geen <c>yyyy-MM</c>. Dat is een fout in de aanroeper en geen
    /// gegeven uit de buitenwereld: de maand wordt door de collector samengesteld, niet ingetypt.
    /// </exception>
    /// <remarks>
    /// <para><strong>Een gat in de reeks is geen onvolledigheid.</strong> Deze methode kijkt alleen
    /// naar de láátste dag en niet naar de dagen ertussen, en dat is opnieuw de ambiguïteit uit
    /// <see cref="AzureCostState.NoLines"/> een niveau lager: Cost Management geeft voor een dag zonder
    /// kosten géén rij. Een klant wiens omgeving een dag uit stond heeft dus een echt gat, en dat gat
    /// is niet te onderscheiden van een dag die nog niet is geboekt. Zou een gat tot "onvolledig"
    /// leiden, dan is die klant nooit te factureren. Zou hij tot "volledig" leiden bij een gat aan het
    /// eind, dan factureren we een halve maand. Alleen de laatste dag bekijken lost precies het geval
    /// op dat wél te weten is.</para>
    ///
    /// <para><strong>Een dag buiten de maand wordt genegeerd en veroorzaakt geen fout.</strong> Zo'n
    /// dag betekent dat de bevraagde periode niet de maand was, en het gevolg van negeren is dat de
    /// maand er onvolledig uitziet. Dat is de veilige kant: liever een maand die niet gefactureerd
    /// wordt dan een maand met de kosten van een andere periode erin.</para>
    /// </remarks>
    public static AzureCostVerdict Judge(
        string month,
        IEnumerable<DateOnly> bookedDays,
        DateOnly observedOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(month);
        ArgumentNullException.ThrowIfNull(bookedDays);

        var (first, last) = Bounds(month);

        DateOnly? covers = null;

        foreach (var day in bookedDays)
        {
            if (day < first || day > last)
            {
                continue;
            }

            if (covers is null || day > covers.Value)
            {
                covers = day;
            }
        }

        if (covers is not { } through)
        {
            // Nul dagen. Niet nul euro — zie AzureCostState.NoLines: achter dit antwoord zitten drie
            // werkelijkheden en maar één ervan is nul.
            return new AzureCostVerdict(AzureCostState.NoLines, CoversThrough: null);
        }

        var settled = through >= last && observedOn >= last.AddDays(SettlementDays);

        return new AzureCostVerdict(
            settled ? AzureCostState.Measured : AzureCostState.Partial,
            through);
    }

    /// <summary>
    /// De eerste en de laatste dag van een maand.
    /// </summary>
    /// <param name="month">De maand als <c>yyyy-MM</c>.</param>
    /// <returns>De eerste en de laatste dag.</returns>
    /// <remarks>
    /// Via <see cref="DateTime.DaysInMonth"/> en niet met een tabel van maandlengtes: februari 2028
    /// heeft 29 dagen en een tabel die dat niet weet laat de laatste dag van die maand stil buiten de
    /// facturatie vallen.
    /// </remarks>
    public static (DateOnly First, DateOnly Last) Bounds(string month)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(month);

        if (!DateOnly.TryParseExact(
                month.Trim() + "-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var first))
        {
            throw new ArgumentException(
                $"'{month}' is geen maand in de vorm jjjj-mm. Deze waarde wordt door de collector "
                + "samengesteld en niet ingetypt, dus dit is een fout in de aanroeper en geen "
                + "onbruikbare invoer.",
                nameof(month));
        }

        return (first, first.AddDays(DateTime.DaysInMonth(first.Year, first.Month) - 1));
    }
}
