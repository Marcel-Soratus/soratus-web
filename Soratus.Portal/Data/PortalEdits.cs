using System.Globalization;

namespace Soratus.Portal.Data;

/// <summary>
/// Wat een operator invult om een klant aan te maken (§3.9).
/// </summary>
/// <remarks>
/// <para>Eén verzoek voor klant, contract én toegang, omdat het één schrijfactie is. Zie
/// <see cref="IPortalDataStore.CreateCustomerAsync"/>: de drie documenten delen de partitiesleutel
/// en gaan als <c>TransactionalBatch</c> naar Cosmos. Zouden ze in drie aanroepen gaan, dan bestaat
/// de halve klant — een klantdocument zonder contract, of met toegang voor iemand die nog niet bij
/// een contract kan — en dan moet iemand hem met de hand afmaken.</para>
///
/// <para><see cref="CustomerId"/> is een veld en geen afleiding uit de naam. Zie
/// <see cref="PortalSlug"/>: de mockup leidt hem af, en dat levert een klant op waarvan de agents
/// onvindbaar zijn.</para>
/// </remarks>
public sealed record NewCustomerRequest
{
    /// <summary>De klantslug. Moet gelijk zijn aan <c>customerId</c> in de agentconfiguratie.</summary>
    public string CustomerId { get; init; } = string.Empty;

    /// <summary>De klantnaam zoals hij op het scherm hoort te staan.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Of dit een interne beheerklant is (§4).</summary>
    public bool IsInternal { get; init; }

    /// <summary>Korte omgevingsaanduiding, bijvoorbeeld <c>West-Europa</c>.</summary>
    public string? Environment { get; init; }

    /// <summary>De volledige omgeving, bijvoorbeeld <c>sub-77b2e0 · rg-soratus-bakker</c>.</summary>
    public string? EnvironmentDetail { get; init; }

    /// <summary>
    /// De Azure-scope waartegen de kosten worden gemeten, of <c>null</c> om hem later vast te leggen.
    /// </summary>
    /// <remarks>Zie <see cref="CustomerDocument.AzureScope"/> en <see cref="Data.AzureScope"/>.</remarks>
    public string? AzureScope { get; init; }

    /// <summary>De eigen Cosmos-endpoint van deze klant, of leeg voor de standaard.</summary>
    public string? TelemetryEndpoint { get; init; }

    /// <summary>De databasenaam bij de eigen endpoint, of leeg voor de standaard.</summary>
    public string? TelemetryDatabase { get; init; }

    /// <summary>Het contract, of <c>null</c> om dat later vast te leggen.</summary>
    public ContractEdit? Contract { get; init; }

    /// <summary>Wie er meteen toegang krijgt. Mag leeg zijn.</summary>
    public IReadOnlyList<AccessGrant> Access { get; init; } = [];

    /// <summary>
    /// Controleert het verzoek.
    /// </summary>
    /// <returns><c>null</c> als het klopt, anders de melding voor het formulier.</returns>
    public string? Validate()
    {
        if (PortalSlug.Validate(CustomerId) is { } slugError)
        {
            return slugError;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Vul een klantnaam in.";
        }

        // De scope wordt hier gecontroleerd en niet alleen op het formulier: dit is de controle die de
        // opslag zelf doet, en dus de enige die ook geldt voor een aanroeper die het formulier omzeilt.
        // Een onbruikbare scope hoort niet in een document te belanden, want de fout die eruit volgt is
        // een geslaagd leeg antwoord van Cost Management — zie AzureScope.
        if (Data.AzureScope.Validate(AzureScope) is { } scopeError)
        {
            return scopeError;
        }

        if (Contract?.Validate() is { } contractError)
        {
            return contractError;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grant in Access)
        {
            if (grant.Validate() is { } accessError)
            {
                return accessError;
            }

            if (!seen.Add(PortalEmail.Normalize(grant.Email)))
            {
                return $"Het e-mailadres {PortalEmail.Normalize(grant.Email)} staat er twee keer in.";
            }
        }

        // Cosmos laat maximaal honderd bewerkingen in één transactionele batch. Dat halen we bij een
        // klant nooit, maar een grens die stil wordt overschreden levert een 400 op de laatste
        // toegangsregel — en dan is de klant niet aangemaakt zonder dat het formulier weet waarom.
        return Access.Count > 90
            ? "Meer dan negentig toegangen in één keer kan niet. Maak de klant aan en voeg de rest " +
              "daarna toe."
            : null;
    }
}

