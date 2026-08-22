using Soratus.Portal.Sprints;

namespace Soratus.Portal.Views;

/// <summary>
/// De teksten van het sprintscherm die geen gegeven zijn maar een mededeling (§3.4).
/// </summary>
/// <remarks>
/// <para>Eén plek, en het viewmodel draagt de tekst mee in plaats van dat de Razor hem verzint. Dezelfde
/// afspraak als bij <see cref="BillingNotice"/>, <see cref="HoursNotice"/> en
/// <see cref="ContractNotice"/>.</para>
///
/// <para><strong>De klantteksten en de operatorteksten staan hier als aparte constanten en niet als één
/// tekst met een variabele erin.</strong> Bij de facturatie was dat scherp omdat het woord
/// "beheeropslag" onze marge noemt; hier is het scherp om een andere reden — de operatorteksten noemen de
/// <em>koppeling</em> (organisatie, project, team, rolverleningen), en §2 zet die dicht voor de klant. Een
/// gedeelde tekst met een <c>if</c> erin is één verschrijving verwijderd van onze DevOps-inrichting op het
/// scherm van de klant.</para>
/// </remarks>
public static class SprintNotice
{
    /// <summary>Waarom een klant hier niets kan invullen.</summary>
    /// <remarks>
    /// §3.4: "Het portaal schrijft niets terug." Dat is geen ontbrekende functie maar het ontwerp, en het
    /// hoort op het scherm te staan in plaats van als afwezigheid van knoppen — §1 van de spec: geen
    /// knoppen die suggereren dat je kunt ingrijpen zolang dat niet kan, en de beperking laten zien in
    /// plaats van hem weg te poetsen.
    /// </remarks>
    public const string ReadOnly =
        "Deze gegevens komen uit Azure DevOps en zijn hier alleen te lezen. Het bord is leidend: het "
        + "portaal schrijft er nooit iets terug. Wijzigen doe je in DevOps, en dan staat het hier bij de "
        + "volgende ophaling.";

    /// <summary>Dat wat je ziet een momentopname is (voor beide rollen).</summary>
    /// <remarks>
    /// §3.4 vraagt het tijdstip van laatste ophalen, en dit is de zin die uitlegt waarom dat er staat. §1
    /// van de spec: eerlijke systeemeigenschappen benoemen.
    /// </remarks>
    public const string Snapshot =
        "Dit is een momentopname en geen live beeld. Het portaal haalt de sprint periodiek op; hoe oud "
        + "deze lezing is staat erboven.";

    /// <summary>De klanttekst bij <see cref="SprintState.Unknown"/>.</summary>
    /// <remarks>
    /// Zonder de reden, want die kan een adres, een rolverlening of een tokenfout noemen — dat is de
    /// koppeling, en die is operator-only (§2). Wat de klant nodig heeft is dat dit geen "geen werk"
    /// betekent en dat er iemand naar kijkt.
    /// </remarks>
    public const string CustomerUnknown =
        "De sprint is nog niet opgehaald. Dat betekent niet dat er geen werk is — het betekent dat wij "
        + "hier nog niets hebben gelezen. Soratus ziet waarom en pakt het op.";

    /// <summary>De operatortekst bij <see cref="SprintState.Unknown"/>.</summary>
    public const string OperatorUnknown =
        "Er is niets opgehaald, of de laatste ophaling gaf een antwoord dat niet te gebruiken was. Een "
        + "lege lijst hieronder betekent hier 'niet gelezen' en niet 'geen werk'. De reden staat eronder "
        + "als hij bekend is.";

    /// <summary>De tekst bij <see cref="SprintState.NoIterations"/>.</summary>
    public const string NoIterations =
        "Dit bord heeft geen enkele iteratie. Er is dus geen sprint om te tonen; er valt niets te wachten "
        + "tot er iteraties zijn aangemaakt en aan het team toegewezen.";

