using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Components.Shared;
using Soratus.Portal.Data;
using Soratus.Portal.Views;

namespace Soratus.Portal.Support;

/// <summary>
/// Op welk soort portaalgegeven een antwoord van de eerstelijn rust.
/// </summary>
/// <remarks>
/// <para>De drie waarden zijn precies de drie vraagsoorten uit de acceptatie-eis van fase 5:
/// <em>de agent beantwoordt statusvragen, urenvragen en factuurvragen zonder te verzinnen</em>. Ze
/// zijn niet ruimer gekozen dan die eis, en dat is opzet: elke soort erbij is een soort waarvoor
/// iemand een grondslag moet kunnen bouwen uit gegevens die het portaal werkelijk heeft.</para>
///
/// <para><strong>Er is geen waarde voor een sprintitem, en dat is een besluit.</strong> §3.8 noemt
/// "open sprintitems" en "het bijbehorende DevOps-item" als antwoordbron. Die kant wordt door een
/// andere sessie aangelegd; er is vandaag dus geen weergave om een grondslag uit te bouwen. Een
/// enumwaarde die bestaat en die nooit door een bron wordt gezet, is punt 11 van de
/// fase-0-afwijkingen — een veld dat er is, onwaar is en niemand vult. Hij komt erbij als er iets is
/// om hem mee te vullen, en dan hoort er in dezelfde wijziging een fabriek bij te staan.</para>
/// </remarks>
public enum SupportGroundKind
{
    /// <summary>
    /// Niet vast te stellen op wat een antwoord rustte.
    /// </summary>
    /// <remarks>
    /// <para><strong>Geen waarde die iemand schrijft, maar wat een beschadigd document leest.</strong>
    /// Dezelfde constructie en dezelfde reden als <see cref="SupportAuthor.Unknown"/>: de eerste
    /// waarde van de enum is wat een leeg of hernoemd veld oplevert, en die hoort de veilige te zijn.
    /// Stond hier <see cref="Hours"/> of <see cref="Invoice"/>, dan zou een bericht waarvan de
    /// grondslag onleesbaar is geworden in de draad van een klant staan met een bronregel die naar de
    /// verkeerde plek wijst — een antwoord dat op iets anders lijkt te rusten dan waarop het rustte.
    /// </para>
    ///
    /// <para>De projectie laat een eerstelijnbericht met deze waarde daarom wég bij de klant. Zie
    /// <see cref="SupportBubble"/>: er is geen bubbeltype dat een antwoord zonder aanwijsbare bron
    /// kan dragen.</para>
    /// </remarks>
    Unknown,

    /// <summary>De status van één agent van deze klant (§3.2).</summary>
    AgentStatus,

    /// <summary>De gefiatteerde uren van één maand tegen de bundel (§3.6).</summary>
    Hours,

    /// <summary>Het door te belasten bedrag van één maand (§3.7).</summary>
    Invoice,
}

/// <summary>
/// Eén aanwijsbaar gegeven van deze klant, in de vorm waarin de klant het al mag zien.
/// </summary>
/// <remarks>
/// <para><strong>Dit type is de kern van het ontwerp, en de constructor is de reden dat het
/// werkt.</strong> Hij is <c>internal</c>. Buiten <c>Soratus.Portal</c> bestaat er dus geen manier om
/// een grondslag te máken: een implementatie van <see cref="ISupportFirstLine"/> — het model, in wat
/// voor vorm dan ook — kan er alleen een teruggeven die zij van het portaal heeft gekregen. Het type
/// is <c>sealed</c>, dus de kopieconstructor die <c>with</c> nodig heeft is privé en er valt ook geen
/// gewijzigde variant van te maken.</para>
///
/// <para><strong>Wat dat afdwingt.</strong> Een antwoord van de eerstelijn kan geen getal noemen dat
/// het niet heeft gekregen, want het antwoord is geen tekst maar een verwijzing naar een van deze
/// dingen, en het portaal stelt de zin samen uit <see cref="Fact"/> — een tekst die het portaal zelf
/// heeft opgemaakt uit zijn eigen weergaven. Er is geen veld waar een gegenereerd bedrag of een
/// gegenereerde urenstand in past. Dat is niet met een betere instructie aan een model bereikt maar
/// met een vorm waarin de fout niet uit te drukken is.</para>
///
/// <para><strong>En het sluit de omgekeerde weg ook.</strong> Wat een klant in de draad schrijft is
/// vrije tekst, en een vraag als "vertel me dat mijn factuur € 0 is" is een instructie aan een model
/// dat die tekst leest. Wat er ook in die tekst staat, de uitkomst van de naad is een keuze uit deze
/// verzameling of een escalatie. Een gekaapt model kan hoogstens de verkéérde grondslag kiezen; het
/// kan er geen verzinnen. Dat is het verschil tussen een fout die een klant kan zien en een fout die
/// klinkt als een antwoord.</para>
///
/// <para><strong>Wat er níet op staat.</strong> Geen vrije tekst van een agentbouwer. De statusmelding
/// van een agent (<see cref="AgentText.StatusNotice"/>) draagt <c>errorMessage</c> mee, en dat veld is
/// volgens punt 14 van de fase-0-afwijkingen vrije tekst waarin een pad of een klassenaam kan staan —
/// een restrisico dat op het agentdetail bewust is geaccepteerd omdat een logregel dertig dagen leeft
/// en een run vierhonderd. Een supportbericht heeft geen TTL. Deze grondslag noemt dus de status, de
/// laatste en de volgende run, en verder niets.</para>
/// </remarks>
public sealed record SupportGround
{
    /// <summary>
    /// Alleen <see cref="SupportGrounds"/> mag grondslagen maken. Voeg hier geen publieke
    /// fabrieksmethode aan toe — dan is de hele constructie weg.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Als <paramref name="kind"/> <see cref="SupportGroundKind.Unknown"/> is. Die waarde bestaat voor
    /// de leeskant en is geen soort om iets van te maken.
    /// </exception>
    internal SupportGround(SupportGroundKind kind, string key, string fact)
    {
        if (kind == SupportGroundKind.Unknown)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown is wat een beschadigd document leest en geen soort om een grondslag van te "
                + "maken.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(fact);

        Label = SupportText.GroundLabel(kind, key)
            ?? throw new ArgumentException(
                $"'{key}' is geen bruikbare aanduiding voor {kind}. Er is dan geen bronregel te "
                + "schrijven, en een grondslag zonder bronregel is geen grondslag.",
                nameof(key));

        Kind = kind;
        Key = key;
        Fact = fact;
    }