/// <summary>
/// De contractkaart zoals een operator hem bewerkt (§3.5).
/// </summary>
/// <remarks>
/// <para><see cref="BasedOnETag"/> is de kern van de gelijktijdigheid: dat is de versie die op het
/// scherm stond toen de operator begon te typen. Hij gaat als <c>If-Match</c> mee. Is het document
/// intussen door iemand anders gewijzigd, dan weigert Cosmos de schrijfactie en krijgt de aanroeper
/// een <see cref="PortalWriteStatus.Conflict"/> met het huidige document erbij.</para>
///
/// <para><c>null</c> betekent iets anders dan "sla de controle over": het betekent dat er nog geen
/// contract was. Dan wordt het document aangemaakt, en als iemand anders net vóór jou hetzelfde
/// deed levert dat óók een conflict op. Er is dus geen waarde van dit veld waarmee je een ander
/// stil overschrijft.</para>
/// </remarks>
public sealed record ContractEdit
{
    /// <summary>Contractnummer.</summary>
    public string? Number { get; init; }

    /// <summary>Soort contract.</summary>
    public string? Type { get; init; }

    /// <summary>Ingangsdatum als <c>yyyy-MM-dd</c>. Dat is wat een HTML-datumveld oplevert.</summary>
    public string? StartsOn { get; init; }

    /// <summary>Looptijd als tekst.</summary>
    public string? Term { get; init; }

    /// <summary>Opzegtermijn als tekst.</summary>
    public string? NoticePeriod { get; init; }

    /// <summary>De SLA in één regel.</summary>
    public string? Sla { get; init; }

    /// <summary>
    /// Urenbundel per maand, of <c>null</c> om vast te leggen dat er geen bundel is afgesproken.
    /// </summary>
    /// <remarks>
    /// <c>null</c> is geen "laat staan wat er stond": dit is een volledige bewerking en een leeg veld
    /// betekent leeg. Zie <see cref="ContractDocument.BundledHours"/> voor waarom nul en <c>null</c>
    /// niet dezelfde waarde zijn.
    /// </remarks>
    public decimal? BundledHours { get; init; }

    /// <summary>Uurtarief buiten de bundel, of <c>null</c> als er geen tarief is afgesproken.</summary>
    public decimal? HourlyRate { get; init; }

    /// <summary>Indexatie.</summary>
    public string? Indexation { get; init; }

    /// <summary>Contactpersoon bij de klant.</summary>
    public string? Contact { get; init; }

    /// <summary>Beheerd door.</summary>
    public string? ManagedBy { get; init; }

    /// <summary>
    /// Opslagpercentage op de Azure-kosten, of <c>null</c> als er niets is afgesproken.
    /// Operator-only.
    /// </summary>
    public decimal? AzureSurchargePercentage { get; init; }

    /// <summary>
    /// De etag waarop deze bewerking is gebaseerd, of <c>null</c> als er nog geen contract was.
    /// </summary>
    public string? BasedOnETag { get; init; }

    /// <summary>
    /// Controleert de invoer.
    /// </summary>
    /// <returns><c>null</c> als het klopt, anders de melding voor het formulier.</returns>
    /// <remarks>
    /// <para>Bewust geen verplichte velden buiten de vorm om. Een klant in onboarding heeft nog geen
    /// contractnummer, en een verplicht veld levert dan een verzonnen nummer op — dat is erger dan
    /// een streepje op de kaart. Wat hier wél wordt tegengehouden is een waarde die niet kán:
    /// een datum in een andere vorm sorteert stil verkeerd, en een negatief tarief gaat de factuur
    /// in.</para>
    ///
    /// <para><strong><c>null</c> is hier geen nul.</strong> De drie bedragen zijn <c>decimal?</c> en
    /// <c>null</c> betekent "niet vastgelegd". Er valt aan een niet-vastgelegd getal niets te
    /// controleren, dus zo'n veld komt langs deze regels heen — het wordt niet stil op nul gezet en
    /// dan als nul goedgekeurd. Daarom staan de grenzen als patronen (<c>is &lt; 0</c>) en niet als
    /// vergelijkingen op een uitgepakte waarde: een patroon matcht <c>null</c> niet, en dan is aan de
    /// code te zien dat het onderscheid bedoeld is.</para>
    /// </remarks>
    public string? Validate()
    {
        if (!string.IsNullOrWhiteSpace(StartsOn) && !IsIsoDate(StartsOn))
        {
            return "De ingangsdatum hoort de vorm jjjj-mm-dd te hebben, bijvoorbeeld 2026-02-01.";
        }

        if (BundledHours is < 0)
        {
            return "Een urenbundel kan niet negatief zijn.";
        }

        if (HourlyRate is < 0)
        {
            return "Een uurtarief kan niet negatief zijn.";
        }

        return AzureSurchargePercentage is < 0 or > 100
            ? "Het opslagpercentage op de Azure-kosten hoort tussen 0 en 100 te liggen."
            : null;
    }

    private static bool IsIsoDate(string value) =>
        DateOnly.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
}