    /// <summary>De tekst bij <see cref="SprintState.NoDatedIterations"/>.</summary>
    /// <remarks>
    /// <para><strong>Dit was de werkelijke toestand van dit bord tot 21 augustus 2026, en hij was
    /// stil.</strong> De teaminstelling stond op <c>@currentIteration</c>, die macro wordt door datums
    /// bepaald, en er was dus helemaal geen huidige sprint — terwijl er wél werk op het bord stond. Deze
    /// tekst is er zodat die toestand niet als een lege pagina verschijnt.</para>
    /// </remarks>
    public const string NoDatedIterations =
        "Geen enkele iteratie op dit bord heeft een begin- en einddatum, dus er is geen sprint aan te "
        + "wijzen. Er staat wél werk op het bord — het valt alleen in geen enkele periode. Zet datums op "
        + "de iteraties en het verschijnt hier.";

    /// <summary>De tekst bij <see cref="SprintState.NoCurrentSprint"/>.</summary>
    /// <remarks>
    /// Dit is een geldige stand van een gezond project: tussen twee sprints, of een sprint die morgen
    /// begint. Hij valt met opzet niet samen met <see cref="SprintState.Unknown"/> — "wij hebben het niet
    /// kunnen ophalen" en "wij hebben het opgehaald en er loopt nu niets" zijn twee verschillende
    /// uitspraken, en zouden ze samenvallen, dan ziet een echte weigering uit als een rustige maand.
    /// </remarks>
    public const string NoCurrentSprint =
        "Er loopt vandaag geen sprint: er zijn wel periodes vastgelegd, maar vandaag valt in geen ervan. "
        + "Dat is een gewone stand tussen twee sprints.";

    /// <summary>De klanttekst bij <see cref="SprintState.Ambiguous"/>.</summary>
    /// <remarks>
    /// Zonder de namen van de overlappende iteraties: die zijn boordhygiëne en dus operator-only. Wat de
    /// klant nodig heeft is dat dit geen storing in zijn werk is.
    /// </remarks>
    public const string CustomerAmbiguous =
        "Er lopen op dit bord meerdere periodes tegelijk, dus er is geen enkele sprint aan te wijzen. Het "
        + "portaal kiest er met opzet geen: dan zou er een sprint staan die net zo goed een andere had "
        + "kunnen zijn. Soratus zet de periodes recht.";

    /// <summary>De operatortekst bij <see cref="SprintState.Ambiguous"/>.</summary>
    public const string OperatorAmbiguous =
        "Meer dan één iteratie met datums bevat vandaag. Het portaal kiest er géén: stil de eerste nemen "
        + "zou een sprint op het scherm zetten die niet van een juiste te onderscheiden is. De "
        + "overlappende iteraties staan hieronder; corrigeer hun periodes in DevOps.";

    /// <summary>
    /// Dat er iteraties zonder datums zijn en dat hun werk hier niet in staat.
    /// </summary>
    /// <remarks>
    /// <para><strong>Het aantal work items van die iteraties staat er met opzet niet bij, en deze tekst
    /// zegt dat.</strong> Dat aantal kost een aanroep per iteratie en de mededeling hangt er niet van af;
    /// een gedeeltelijk opgehaald aantal zou te laag zijn en dat is de fout die niemand ziet. Maar een
    /// ontbrekend aantal leest als nul, en daarom staat het er expliciet.</para>
    /// </remarks>
    public const string Undated =
        "Er zijn iteraties zonder datums. Werk dat daarin staat valt in geen enkele periode en komt dus "
        + "op geen enkele sprintweergave voor — ook niet hieronder. Hoeveel items dat zijn is niet "
        + "geteld, dus lees dit niet als nul.";