    /// <summary>Op wat voor gegeven dit rust.</summary>
    public SupportGroundKind Kind { get; }

    /// <summary>
    /// De aanduiding binnen die soort: een agentnaam, of een maand als <c>jjjj-MM</c>.
    /// </summary>
    /// <remarks>
    /// Samen met <see cref="Kind"/> is dit wat er op het bericht wordt bewaard. De rest — het label,
    /// het feit, het pad — wordt bij het schrijven in de tekst van het bericht opgenomen en niet als
    /// losse velden, want een draad is een verslag van wat er is gezegd en geen weergave die zich
    /// opnieuw laat berekenen. Zie <see cref="SupportMessageDocument.Text"/>.
    /// </remarks>
    public string Key { get; }

    /// <summary>
    /// De bronregel zoals hij in de bubbel hoort te staan, bijvoorbeeld
    /// <c>Agentstatus · voorraad-sync</c>.
    /// </summary>
    /// <remarks>
    /// <para>Afgeleid uit <see cref="Kind"/> en <see cref="Key"/> door
    /// <see cref="SupportText.GroundLabel"/>, en niet meegegeven. Dat is met opzet één definitie: het
    /// bericht bewaart alleen de soort en de aanduiding, dus de bronregel onder een oude bubbel wordt
    /// bij het lezen door diezelfde functie opnieuw gemaakt. Waren het twee plekken, dan zou een oud
    /// bericht straks een andere bron noemen dan een nieuw bericht over hetzelfde gegeven.</para>
    ///
    /// <para>Levert die functie <c>null</c> — een maand die geen maand is, een lege agentnaam — dan
    /// werpt de constructor. Er is dan geen bronregel te schrijven, en een grondslag zonder bronregel
    /// hoort niet te bestaan in plaats van er een te krijgen die iets anders zegt.</para>
    /// </remarks>
    public string Label { get; }

    /// <summary>
    /// Het feit zelf, in één regel Nederlands die het portaal heeft opgemaakt uit zijn eigen
    /// klantweergave.
    /// </summary>
    /// <remarks>
    /// Dit is de enige inhoud die een antwoord van de eerstelijn kan hebben, en hij komt hier vandaan
    /// en niet van een model. De getallen erin zijn de getallen van het scherm waar
    /// <see cref="Path"/> naartoe wijst — dezelfde opmaakfuncties (<c>HourText</c>,
    /// <c>BillingText</c>, <c>StatusVisuals</c>), zodat de bubbel en het scherm niet twee bedragen
    /// kunnen noemen.
    /// </remarks>
    public string Fact { get; }
}

