using Soratus.Portal.Security;

namespace Soratus.Portal.Mail;

/// <summary>
/// De bron van de bedragen die in het maandoverzicht staan.
/// </summary>
/// <remarks>
/// <para><strong>Dit is een naad en geen berekening.</strong> De Azure-kosten, de beheeropslag en de
/// uren boven bundel worden op één plek uitgerekend, en die plek is niet deze map. Een tweede plek
/// die een bedrag berekent is een tweede plek die het anders kan berekenen, en dan is de vraag
/// "welk van de twee bedragen heeft de klant gekregen" niet meer te beantwoorden. De mailkant
/// consumeert dus, en rekent nergens.</para>
///
/// <para><strong>De grens zit in het teruggegeven type en niet in het scope-type.</strong> Er is
/// precies één retourvorm en die draagt geen dienstuitsplitsing, geen opslagpercentage en niets
/// over de fiatteringsstroom — §2 zegt over "Facturatie: Azure per dienst + beheeropslag"
/// onomwonden <strong>nee</strong> voor de klant, en een mail is een klantoppervlak. Wat de
/// implementatie ook intern heeft staan, hierlangs komt alleen de smalle vorm.</para>
///
/// <para><strong>Waarom een <see cref="CustomerWriteScope"/> en niet een
/// <see cref="CustomerScope"/>.</strong> Het eerste ontwerp had de leesscope, en dat leek de juiste
/// keuze: klantzichtbare gegevens vragen een leesrecht. Het kan niet. Een
/// <see cref="CustomerScope"/> bestaat alleen voor een klant met een <em>ingerichte
/// telemetrie-opslag</em> — dat staat in de opmerkingen bij
/// <see cref="CustomerWriteScope"/> zelf, en het is de reden dat dat type zijn leesscope niet
/// draagt. Juist de klant zonder uitgerolde agents is degene die wel een contract en wel Azure-kosten
/// heeft; die zou dan geen maandoverzicht kunnen krijgen omdat zijn agents nog niet draaien, en dat
/// is een koppeling tussen twee dingen die niets met elkaar te maken hebben.</para>
/// </remarks>
public interface IMonthlyStatementFigures
{
    /// <summary>
    /// De bedragen van één klant over één maand, in de vorm die de klant mag zien.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant. Levert de klantslug.</param>
    /// <param name="month">De maand als <c>jjjj-MM</c>.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De bedragen, of <c>null</c> als er over die maand niets is gemeten. <c>null</c> is een
    /// gewone uitkomst en geen fout — een klant die halverwege de maand is aangesloten heeft geen
    /// meting over de maand ervoor — en het is uitdrukkelijk niet hetzelfde als nul.
    /// </returns>
    Task<MonthlyStatementFigures?> BuildStatementAsync(
        CustomerWriteScope scope,
        string month,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Waarom een bedrag niet bekend is.
/// </summary>
/// <remarks>
/// <para><strong>Een enum en geen string, en dat is de scherpste regel van deze map.</strong> Een
/// reden die als tekst reist, komt uit een <c>catch</c>-blok: een <c>Exception.Message</c>, een pad,
/// een resource-id, een stacktrace. Punt 13 en punt 14 van de fase-0-afwijkingen gaan precies over
/// die klasse fout — tekst die door onze eigen systemen is geschreven en bij een klant belandt — en
/// beide keren stond die tekst op een <em>scherm</em>, waar een operator hem nog kon zien. Hier zou
/// hij in de inbox van de klant liggen. Een enum kan die tekst niet dragen.</para>
///
/// <para>Deze waarden komen dus nooit als woord in een mail terecht; ze staan op het operatorscherm
/// en in de logregel. Wat de klant hoort te lezen is dat er niets is verstuurd, en dat staat er als
/// afwezigheid van een mail en niet als een mail met een gat erin.</para>
/// </remarks>
public enum StatementFigureGap
{
    /// <summary>Er is geen gat: het bedrag is gemeten.</summary>
    None,

    /// <summary>De kostenmeting is niet gelukt. Een 429, een timeout, of de 404 die "probeer opnieuw" betekent.</summary>
    CostReadFailed,

    /// <summary>De meting is er wel, maar de laatste dag van de maand is nog niet volledig geboekt.</summary>
    PeriodIncomplete,

    /// <summary>
    /// Het contract legt niet alles vast wat dit bedrag bepaalt.
    /// </summary>
    /// <remarks>
    /// <para><strong>Hier stonden twee waarden — <c>NoHourlyRate</c> en <c>NoSurcharge</c> — en ze zijn
    /// samengevoegd omdat er aan de bron geen verschil tussen bestaat.</strong> De kostenkant kent dat
    /// verschil wel (<see cref="Data.MonthlyChargeGap"/> heeft er drie waarden voor: geen opslag, geen
    /// bundel, geen tarief), maar gooit het bij de overgang naar de klantvorm bewust weg. De reden
    /// staat bij <see cref="Views.CustomerChargeGap"/>: een waarde die <c>NoSurcharge</c> heet noemt
    /// onze marge, en de mededeling "we hebben nog geen opslag afgesproken" vertelt een klant dat er
    /// een opslag ís. Dat is §2 — de beheeropslag is operator-only — en een mail is een
    /// klantoppervlak.</para>
    ///
    /// <para>Het gevolg is dat die twee waarden hier onbereikbaar zouden zijn: er is geen bron die ze
    /// ooit zet. Punt 11 van de fase-0-afwijkingen gaat precies over zulke velden — waarden die
    /// bestaan, onwaar zijn en nooit worden gevuld — en één plek in dit portaal met dat gebrek is
    /// genoeg. Vandaar één waarde die zegt wat er werkelijk aankomt.</para>
    ///
    /// <para>Wat er verloren gaat is niets dat de klant of deze map iets kost: welke van de drie
    /// afspraken ontbreekt staat op het operatorscherm van de facturatie, en de handeling die erop
    /// volgt is voor alle drie dezelfde.</para>
    /// </remarks>
    ContractIncomplete,

    /// <summary>
    /// Deze klant wordt niet doorbelast: het is de interne beheerklant van Soratus (§4).
    /// </summary>
    /// <remarks>
    /// <para><strong>Geen ontbrekend bedrag maar een bekend antwoord: er valt niets te
    /// factureren.</strong> Het verbruik is gemeten — de beheeragents draaien ergens en dat kost geld —
    /// maar het wordt niet doorbelast, en € 0,00 zou zeggen dat we een factuur van nul sturen.</para>
    ///
    /// <para><strong>En dit is de plek waar dat nog niet helemaal klopt.</strong>
    /// <see cref="StatementRefusal"/> heeft geen waarde voor "deze klant wordt niet gefactureerd", dus
    /// een interne klant weigert vandaag met <see cref="StatementRefusal.AmountsIncomplete"/> — de
    /// uitkomst is goed (er gaat geen mail) en de reden is onwaar. Dat is gemeld en het hoort in
    /// <c>StatementRefusal</c> te worden opgelost, of eerder: met een controle in
    /// <see cref="MonthlyStatementService"/> vóór de bedragen worden gelezen, want een interne klant
    /// hoort geen maandoverzicht te krijgen ongeacht wat er gemeten is.</para>
    /// </remarks>
    NotCharged,
}

/// <summary>
/// De bedragen van één klant over één maand, in de vorm die de klant mag zien.
/// </summary>
/// <remarks>
/// <para><strong>Elk bedrag is <c>decimal?</c>, en <c>null</c> betekent onbekend.</strong> Dat is
/// punt 15 van de fase-0-afwijkingen en regel 1 van §9 van het haalbaarheidsrapport, hier bij elkaar:
/// een 429, een timeout of die 404 uit §2 mag nooit tot € 0,00 leiden. Op een factuur is € 0,00 geen
/// lege waarde maar een verkeerd bedrag. En anders dan op een scherm is een verkeerd bedrag in een
/// mail niet te herstellen door te verversen.</para>
///
/// <para><strong>Wat er niet op staat, en dat is de helft van het ontwerp.</strong> Geen
/// dienstuitsplitsing, geen opslagpercentage, geen resource group, geen subscription, geen
/// <c>ServiceName</c>-regels — dat is allemaal operator-only (§2). En niets over de
/// fiatteringsstroom: geen te-fiatteren-teller, geen naam van een fiatteur, geen aantal dat afwijkt
/// van wat de klant op zijn urenscherm ziet. De acceptatie van fase 3 is dat de klant niets van die
/// stroom ziet, en een mail is de makkelijkste plek om die eis alsnog te breken. Er staat een test
/// op deze boom die op dezelfde woorddelen zoekt als <c>UrencomponentTests</c>.</para>
///
/// <para><see cref="UsedHours"/> en <see cref="ExtraHours"/> staan er wél, want een klant hoort zijn
/// specificatie te kunnen laten optellen tot het bedrag dat hij betaalt. Ze horen dan ook exact de
/// getallen te zijn die op zijn urenscherm staan: de som van de <em>gefiatteerde</em> regels, en
/// niets anders.</para>
/// </remarks>
public sealed record MonthlyStatementFigures
{
    /// <summary>De klantslug. Moet gelijk zijn aan de scope waarmee deze bedragen zijn opgehaald.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De maand als <c>jjjj-MM</c>.</summary>
    public required string Month { get; init; }

    /// <summary>
    /// Wanneer de onderliggende kostenmeting is gedaan, in UTC.
    /// </summary>
    /// <remarks>
    /// Niet "nu". De kosten komen uit een dagelijkse collector en de cache is de bron voor het
    /// scherm, niet een versnelling (§9 van het haalbaarheidsrapport). Dit moment gaat mee in de
    /// mail en in de verzendbevestiging, want een bedrag zonder tijdstip is een bewering zonder
    /// datum.
    /// </remarks>
    public required DateTimeOffset MeasuredAt { get; init; }

    /// <summary>
    /// Het door te belasten Azure-bedrag in euro, inclusief beheeropslag, of <c>null</c> als het niet
    /// bekend is.
    /// </summary>
    /// <remarks>
    /// Eén getal en geen uitsplitsing: dat is het antwoord op de openstaande vraag uit §9 van de
    /// spec zoals §2 hem vandaag beantwoordt — de klant ziet alleen het door te belasten totaal.
    /// De opslag zit erin verwerkt en staat er niet als percentage naast; dat percentage is onze
    /// marge.
    /// </remarks>
    public decimal? AzureAmount { get; init; }

    /// <summary>Het bedrag voor uren boven bundel in euro, of <c>null</c> als het niet bekend is.</summary>
    public decimal? ExtraHoursAmount { get; init; }

    /// <summary>Het aantal uren boven bundel, of <c>null</c> als dat niet is vast te stellen.</summary>
    public decimal? ExtraHours { get; init; }

    /// <summary>
    /// De urenbundel van deze maand, of <c>null</c> als er geen bundel is vastgelegd.
    /// </summary>
    /// <remarks>
    /// <c>null</c> is hier "niet afgesproken" en nul is "geen bundel" — punt 19 van de
    /// fase-0-afwijkingen, en dezelfde regel als bij
    /// <see cref="Data.ContractDocument.BundledHours"/>.
    /// </remarks>
    public decimal? BundledHours { get; init; }

    /// <summary>De gefiatteerde uren van deze maand, of <c>null</c> als ze niet zijn gelezen.</summary>
    public decimal? UsedHours { get; init; }

    /// <summary>
    /// Het totaal in euro, of <c>null</c> zodra een van de delen onbekend is.
    /// </summary>
    /// <remarks>
    /// Komt van de kostenkant en wordt hier <em>niet</em> uit de delen opgeteld. Zou de mailkant hem
    /// zelf uitrekenen, dan bestaat er een tweede definitie van het totaal, en dan kan de mail een
    /// ander bedrag noemen dan het scherm. Er staat een test op dat deze map nergens optelt.
    /// </remarks>
    public decimal? Total { get; init; }

    /// <summary>
    /// Of de bedragen naar het oordeel van de kostenkant volledig zijn.
    /// </summary>
    /// <remarks>
    /// <para>Het oordeel van de meter en niet van de lezer. De volledigheid van de laatste dag van
    /// de maand is een eigenschap van de Cost Management-boeking — die loopt zeven tot tien uur
    /// achter en heeft een failure mode die zich als 404 voordoet — en de mailkant kan daar niets
    /// over weten.</para>
    ///
    /// <para>Staat dit op <c>false</c>, dan gaat er geen mail. Een maandoverzicht met een halve dag
    /// Azure erin is stil verkeerd: niemand ziet het aan het bedrag.</para>
    /// </remarks>
    public required bool AmountsAreComplete { get; init; }

    /// <summary>
    /// Waarom er iets ontbreekt, of <see cref="StatementFigureGap.None"/> als er niets ontbreekt.
    /// </summary>
    /// <remarks>Zie <see cref="StatementFigureGap"/>: een enum, en met opzet geen tekst.</remarks>
    public StatementFigureGap Gap { get; init; } = StatementFigureGap.None;
}
