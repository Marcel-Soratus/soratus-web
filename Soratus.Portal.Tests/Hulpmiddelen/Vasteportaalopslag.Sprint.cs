using Soratus.Portal.Security;
using Soratus.Portal.Sprints;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// De sprintkant van de portaalopslag in het geheugen (§3.4): de laatste lezing per klant.
/// </summary>
/// <remarks>
/// <para><strong>Dit is dezelfde klasse als de rest van <see cref="Vasteportaalopslag"/> en geen tweede
/// opslag.</strong> Het sprintscherm leest de sprintlezing, en het operatorpad leest daarnaast het
/// klantdocument voor het vastgelegde bord. Zouden dat twee fixtures zijn, dan kan het bord in de ene een
/// ander bord zijn dan in de andere — en juist dat verschil is wat er op het operatorscherm te zien hoort
/// te zijn ("bord" naast "bevraagd"). Twee fixtures zouden dat verschil kunnen laten kloppen zonder dat de
/// projectie het bewaart.</para>
///
/// <para><see cref="IPortalSprintStore"/> wordt hier op de partial aangegeven en niet in het bestand met
/// de contractkant. Dat is een lane-keuze en geen ontwerpkeuze: er werken meerdere sessies in deze
/// repository, en een nieuw bestand botst niet. Dezelfde truc als bij
/// <c>Vasteportaalopslag.Kosten.cs</c>.</para>
///
/// <para><strong>Rijk gevuld, en dat is het punt.</strong> De gezaaide sprint dekt precies de gevallen
/// waarin dit onderdeel fout kan gaan, en elk ervan komt uit een meting van 22 augustus 2026:</para>
///
/// <list type="table">
///   <item><term>een item zonder uren, punten, tags en toewijzing</term><description>
///     <strong>De belangrijkste rij van deze fixture.</strong> Van de zestien work items die uit dit bord
///     kwamen had géén enkel item een waarde in <c>RemainingWork</c>, <c>CompletedWork</c> of
///     <c>StoryPoints</c>, en twee hadden geen <c>System.AssignedTo</c> — die velden stonden niet in het
///     antwoord. Een test die hier een nul vindt, heeft een defect gevonden.
///   </description></item>
///   <item><term>een item met uren én punten</term><description>
///     De spiegel: zonder deze rij is "een som bestaat dan en slechts dan als er iets is om op te tellen"
///     niet te onderscheiden van "er is nooit een som".
///   </description></item>
///   <item><term>een item met nul resterende uren</term><description>
///     Een echte nul. Dít hoort als <c>0 u</c> op het scherm en niet als streepje — de keerzijde van punt
///     30, waar nul mét regels ook een echte nul is.
///   </description></item>
///   <item><term>een item met de blokkadetag</term><description>
///     Gemeten heeft dit bord géén <c>Blocked</c>-state, dus een blokkade kan alleen een tag zijn. Deze
///     rij is de enige manier om de statistiek "geblokkeerd" te meten.
///   </description></item>
///   <item><term>een afgerond item</term><description>
///     Categorie <c>Completed</c>, zodat "afgerond" een ander getal is dan "work items".
///   </description></item>
///   <item><term>een verwijderd item</term><description>
///     Categorie <c>Removed</c>. Telt niet mee in het aantal, en dat verschil is niet te meten zonder
///     deze rij.
///   </description></item>
///   <item><term>een item aangemaakt door een agentidentiteit</term><description>
///     Zodat de herkomst drie waarden kan krijgen in plaats van twee.
///   </description></item>
///   <item><term>één iteratie zonder datums</term><description>
///     Op dit bord staan er drie, met werkitems erin, met opzet niet aangeraakt. Er valt dus werk buiten
///     elke sprintweergave, en een scherm dat dat niet meldt biedt een onvolledig beeld als volledig aan.
///   </description></item>
/// </list>
///
/// <para><strong>En er staan met opzet twee e-mailadressen in.</strong> Dat zijn de gegevens waarop de
/// rolgrens te meten valt: ze horen op het operatortype te staan en niet op het klanttype. Een test die
/// <see cref="Aanmakeradres"/> in de klantmarkup vindt, heeft een lek gevonden.</para>
/// </remarks>
internal sealed partial class Vasteportaalopslag : IPortalSprintStore
{
    /// <summary>Het DevOps-bord dat bij de gezaaide klant is vastgelegd. Operator-only (§2).</summary>
    /// <remarks>
    /// Een echte vorm en niet "test": dit veld is op het operatorscherm het enige gereedschap tegen een
    /// tikfout in een teamnaam, en een test die controleert of het er staat hoort naar iets te zoeken dat
    /// op een bord lijkt. Drie segmenten, want een sprint is een teambegrip.
    /// </remarks>
    public const string Standaardbord = "soratus/Acme Logistiek/Acme Logistiek Team";