    /// <summary>
    /// Dat er voor deze klant geen DevOps-bord is vastgelegd (operator-only).
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit is het onderscheid dat "niet opgehaald" niet kan maken.</strong> Een klant zonder
    /// bord levert dezelfde lege pagina op als een klant wiens ophaling van dit kwartier is mislukt, en
    /// die twee vragen een volstrekt verschillende handeling: de eerste is een veld invullen, de tweede is
    /// wachten. Zonder deze regel zou een operator op een ophaling wachten die nooit gaat komen. Precies
    /// de vorm van <see cref="BillingNotice.NoScopeConfigured"/>.</para>
    /// </remarks>
    public const string NoScopeConfigured =
        "Voor deze klant is geen DevOps-bord vastgelegd, dus er wordt niets opgehaald. Een lege pagina "
        + "betekent hier 'niet ingericht' en niet 'nog niet opgehaald'. Leg het bord vast op het "
        + "contractscherm, in het blok Omgeving.";

    /// <summary>Dat het vastgelegde DevOps-bord niet te gebruiken is (operator-only).</summary>
    /// <remarks>
    /// Kan alleen als iemand het klantdocument met de hand heeft aangepast — beide formulieren valideren.
    /// En juist daarom hoort hij hier te staan: een bord dat er wél is en niet werkt is niet te
    /// onderscheiden van een bord dat er niet is, en de collector haalt in beide gevallen niets op.
    /// </remarks>
    public const string ScopeUnusable =
        "Het DevOps-bord van deze klant is niet te gebruiken, dus er wordt niets opgehaald. Corrigeer het "
        + "op het contractscherm, in het blok Omgeving.";

    /// <summary>Dat een sprint zonder items geen fout is.</summary>
    public const string NoItems =
        "Deze sprint heeft geen work items. Dat is een gemeten uitkomst en geen ontbrekende lezing: het "
        + "bord is gelezen en er stond niets in deze periode.";

    /// <summary>
    /// Dat een streepje in een urenkolom geen nul is.
    /// </summary>
    /// <remarks>
    /// <para><strong>De belangrijkste zin van dit scherm, en hij komt uit een meting.</strong> Van de
    /// zestien work items die op 22 augustus 2026 uit dit bord kwamen had géén enkel item een waarde in
    /// <c>RemainingWork</c>, <c>CompletedWork</c> of <c>StoryPoints</c> — die velden stonden niet in het
    /// antwoord. Zonder deze tekst leest een streepje als een storing, en dan gaat iemand het "oplossen"
    /// door er nul van te maken.</para>
    /// </remarks>
    public const string HoursUnknown =
        "Een streepje is geen nul: dat veld is in DevOps niet ingevuld. Nul betekent dat iemand nul heeft "
        + "ingevuld, en een streepje betekent dat er niets staat om op te tellen.";
}

/// <summary>
/// Eén work item op de sprintweergave zoals de <em>klant</em> hem ziet (§3.4).
/// </summary>
/// <remarks>
/// <para><strong>Dit type heeft geen aanmaker en geen e-mailadres.</strong> §3.4 vraagt per item de
/// <em>herkomst</em> — "aangemaakt door agent of handmatig" — en dat is een andere vraag dan "door wie".
/// Het antwoord op de eerste staat in <see cref="Origin"/>; de tweede is een koppelingsdetail (§2) en
/// bovendien een persoonsgegeven van een medewerker. Dat verschil is hier een typeverschil en geen
/// <c>@if</c>: er bestaat geen uitdrukking in de klantmarkup die een adres op het scherm zet, want het
/// veld is er niet. Voor de zevende keer dezelfde vorm — <see cref="CustomerChargeRow"/>,
/// <see cref="CustomerHourRow"/>, <see cref="CustomerLogLine"/>, <see cref="CustomerRunRow"/>,
/// <see cref="CustomerAgentsView"/> en de contractmarge — en om dezelfde reden: wat er niet op het type
/// staat kan niet lekken.</para>
///
/// <para><strong>Wat de klant wél krijgt is de toegewezen weergavenaam.</strong> Dat is een afweging die
/// ik expliciet maak: §3.4 vraagt "toegewezen" als kolom, en een sprint zonder te zien wie waaraan werkt
/// is geen sprintweergave. Wat er niet doorheen komt is het adres — dus wel "Dennis Verhamme", niet
/// "dennis@soratus.com". Een naam staat op het bord waar deze klant zelf werk in heeft; een adres is een
/// contactgegeven dat niemand hier heeft gevraagd.</para>
/// </remarks>
public sealed record CustomerSprintRow
{
    /// <summary>Het nummer van het work item.</summary>
    public required int Id { get; init; }

