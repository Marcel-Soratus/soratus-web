namespace Soratus.Portal.Sprints;

/// <summary>
/// Het Azure DevOps-bord van één klant: organisatie, project en team.
/// </summary>
/// <remarks>
/// <para><strong>Dit type is de tweelingbroer van <see cref="Data.AzureScope"/> en bestaat om dezelfde
/// reden: een weergavetekst ontleden gaat stil fout.</strong> Het klantdocument heeft <c>env</c> en
/// <c>envFull</c> — vrije tekst voor een operator — en er was geen veld waarmee een programma kon weten
/// welk bord bij welke klant hoort. Bij de kosten was de prijs van die fout gemeten: een resource group
/// die niet bestaat geeft HTTP 200 met nul rijen, dus een tikfout levert een geslaagd leeg antwoord op
/// dat als "geen kosten" doorrolt naar een factuur (punt 30).</para>
///
/// <para><strong>Hier is de prijs anders en dat is het opschrijven waard.</strong> Een DevOps-project dat
/// niet bestaat geeft géén geslaagd leeg antwoord: hij geeft een <c>404</c>, en een team dat niet bestaat
/// geeft een <c>404</c>. Dat is de vriendelijke kant. Maar er is één geval dat wél stil is en dat het
/// bestaan van dit type rechtvaardigt: <strong>een project dat bestaat en een team dat bestaat, met een
/// tikfout in de teamnaam die per ongeluk een ánder bestaand team raakt</strong> — dan staat er een
/// sprint van een ander team op het scherm van deze klant. Dat is geen leeg antwoord maar een verkeerd
/// antwoord, en het is niet aan de vorm te zien. De verdediging is dezelfde als bij de kosten en bestaat
/// uit twee delen die elkaar niet vervangen: <em>controleren wat te controleren is</em> (dit type) en
/// <em>tonen wat er is bevraagd</em> (<see cref="SprintDocument.Scope"/>).</para>
///
/// <para><strong>Waarom dit één veld is en niet drie, en waarom het toch wordt gesneden.</strong> Het
/// argument van <see cref="Data.AzureScope"/> — "één veld heeft twee toestanden en twee velden hebben
/// drie" — geldt hier sterker, want drie velden hebben zeven halve toestanden die niets betekenen. Leeg
/// betekent "niet ingericht" en dat is een geldige toestand; een pad is één waarde.</para>
///
/// <para>Waar dit type afwijkt van <see cref="Data.AzureScope"/> is dat de tekenreeks die de deur uit
/// gaat niet één is maar drie: de iteraties hangen aan het team, de WIQL-vraag aan het project en de
/// veldenbatch aan de organisatie. Dat is geen tweede waarheid, en het verschil met "twee velden die de
/// collector en het scherm elk zelf samenstellen" is precies aan te wijzen: er is één opgeslagen waarde,
/// één ontleding, en drie voorvoegsels die uit diezelfde ontleding volgen
/// (<see cref="OrganizationPath"/>, <see cref="ProjectPath"/>, <see cref="Path"/>). Wat er op het scherm
/// staat is <see cref="Path"/>, en de andere twee zijn er prefixen van — ze kunnen niet uiteenlopen
/// zonder dat deze drie eigenschappen uiteenlopen, en die staan in één bestand naast elkaar.</para>
///
/// <para><strong>Waarom het team erin staat en niet alleen organisatie en project.</strong> Een sprint is
/// een teambegrip en geen projectbegrip. Gemeten op 22 augustus 2026 op <c>MBVApp4 MAUI</c>: de
/// iteratieboom van het project en de iteratielijst van het team leveren vandaag dezelfde acht
/// iteraties, maar het zijn twee endpoints met twee betekenissen — een iteratie bestaat in het project
/// en wordt aan een team <em>toegewezen</em>, en <c>@currentIteration</c> is een teaminstelling
/// (gemeten: <c>defaultIterationMacro: "@currentIteration"</c> op <c>MBVApp4 MAUI Team</c>). Zonder
/// team is er geen sprint om te tonen.</para>
///
/// <para><strong>Wat er níet in staat: een area path.</strong> §3.4 vraagt de work items "van deze
/// klant", en de verleiding is om dat op een area path te filteren. Dat is hier niet gedaan: gemeten
/// staat het team op <c>defaultAreaPath: "MBVApp4 MAUI"</c> met <c>includeChildren: false</c>, dus het
/// area path van dit team is het hele project en een filter erop zou niets doen. Een vierde segment dat
/// vandaag niets filtert is een veld dat een operator moet invullen zonder dat iemand kan zien of het
/// klopt — en dat is de fout waar dit type tegen bestaat. Eén klant is één bord; wordt dat ooit één
/// klant is één area path binnen één bord, dan is dat een besluit met een meting eronder.</para>
/// </remarks>
/// <param name="Organization">De organisatie, bijvoorbeeld <c>soratus</c>.</param>
/// <param name="Project">De projectnaam, in de schrijfwijze waarin hij is ingevuld.</param>
/// <param name="Team">De teamnaam, in de schrijfwijze waarin hij is ingevuld.</param>
public sealed record DevOpsScope(string Organization, string Project, string Team)
{
    /// <summary>De langste organisatienaam die Azure DevOps toestaat.</summary>
    /// <remarks>
    /// Vijftig, uit de documentatie van Azure DevOps. Dit is geen grens die wij kiezen: een organisatie
    /// met een langere naam bestaat niet, dus een scope die hem noemt kan alleen een 404 opleveren.
    /// </remarks>
    public const int MaximumOrganizationLength = 50;

