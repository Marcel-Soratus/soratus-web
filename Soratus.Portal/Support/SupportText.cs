using Soratus.Agents.Contracts;
using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Components.Shared;
using Soratus.Portal.Data;
using Soratus.Portal.Views;

namespace Soratus.Portal.Support;

/// <summary>
/// De Nederlandse teksten van de supportkant (§3.8).
/// </summary>
/// <remarks>
/// <para><strong>Dit is de plek waar de zinnen van de eerstelijn worden geschreven, en dat is het punt
/// van het hele ontwerp.</strong> Niet een model, niet een sjabloon in configuratie, niet een veld op
/// een antwoordtype: hier, met de hand, in code die in review komt. <see cref="SupportAnswer"/> draagt
/// geen tekst, dus dit is de enige plek waar de tekst van een AI-bubbel kan ontstaan.</para>
///
/// <para><strong>De getallen komen uit dezelfde opmaakfuncties als de schermen.</strong>
/// <see cref="HourText"/>, <see cref="BillingText"/>, <see cref="HourStatusVisuals"/>,
/// <see cref="StatusVisuals"/>, <see cref="TimeFormat"/>. Een tweede plek die een bedrag opmaakt is een
/// tweede plek die het anders opmaakt, en dan noemt de bubbel een ander getal dan het scherm waarnaar
/// de bronregel verwijst. Diezelfde regel staat bij de mailkant als "deze map rekent nergens".</para>
/// </remarks>
public static class SupportText
{
    /// <summary>
    /// Het merkteken op elke bubbel van de eerstelijn (§3.8).
    /// </summary>
    /// <remarks>
    /// <para>Eén constante, en de reden is dat hij op precies één plek in de markup mag staan: in het
    /// component dat óók de bronregel rendert. Twee plekken zou betekenen dat er een bubbel met een
    /// merkteken kan bestaan zonder bronregel, en dat is de eis van §3.8 die dan niet meer geldt. Zie
    /// <c>Components/Pages/Klant/SupportThread.razor</c>: daar staat hij in de twee AI-takken, en er
    /// staat een broncodetest op dat hij nergens anders in de markup voorkomt.</para>
    /// </remarks>
    public const string FirstLineBadge = "AI · eerstelijn";

    /// <summary>Het label boven de bronregel van een AI-bubbel.</summary>
    public const string GroundIntro = "Bron";

    /// <summary>De uitweg uit §3.8: de klant wil een mens.</summary>
    public const string HumanEscape = "Toch een mens van Soratus spreken";

    /// <summary>
    /// Het bericht dat de uitweg in de draad zet.
    /// </summary>
    /// <remarks>
    /// <para><strong>De uitweg is een gewoon bericht van de klant, en het pad eromheen raakt de
    /// eerstelijn niet.</strong> Hij loopt langs <see cref="ISupportStore.PostQuestionAsync"/> en niet
    /// langs <see cref="SupportDesk"/>, dus er komt per definitie geen antwoord van de eerstelijn op.
    /// Dat is de vorm waarin §3.8 hier staat: een klant die om een mens vraagt, krijgt geen agent die
    /// hem uitlegt dat hij een agent is. Er staat een test op dat dit pad geen enkele AI-bubbel
    /// oplevert.</para>
    ///
    /// <para>Een vaste tekst en geen invulveld. Wie iets wil toevoegen, typt een gewoon bericht; deze
    /// knop doet één ding en zegt wat dat is.</para>
    /// </remarks>
    public const string HumanRequest = "Ik wil hier graag een mens van Soratus over spreken.";

    /// <summary>Het scheidingsteken tussen een soort en zijn aanduiding.</summary>
    private const string Separator = " · ";

    /// <summary>
    /// De naam van de queryparameter waarmee het oudere deel van de draad wordt opgevraagd.
    /// </summary>
    /// <remarks>
    /// Nederlands, zoals de andere queryparameters van dit portaal (<c>maand</c>, <c>jaar</c>,
    /// <c>alle</c>, <c>beoordeel</c>). Er staat een constante omdat de pagina hem als
    /// <c>SupplyParameterFromQuery</c> leest en de padfunctie hem schrijft: twee letterlijke teksten
    /// zouden een link opleveren die niets doet.
    /// </remarks>
    public const string OlderQuery = "voor";