    /// <summary>De werkitemsoort, bijvoorbeeld <c>Task</c>.</summary>
    public required string Type { get; init; }

    /// <summary>De titel.</summary>
    public required string Title { get; init; }

    /// <summary>De statenaam zoals hij op het bord staat.</summary>
    /// <remarks>
    /// Het woord van DevOps en niet een van de vijf uit §3.4. Gemeten heeft dit bord <c>New</c>,
    /// <c>Active</c>, <c>Closed</c> en <c>Removed</c> en geen <c>Blocked</c> of <c>Resolved</c>; een
    /// portaal dat er andere woorden van maakt laat twee mensen over hetzelfde item verschillende dingen
    /// zeggen.
    /// </remarks>
    public required string State { get; init; }

    /// <summary>De categorie van de state. Hierop rekent het scherm zijn kleur en de statistiek.</summary>
    public required WorkItemStage Stage { get; init; }

    /// <summary>De tags.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>De herkomst: agent, mens, of niet vast te stellen (§3.4).</summary>
    public required WorkItemOrigin Origin { get; init; }

    /// <summary>Of dit item geblokkeerd is.</summary>
    public required bool IsBlocked { get; init; }

    /// <summary>Aan wie het item is toegewezen, of <c>null</c> als het niet is toegewezen.</summary>
    /// <remarks>
    /// <c>null</c> en niet "Niet toegewezen": dat woord is een schermtekst. Gemeten kwamen twee van de
    /// zestien items zonder toewijzing terug.
    /// </remarks>
    public string? AssignedTo { get; init; }

    /// <summary>De openstaande uren, of <c>null</c> als het veld niet is ingevuld.</summary>
    /// <remarks><c>null</c> is nooit nul; zie <see cref="SprintNotice.HoursUnknown"/>.</remarks>
    public decimal? OpenHours { get; init; }

    /// <summary>De gedane uren, of <c>null</c> als het veld niet is ingevuld.</summary>
    public decimal? DoneHours { get; init; }

    /// <summary>De story points, of <c>null</c> als het veld niet is ingevuld.</summary>
    public decimal? StoryPoints { get; init; }
}

/// <summary>
/// Eén work item op de sprintweergave zoals de <em>operator</em> hem ziet (§3.4).
/// </summary>
/// <remarks>
/// De variant met de aanmaker en de adressen. Er is bewust geen gemeenschappelijk basistype met
/// <see cref="CustomerSprintRow"/>: dat zou het verboden veld op een basistype zetten waar de klantvorm
/// van erft, en dan is het weer één cast ver weg. Dezelfde keuze als bij de logregels (punt 12) en de
/// runs (punt 14).
/// </remarks>
public sealed record OperatorSprintRow
{
    /// <summary>Het nummer van het work item.</summary>
    public required int Id { get; init; }

    /// <summary>De werkitemsoort.</summary>
    public required string Type { get; init; }

    /// <summary>De titel.</summary>
    public required string Title { get; init; }

    /// <summary>De statenaam zoals hij op het bord staat.</summary>
    public required string State { get; init; }

    /// <summary>De categorie van de state.</summary>
    public required WorkItemStage Stage { get; init; }

    /// <summary>De tags.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>De herkomst (§3.4).</summary>
    public required WorkItemOrigin Origin { get; init; }

    /// <summary>Of dit item geblokkeerd is.</summary>
    public required bool IsBlocked { get; init; }