    /// <summary>Het bord waartegen de gezaaide lezing is gedaan.</summary>
    /// <remarks>
    /// <strong>Gelijk aan <see cref="Standaardbord"/>, en dat is met opzet.</strong> De twee mógen
    /// verschillen — dan staat er op het operatorscherm een extra regel — maar in de gewone stand doen ze
    /// dat niet, en een fixture waarin ze standaard verschillen zou van die extra regel de normale
    /// toestand maken. Een test die het verschil wil meten zet er zelf een andere lezing neer.
    /// </remarks>
    public const string Bevraagdbord = Standaardbord;

    /// <summary>De naam van de gezaaide sprint.</summary>
    /// <remarks>
    /// In de vorm van het echte bord — <c>2026-08 Augustus</c> — juist omdát het portaal die naam nergens
    /// voor gebruikt. Een test die de maand uit deze naam zou kunnen halen, meet de fout die deze lane
    /// verbiedt.
    /// </remarks>
    public const string Sprintnaam = "2026-08 Augustus";

    /// <summary>Het boardpad van de gezaaide sprint (§3.4).</summary>
    /// <remarks>
    /// Zonder het <c>\Iteration\</c>-knooppunt, want zo geeft de teamiteratielijst hem — gemeten. Zie
    /// <see cref="DevOpsIteration.Path"/>.
    /// </remarks>
    public const string Boardpad = @"Acme Logistiek\2026-08 Augustus";

    /// <summary>De naam van de iteratie zonder datums.</summary>
    public const string Ongedateerdeiteratie = "Iteration 1";

    /// <summary>Het pad van de iteratie zonder datums. Operator-only.</summary>
    public const string Ongedateerdpad = @"Acme Logistiek\Iteration 1";

    /// <summary>De weergavenaam van de persoon aan wie werk is toegewezen.</summary>
    public const string Toegewezenaam = "Dennis Verhamme";

    /// <summary>Het adres van die persoon. Operator-only (§2).</summary>
    /// <remarks>
    /// Het gegeven waarop de rolgrens van dit scherm te meten valt. §2 zet koppelingsdetails dicht voor de
    /// klant, en dit is bovendien een persoonsgegeven van een medewerker. Een test die deze tekst in de
    /// klantmarkup vindt, heeft een lek gevonden.
    /// </remarks>
    public const string Toegewezenadres = "dennis@soratus.com";

    /// <summary>De weergavenaam van de aanmaker van de handmatige items. Operator-only.</summary>
    public const string Aanmakernaam = "Sanne de Wit";

    /// <summary>Het adres van die aanmaker. Operator-only.</summary>
    public const string Aanmakeradres = "sanne@soratus.com";

    /// <summary>De identiteit die als agent geldt.</summary>
    /// <remarks>
    /// Een service principal en geen mens, want dat is wat <c>devops-sync</c> zal zijn. De naam staat in
    /// <see cref="VasteSprintweergaven"/> in de agentenlijst; zonder die lijst komt élk item op
    /// <see cref="WorkItemOrigin.Unknown"/> uit, en dat is de gewone stand vandaag.
    /// </remarks>
    public const string Agentidentiteit = "sp-devops-sync@soratus.com";

    /// <summary>De blokkademarkering die de gezaaide items gebruiken.</summary>
    /// <remarks>
    /// Gelijk aan de standaard van <see cref="SprintOptions.BlockedMarker"/>. Staat hier als constante
    /// zodat een test hem kan wijzigen zonder de tag in de fixture te hoeven raden.
    /// </remarks>
    public const string Blokkademarkering = "Blocked";