    /// <summary>De supportdraad van één klant.</summary>
    /// <param name="slug">De klantslug.</param>
    /// <returns>Bijvoorbeeld <c>/klant/bakker/support</c>.</returns>
    /// <remarks>
    /// De slug wordt geëscaped, om dezelfde reden als bij <see cref="HourText.Path"/>: hij komt hier
    /// binnen als tekst uit een viewmodel, en een pad dat op het formaat van zijn invoer vertrouwt breekt
    /// stil zodra dat formaat verandert.
    /// </remarks>
    public static string Path(string slug) => $"/klant/{Uri.EscapeDataString(slug)}/support";

    /// <summary>
    /// Het oudere deel van de draad.
    /// </summary>
    /// <param name="slug">De klantslug.</param>
    /// <param name="messageId">De documentsleutel van het oudste bericht dat nu in beeld is.</param>
    /// <returns>Bijvoorbeeld <c>/klant/bakker/support?voor=supportMessage-...</c>.</returns>
    /// <remarks>
    /// <para><strong>Een <c>GET</c> met een grens erin, en geen werkwoord.</strong> §29.9 van de
    /// fase-0-afwijkingen legt uit waarom dat verschil telt: een <c>GET</c> wordt aangeroepen door een
    /// prefetch, een linkchecker, een spamfilter dat elke URL in een bericht opent en een tabblad dat na
    /// een herstart zijn adressen opnieuw bezoekt. Deze parameter kiest een deel van een lijst en doet
    /// verder niets — hij plaatst geen bericht, wekt de eerstelijn niet en verandert niets in de opslag.
    /// Er staat een test op dat een <c>GET</c> op dit scherm niets schrijft.</para>
    /// </remarks>
    public static string OlderPath(string slug, string messageId) =>
        $"{Path(slug)}?{OlderQuery}={Uri.EscapeDataString(messageId)}";

    /// <summary>
    /// De bronregel van een grondslag, bijvoorbeeld <c>Uren · juli 2026</c>.
    /// </summary>
    /// <param name="kind">De soort.</param>
    /// <param name="key">De aanduiding: een agentnaam, of een maand als <c>jjjj-MM</c>.</param>
    /// <returns>Het label.</returns>
    /// <remarks>
    /// <para><strong>Eén definitie, gebruikt bij het bouwen én bij het lezen.</strong> Het bericht
    /// bewaart alleen <c>groundKind</c> en <c>groundKey</c>; het label wordt bij het weergeven opnieuw
    /// gemaakt. Stond het label ook op het document, dan bestonden er twee versies van dezelfde regel en
    /// zou een oud bericht straks een andere bron noemen dan een nieuw bericht over hetzelfde
    /// gegeven.</para>
    ///
    /// <para><see cref="SupportGroundKind.Unknown"/> levert géén label maar <c>null</c>. Dat is
    /// belangrijk: het is de manier waarop een beschadigd bericht uit de klantweergave valt in plaats
    /// van er met een verzonnen bron in te blijven staan.</para>
    /// </remarks>
    public static string? GroundLabel(SupportGroundKind kind, string? key) => kind switch
    {
        SupportGroundKind.AgentStatus when Filled(key) => $"Agentstatus{Separator}{key!.Trim()}",
        SupportGroundKind.Hours when Month(key) is { } hours => $"Uren{Separator}{hours}",
        SupportGroundKind.Invoice when Month(key) is { } invoice => $"Facturatie{Separator}{invoice}",
        _ => null,
    };

