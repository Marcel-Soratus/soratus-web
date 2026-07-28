namespace Soratus.Web.Models;

/// <summary>
/// Eén klantcase. Nieuwe case toevoegen = één record aan <see cref="CaseStudies.All"/>.
/// Klantnamen blijven anoniem tenzij er expliciet toestemming is.
/// </summary>
public sealed record CaseStudy
{
    /// <summary>URL-segment: /cases/{Slug}. Wijzig dit niet na publicatie (links breken).</summary>
    public required string Slug { get; init; }

    /// <summary>Korte sectorlabel, bijv. "Accountancy".</summary>
    public required string Sector { get; init; }

    /// <summary>Anonieme klantomschrijving, bijv. "Accountantskantoor, MKB-klanten".</summary>
    public required string Client { get; init; }

    /// <summary>Kaarttitel op /cases en h1 op de detailpagina.</summary>
    public required string Title { get; init; }

    /// <summary>Eén regel die de case verkoopt. Verschijnt op de kaart en onder de h1.</summary>
    public required string Lede { get; init; }

    /// <summary>
    /// Tijd tot een werkende demo, bijv. "2 uur". Overal gelabeld als demo en niet
    /// als bouwtijd: productieklaar maken is meer werk, en dat moet een prospect
    /// niet verkeerd kunnen lezen.
    /// </summary>
    public required string BuildTime { get; init; }

    /// <summary>
    /// Wat de klant eraan overhoudt. Bewust niet "tijdwinst": bij de ene case zijn
    /// het uren, bij de andere uitgespaarde licentiekosten. Leeg laten als er geen
    /// cijfer is; dan toont de pagina er ook geen.
    /// </summary>
    public CaseResult? Result { get; init; }

    /// <summary>Systemen waarmee gekoppeld is, bijv. ["SnelStart"].</summary>
    public required string[] Integrations { get; init; }

    /// <summary>Het handwerk zoals het was. Elke regel één concrete taak.</summary>
    public required string[] Problem { get; init; }

    /// <summary>Wat de agent doet, in stappen.</summary>
    public required CaseStep[] Steps { get; init; }

    /// <summary>Welk werk de agent overneemt. Kern van de pagina.</summary>
    public required string[] HandsOff { get; init; }

    /// <summary>Indicatief rekenvoorbeeld. Altijd gelabeld als scenario, nooit als gemeten resultaat.</summary>
    public required CaseScenario Scenario { get; init; }

    /// <summary>Schermafbeeldingen. Zonder Src rendert een placeholder met de verwachte bestandsnaam.</summary>
    public CaseShot[] Shots { get; init; } = [];

    /// <summary>Slotalinea: waarom dit breder telt dan deze ene klant.</summary>
    public required string Takeaway { get; init; }

    /// <summary>Meta description voor SEO en og:description.</summary>
    public required string MetaDescription { get; init; }
}

public sealed record CaseStep(string Number, string Title, string Body);

/// <summary>
/// De opbrengst voor de klant.
/// </summary>
/// <param name="Label">Kop in de feiten-rij, bijv. "Bespaarde licentie".</param>
/// <param name="Value">Het getal dat groot wordt gezet, bijv. "€ 100.000".</param>
/// <param name="CardNote">Wat op de overzichtskaart achter het getal komt.</param>
/// <param name="IsEstimate">
/// True bij een eigen aanname in plaats van een cijfer dat de klant heeft
/// teruggekoppeld. De pagina labelt het dan als schatting, zodat een gemeten
/// resultaat en een rekenvoorbeeld niet door elkaar gaan lopen.
/// </param>
public sealed record CaseResult(string Label, string Value, string CardNote, bool IsEstimate = false);

public sealed record CaseShot(string Alt, string Caption, string FileName, string? Src = null);

/// <summary>
/// Rekenvoorbeeld: handwerk versus met agent. <see cref="Note"/> maakt expliciet dat dit
/// een scenario is en geen gemeten klantcijfer.
/// </summary>
public sealed record CaseScenario(string Intro, CaseScenarioRow[] Rows, string Outcome, string Note);

public sealed record CaseScenarioRow(string Task, string Manual, string WithAgent);

