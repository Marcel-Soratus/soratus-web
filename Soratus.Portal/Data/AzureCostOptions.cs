using System.ComponentModel.DataAnnotations;

namespace Soratus.Portal.Data;

/// <summary>
/// De configuratiesectie <c>PortalCosts</c>: wanneer en hoe traag de kostencollector Cost Management
/// bevraagt.
/// </summary>
/// <remarks>
/// <para><strong>Elke waarde hieronder komt uit een meting en niet uit een gevoel.</strong> Dat is de
/// enige reden dat ze instelbaar zijn: als de emmer van Cost Management morgen anders staat, hoort er
/// een <em>nieuwe meting</em> tegenover te staan en geen uitrol met een gokje. De standaardwaarden zijn
/// zo gekozen dat de collector bij twijfel te langzaam is in plaats van te snel — een run die een uur
/// duurt is 's nachts gratis, en een run die de emmer leegtrekt kost het scherm zijn bedragen.</para>
///
/// <para><strong>Er staat géén <c>ValidateOnStart</c> op, om dezelfde reden als bij
/// <see cref="PortalDataOptions"/>:</strong> een verkeerd ingestelde collector is een inrichtingsfout,
/// en een inrichtingsfout die het opstarten tegenhoudt neemt <c>/healthz</c> mee en rolt daarmee de
/// uitrol terug. De grenzen hieronder zijn data-annotaties, dus een onzinnige waarde meldt zich; wat
/// hij niet doet is het portaal tegenhouden.</para>
/// </remarks>
public sealed class AzureCostOptions
{
    /// <summary>De naam van de configuratiesectie.</summary>
    public const string SectionName = "PortalCosts";

    /// <summary>
    /// Of de collector draait.
    /// </summary>
    /// <remarks>
    /// <para><strong>Standaard aan, en in <c>appsettings.Development.json</c> uit.</strong> Die kant op
    /// en niet andersom, om de reden die in <c>Program.cs</c> bij het maandoverzicht staat: een
    /// standaard-uit vlag levert een storing op die zich voordoet als werkende functionaliteit — het
    /// portaal start, er staat nergens een fout, en er wordt stil nooit gemeten.</para>
    ///
    /// <para><strong>En er zit al een tweede rem op die geen vlag is.</strong> De collector bevraagt
    /// alleen klanten met een vastgelegde Azure-scope, en die legt een operator met de hand vast; er is
    /// geen migratie die er zeven verzint. Een verse opslag levert dus nul aanroepen op zonder dat
    /// iemand iets hoeft uit te zetten. De vlag is er voor het geval dat je hem wél wilt kunnen
    /// uitzetten — bijvoorbeeld terwijl er met de hand wordt gemeten.</para>
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Het uur in UTC waarop de dagelijkse run begint.
    /// </summary>
    /// <remarks>
    /// <para>Vier, uit §9 van het haalbaarheidsonderzoek. <strong>Dat moment is met opzet niet
    /// verschoven, en dat is de winst van controleren boven later draaien.</strong> Om 04:00 op de 1e
    /// geeft een vraag over de nieuwe maand nul rijen (punt 30) en is de laatste dag van de vorige
    /// maand nog niet volledig geboekt (punt 31) — en beide leveren via
    /// <see cref="AzureCostCompleteness"/> een toestand op waarop niet gefactureerd wordt, in plaats
    /// van een bedrag dat te laag is. Het uur later zetten zou de gok verplaatsen en niet
    /// weghalen.</para>
    /// </remarks>
    [Range(0, 23, ErrorMessage = "PortalCosts:RunHourUtc hoort tussen 0 en 23 te liggen.")]
    public int RunHourUtc { get; set; } = 4;

    /// <summary>
    /// Hoeveel seconden er tussen twee aanroepen aan Cost Management zit.
    /// </summary>
    /// <remarks>
    /// <para><strong>Tweehonderdveertig, en dat is vier keer zoveel als het onderzoek suggereert.
    /// Gemeten op 21 augustus 2026, als één aanroeper, tegen <c>resourceGroups/MBV</c> met
    /// <c>timeframe: Custom</c> over een hele maand met dagkorrel:</strong></para>
    ///
    /// <code>
    /// 12:09:22  200   112 rijen
    /// 12:10:15  429   (+53 s)   clienttype-retry-after: 3
    /// 12:12:07  429   (+165 s)  clienttype-retry-after: 12
    /// 12:15:24  200   (+362 s)  112 rijen
    /// </code>
    ///
    /// <para>Het onderzoek meldt dat een geslaagde aanroep dertig tot veertig seconden stilte vroeg. Dat
    /// gold voor een <c>MonthToDate</c>-vraag; een maandvraag met dagkorrel is zwaarder en drieënvijftig
    /// seconden stilte was er niet genoeg voor. In alle drie de 429's stonden de zichtbare tellers
    /// bovendien ruim in de plus (<c>entity-requests DefaultQuota:3</c>,
    /// <c>qpu QueriesPerHour:597, QueriesPerMin:59, QueriesPer10Sec:11</c>) — dus de emmer die het
    /// tegenhoudt is niet degene die je kunt zien. Punt 32 voor de tweede keer.</para>
    ///
    /// <para><strong>Wat deze meting níet kan uitsluiten:</strong> dat er op dat moment een tweede
    /// aanroeper in dezelfde tenant meedeed. De emmer hangt aan de aanroeper, en er werkten die dag
    /// meer sessies aan deze lane. Dat maakt het getal niet minder bruikbaar — de veilige kant is
    /// dezelfde — maar het maakt van 240 een bovengrens met marge en niet een gemeten minimum.</para>
    /// </remarks>
    [Range(5, 3600, ErrorMessage = "PortalCosts:PauseSeconds hoort tussen 5 en 3600 te liggen.")]
    public int PauseSeconds { get; set; } = 240;