    /// <summary>
    /// Het pad naar het scherm waar de grondslag staat.
    /// </summary>
    /// <param name="customerId">De klantslug.</param>
    /// <param name="kind">De soort.</param>
    /// <param name="key">De aanduiding.</param>
    /// <returns>Het pad, of <c>null</c> als er geen scherm bij past.</returns>
    /// <remarks>
    /// De tweede helft van de eis uit §3.8: niet alleen dát er een bron is, maar welke — en na te
    /// kijken. Een bronregel die alleen een woord is, is een bewering over een bewering. De paden komen
    /// uit <see cref="HourText"/> en <see cref="BillingText"/> en worden hier niet nagebouwd; alleen het
    /// agentdetail heeft geen padfunctie en staat daarom hier voluit, met de route van
    /// <c>AgentDetail.razor</c> ernaast.
    /// </remarks>
    public static string? GroundPath(string customerId, SupportGroundKind kind, string? key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        if (!Filled(key))
        {
            return null;
        }

        var trimmed = key!.Trim();

        return kind switch
        {
            // Route: /klant/{Slug}/agents/{Agentnaam} — zie AgentDetail.razor.
            SupportGroundKind.AgentStatus =>
                $"/klant/{Uri.EscapeDataString(customerId)}/agents/{Uri.EscapeDataString(trimmed)}",
            SupportGroundKind.Hours when HourMonths.Parse(trimmed) is not null =>
                HourText.MonthPath(customerId, trimmed),
            SupportGroundKind.Invoice when HourMonths.YearOf(trimmed) is { } year =>
                BillingText.MonthPath(customerId, year, trimmed),
            _ => null,
        };
    }