    /// <summary>De langste project- of teamnaam die Azure DevOps toestaat.</summary>
    /// <remarks>
    /// Vierenzestig, uit de documentatie van Azure DevOps voor projectnamen; teamnamen volgen dezelfde
    /// regels. Zie <see cref="MaximumOrganizationLength"/> voor waarom dit hier wordt tegengehouden en
    /// niet aan de API wordt overgelaten.
    /// </remarks>
    public const int MaximumNameLength = 64;

    /// <summary>
    /// De langste scope die nog drie geldige segmenten kan zijn.
    /// </summary>
    /// <remarks>
    /// Drie segmenten en twee schuine strepen. Deze grens staat er om de meldingen bruikbaar te houden:
    /// zonder hem zou een per ongeluk geplakte lap tekst als geheel in een foutmelding op het scherm
    /// belanden. Dezelfde reden als bij <see cref="Data.AzureScope.MaximumScopeLength"/>.
    /// </remarks>
    public const int MaximumScopeLength =
        MaximumOrganizationLength + 1 + MaximumNameLength + 1 + MaximumNameLength;

    /// <summary>
    /// De tekens die Azure DevOps in een project- of teamnaam niet toestaat.
    /// </summary>
    /// <remarks>
    /// <para>Uit de documentatie van Azure DevOps ("Naming restrictions"). De schuine streep staat er
    /// niet bij en dat is met opzet: die is hier het scheidingsteken, en een segment kan hem dus per
    /// constructie niet bevatten. Een naam met een van deze tekens bestaat niet, dus een scope die hem
    /// noemt kan alleen een 404 of — erger — een verzoek opleveren dat de API anders leest dan wij hem
    /// bedoelden.</para>
    ///
    /// <para><strong>Dit is waar dit type strenger is dan zijn tweelingbroer, en dat volgt uit de
    /// vorm.</strong> Een resourcegroepnaam gaat als één padsegment de deur uit; een project- en
    /// teamnaam ook, maar deze scope wordt ook nog als tekst in een URL gezet. Een naam met een
    /// <c>?</c> erin zou de rest van het pad in een querystring veranderen, en dan is het bevraagde
    /// adres een ander adres dan wat er op het scherm staat.</para>
    /// </remarks>
    private static readonly char[] Forbidden =
    [
        '\\', ':', '<', '>', '|', '?', '*', '"', ';', '#', '$', '&', '%', '+', '=', '{', '}', ',',
        '[', ']', '~', '\'', '@', '!',
    ];

    /// <summary>Het pad naar de organisatie: het voorvoegsel van de veldenbatch.</summary>
    /// <remarks>
    /// <c>POST {endpoint}/{organisatie}/_apis/wit/workitemsbatch</c>. De batch loopt op organisatieniveau
    /// en niet op projectniveau; gemeten (22 augustus 2026) geeft hij de velden van work items uit
    /// <c>MBVApp4 MAUI</c> terug.
    /// </remarks>
    public string OrganizationPath => $"/{Organization}";

    /// <summary>Het pad naar het project: het voorvoegsel van de WIQL-vraag.</summary>
    /// <remarks><c>POST {endpoint}/{organisatie}/{project}/_apis/wit/wiql</c>.</remarks>
    public string ProjectPath => $"/{Organization}/{Project}";

    /// <summary>Het pad naar het team: het voorvoegsel van de iteratielijst.</summary>
    /// <remarks>
    /// <c>GET {endpoint}/{organisatie}/{project}/{team}/_apis/work/teamsettings/iterations</c>. Dit is
    /// ook de tekenreeks die op het scherm komt te staan als "bevraagd: …", en daarom is het de
    /// tekenreeks die <see cref="ToString"/> teruggeeft.
    /// </remarks>
    public string Path => $"/{Organization}/{Project}/{Team}";