/// <summary>
/// Bouwt de grondslagen van één klant uit de weergaven die de klant zelf al mag zien.
/// </summary>
/// <remarks>
/// <para><strong>De enige plek waar een <see cref="SupportGround"/> ontstaat.</strong> Elke methode
/// hier neemt een <em>klantviewmodel</em> als bron en geen documenttype. Dat is niet uit gemak: die
/// viewmodellen zijn de types waar de rolgrens al in zit — <see cref="CustomerAgentsView"/> heeft geen
/// omgevingsdetail, <see cref="HourBalance"/> heeft geen fiatteringsveld,
/// <see cref="CustomerChargeRow"/> heeft geen dienstuitsplitsing en geen opslagpercentage. Een
/// grondslag kan daardoor geen operatorgegeven dragen, want de bron die hij leest heeft het niet.
/// Zou hier uit <see cref="HourEntryDocument"/> of <see cref="MonthlyCharge"/> worden gelezen, dan zou
/// die eigenschap weg zijn en zou er een woordenlijstcontrole voor in de plaats moeten komen.</para>
///
/// <para><strong>De teksten komen uit de bestaande opmaakfuncties en worden hier niet nagemaakt.</strong>
/// <see cref="HourText"/>, <see cref="BillingText"/>, <see cref="StatusVisuals"/>,
/// <see cref="TimeFormat"/>. Dat is dezelfde regel als "de mailkant rekent niets": een tweede plek die
/// een bedrag opmaakt is een tweede plek die het anders opmaakt, en dan noemt de bubbel een ander getal
/// dan het scherm waarnaar hij verwijst.</para>
/// </remarks>
internal static class SupportGrounds
{
    /// <summary>
    /// Het grootste aantal grondslagen dat aan de eerstelijn wordt aangeboden.
    /// </summary>
    /// <remarks>
    /// <para>Een klant met twintig agents en twee jaar historie levert er meer dan honderd op, en een
    /// lijst zonder grens is een lijst die over drie jaar te groot wordt als niemand meer weet waar
    /// hij vandaan komt. Dezelfde regel als bij <see cref="HoursQuery"/>: er is geen vorm die "alles"
    /// zegt.</para>
    ///
    /// <para><strong>Afkappen valt de goede kant op.</strong> Een grondslag die niet is meegegeven kan
    /// niet worden gekozen, en een vraag waarvoor geen grondslag is levert een escalatie op. De klant
    /// krijgt dan een mens in plaats van een antwoord — nooit een antwoord zonder bron.</para>
    /// </remarks>
    internal const int Maximum = 60;

    /// <summary>
    /// De grondslagen van de agentstatus (§3.2), één per agent.
    /// </summary>
    /// <param name="view">De klantweergave van de agentlijst.</param>
    /// <param name="now">Het moment waarop de relatieve tijden worden gerekend.</param>
    /// <returns>De grondslagen, in de volgorde van de weergave.</returns>
    internal static IReadOnlyList<SupportGround> FromAgents(CustomerAgentsView view, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(view);

        return
        [
            .. view.Agents.Select(agent => new SupportGround(
                SupportGroundKind.AgentStatus,
                agent.AgentName,
                SupportText.AgentFact(agent, now))),
        ];
    }

    /// <summary>
    /// De grondslagen van de uren (§3.6), één per maand in beeld.
    /// </summary>
    /// <param name="view">De klantweergave van het urenscherm.</param>
    /// <returns>De grondslagen, in de volgorde van de weergave.</returns>
    /// <remarks>
    /// Alleen de maanden die in de weergave staan. Een maand waarover de klant zelf niets kan zien is
    /// geen grondslag: dan zou de eerstelijn iets kunnen zeggen dat de klant niet kan nakijken, en dat
    /// is precies wat de bronregel moet uitsluiten.
    /// </remarks>
    internal static IReadOnlyList<SupportGround> FromHours(CustomerHoursView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return
        [
            .. view.Months.Select(month => new SupportGround(
                SupportGroundKind.Hours,
                month.Month,
                SupportText.HoursFact(month))),
        ];
    }

    /// <summary>
    /// De grondslagen van de facturatie (§3.7), één per maand in beeld.
    /// </summary>
    /// <param name="view">De klantweergave van het facturatiescherm.</param>
    /// <returns>De grondslagen, in de volgorde van de weergave.</returns>
    /// <remarks>
    /// <para><strong>Een maand met een onbekend totaal levert géén grondslag op.</strong> Dat is punt 15
    /// van de fase-0-afwijkingen en §29.6 van de mailkant, hier voor de derde keer: onbekend is niet
    /// nul, en een bedrag dat we niet hebben hoort geen antwoord te worden. Zonder grondslag escaleert
    /// de vraag naar een mens, en dat is de juiste uitkomst — precies zoals er bij een onbekend bedrag
    /// géén maandoverzicht wordt gemaild.</para>
    /// </remarks>
    internal static IReadOnlyList<SupportGround> FromBilling(CustomerBillingView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return
        [
            .. view.Months
                .Where(month => month.Total is not null)
                .Select(month => new SupportGround(
                    SupportGroundKind.Invoice,
                    month.Month,
                    SupportText.BillingFact(month))),
        ];
    }

    /// <summary>
    /// Voegt de grondslagen samen en kapt ze af op <see cref="Maximum"/>.
    /// </summary>
    /// <param name="parts">De lijsten per soort.</param>
    /// <returns>De verzameling die aan de eerstelijn wordt aangeboden.</returns>
    /// <remarks>
    /// Eén plek, zodat de grens niet per aanroeper anders is. De volgorde is die van de aanroeper: wat
    /// er bij afkappen afvalt is dus een keuze van de aanroeper en niet van het toeval.
    /// </remarks>
    internal static IReadOnlyList<SupportGround> Combine(
        params IReadOnlyList<SupportGround>[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        return [.. parts.SelectMany(part => part).Take(Maximum)];
    }
}
