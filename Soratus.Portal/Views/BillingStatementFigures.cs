using Soratus.Portal.Data;
using Soratus.Portal.Mail;
using Soratus.Portal.Security;

namespace Soratus.Portal.Views;

/// <summary>
/// Levert de bedragen van het maandoverzicht per mail (§3.7) uit de facturatiekant aan.
/// </summary>
/// <remarks>
/// <para><strong>Dit is een naad en geen berekening, en er staat in deze klasse geen enkele
/// optelling.</strong> Elk bedrag komt uit <see cref="IBillingViews.BuildMonthAsync(CustomerWriteScope, string, CancellationToken)"/>
/// en daarmee uit <see cref="MonthlyChargeCalculator"/>. Zou hier iets worden opgeteld, dan bestaat er
/// een tweede definitie van "wat deze maand kost" en dan kan de mail een ander bedrag noemen dan het
/// scherm waar de klant naar kijkt. Dat is dezelfde regel die <c>MonthlyStatementFigures</c> aan zijn
/// eigen kant stelt, en hij hoort aan beide kanten van de naad te gelden — een naad waar één kant zich
/// aan de afspraak houdt is geen naad.</para>
///
/// <para><strong>Hij staat in <c>Views</c> en niet in <c>Mail</c>, en dat is een keuze over
/// eigendom.</strong> De betekenis van de bedragen zit hier: wat een <c>null</c> betekent, wanneer een
/// maand volledig is, welke gaten er zijn en welke daarvan de klant mag zien. Zou deze omzetting in
/// <c>Mail</c> staan, dan zou die map moeten weten wat
/// <see cref="AzureCostState"/> en <see cref="CustomerChargeGap"/> betekenen — en dan is de regel "de
/// mailkant consumeert en rekent nergens" een afspraak in plaats van een eigenschap.</para>
///
/// <para><strong>De vertaling gooit informatie weg en dat is de bedoeling.</strong> Drie
/// operatorgaten — geen opslag, geen bundel, geen tarief — zijn aan de klantkant al tot
/// <see cref="CustomerChargeGap.ContractIncomplete"/> samengevouwen (zie <see cref="BillingViews"/>),
/// en <see cref="StatementFigureGap"/> volgt die verdeling. Er is dus geen enkele weg waarlangs het
/// woord "opslag" een mail bereikt, ook niet als iemand hier een veld bij zet.</para>
/// </remarks>
internal sealed class BillingStatementFigures(IBillingViews views, ILogger<BillingStatementFigures> logger)
    : IMonthlyStatementFigures
{
    /// <inheritdoc />
    /// <remarks>
    /// <para><strong><c>null</c> betekent hier precies één ding: er is over deze maand nooit een
    /// kostenmeting gedaan.</strong> Dat is de betekenis die <see cref="IMonthlyStatementFigures"/>
    /// eraan geeft en het is een gewone uitkomst — een klant die halverwege de maand is aangesloten
    /// heeft geen meting over de maand ervoor. De mailkant weigert dan met
    /// <see cref="StatementRefusal.NoFigures"/>, en dat is waar.</para>
    ///
    /// <para><strong>Wat níet tot <c>null</c> leidt: een bedrag dat ontbreekt.</strong> Een maand met
    /// een geslaagde meting zonder regels, een mislukte meting, of een ontbrekende contractafspraak
    /// levert wél bedragen op — met <c>null</c> in de velden die niet bekend zijn en een
    /// <see cref="MonthlyStatementFigures.Gap"/> die zegt welke. Dat onderscheid is niet cosmetisch:
    /// "er is nooit gemeten" en "de meting gaf geen bedrag" vragen een verschillende handeling, en de
    /// tweede hoort met zijn reden op het operatorscherm te belanden in plaats van als afwezigheid.
    /// </para>
    /// </remarks>
    public async Task<MonthlyStatementFigures?> BuildStatementAsync(
        CustomerWriteScope scope,
        string month,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(month);

        var row = await views.BuildMonthAsync(scope, month, cancellationToken).ConfigureAwait(false);

        if (row.MeasuredAt is not { } measured)
        {
            logger.LogInformation(
                "Er is over {Month} geen kostenmeting van klant {CustomerId}, dus er zijn geen "
                + "bedragen voor het maandoverzicht. Dat is geen bedrag van nul.",
                month,
                scope.CustomerId);

            return null;
        }

        return new MonthlyStatementFigures
        {
            CustomerId = scope.CustomerId,
            Month = row.Month,
            MeasuredAt = measured,

            // Eén op één, zonder enige bewerking. Elke null blijft een null: dat is punt 15 en het is
            // de enige eigenschap van deze klasse die werkelijk iets doet.
            AzureAmount = row.AzureCharged,
            ExtraHoursAmount = row.HoursAmount,
            ExtraHours = row.OverBundleHours,
            BundledHours = row.BundledHours,
            UsedHours = row.UsedHours,
            Total = row.Total,

            // Het oordeel van de meter en niet van de lezer. Zie MonthlyCharge.IsFinal: de maand is
            // afgelopen én er is minstens een volle dag ná het einde van de maand gemeten én elk deel
            // van het totaal is bekend. Staat dit op false, dan gaat er geen mail — en dat is de
            // bedoeling, want een maandoverzicht met een halve dag Azure erin is stil verkeerd.
            AmountsAreComplete = row.IsFinal,
            Gap = Gap(row),
        };
    }

    /// <summary>
    /// Zet de klantvlaggen van de facturatiekant om naar de vorm die de mailkant kent.
    /// </summary>
    /// <param name="row">De maand.</param>
    /// <returns>De reden, of <see cref="StatementFigureGap.None"/>.</returns>
    /// <remarks>
    /// <para><strong>Één enkele waarde en geen vlaggen, en dat is een verlies dat hier hoort te
    /// vallen.</strong> <see cref="CustomerChargeGap"/> is <c>[Flags]</c> omdat er twee dingen tegelijk
    /// kunnen ontbreken; <see cref="StatementFigureGap"/> is dat niet. De mailkant doet met deze waarde
    /// één ding — hij legt hem vast bij de weigering — en voor die vraag is de eerste reden de reden.
    /// Zou deze omzetting twee redenen moeten kunnen dragen, dan hoort die enum <c>[Flags]</c> te
    /// worden, en dat is een besluit in die map en niet hier.</para>
    ///
    /// <para><strong>De volgorde is de betekenis, en hij is bij het meten omgegooid.</strong> Niet
    /// doorbelasten gaat voorop: bij de interne beheerklant is het ontbreken van een bedrag geen gat
    /// maar een antwoord, en de andere redenen zeggen er dan niets nuttigs bij. Daarna de mislukte
    /// meting, want die is te verhelpen door opnieuw te meten. Dan het contract, want dat vraagt een
    /// mens. En als laatste het onvolledige tijdvak, want dat verhelpt zichzelf.</para>
    ///
    /// <para>De eerste opzet had het tijdvak vóór het contract en leunde daarvoor op
    /// <see cref="CustomerChargeRow.IsFinal"/>. Dat was fout, en een test heeft het gevonden: een klant
    /// zonder contract kreeg "het tijdvak is nog niet volledig" te horen over een maand die allang
    /// volledig gemeten was. <c>IsFinal</c> is <c>false</c> zodra er íets ontbreekt, dus hij kan de
    /// twee redenen niet scheiden. Vandaar
    /// <see cref="CustomerChargeRow.IsPeriodComplete"/>, dat precies één ding zegt.</para>
    ///
    /// <para><strong>Waarom <see cref="StatementFigureGap.PeriodIncomplete"/> niet uit
    /// <see cref="CustomerChargeGap"/> komt.</strong> Die enum kent hem niet, en met reden: een
    /// onvolledig tijdvak is geen ontbrekend bedrag — er staat een getal, het loopt alleen nog op. Op
    /// het scherm is dat de lopende maand die §3.7 als concept bovenaan zet. Voor een mail is het wél
    /// een blokkade. Dat die twee verschillend zijn is precies het verschil tussen een scherm dat je
    /// kunt verversen en een mail die de deur uit is.</para>
    /// </remarks>
    private static StatementFigureGap Gap(CustomerChargeRow row)
    {
        if (row.Gap.HasFlag(CustomerChargeGap.NotCharged))
        {
            return StatementFigureGap.NotCharged;
        }

        if (row.Gap.HasFlag(CustomerChargeGap.ConsumptionUnknown))
        {
            return StatementFigureGap.CostReadFailed;
        }

        if (row.Gap.HasFlag(CustomerChargeGap.ContractIncomplete))
        {
            return StatementFigureGap.ContractIncomplete;
        }

        return row.IsPeriodComplete ? StatementFigureGap.None : StatementFigureGap.PeriodIncomplete;
    }
}
