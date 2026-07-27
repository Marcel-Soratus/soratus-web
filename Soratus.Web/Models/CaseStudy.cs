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

    /// <summary>Bouwtijd, bijv. "2 uur". Verschijnt prominent als bewijs van snelheid.</summary>
    public required string BuildTime { get; init; }

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
            Title = "Een accountant-agent die het jaarverslag schrijft",
            Lede = "Gekoppeld aan SnelStart, kijkt mee in de administratie en levert een eerste versie van het jaarverslag terwijl je erover praat. Bouwtijd: twee uur.",
            BuildTime = "2 uur",
            Integrations = ["SnelStart"],
            Problem =
            [
                "Cijfers uit de administratie exporteren naar Excel, per jaar, per grootboekrekening.",
                "Ratio's narekenen: liquiditeit, solvabiliteit, marge, resultaat tegen vorig jaar.",
                "De toelichting schrijven. Elk jaar hetzelfde skelet, elke klant andere cijfers.",
                "Bij elke vraag van de klant terug naar de administratie om het na te kijken.",
            ],
            Steps =
            [
                new("01", "Koppelen aan SnelStart",
                    "De agent leest de administratie rechtstreeks uit. Geen export, geen tussenbestand dat een dag later al niet meer klopt."),
                new("02", "Vragen stellen in gewone taal",
                    "\"Hoe staan liquiditeit en solvabiliteit ervoor?\" De agent rekent het na op de echte cijfers en legt uit waar het getal vandaan komt."),
                new("03", "Het rapport bouwt zich op",
                    "Sectie voor sectie, terwijl het gesprek loopt. Je stuurt bij in de chat en ziet het jaarverslag naast je meegroeien."),
                new("04", "Jij controleert en tekent",
                    "De agent levert de eerste versie en de onderbouwing. Het vakoordeel blijft bij de accountant. Dat is precies de bedoeling."),
            ],
            HandsOff =
            [
                "Exporteren en overtypen van cijfers uit de administratie.",
                "Het narekenen van standaardratio's en de vergelijking met vorig jaar.",
                "De eerste schrijfronde van elke standaardsectie.",
                "Opzoekwerk bij tussentijdse vragen over de cijfers.",
            ],
            Scenario = new(
                "Wat neemt zo'n agent nu echt uit handen? Reken mee met een kantoor dat vijftig jaarverslagen per jaar maakt.",
                [
                    new("Cijfers verzamelen en controleren", "circa 2 uur", "minuten"),
                    new("Ratio's en vergelijking vorig jaar", "circa 1 uur", "direct"),
                    new("Eerste schrijfronde toelichting", "circa 3 uur", "circa 20 min"),
                    new("Controle, vakoordeel en ondertekening", "circa 2 uur", "circa 2 uur"),
                ],
                "Van ongeveer acht uur naar ongeveer drie uur per jaarverslag. Bij vijftig verslagen is dat een paar honderd uur per jaar die vrijkomt voor werk waar een accountant echt voor is ingehuurd.",
                "Indicatief rekenvoorbeeld om de orde van grootte te laten zien. Geen gemeten klantresultaat. Jouw praktijk heeft andere cijfers, en die rekenen we graag samen door."),
            Shots =
            [
                new("Chat met de accountant-agent naast het opbouwende jaarverslag",
                    "Links het gesprek, rechts het rapport dat sectie voor sectie volloopt.",
                    "jaarverslag-chat.png"),
                new("De agent legt liquiditeit en solvabiliteit uit op basis van de echte cijfers",
                    "Elk getal is te herleiden naar de administratie. Geen zwarte doos.",
                    "jaarverslag-ratios.png"),
            ],
            Takeaway = "Een jaarverslag is voor een groot deel voorspelbaar werk op onvoorspelbare cijfers. Precies waar een agent goed in is. Het vakoordeel blijft bij de mens, het sjouwwerk gaat naar de machine. En het kostte twee uur om te bouwen, niet twee kwartalen.",
            MetaDescription = "Case: accountant-agent gekoppeld aan SnelStart die het jaarverslag opstelt terwijl je erover praat. Gebouwd in twee uur.",
        },

        new()
        {
            Slug = "snelstart-declaraties-matchen",
            Sector = "Zorg",
            Client = "Zorgpraktijk die declareert bij zorgverzekeraars",
            Title = "Declaraties matchen met betalingen, inclusief de rare gevallen",
            Lede = "Excel met ingediende declaraties erin, gekoppeld aan de betalingen uit SnelStart. De agent vindt niet de regels die kloppen. Hij vindt de regels die niet kloppen.",
            BuildTime = "2 uur",
            Integrations = ["SnelStart", "Excel"],
            Problem =
            [
                "Honderden ingediende declaraties per jaar, verspreid over Zilveren Kruis, VGZ, CZ, Menzis en ONVZ.",
                "Betalingen komen op eigen tempo binnen, vaak gebundeld en zelden met een net declaratienummer.",
                "Deelbetalingen door verrekening van het eigen risico. Het bedrag klopt niet, de declaratie wel.",
                "Naverrekeningen over een voorgaand jaar die opduiken tussen de gewone betalingen.",
                "En de vraag die echt geld kost: welke declaratie is nooit betaald?",
            ],
            Steps =
            [
                new("01", "Declaratiebestand uploaden",
                    "Het Excel-overzicht van ingediende declaraties gaat erin. Geen nieuw systeem, geen ander werkproces."),
                new("02", "Betalingen ophalen uit SnelStart",
                    "De agent leest de ontvangen betalingen rechtstreeks uit de administratie, per boekjaar."),
                new("03", "Matchen op wat er echt staat",
                    "Declaratienummer, bedrag, datum en verzekeraar tegen elkaar. Ook als de omschrijving \"deelbetaling, verrekening eigen risico\" is in plaats van een net nummer."),
                new("04", "De uitzonderingen naar boven",
                    "Wat wel is ingediend en niet betaald. Wat gedeeltelijk is betaald. Wat een naverrekening over vorig jaar blijkt. Die lijst is het hele punt."),
            ],
            HandsOff =
            [
                "Het regel-voor-regel afvinken van declaraties tegen bankbetalingen.",
                "Uitzoeken waarom een bedrag afwijkt, bij deelbetalingen en verrekend eigen risico.",
                "Naverrekeningen over een ander boekjaar op de juiste plek zetten.",
                "Het opsporen van niet-betaalde declaraties, voordat de termijn verloopt.",
            ],
            Scenario = new(
                "De winst zit niet in de makkelijke matches. Die zijn zo gedaan. De winst zit in de uitzonderingen die je met het oog mist. Reken mee met een praktijk die maandelijks tweehonderd declaraties indient.",
                [
                    new("Betalingen afvinken tegen declaraties", "circa 4 uur per maand", "minuten"),
                    new("Afwijkende bedragen uitzoeken", "circa 2 uur per maand", "agent markeert ze"),
                    new("Openstaande declaraties opsporen", "vaak pas als het opvalt", "elke maand compleet"),
                    new("Beoordelen en actie ondernemen", "circa 1 uur per maand", "circa 1 uur per maand"),
                ],
                "Van ongeveer zeven uur per maand naar ongeveer één uur. Dat is bijna een werkweek per jaar. De echte opbrengst is de declaratie die je anders was vergeten, want die is honderd procent verlies.",
                "Indicatief rekenvoorbeeld om de orde van grootte te laten zien. Geen gemeten klantresultaat. Bij jouw volume en verzekeraars ziet het er anders uit."),
            Shots =
            [
                new("Overzicht van ontvangen betalingen per verzekeraar",
                    "De betalingen zoals ze uit SnelStart komen, per boekjaar.",
                    "declaraties-betalingen.png"),
                new("Resultaat van de matching met gemarkeerde uitzonderingen",
                    "Gematcht, deels betaald, of nooit betaald. Die laatste categorie is het geld.",
                    "declaraties-matching.png"),
            ],
            Takeaway = "Dit is het soort werk waar niemand voor heeft gestudeerd en dat toch elke maand terugkomt. Een agent doet het zonder te verslappen bij regel honderd. Wat overblijft is de beoordeling, en die hoort bij een mens. Ook dit stond in twee uur.",
            MetaDescription = "Case: declaraties automatisch matchen met betalingen uit SnelStart, inclusief deelbetalingen en verrekend eigen risico. Gebouwd in twee uur.",
        },
    ];

    public static CaseStudy? BySlug(string? slug) =>
        All.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