    /// <inheritdoc />
    /// <remarks>
    /// Gelijk aan <see cref="Path"/>. Dat is hier geen gemak maar een grens, en het is de reden die
    /// <see cref="Data.AzureScope.ToString"/> ook geeft: een scope die in een logregel of in een melding
    /// terechtkomt hoort dezelfde tekenreeks te zijn als die naar de API gaat. De standaard-<c>ToString</c>
    /// van een record zou hier <c>DevOpsScope { Organization = …, Project = … }</c> neerzetten, en dan
    /// staat er in het log iets wat niet is bevraagd.
    /// </remarks>
    public override string ToString() => Path;

    /// <summary>
    /// Leest een scope uit de tekst die een operator invult of die in een document staat.
    /// </summary>
    /// <param name="text">De tekst, of <c>null</c>.</param>
    /// <param name="scope">De scope, of <c>null</c> als de tekst leeg of onbruikbaar is.</param>
    /// <returns><c>true</c> als er een scope uit kwam.</returns>
    /// <remarks>
    /// <para><strong>Leeg levert <c>false</c> met <c>null</c> en dat is geen fout.</strong> "Er is geen
    /// bord vastgelegd" is een geldige toestand — punt 15, hier op de plek waar hij de sprintweergave
    /// raakt: een klant zonder scope wordt niet bevraagd, en dan staat er op het sprintscherm dat er
    /// niets is ingericht en niet een leeg sprintoverzicht dat op "geen werk" lijkt. Wie het onderscheid
    /// tussen leeg en onbruikbaar nodig heeft, gebruikt <see cref="Validate"/>.</para>
    ///
    /// <para><strong>Deze methode en <see cref="Validate"/> horen dezelfde grens te trekken, en dat is
    /// een eis en geen dubbele controle.</strong> Ze worden door verschillende kanten gebruikt — de
    /// formulieren valideren, de collector en het scherm ontleden — en zouden ze uiteenlopen, dan staat
    /// er "wordt opgehaald" bij een klant die niet wordt opgehaald, of weigert het formulier een scope
    /// waar de collector prima mee uit de voeten kan. Dat is gat 1 uit punt 41, en het is daar met een
    /// mutatie gevonden en niet met een test. Er staan hier tests op beide kanten, in beide richtingen.
    /// </para>
    /// </remarks>
    public static bool TryParse(string? text, out DevOpsScope? scope)
    {
        scope = null;

        if (string.IsNullOrWhiteSpace(text) || Validate(text) is not null)
        {
            return false;
        }

        var parts = Segments(text);
        scope = new DevOpsScope(parts[0], parts[1], parts[2]);
        return true;
    }

    /// <summary>
    /// Wat er niet klopt aan de ingevulde scope, of <c>null</c> als hij klopt of leeg is.
    /// </summary>
    /// <param name="text">De tekst uit het formulier.</param>
    /// <returns>De melding voor het formulier, of <c>null</c>.</returns>
    /// <remarks>
    /// <para><strong>Leeg geeft <c>null</c>: niets invullen is toegestaan.</strong> Een klant zonder
    /// DevOps-bord heeft geen scope, en een verplicht veld zou daar een verzonnen pad opleveren —
    /// hetzelfde mechanisme waarmee een verplicht contractnummer een verzonnen nummer oplevert. Dat pad
    /// zou hier een 404 geven en dus zichtbaar zijn, en juist dat is de verleiding: een operator die "er
    /// staat toch een foutmelding" denkt, vult iets in. Wat hij daarmee verliest is het onderscheid
    /// tussen "niet ingericht" en "verkeerd ingericht", en dat zijn twee verschillende handelingen.</para>
    ///
    /// <para>De meldingen noemen wat er wordt verwacht en waar het te vinden is. Dat is geen
    /// vriendelijkheid: de betrouwbare invoerweg is overtypen uit de adresregel van het bord, en een
    /// melding die dat niet zegt laat iemand gokken.</para>
    /// </remarks>
    public static string? Validate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();

        if (trimmed.Length > MaximumScopeLength)
        {
            return $"Dit DevOps-bord is langer dan {MaximumScopeLength} tekens en kan dus geen "
                + "organisatie met een project en een team zijn.";
        }

        var parts = Segments(trimmed);

        if (parts.Length != 3 || parts.Any(part => part.Length == 0))
        {
            return "Een DevOps-bord heeft de vorm organisatie/project/team, bijvoorbeeld "
                + "soratus/MBVApp4 MAUI/MBVApp4 MAUI Team. Die drie staan in de adresregel van het bord "
                + "in Azure DevOps.";
        }