    /// <summary>Aan wie het item is toegewezen, of <c>null</c>.</summary>
    public string? AssignedTo { get; init; }

    /// <summary>Het adres van de toegewezen persoon, of <c>null</c>. Operator-only.</summary>
    public string? AssignedToAddress { get; init; }

    /// <summary>Wie het item heeft aangemaakt, of <c>null</c>. Operator-only.</summary>
    /// <remarks>
    /// Operator-only omdat §3.4 aan de klant de <em>herkomst</em> toezegt en niet de persoon. Voor een
    /// operator is deze naam wél het antwoord op de vraag achter de herkomst: hij is de reden dat
    /// <see cref="WorkItemOrigin.Unknown"/> te verklaren valt zonder in DevOps te kijken.
    /// </remarks>
    public string? CreatedBy { get; init; }

    /// <summary>Het adres van de aanmaker, of <c>null</c>. Operator-only.</summary>
    /// <remarks>
    /// Dit is het gegeven waarop <see cref="SprintJudgement.Origin"/> vergelijkt, en het staat hier zodat
    /// een operator kan zien waarom een item niet als agentitem is herkend — bijvoorbeeld omdat de
    /// identiteitenlijst een andere schrijfwijze heeft.
    /// </remarks>
    public string? CreatedByAddress { get; init; }

    /// <summary>De openstaande uren, of <c>null</c>.</summary>
    public decimal? OpenHours { get; init; }

    /// <summary>De gedane uren, of <c>null</c>.</summary>
    public decimal? DoneHours { get; init; }

    /// <summary>De story points, of <c>null</c>.</summary>
    public decimal? StoryPoints { get; init; }
}

/// <summary>
/// De sprintweergave zoals de klant hem ziet (§3.4).
/// </summary>
/// <remarks>
/// <para><strong>Wat er niet op staat: de bevraagde scope, de reden van een mislukking, de paden van de
/// iteraties zonder datums, en de overlappende iteraties.</strong> §2 zet "Koppelingen (MCP/DevOps-details)"
/// dicht voor de klant, en dat zijn precies deze vier. De rijen zijn <see cref="CustomerSprintRow"/> en dat
/// type draagt de adressen niet, dus er is geen plek in dit viewmodel waar ze in kunnen belanden — ook niet
/// per ongeluk, ook niet als iemand er over een half jaar een tooltip bij zet.</para>
///
/// <para><strong>Wat er wél op staat en waar ik over heb geaarzeld: het boardpad.</strong> §3.4 noemt hem
/// bij naam als een van de vier kopgegevens, en het is het pad binnen het project van deze klant zelf —
/// <c>MBVApp4 MAUI\2026-08 Augustus</c>. Wat §2 dichtzet is de <em>koppeling</em> (organisatie, team,
/// rechten, MCP), en niet waar het werk van de klant op zijn eigen bord staat. Het tegenargument is echt:
/// dit pad bevat een projectnaam die uit DevOps komt. Ik heb hem laten staan omdat §3.4 hem expliciet
/// vraagt en het gegeven van de klant is; wie dat anders weegt haalt één eigenschap weg.</para>
/// </remarks>
public sealed record CustomerSprintView
{
    /// <summary>De klantslug.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam, voor de kop.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Wat er van de sprint bekend is.</summary>
    public required SprintState State { get; init; }

    /// <summary>Wat die toestand betekent, in gewone taal.</summary>
    /// <remarks>
    /// <c>null</c> bij <see cref="SprintState.Current"/>: er is dan niets uit te leggen. Bij elke andere
    /// waarde staat er een zin, want een lege pagina zonder tekst leest als "geen werk".
    /// </remarks>
    public string? StateNotice { get; init; }

    /// <summary>De sprintnaam, of <c>null</c> (§3.4).</summary>
    public string? SprintName { get; init; }