    /// <summary>Waarom de laatste lezing mislukte, in de mislukte stand. Operator-only.</summary>
    /// <remarks>
    /// In gewone taal en zonder statuscode, zoals <see cref="SprintDocument.Failure"/> vraagt. Een test
    /// die deze tekst op het klantscherm vindt, heeft een lek gevonden — hij noemt onze rolverlening.
    /// </remarks>
    public const string Leesfout =
        "Het portaal mag dit bord niet lezen. De identiteit heeft leesrecht op het project nodig.";

    private readonly Dictionary<string, SprintDocument> _sprints =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _sprintgezaaid;

    /// <inheritdoc />
    public Task<SprintDocument?> GetSprintAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult(Sprintlezing(scope.CustomerId));
    }

    /// <inheritdoc />
    public Task<SprintDocument?> GetSprintAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult(Sprintlezing(scope.CustomerId));
    }

    /// <summary>
    /// Haalt elke sprintlezing weg, zodat het scherm op "nog niet opgehaald" uitkomt.
    /// </summary>
    /// <remarks>
    /// Voor de toestand die in productie de gewone beginstand is: het portaal staat er, de sprintcollector
    /// heeft nog nooit gedraaid. Dat hoort een scherm met een mededeling op te leveren en geen leeg
    /// sprintoverzicht, en dat verschil is niet te meten zonder deze methode.
    /// </remarks>
    public void GeenSprint()
    {
        _sprintgezaaid = true;
        _sprints.Clear();
    }

    /// <summary>
    /// Zet een sprintlezing neer, buiten de collector om.
    /// </summary>
    /// <param name="lezing">De lezing. De etag wordt door deze opslag gezet.</param>
    /// <param name="klant">De klantslug.</param>
    /// <remarks>
    /// Er is geen schrijfpad op <see cref="IPortalSprintStore"/> en dat blijft zo (zie die interface), dus
    /// een test die een bijzondere toestand nodig heeft moet hem hier neerzetten. Dezelfde vorm als
    /// <c>LegMetingVast</c> voor een kostenmeting.
    /// </remarks>
    public void LegSprintVast(SprintDocument lezing, string klant = Standaardklant)
    {
        ArgumentNullException.ThrowIfNull(lezing);

        _sprintgezaaid = true;
        _sprints[klant] = lezing with { ETag = NieuweEtag() };
    }

    /// <summary>De sprintlezing zoals hij nu in de opslag staat.</summary>
    /// <param name="klant">De klantslug.</param>
    /// <returns>Het document, of <c>null</c>.</returns>
    public SprintDocument? Sprint(string klant = Standaardklant) => Sprintlezing(klant);

    /// <summary>
    /// De gezaaide sprintlezing, of een eigen lezing als een test er een heeft neergezet.
    /// </summary>
    /// <param name="klant">De klantslug.</param>
    /// <returns>Het document, of <c>null</c>.</returns>
    /// <remarks>
    /// Lui gezaaid en niet in de constructor, om dezelfde reden als bij de uren en de kosten: de
    /// constructor staat in het andere deel van deze klasse, en een test die <see cref="GeenSprint"/>
    /// aanroept hoort dat te kunnen doen vóór de eerste lezing.
    /// </remarks>
    private SprintDocument? Sprintlezing(string klant)
    {
        if (!_sprintgezaaid && string.Equals(klant, Standaardklant, StringComparison.OrdinalIgnoreCase))
        {
            _sprintgezaaid = true;
            _sprints[Standaardklant] = Gezaaidesprint();
        }

        return _sprints.TryGetValue(klant, out var lezing) ? lezing : null;
    }

    /// <summary>De lezing die bovenaan dit bestand staat beschreven.</summary>
    /// <returns>Het document.</returns>
    private SprintDocument Gezaaidesprint() => new()
    {
        Id = SprintDocumentKeys.Id,
        PartitionKey = Standaardklant,
        CustomerId = Standaardklant,
        State = SprintState.Current,
        Scope = Bevraagdbord,

        // Ruim binnen het kwartier, zodat "opgehaald 8 min geleden" op het scherm komt en niet een
        // relatieve tijd in uren. Het absolute moment staat in de tooltip; zie §1 van de spec.
        ReadAt = Testgegevens.Nu - TimeSpan.FromMinutes(8),
        SprintId = "2de79897-d29b-47f9-b6d0-fff5493a6e1a",
        SprintName = Sprintnaam,
        BoardPath = Boardpad,

        // De kalendermaand van de gezaaide klok. Uit de datums en niet uit de naam — dat is de hele
        // regel van deze lane, en een fixture die daar een andere maand neerzet dan de naam zegt, is
        // precies de fixture die dat kan meten.
        Start = "2026-08-01",
        Finish = "2026-08-31",
        Items =
        [
            // Het lege item. Geen uren, geen punten, geen tags, geen toewijzing — precies zoals de
            // zestien gemeten items. Dit is de rij waarop "een streepje is geen nul" te meten valt.
            new SprintWorkItem
            {
                Id = 4566,
                Type = "User Story",
                Title = "iOS MAUI",
                State = "New",
                Stage = WorkItemStage.Proposed,
                CreatedByName = Aanmakernaam,
                CreatedByUniqueName = Aanmakeradres,
            },

            // Het volle item: uren, gedane uren én punten. Zonder deze rij is een ontbrekende som niet
            // van een som die nooit bestaat te onderscheiden.
            new SprintWorkItem
            {
                Id = 4571,
                Type = "Task",
                Title = "Declaratieregels valideren",
                State = "Active",
                Stage = WorkItemStage.InProgress,
                CreatedByName = Aanmakernaam,
                CreatedByUniqueName = Aanmakeradres,
                AssignedToName = Toegewezenaam,
                AssignedToUniqueName = Toegewezenadres,
                RemainingWork = 6.5m,
                CompletedWork = 1.5m,
                StoryPoints = 3m,
            },

            // Nul resterende uren, en dat is een échte nul: iemand heeft nul ingevuld. Hij hoort als
            // 0 u op het scherm en niet als streepje.
            new SprintWorkItem
            {
                Id = 4572,
                Type = "Task",
                Title = "PDF-export nakijken",
                State = "Active",
                Stage = WorkItemStage.InProgress,
                CreatedByName = Aanmakernaam,
                CreatedByUniqueName = Aanmakeradres,
                AssignedToName = Toegewezenaam,
                AssignedToUniqueName = Toegewezenadres,
                RemainingWork = 0m,
                CompletedWork = 4m,
            },

            // Het geblokkeerde item. Een tag en geen state, want gemeten heeft dit bord geen
            // Blocked-state en in de veldenlijst van Task staat geen blokkadeveld.
            new SprintWorkItem
            {
                Id = 4573,
                Type = "Task",
                Title = "Wachten op SFTP-sleutel van de klant",
                State = "Active",
                Stage = WorkItemStage.InProgress,
                Tags = [Blokkademarkering, "infra"],
                CreatedByName = Aanmakernaam,
                CreatedByUniqueName = Aanmakeradres,
                RemainingWork = 2m,
            },

            // Afgerond, zodat "afgerond" een ander getal is dan "work items".
            new SprintWorkItem
            {
                Id = 4574,
                Type = "Task",
                Title = "Jaarverslag-snapshot",
                State = "Closed",
                Stage = WorkItemStage.Completed,
                CreatedByName = Aanmakernaam,
                CreatedByUniqueName = Aanmakeradres,
                AssignedToName = Toegewezenaam,
                AssignedToUniqueName = Toegewezenadres,
                CompletedWork = 8m,
                StoryPoints = 5m,
            },

            // Aangemaakt door een agent. De enige rij waarop de herkomst een derde waarde kan krijgen.
            new SprintWorkItem
            {
                Id = 4588,
                Type = "Task",
                Title = "Storing gemeld: hartslag ontbreekt",
                State = "New",
                Stage = WorkItemStage.Proposed,
                Tags = ["agent"],
                CreatedByName = "devops-sync",
                CreatedByUniqueName = Agentidentiteit,
            },

            // Verwijderd. Telt niet mee in het aantal work items — een verwijderd item is geen werk —
            // en dat verschil is niet te meten zonder deze rij.
            new SprintWorkItem
            {
                Id = 4592,
                Type = "Task",
                Title = "Dubbel aangemaakt",
                State = "Removed",
                Stage = WorkItemStage.Removed,
                CreatedByName = Aanmakernaam,
                CreatedByUniqueName = Aanmakeradres,
                RemainingWork = 99m,
            },
        ],
        Undated =
        [
            new SprintIterationRef { Name = Ongedateerdeiteratie, Path = Ongedateerdpad },
        ],
        DatedCount = 5,
        ETag = NieuweEtag(),
    };
}