public static class CaseStudies
{
    public static readonly CaseStudy[] All =
    [
        new()
        {
            Slug = "snelstart-jaarverslag-agent",
            Sector = "Accountancy",
            Client = "Accountantskantoor met MKB-klanten",
            Title = "Een accountant-agent die het jaarverslag schrijft en zijn cijfers kan uitleggen",
            Lede = "Gekoppeld aan SnelStart. Hij levert een compleet bestuursverslag met balans en kengetallen, zet bij elk ratio de formule waarmee het is berekend, en herschrijft het op verzoek in het gesprek. In twee uur stond er een werkende demo.",
            BuildTime = "2 uur",
            // De winst zat hier in geld, niet in uren: de tool die hiervoor in beeld
            // was zou een ton aan licentiekosten kosten.
            Result = new("Uitgespaarde licentie", "€ 100.000", "aan licentiekosten die niet meer nodig waren"),
            Integrations = ["SnelStart"],
            Problem =
            [
                "Cijfers uit de administratie exporteren naar Excel, per jaar, per grootboekrekening.",
                "Ratio's narekenen: current ratio, quick ratio, solvabiliteit, brutomarge, werkkapitaal.",
                "De toelichting schrijven. Elk jaar hetzelfde skelet, elke klant andere cijfers.",
                "De aandachtspunten eruit halen. Groeit het werkkapitaal mee met de omzet, of loopt het uit de hand?",
                "En daarna nog een keer, omdat de directie een compactere versie wil dan de bank.",
            ],
            Steps =
            [
                new("01", "Koppelen aan SnelStart",
                    "De agent leest de administratie rechtstreeks uit. Geen export, geen tussenbestand dat een dag later al niet meer klopt."),
                new("02", "Vragen stellen in gewone taal",
                    "\"Hoe staan liquiditeit en solvabiliteit ervoor?\" Hij rekent het na op de echte cijfers, legt uit hoe hij eraan komt en benoemt wat eruit springt."),
                new("03", "Een compleet verslag, geen samenvatting",
                    "Bestuursverslag, continuïteit en advies, resultaatbestemming en de balans. Met een kengetallentabel waarin bij elk ratio de formule staat, zodat elk getal na te rekenen is. Klaar om af te drukken of als PDF te versturen."),
                new("04", "Bijsturen in het gesprek",
                    "\"Doe het formeler.\" \"Maak een compacte versie voor de directie.\" De agent herschrijft de passage en vertelt wat hij heeft aangepast. Het vakoordeel en de ondertekening blijven bij de accountant."),
            ],
            HandsOff =
            [
                "Exporteren en overtypen van cijfers uit de administratie.",
                "Het narekenen van de kengetallen en de vergelijking met vorig boekjaar.",
                "De eerste schrijfronde van bestuursverslag, continuïteit en resultaatbestemming.",
                "De formule bij elk ratio zetten zodat een reviewer het kan controleren.",
                "Het benoemen van aandachtspunten zoals oplopende voorraden en debiteuren.",
                "Het herschrijven naar een andere toon of lengte voor een andere lezer.",
            ],
            Scenario = new(
                "Wat neemt zo'n agent nu echt uit handen? Reken mee met een kantoor dat vijftig jaarverslagen per jaar maakt.",
                [
                    new("Cijfers verzamelen en controleren", "circa 2 uur", "minuten"),
                    new("Kengetallen en vergelijking vorig jaar", "circa 1 uur", "direct, met formule"),
                    new("Eerste schrijfronde toelichting", "circa 3 uur", "circa 20 min"),
                    new("Tweede versie voor een andere lezer", "circa 1 uur", "een vraag in de chat"),
                    new("Controle, vakoordeel en ondertekening", "circa 2 uur", "circa 2 uur"),
                ],
                "Van ongeveer negen uur naar ongeveer drie uur per jaarverslag, waarvan het grootste deel controle blijft. Maar de aanleiding was hier niet tijd. Het was geld: de tool die hiervoor in beeld was, zou een ton aan licentiekosten kosten. In twee uur stond er een demo die dat bedrag overbodig maakte.",
                "De ton aan licentiekosten is wat deze klant zou gaan betalen. De urentabel hierboven is een indicatief rekenvoorbeeld om te laten zien waar het werk zit, geen gemeten klantresultaat. Jouw praktijk heeft andere cijfers, en die rekenen we graag samen door."),
            Shots =
            [
                new("Startscherm met de chat links en het lege rapport-paneel rechts",
                    "Links het gesprek, rechts het rapport. Je begint met een vraag of laat het verslag opstellen.",
                    "jaarverslag-start.png"),
                new("Het opgestelde jaarverslag met bestuursverslag naast de samenvatting in de chat",
                    "De agent vat het kernbeeld samen en zet het volledige bestuursverslag in het rapport-paneel.",
                    "jaarverslag-rapport.png"),
                new("Kengetallentabel met per ratio de formule, naast de uitleg in het gesprek",
                    "Bij elk kengetal staat waaruit het is berekend. Daarmee is het rapport controleerbaar en geen zwarte doos.",
                    "jaarverslag-kengetallen.png"),
            ],
            Takeaway = "Het verschil zit in de kolom \"toelichting\" naast de kengetallen. Current ratio 2,13, en ernaast staat waaruit dat is berekend. Een accountant tekent niet voor een getal dat hij niet kan navertellen. Daarom is die kolom belangrijker dan de snelheid. Het sjouwwerk gaat naar de machine, de verantwoordelijkheid blijft waar die hoort. En de demo stond er in twee uur, niet in twee kwartalen.",
            MetaDescription = "Case: accountant-agent gekoppeld aan SnelStart die een compleet jaarverslag opstelt met kengetallen en de formule per ratio. Werkende demo in twee uur.",
        },

        new()
        {
            Slug = "snelstart-declaraties-matchen",
            Sector = "Zorg",
            Client = "Zorgpraktijk die declareert bij zorgverzekeraars",
            Title = "Een agent die declaraties nakijkt en zelf voorstelt wat er moet gebeuren",
            Lede = "Gekoppeld aan SnelStart. Hij matcht declaraties met betalingen, stelt een diagnose bij elke afwijking en draagt een vervolgstap aan. Boven 80 procent zekerheid handelt hij zelf af. Daaronder vraagt hij het jou. In twee uur stond er een werkende demo.",
            BuildTime = "2 uur",
            // Door de klant teruggekoppeld.
            Result = new("Bespaart de klant", "400 uur per jaar", "minder uitzoekwerk bij de klant"),
            Integrations = ["SnelStart", "Excel"],
            Problem =
            [
                "Honderden ingediende declaraties per jaar, verspreid over Zilveren Kruis, VGZ, CZ, Menzis en ONVZ.",
                "Betalingen komen op eigen tempo binnen, vaak gebundeld en zelden met een net declaratienummer.",
                "Een lager bedrag terug dan gedeclareerd. Klopt dat, of is het eigen risico verrekend?",
                "Tikfouten die niemand ziet. Een komma verkeerd, een dag en maand omgedraaid, twee keer hetzelfde nummer ingediend.",
                "De declaratietermijn van 90 dagen die verloopt terwijl je het niet in beeld hebt.",
                "En de vraag die echt geld kost: welke declaratie is nooit betaald?",
            ],
            Steps =
            [
                new("01", "Declaratiebestand erin, betalingen erbij",
                    "Het Excel-overzicht van ingediende declaraties gaat erin. De ontvangen betalingen komen rechtstreeks uit SnelStart, per boekjaar. Geen nieuw systeem, geen ander werkproces."),
                new("02", "Matchen en het verschil benoemen",
                    "Declaratienummer, bedrag, datum en verzekeraar tegen elkaar. Elke declaratie krijgt een status: ontvangen, afwijkend bedrag, of niet ontvangen. Met het openstaande bedrag eronder."),
                new("03", "Diagnose per bevinding",
                    "Niet alleen dát het afwijkt, maar waaróm. Behandeldatum na de indiendatum, dus vermoedelijk dag en maand omgedraaid. Een bedrag dat niet kan, dus vermoedelijk een kommafout. Twee keer hetzelfde declaratienummer, waarvan de eerste al is uitbetaald."),
                new("04", "Voorstel met zekerheidsscore",
                    "Opnieuw indienen met de correctie er al bij, laten vervallen, navragen bij de verzekeraar, of het niet-vergoede deel bij de patiënt factureren. Boven 80 procent zekerheid handelt de agent zelf af. Daaronder legt hij het met een concrete vraag bij jou neer."),
            ],
            HandsOff =
            [
                "Het regel voor regel afvinken van declaraties tegen ontvangen betalingen.",
                "Uitzoeken waarom een bedrag afwijkt, bijvoorbeeld bij verrekening van het eigen risico.",
                "Tikfouten opsporen die je met het oog mist: kommafouten, omgedraaide datums, dubbele indieningen.",
                "Bepalen wat de vervolgstap is en de correctie klaarzetten.",
                "Bijhouden welke declaraties tegen de termijn van 90 dagen aanlopen.",
                "De scheiding maken tussen wat veilig automatisch kan en wat een mens moet zien.",
            ],
            Scenario = new(
                "De winst zit niet in de makkelijke matches. Die zijn zo gedaan. De winst zit in de bevindingen die je met het oog mist, en in het feit dat de agent er meteen een vervolgstap bij levert. Reken mee met een praktijk die maandelijks tweehonderd declaraties indient.",
                [
                    new("Betalingen afvinken tegen declaraties", "circa 4 uur per maand", "minuten"),
                    new("Uitzoeken waarom iets afwijkt", "circa 2 uur per maand", "diagnose staat er al"),
                    new("Vervolgstap bepalen en correctie klaarzetten", "circa 1 uur per maand", "voorstel klaar"),
                    new("Openstaande declaraties opsporen", "vaak pas als het opvalt", "elke maand compleet"),
                    new("Beoordelen wat de agent aanreikt", "niet van toepassing", "circa 30 min per maand"),
                ],
                "Van ongeveer zeven uur per maand naar ongeveer een half uur beoordelen. Deze klant zit ruim boven dat voorbeeldvolume en kwam uit op 400 uur per jaar, oftewel tien werkweken. In de demo handelde de agent 6 van de 8 bevindingen zelf af en legde hij er 2 ter beoordeling neer. De echte opbrengst is niet de tijd. Het is de declaratie die je anders was vergeten, want die is honderd procent verlies.",
                "De 400 uur per jaar is wat deze klant zelf heeft teruggekoppeld; die praktijk verwerkt meer declaraties dan de tweehonderd per maand uit dit voorbeeld. De urentabel is een indicatief rekenvoorbeeld, en de verhouding 6 van 8 komt uit de demo met voorbeeldgegevens en niet uit een klantmeting."),
            Shots =
            [
                new("Overzicht van ontvangen betalingen per verzekeraar",
                    "Het startpunt: de betalingen zoals ze uit SnelStart komen, per boekjaar.",
                    "declaraties-betalingen.png"),
                new("Matchingresultaat met statussen, openstaand bedrag en bevindingen per declaratie",
                    "Gedeclareerd tegen ontvangen, met het openstaande bedrag en per regel de bevinding. Filteren op ontvangen, afwijkend bedrag of niet ontvangen.",
                    "declaraties-matching.png"),
                new("Afhandeling door de AI-agent met voorgestelde actie en zekerheidsscore per bevinding",
                    "Per bevinding een voorstel met zekerheidsscore. Onder 80 procent stelt de agent een vraag in plaats van zelf te handelen.",
                    "declaraties-agent.png"),
            ],
            Takeaway = "Het aardige zit in wat de agent níet doet. Een dubbele declaratie laat hij bij 98 procent zekerheid vervallen. Bij 58 procent stelt hij een vraag in plaats van iets te doen. Die grens maakt het verschil tussen een hulpmiddel dat je vertrouwt en een zwarte doos die je moet nacontroleren. Het vakoordeel blijft bij de praktijk, het uitzoekwerk niet. En de demo stond er in twee uur.",
            MetaDescription = "Case: een AI-agent die declaraties matcht met betalingen uit SnelStart, afwijkingen diagnosticeert en per bevinding een vervolgstap voorstelt met zekerheidsscore. Werkende demo in twee uur.",
        },
    ];

    public static CaseStudy? BySlug(string? slug) =>
        All.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