    /// <summary>
    /// Het feit over één agent, zoals de eerstelijn het mag noemen.
    /// </summary>
    /// <param name="agent">De klantrij van de agent.</param>
    /// <param name="now">Het moment waartegen de relatieve tijden worden gerekend.</param>
    /// <returns>Eén regel Nederlands.</returns>
    /// <remarks>
    /// <para><strong>Let op wat er níet in staat: geen statusmelding en geen foutmelding.</strong>
    /// <see cref="AgentText.StatusNotice"/> is de uitleg die §3.3 op het agentdetail vraagt, en die
    /// draagt bij een mislukte run de <c>errorMessage</c> van de agentbouwer mee. Punt 14 van de
    /// fase-0-afwijkingen zegt van dat veld dat er een pad of een klassenaam in kan staan, en
    /// accepteert dat als restrisico op een scherm waar een logregel dertig dagen leeft en een run
    /// vierhonderd. Een supportbericht heeft geen TTL: wat hier binnenkomt staat er over drie jaar nog.
    /// De uitleg staat dus op het agentdetail, waar de bronregel naartoe wijst, en niet in de
    /// bubbel.</para>
    ///
    /// <para>De agentnaam zelf is er ook vrije tekst — maar dat is een naam die wíj kiezen en die op
    /// elk klantscherm al staat, dus die grens is niet nieuw.</para>
    /// </remarks>
    public static string AgentFact(CustomerAgentRow agent, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var parts = new List<string>
        {
            $"De agent {agent.AgentName} heeft de status {StatusVisuals.Label(agent.Status)}.",
        };

        parts.Add(agent.LastActivityAt is { } last
            ? $"Laatste run {TimeFormat.Relative(last, now)}."
            : "Er is nog geen run geweest.");

        if (agent.NextRunAt is { } next)
        {
            parts.Add($"Volgende run {TimeFormat.Relative(next, now)}.");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Het feit over de uren van één maand.
    /// </summary>
    /// <param name="month">De maandstand uit de klantweergave van het urenscherm.</param>
    /// <returns>Eén regel Nederlands.</returns>
    /// <remarks>
    /// <para><see cref="HourBalance"/> is het type dat beide rollen delen en dat met opzet geen enkel
    /// spoor van de fiatteringsstroom draagt: geen te-fiatteren-teller, geen vlag dat er nog iets ligt.
    /// Deze zin kan dus niet per ongeluk over die stroom gaan, want de bron heeft de velden niet. Dat is
    /// dezelfde reden waarom <see cref="SupportGrounds"/> uit viewmodellen leest en niet uit
    /// documenten.</para>
    ///
    /// <para><c>null</c> bij de bundel is "niet afgesproken" en nul is "geen bundel" — punt 19 van de
    /// fase-0-afwijkingen. De zin zegt dat verschil dan ook, in plaats van een streepje te tonen waar
    /// een getal hoort.</para>
    /// </remarks>
    public static string HoursFact(HourBalance month)
    {
        ArgumentNullException.ThrowIfNull(month);

        var booked = HourText.Hours(month.Booked);

        var head = month.BundledHours is { } bundle
            ? $"In {month.MonthLabel} staan {booked} gefiatteerde uren op een bundel van {HourText.Hours(bundle)}."
            : $"In {month.MonthLabel} staan {booked} gefiatteerde uren. Er is geen urenbundel vastgelegd.";

        var tail = month.Status switch
        {
            HourMonthStatus.OverBundle when month.OverBundleHours is { } over =>
                $"Dat is {HourText.Hours(over)} boven bundel; het meerdere wordt achteraf gefactureerd.",
            HourMonthStatus.WithinBundle when month.Balance is { } balance =>
                $"Er is nog {HourText.Hours(balance)} over.",
            _ => HourStatusVisuals.Label(month.Status) + ".",
        };

        return $"{head} {tail}";
    }

    /// <summary>
    /// Het feit over het door te belasten bedrag van één maand.
    /// </summary>
    /// <param name="month">De maandrij uit de klantweergave van het facturatiescherm.</param>
    /// <returns>Eén regel Nederlands.</returns>
    /// <remarks>
    /// <para>Deze functie wordt alleen aangeroepen voor een maand met een bekend totaal; zie
    /// <see cref="SupportGrounds.FromBilling"/>, dat een maand zonder totaal overslaat. Er staat hier
    /// daarom geen terugval voor een onbekend bedrag, en dat is opzet: een terugval zou een zin
    /// opleveren die zegt dat we het niet weten, en dat is een antwoord over een gat. Een gat hoort een
    /// escalatie te worden.</para>
    ///
    /// <para>"Concept" en niet "factuur" bij de lopende maand. Dezelfde correctie als §7 van het
    /// haalbaarheidsrapport op de factuurstatus maakt: een label boven een gegeven dat iets anders
    /// betekent is een onwaarheid met een tijdstempel eronder.</para>
    /// </remarks>
    public static string BillingFact(CustomerChargeRow month)
    {
        ArgumentNullException.ThrowIfNull(month);

        var what = month.IsFinal ? "gefactureerd" : "een concept";

        var head =
            $"Over {month.MonthLabel} staat {BillingText.Amount(month.Total)} door te belasten, en dat is {what}.";

        return month.MeasuredAt is { } measured
            ? $"{head} De meting is van {TimeFormat.Absolute(measured)}."
            : head;
    }

    /// <summary>
    /// De tekst van een antwoord van de eerstelijn.
    /// </summary>
    /// <param name="ground">De grondslag waarop het antwoord rust.</param>
    /// <returns>De tekst zoals hij op het bericht komt te staan.</returns>
    /// <remarks>
    /// <para><strong>De tekst is het feit, en er komt niets bij.</strong> Geen inleiding die iets
    /// belooft, geen samenvatting die iets afleidt, geen "waarschijnlijk". Elke zin die niet uit de
    /// grondslag komt, is een zin waarvoor geen bron aan te wijzen is, en dan zou de bronregel eronder
    /// over een deel van de bubbel gaan in plaats van over de bubbel.</para>
    ///
    /// <para>Dat deze functie zo kort is, is het bewijs dat het ontwerp klopt en geen aanwijzing dat er
    /// iets ontbreekt. De hele inhoud van een AI-bubbel is een tekst die het portaal uit zijn eigen
    /// weergave heeft opgemaakt; de eerstelijn heeft een keuze gemaakt en verder niets bijgedragen.
    /// </para>
    /// </remarks>
    public static string Answer(SupportGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);

        return ground.Fact;
    }

    /// <summary>
    /// De tekst van een escalatie: de eerstelijn weet het niet en zet de vraag door.
    /// </summary>
    /// <returns>De tekst zoals hij op het bericht komt te staan.</returns>
    /// <remarks>
    /// <para><strong>Één zin voor alle vier de redenen, en dat is een bewust verlies.</strong> Zie
    /// <see cref="SupportEscalation"/>: zodra het model uit vier zinnen mag kiezen, mag het vier
    /// verschillende dingen beweren over wat wij van deze klant weten. De reden reist als enum mee en
    /// staat op het operatorscherm.</para>
    ///
    /// <para>De zin noemt de SLA niet. Die staat als aparte regel onder de bubbel en komt live van het
    /// contract (<see cref="ContractDocument.Sla"/>). Zou hij in de tekst worden meegeschreven, dan
    /// stond er in elk bericht een kopie van een contractafspraak, en dan is de vraag "welke SLA gold
    /// er" op twee plekken te beantwoorden.</para>
    /// </remarks>
    public static string Handoff() =>
        "Dit weet ik niet zeker, en dan zeg ik het liever dan dat ik het erbij verzin. Ik heb je "
        + "vraag doorgezet naar het team van Soratus.";

    /// <summary>
    /// De regel onder een escalatie: naar wie de vraag gaat en binnen welke termijn.
    /// </summary>
    /// <param name="sla">De SLA van het contract, of <c>null</c> als er niets is vastgelegd.</param>
    /// <returns>De regel.</returns>
    /// <remarks>
    /// <para><strong>Geen getal als er geen SLA is.</strong> §3.8 zegt "escaleren gebeurt naar het team
    /// binnen de SLA", en het contract heeft daar één veld voor:
    /// <see cref="ContractDocument.Sla"/> — één regel tekst, bijvoorbeeld <c>Reactie 4 werkuren ·
    /// herstel 1 werkdag</c>. Er wordt hier dus niets omgerekend en niets verzonnen; de tekst van het
    /// contract gaat door. Is er geen contract of geen SLA, dan staat dat er, en niet "binnen 24 uur" —
    /// dat is punt 15 van de fase-0-afwijkingen in woorden: een afspraak die ontbreekt is geen
    /// afspraak met een standaardwaarde.</para>
    ///
    /// <para>De SLA is vrije tekst uit onze eigen administratie en gaat daarom langs
    /// <see cref="MessageTruncation.Cut"/> — dezelfde functie als de logregels en de mailkant, en
    /// dezelfde reden. Eén regel, want dit is een regel onder een bubbel.</para>
    /// </remarks>
    public static string SlaNotice(string? sla)
    {
        var one = string.IsNullOrWhiteSpace(sla)
            ? null
            : MessageTruncation.Cut(sla, MessageTruncation.MinimumLength * 4).Message.Trim();

        return string.IsNullOrEmpty(one)
            ? "Doorgezet naar het team van Soratus. Er is geen reactietermijn vastgelegd in het "
              + "contract; we pakken het zo snel mogelijk op."
            : $"Doorgezet naar het team van Soratus{Separator}{one}";
    }

    /// <summary>
    /// De reden van een escalatie, in woorden voor een operator.
    /// </summary>
    /// <param name="escalation">De reden.</param>
    /// <returns>De tekst.</returns>
    /// <remarks>
    /// <strong>Operator-only.</strong> Deze woorden staan op geen enkel klanttype en komen dus nergens
    /// in een klantbubbel terecht: <see cref="SupportHandoffBubble"/> heeft geen redenveld, en het
    /// enige type dat de reden dráágt is <see cref="OperatorHandoff"/> — dat hangt aan
    /// <see cref="OperatorSupportView"/> en niet aan <see cref="CustomerSupportView"/>. Dezelfde vorm
    /// als bij <c>errorType</c> in punt 14: wat er niet op het type staat kan niet lekken, ook niet als
    /// iemand er over een half jaar een tooltip bij zet.
    /// </remarks>
    public static string EscalationLabel(SupportEscalation escalation) => escalation switch
    {
        SupportEscalation.OutsideTheData => "Valt buiten de portaalgegevens",
        SupportEscalation.NeedsAHuman => "Vraagt een besluit en geen feit",
        SupportEscalation.AnswerNotUsable => "Antwoord niet aangenomen",
        _ => "Wist het niet zeker",
    };

    /// <summary>
    /// De maandsleutel als label, of <c>null</c> als het geen maand is.
    /// </summary>
    /// <remarks>
    /// <see cref="HourMonths.Label"/> geeft de sleutel zelf terug als hij niet te lezen is, en dat is
    /// daar het juiste gedrag — een tabelkop hoort iets te tonen. Hier is het dat niet: een grondslag
    /// waarvan de maand niet te lezen is, is geen grondslag, en dan hoort er geen bronregel te staan.
    /// </remarks>
    private static string? Month(string? key) =>
        Filled(key) && HourMonths.Parse(key) is not null
            ? HourMonths.Label(key!.Trim())
            : null;

    private static bool Filled(string? value) => !string.IsNullOrWhiteSpace(value);
}