    /// <summary>De eerste dag van de sprint, of <c>null</c> (§3.4, "periode").</summary>
    public DateOnly? Start { get; init; }

    /// <summary>De laatste dag van de sprint, of <c>null</c>. Inclusief.</summary>
    public DateOnly? Finish { get; init; }

    /// <summary>Het boardpad, of <c>null</c> (§3.4).</summary>
    public string? BoardPath { get; init; }

    /// <summary>
    /// Wanneer de sprint bij DevOps is opgehaald, of <c>null</c> als er nooit is opgehaald.
    /// </summary>
    /// <remarks>
    /// §3.4 vraagt dit met zoveel woorden, en het is bij élke toestand relevant — juist bij
    /// <see cref="SprintState.Unknown"/>, want dan is het het antwoord op "hoe oud is wat ik hier zie".
    /// </remarks>
    public DateTimeOffset? ReadAt { get; init; }

    /// <summary>De statistieken van de sprint (§3.4).</summary>
    public required SprintTally Tally { get; init; }

    /// <summary>De work items van de sprint.</summary>
    public required IReadOnlyList<CustomerSprintRow> Items { get; init; }

    /// <summary>
    /// Hoeveel iteraties er geen datums hebben, en dus buiten elke sprintweergave vallen.
    /// </summary>
    /// <remarks>
    /// Een aantal en geen lijst: de klant hoort te weten dát er werk buiten valt, en welke iteraties dat
    /// zijn is boordhygiëne die Soratus repareert. Het aantal is nul zolang het bord schoon is.
    /// </remarks>
    public required int UndatedCount { get; init; }

    /// <summary>Wat dat aantal betekent, of <c>null</c> als het nul is.</summary>
    public string? UndatedNotice { get; init; }

    /// <summary>Waarom er op dit scherm niets in te vullen valt.</summary>
    public required string ReadOnlyNotice { get; init; }

    /// <summary>Dat dit een momentopname is.</summary>
    public required string SnapshotNotice { get; init; }

    /// <summary>Wat een streepje in een urenkolom betekent.</summary>
    public required string HoursNotice { get; init; }
}

/// <summary>
/// De sprintweergave zoals de operator hem ziet (§3.4).
/// </summary>
/// <remarks>
/// <para>Alles wat §2 als operator-only aanmerkt: de bevraagde scope, de reden van een mislukking, de
/// adressen op een work item, de paden van de iteraties zonder datums, de overlappende iteraties, en
/// hoeveel iteraties er wél datums hebben.</para>
///
/// <para><strong>§2 geeft de sprint aan beide rollen, dus dit is het antwoord op "wat is er dan tóch
/// operator-only".</strong> Zes dingen, en ze horen allemaal in dezelfde categorie: ze gaan niet over het
/// werk van de klant maar over onze koppeling en over de hygiëne van het bord. Wie de rijen van dit type
/// naast <see cref="CustomerSprintRow"/> legt, ziet die grens als een veldenverschil.</para>
/// </remarks>
public sealed record OperatorSprintView
{
    /// <summary>De klantslug.</summary>
    public required string CustomerId { get; init; }

    /// <summary>De klantnaam.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Wat er van de sprint bekend is.</summary>
    public required SprintState State { get; init; }

    /// <summary>Wat die toestand betekent, in gewone taal.</summary>
    public string? StateNotice { get; init; }

    /// <summary>De sprintnaam, of <c>null</c>.</summary>
    public string? SprintName { get; init; }

    /// <summary>De eerste dag van de sprint, of <c>null</c>.</summary>
    public DateOnly? Start { get; init; }

    /// <summary>De laatste dag van de sprint, of <c>null</c>.</summary>
    public DateOnly? Finish { get; init; }

    /// <summary>Het boardpad, of <c>null</c>.</summary>
    public string? BoardPath { get; init; }

    /// <summary>Wanneer de sprint is opgehaald, of <c>null</c>.</summary>
    public DateTimeOffset? ReadAt { get; init; }