    /// <summary>
    /// Hoeveel keer één maand van één klant hoogstens wordt opgevraagd binnen één run.
    /// </summary>
    /// <remarks>
    /// <para><strong>Twee, en dat is laag met een reden: elke respons kost budget, ook een mislukte.</strong>
    /// Gemeten (punt 32): <c>qpu-remaining</c> liep over eenentwintig aanroepen van 599 naar 578,
    /// terwijl de meeste van die aanroepen 429's waren. Opnieuw proberen is niet gratis, dus een derde
    /// poging kost de vólgende klant zijn meting.</para>
    ///
    /// <para>Wat er gebeurt als de pogingen op zijn: er wordt <em>niets</em> weggeschreven. De lezing
    /// van gisteren blijft staan met haar eigen tijdstip erbij, en dat is precies wat §32 als het
    /// eerlijkere antwoord aanwijst. Een run die daarop stukloopt is bovendien geen mislukte run — zie
    /// <see cref="AzureCostCollector"/>.</para>
    /// </remarks>
    [Range(1, 5, ErrorMessage = "PortalCosts:MaxAttempts hoort tussen 1 en 5 te liggen.")]
    public int MaxAttempts { get; set; } = 2;

    /// <summary>
    /// De eigen ondergrens, in seconden, voor het wachten na een 429 of een 404.
    /// </summary>
    /// <remarks>
    /// <para><strong>Deze ondergrens bestaat omdat de wachthint van Azure te kort kan zijn.</strong>
    /// Gemeten waarden voor <c>clienttype-retry-after</c>: 1, 2, 3, 8, 12, 16, 17, 19, 22, 25, 26, 29,
    /// 34, 35 — en de 1 was aantoonbaar te kort, net als de 3 en de 12 hierboven. De collector leest
    /// beide hints (<c>entity-retry-after</c> en <c>clienttype-retry-after</c>), neemt de grootste, en
    /// wacht daarna alsnog minstens dit aantal seconden.</para>
    ///
    /// <para>Gelijk aan <see cref="PauseSeconds"/> als standaard, want dat is wat er tussen twee
    /// aanroepen hoe dan ook aan stilte nodig is. Een kortere backoff dan de gewone pauze zou betekenen
    /// dat een mislukte aanroep sneller wordt herhaald dan een geslaagde wordt opgevolgd, en dat is
    /// precies de verkeerde kant op.</para>
    /// </remarks>
    [Range(5, 3600, ErrorMessage = "PortalCosts:BackoffSeconds hoort tussen 5 en 3600 te liggen.")]
    public int BackoffSeconds { get; set; } = 240;

    /// <summary>De api-version van Cost Management.</summary>
    /// <remarks>
    /// <c>2023-11-01</c>: de versie waarop élke meting in deze notitie is gedaan. Instelbaar zodat een
    /// nieuwe versie te proberen is zonder uitrol, en met dit als aantekening: de kolomvolgorde van het
    /// antwoord is per vraag anders (punt 33), dus een versiewissel is een meting en geen instelling.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PortalCosts:ApiVersion ontbreekt.")]
    public string ApiVersion { get; set; } = "2023-11-01";

    /// <summary>Het adres van Azure Resource Manager.</summary>
    /// <remarks>
    /// Instelbaar voor een test die de collector tegen een eigen server laat lopen, en om geen andere
    /// reden. Er is geen tweede cloud in beeld.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PortalCosts:ManagementEndpoint ontbreekt.")]
    public string ManagementEndpoint { get; set; } = "https://management.azure.com";

    /// <summary>De pauze tussen twee aanroepen.</summary>
    public TimeSpan Pause => TimeSpan.FromSeconds(PauseSeconds);

    /// <summary>De ondergrens van de backoff.</summary>
    public TimeSpan Backoff => TimeSpan.FromSeconds(BackoffSeconds);
}