        return OrganizationError(parts[0])
            ?? NameError(parts[1], "project")
            ?? NameError(parts[2], "team");
    }

    /// <summary>
    /// De drie segmenten van een scope, zonder de schuine strepen eromheen.
    /// </summary>
    /// <param name="text">De tekst.</param>
    /// <returns>De segmenten, met de witruimte eraf.</returns>
    /// <remarks>
    /// <para>Een voorloop- of afsluitende schuine streep wordt weggehaald, want iemand die uit een URL
    /// kopieert neemt hem mee. De witruimte rond een segment ook: "soratus / MBVApp4 MAUI / …" is wat
    /// een mens typt en een naam met een spatie ervoor bestaat niet.</para>
    ///
    /// <para><strong>De schrijfwijze binnen een segment blijft van de operator</strong>, en dat is
    /// dezelfde keuze en dezelfde reden als bij de resourcegroepnaam in
    /// <see cref="Data.AzureScope"/>: deze tekenreeks komt op het scherm als "bevraagd: …", en daar hoort
    /// te staan wat er is ingevuld en niet wat wij ervan hebben gemaakt. Er is hier geen vast segment om
    /// te normaliseren — anders dan bij een ARM-pad bestaat deze scope uitsluitend uit namen die van de
    /// klant zijn.</para>
    /// </remarks>
    private static string[] Segments(string text) =>
    [
        .. text.Trim().Trim('/').Split('/').Select(part => part.Trim()),
    ];

    /// <summary>Wat er niet klopt aan een organisatienaam, of <c>null</c>.</summary>
    /// <param name="name">De naam.</param>
    /// <returns>De melding, of <c>null</c>.</returns>
    /// <remarks>
    /// Strenger dan een project- of teamnaam, en dat komt uit de documentatie van Azure DevOps: een
    /// organisatienaam bestaat uit letters, cijfers en koppelstreepjes, begint en eindigt op een letter
    /// of cijfer, en is ten hoogste vijftig tekens. Hij is bovendien een subdomein-achtig deel van het
    /// adres en niet een naam die een mens verzint per project, dus hij is werkelijk zo beperkt.
    /// </remarks>
    private static string? OrganizationError(string name)
    {
        if (name.Length > MaximumOrganizationLength)
        {
            return $"Een DevOps-organisatienaam is ten hoogste {MaximumOrganizationLength} tekens lang.";
        }

        var geldig = name.All(teken => char.IsAsciiLetterOrDigit(teken) || teken == '-')
            && char.IsAsciiLetterOrDigit(name[0])
            && char.IsAsciiLetterOrDigit(name[^1]);

        return geldig
            ? null
            : $"'{name}' kan geen DevOps-organisatie zijn: dat is de naam uit "
                + "dev.azure.com/<organisatie>, met alleen letters, cijfers en koppelstreepjes.";
    }

    /// <summary>Wat er niet klopt aan een project- of teamnaam, of <c>null</c>.</summary>
    /// <param name="name">De naam.</param>
    /// <param name="soort">Het woord voor de melding: <c>project</c> of <c>team</c>.</param>
    /// <returns>De melding, of <c>null</c>.</returns>
    /// <remarks>
    /// De regels komen uit de documentatie van Azure DevOps: ten hoogste vierenzestig tekens, geen van
    /// de tekens uit <see cref="Forbidden"/>, niet beginnend met een onderstrepingsteken en niet
    /// beginnend of eindigend op een punt. Unicodeletters zijn toegestaan — een project met een accent
    /// in de naam bestaat — dus er wordt niet op ASCII gecontroleerd, alleen op wat er verboden is.
    /// Dezelfde keuze als bij een resourcegroepnaam, waar <c>café</c> geldig is.
    /// </remarks>
    private static string? NameError(string name, string soort)
    {
        if (name.Length > MaximumNameLength)
        {
            return $"Een DevOps-{soort}naam is ten hoogste {MaximumNameLength} tekens lang.";
        }

        foreach (var teken in name)
        {
            if (Forbidden.Contains(teken) || char.IsControl(teken))
            {
                return $"'{name}' kan geen DevOps-{soort}naam zijn: het teken '{teken}' mag daar niet "
                    + "in staan.";
            }
        }

        if (name[0] == '_')
        {
            return $"Een DevOps-{soort}naam begint niet met een onderstrepingsteken.";
        }

        return name[0] == '.' || name[^1] == '.'
            ? $"Een DevOps-{soort}naam begint en eindigt niet op een punt."
            : null;
    }
}