    /// <summary>De statistieken.</summary>
    public required SprintTally Tally { get; init; }

    /// <summary>De work items.</summary>
    public required IReadOnlyList<OperatorSprintRow> Items { get; init; }

    /// <summary>
    /// Het DevOps-bord dat bij deze klant is vastgelegd, of <c>null</c>. Operator-only.
    /// </summary>
    /// <remarks>
    /// <para>Operator-only, en dat is geen extra regel: §2 wijst de koppelingsdetails aan de operator toe.
    /// Zie <see cref="CustomerSprintView"/>, dat dit veld daarom niet <em>heeft</em> in plaats van het te
    /// hebben en te verbergen.</para>
    ///
    /// <para>Staat hier naast <see cref="QueriedScope"/>, en die twee mogen verschillen — dat is de
    /// bedoeling. Dit is het bord dat vanaf nu wordt bevraagd; dat andere is het bord waartegen de lezing
    /// die hier staat werkelijk is gedaan. Bij een gecorrigeerde tikfout is het verschil tussen die twee
    /// precies het antwoord op de vraag waarom er nog een sprint van een ander team op het scherm staat.
    /// Dezelfde constructie als <see cref="OperatorBillingView.AzureScope"/> naast
    /// <see cref="OperatorChargeRow.Scope"/>.</para>
    /// </remarks>
    public string? DevOpsScope { get; init; }

    /// <summary>Het bord waartegen de lezing die hier staat is gedaan, of <c>null</c>. Operator-only.</summary>
    public string? QueriedScope { get; init; }

    /// <summary>
    /// Waarom er voor deze klant niets wordt opgehaald, of <c>null</c> als er wél wordt opgehaald.
    /// </summary>
    /// <remarks>
    /// <see cref="SprintNotice.NoScopeConfigured"/> of <see cref="SprintNotice.ScopeUnusable"/>. Twee
    /// teksten en geen enum: dit is een mededeling en geen gegeven waarop iets rekent. Zelfde afweging als
    /// bij <see cref="OperatorBillingView.ScopeNotice"/>.
    /// </remarks>
    public string? ScopeNotice { get; init; }

    /// <summary>Waarom de laatste ophaling niets opleverde, of <c>null</c>. Operator-only.</summary>
    /// <remarks>
    /// Alleen gevuld bij <see cref="SprintState.Unknown"/>. De klant hoort niet te weten met welke API wij
    /// vechten, en zo'n tekst kan een adres of een rolverlening noemen.
    /// </remarks>
    public string? Failure { get; init; }

    /// <summary>De iteraties zonder datums, met hun pad. Operator-only.</summary>
    public IReadOnlyList<SprintIterationRef> Undated { get; init; } = [];

    /// <summary>Wat die lijst betekent, of <c>null</c> als hij leeg is.</summary>
    public string? UndatedNotice { get; init; }

    /// <summary>De iteraties die vandaag allemaal bevatten, bij <see cref="SprintState.Ambiguous"/>. Operator-only.</summary>
    public IReadOnlyList<SprintIterationRef> Overlapping { get; init; } = [];

    /// <summary>Hoeveel iteraties er datums hebben. Operator-only.</summary>
    /// <remarks>
    /// Nodig om <see cref="SprintState.NoCurrentSprint"/> te kunnen uitleggen: "er zijn vijf sprints met
    /// datums en vandaag valt in geen ervan" is een andere mededeling dan "er is er één en die is
    /// afgelopen".
    /// </remarks>
    public int DatedCount { get; init; }

    /// <summary>Waarom er op dit scherm niets in te vullen valt.</summary>
    public required string ReadOnlyNotice { get; init; }

    /// <summary>Dat dit een momentopname is.</summary>
    public required string SnapshotNotice { get; init; }

    /// <summary>Wat een streepje in een urenkolom betekent.</summary>
    public required string HoursNotice { get; init; }
}