/// <summary>
/// De klantvelden die na het aanmaken nog te wijzigen zijn.
/// </summary>
/// <remarks>
/// <see cref="NewCustomerRequest.CustomerId"/> staat hier niet, en dat is opzet: de slug is de
/// sleutel waar de telemetrie van elke agent op aansluit. Hem wijzigen zou elk bestaand
/// telemetriedocument stil laten verwijzen naar een klant die niet meer zo heet. Moet hij toch
/// anders, dan is dat een nieuwe klant en een migratie, niet een formulierveld.
/// </remarks>
public sealed record CustomerEdit
{
    /// <summary>De klantnaam.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Of dit een interne beheerklant is (§4). Geen formulierveld: het formulier draagt hem alleen
    /// door.
    /// </summary>
    /// <remarks>
    /// <para>Dit is geen keuze die een operator hier maakt — een klant intern maken raakt de
    /// facturatie en is geen naamswijziging — en de schrijfkant behandelt hem daarom ook niet als
    /// keuze: <see cref="IPortalDataStore.SaveCustomerAsync"/> houdt vast wat er op het bestaande
    /// document staat en gebruikt deze waarde alleen als er nog geen document is.</para>
    ///
    /// <para><strong>Waarom het veld er dan toch staat.</strong> Zonder dit veld schreef de eerste
    /// wijziging aan een klant die alleen uit de configuratie komt <c>isInternal: false</c> weg,
    /// ongeacht wat de configuratie zei. Bij de interne beheerklant zou die ene klik hem daarmee tot
    /// een gewone, factureerbare klant maken — stil, en zonder dat de verschillenkaart er iets over
    /// zegt, want het formulier heeft dat veld niet. Zie de opmerking bij <c>IsInternal</c> in
    /// <see cref="CosmosPortalDataStore.SaveCustomerAsync"/>.</para>
    /// </remarks>
    public bool IsInternal { get; init; }

    /// <summary>Korte omgevingsaanduiding.</summary>
    public string? Environment { get; init; }

    /// <summary>De volledige omgeving. Operator-only op het scherm.</summary>
    public string? EnvironmentDetail { get; init; }

    /// <summary>
    /// De Azure-scope waartegen de kosten worden gemeten, of <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Anders dan <see cref="IsInternal"/> is dit een gewoon formulierveld: hij is te corrigeren. Dat
    /// is de hele reden dat het omgevingsblok bestaat — zie de opmerking daar: een verkeerd getypt
    /// abonnements-id was na het aanmaken alleen met de hand in Cosmos te herstellen.
    /// </remarks>
    public string? AzureScope { get; init; }

    /// <summary>De eigen Cosmos-endpoint van deze klant, of leeg voor de standaard.</summary>
    public string? TelemetryEndpoint { get; init; }

    /// <summary>De databasenaam bij de eigen endpoint.</summary>
    public string? TelemetryDatabase { get; init; }

    /// <summary>De etag waarop deze bewerking is gebaseerd.</summary>
    public string? BasedOnETag { get; init; }

    /// <summary>
    /// Controleert de invoer.
    /// </summary>
    /// <returns><c>null</c> als het klopt, anders de melding.</returns>
    /// <remarks>
    /// De naam eerst en de scope daarna, in de volgorde van de velden op het scherm. Een leeg
    /// scopeveld is toegestaan: zie <see cref="Data.AzureScope.Validate"/>.
    /// </remarks>
    public string? Validate() =>
        string.IsNullOrWhiteSpace(Name)
            ? "Vul een klantnaam in."
            : Data.AzureScope.Validate(AzureScope);
}

/// <summary>
/// Eén toegang die een operator uitdeelt (§3.5).
/// </summary>
public sealed record AccessGrant
{
    /// <summary>Het e-mailadres. Wordt genormaliseerd naar kleine letters.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>De naam, voor het toegangsoverzicht.</summary>
    public string? Name { get; init; }

    /// <summary>De rol binnen de klant. Zie <see cref="PortalAccessRoles"/>.</summary>
    public string Role { get; init; } = PortalAccessRoles.Reader;

    /// <summary>
    /// Controleert de invoer.
    /// </summary>
    /// <returns><c>null</c> als het klopt, anders de melding voor het formulier.</returns>
    public string? Validate()
    {
        var email = PortalEmail.Normalize(Email);

        if (PortalEmail.Validate(email) is { } emailError)
        {
            return emailError;
        }

        // Een onbekende rol wordt geweigerd en niet stil naar Lezer teruggezet. De rol
        // "Soratus-operator" uit de mockup valt hier af, en dat is de bedoeling: operator worden
        // gebeurt in Entra en niet in een toegangslijst.
        return PortalAccessRoles.IsKnown(Role)
            ? null
            : $"'{Role}' is geen rol binnen een klant. Kies {PortalAccessRoles.Administrator} of " +
              $"{PortalAccessRoles.Reader}.";
    }
}
