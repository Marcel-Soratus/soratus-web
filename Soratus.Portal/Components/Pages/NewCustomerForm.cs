using Soratus.Portal.Data;

namespace Soratus.Portal.Components.Pages;

/// <summary>
/// Wat er in het formulier "Nieuwe klant" staat (§3.9), als tekst zoals de browser het oplevert.
/// </summary>
/// <remarks>
/// <para><strong>Alles is <c>string</c>.</strong> Dat is geen luiheid: dit model wordt door een
/// POST gevuld (static SSR, model binding op <c>Invoer.Name</c> en zo verder), en een POST levert
/// tekst. Omzetten naar <c>decimal</c> en <c>bool</c> gebeurt in <see cref="ToRequest"/>, op één
/// plek, ná <see cref="FieldErrors"/>.</para>
///
/// <para><strong>Waarom dit een eigen klasse is en geen velden op de pagina.</strong> Twee redenen.
/// De namen van de eigenschappen zijn de namen in de POST — <c>nameof</c> houdt de markup en het
/// model aan elkaar vast, en een verschrijving in een <c>name</c>-attribuut is precies de fout die
/// een veld stil laat verdwijnen. En de omzetting naar <see cref="NewCustomerRequest"/> hoort bij
/// de velden te staan en niet in een Razor-bestand, zodat zichtbaar is dat elk veld ergens
/// aankomt.</para>
///
/// <para><strong>Wat hier niet gebeurt: beslissen of een waarde mag.</strong> Dat doet
/// <see cref="NewCustomerRequest.Validate"/>, en de opslag roept die aan. <see cref="FieldErrors"/>
/// doet alleen wat aan één veld hangt en waarvoor de datalaag zelf een controle heeft
/// (<see cref="PortalSlug"/>, <see cref="PortalEmail"/>) of wat het scherm bezit: of de tekst in
/// een getalveld een getal is. Zo bestaat er één definitie van "klopt dit" en niet twee die uit
/// elkaar lopen.</para>
/// </remarks>
public sealed class NewCustomerForm
{
    /// <summary>De keuzewaarde voor een gewone klantomgeving.</summary>
    public const string CustomerEnvironment = "klant";

    /// <summary>De keuzewaarde voor de interne beheeromgeving van Soratus (§4).</summary>
    public const string InternalEnvironment = "intern";

    /// <summary>Het aantal toegangsregels op het formulier.</summary>
    /// <remarks>
    /// Drie vaste regels en geen "+ regel"-knop. Zo'n knop vraagt een interactief eiland, en dit
    /// formulier is met opzet static SSR: het wordt één keer ingevuld en heeft geen enkele
    /// afhankelijkheid tussen velden. Wie meer mensen kwijt moet, voegt ze na het aanmaken toe op
    /// het contractscherm — dat staat als voetregel onder het formulier, zodat de grens zichtbaar
    /// is in plaats van dat een knop hem verzwijgt.
    /// </remarks>
    public const int AccessRowCount = 3;

    /// <summary>
    /// De klantslug: de sleutel waar de URL, de partitiesleutel én de telemetrie van elke agent op
    /// aansluiten.
    /// </summary>
    /// <remarks>
    /// Verplicht en met de hand ingevuld. Zie <see cref="PortalSlug"/>: afleiden uit de naam levert
    /// een klant op wiens agents onvindbaar zijn, want die publiceren onder de slug die in hun
    /// configuratie staat.
    /// </remarks>
    public string? CustomerId { get; set; }

    /// <summary>De klantnaam zoals hij op het scherm hoort te staan.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Soort omgeving: <see cref="CustomerEnvironment"/> of <see cref="InternalEnvironment"/>.
    /// </summary>
    /// <remarks>
    /// Een keuzelijst en geen aanvinkvak: "intern" bepaalt of het contract als niet-doorbelast
    /// wordt gelezen, en dat is een eigenschap van de klant en geen instelling. Staat er iets
    /// anders dan <see cref="InternalEnvironment"/>, dan is het een gewone klant — de veilige kant.
    /// </remarks>
    public string? EnvironmentKind { get; set; } = CustomerEnvironment;

    /// <summary>Korte omgevingsaanduiding, bijvoorbeeld <c>West-Europa</c>. Ziet de klant ook.</summary>
    public string? Environment { get; set; }

    /// <summary>De volledige omgeving (subscription · resource group). Operator-only (§2).</summary>
    public string? EnvironmentDetail { get; set; }

    /// <summary>De eigen Cosmos-endpoint van de telemetrie van deze klant, of leeg.</summary>
    public string? TelemetryEndpoint { get; set; }

    /// <summary>De databasenaam bij die endpoint, of leeg voor de standaard.</summary>
    public string? TelemetryDatabase { get; set; }

    /// <summary>Contractnummer.</summary>
    public string? ContractNumber { get; set; }

    /// <summary>Soort contract.</summary>
    public string? ContractType { get; set; }

    /// <summary>Ingangsdatum als <c>yyyy-MM-dd</c>: wat een datumveld oplevert.</summary>
    public string? StartsOn { get; set; }

    /// <summary>Looptijd als tekst.</summary>
    public string? Term { get; set; }

    /// <summary>Opzegtermijn als tekst.</summary>
    public string? NoticePeriod { get; set; }

    /// <summary>Urenbundel per maand.</summary>
    public string? BundledHours { get; set; }

    /// <summary>Uurtarief buiten de bundel.</summary>
    public string? HourlyRate { get; set; }

    /// <summary>Indexatie.</summary>
    public string? Indexation { get; set; }

    /// <summary>De SLA in één regel.</summary>
    public string? Sla { get; set; }

    /// <summary>Contactpersoon bij de klant.</summary>
    public string? Contact { get; set; }

    /// <summary>
    /// Beheerd door, bijvoorbeeld <c>Soratus — accountteam</c>.
    /// </summary>
    /// <remarks>
    /// Staat niet in de veldenlijst van §3.9 maar wel op de contractkaart van §3.5. Zonder dit
    /// veld begint elke nieuwe klant met een streepje op die kaart en kost het een tweede
    /// bewerking om iets in te vullen dat de operator op dit moment gewoon weet.
    /// </remarks>
    public string? ManagedBy { get; set; }

    /// <summary>Opslagpercentage op de Azure-kosten. Operator-only (§2).</summary>
    public string? AzureSurcharge { get; set; }

    /// <summary>De eerste toegangsregel.</summary>
    public AccessRow Access1 { get; set; } = new();

    /// <summary>De tweede toegangsregel.</summary>
    public AccessRow Access2 { get; set; } = new();

    /// <summary>De derde toegangsregel.</summary>
    public AccessRow Access3 { get; set; } = new();

    /// <summary>Eén regel van de toegangslijst op het aanmaakformulier.</summary>
    public sealed class AccessRow
    {
        /// <summary>Het e-mailadres.</summary>
        public string? Email { get; set; }

        /// <summary>De naam, voor het toegangsoverzicht.</summary>
        public string? Name { get; set; }

        /// <summary>De aanduiding: <see cref="PortalAccessRoles.Administrator"/> of <see cref="PortalAccessRoles.Reader"/>.</summary>
        /// <remarks>
        /// Geen bevoegdheid. Beide waarden geven hetzelfde recht — lezen — want alleen Soratus
        /// deelt toegang uit. De standaard is <see cref="PortalAccessRoles.Reader"/> en niet de
        /// eerste uit de lijst: wie niets kiest hoort niet stil "Beheerder klant" te worden.
        /// </remarks>
        public string? Designation { get; set; } = PortalAccessRoles.Reader;

        /// <summary>Of deze regel helemaal leeg is en dus overgeslagen mag worden.</summary>
        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Email)
            && string.IsNullOrWhiteSpace(Name);

        /// <summary>Deze regel als toegang voor de opslag.</summary>
        public AccessGrant Grant() => new()
        {
            Email = PortalEmail.Normalize(Email),
            Name = NullIfBlank(Name),

            // Een onbekende waarde wordt Lezer en niet doorgegeven. De schrijfkant weigert een
            // onbekende aanduiding met een melding, en dat is de juiste plek voor iemand die het
            // formulier omzeilt; maar een waarde die uit onze eigen keuzelijst hoort te komen en
            // dat niet doet, is hier de mildste keuze.
            Role = PortalAccessRoles.IsKnown(Designation) ? Designation! : PortalAccessRoles.Reader,
        };
    }

    /// <summary>De drie toegangsregels, in schermvolgorde.</summary>
    /// <returns>De regels; nummer 1 tot en met <see cref="AccessRowCount"/>.</returns>
    public AccessRow Row(int number) => number switch
    {
        1 => Access1,
        2 => Access2,
        _ => Access3,
    };

    /// <summary>
    /// De meldingen die bij één veld horen.
    /// </summary>
    /// <returns>Veldnaam (zoals in <c>nameof</c>) naar melding; leeg als er niets aan de hand is.</returns>
    /// <remarks>
    /// Alleen wat onder een veld hoort te staan. Wat over het geheel gaat — een dubbel
    /// e-mailadres, een naam die ontbreekt, een negatief tarief — komt uit
    /// <see cref="NewCustomerRequest.Validate"/> en komt als blok boven de knop te staan. Twee
    /// plekken, geen twee definities.
    /// </remarks>
    public IReadOnlyDictionary<string, string> FieldErrors()
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        if (PortalSlug.Validate(CustomerId) is { } slugError)
        {
            errors[nameof(CustomerId)] = slugError;
        }

        // De invoer gaat mee naar de melding. Bij een duizendscheiding ("1.250") zegt hij dan wat
        // er dubbelzinnig is in plaats van om een getal te vragen terwijl er al een getal staat.
        // Zie ContractText.NumberError: laat je de invoer weg, dan is de melding niet onwaar maar
        // wel minder scherp bij precies het geval waar hij het meest te zeggen heeft.
        if (!ContractText.TryNumber(BundledHours, out _))
        {
            errors[nameof(BundledHours)] = ContractText.NumberError("8", BundledHours);
        }

        if (!ContractText.TryNumber(HourlyRate, out _))
        {
            errors[nameof(HourlyRate)] = ContractText.NumberError("125,50", HourlyRate);
        }

        if (!ContractText.TryNumber(AzureSurcharge, out _))
        {
            errors[nameof(AzureSurcharge)] = ContractText.NumberError("8", AzureSurcharge);
        }

        for (var number = 1; number <= AccessRowCount; number++)
        {
            var row = Row(number);

            if (row.IsEmpty)
            {
                continue;
            }

            var key = EmailField(number);

            // Een naam zonder adres is geen halve toegang maar een vergeten veld. Stil overslaan
            // zou de klant aanmaken zonder de persoon die de operator net intypte.
            if (string.IsNullOrWhiteSpace(row.Email))
            {
                errors[key] =
                    "Vul het e-mailadres in, of maak deze regel helemaal leeg. Een naam zonder "
                    + "adres levert geen toegang op.";
                continue;
            }

            if (PortalEmail.Validate(PortalEmail.Normalize(row.Email)) is { } emailError)
            {
                errors[key] = emailError;
            }
        }

        return errors;
    }

    /// <summary>
    /// Het pad van een veld op een toegangsregel.
    /// </summary>
    /// <param name="number">Het regelnummer, vanaf 1.</param>
    /// <param name="field">De naam van de eigenschap, uit <c>nameof</c>.</param>
    /// <returns>Bijvoorbeeld <c>Access1.Email</c>.</returns>
    /// <remarks>
    /// Eén plek waar de naam <c>Access</c> staat. Dit pad is zowel het <c>name</c>-attribuut in
    /// de markup (achter het voorvoegsel van het formulier) als de sleutel van de melding uit
    /// <see cref="FieldErrors"/>. Zouden die twee elk hun eigen tekenreeks bouwen, dan komt de
    /// melding onder een veld dat niet bestaat — of erger, komt de invoer nooit aan.
    /// </remarks>
    public static string AccessField(int number, string field) => $"Access{number}.{field}";

    /// <summary>De sleutel waaronder de melding van een e-mailveld staat.</summary>
    /// <param name="number">Het regelnummer, vanaf 1.</param>
    /// <returns>Bijvoorbeeld <c>Access1.Email</c>.</returns>
    public static string EmailField(int number) =>
        AccessField(number, nameof(AccessRow.Email));

    /// <summary>
    /// Het formulier als verzoek aan de opslag.
    /// </summary>
    /// <returns>Het verzoek; klant, contract en toegangen in één schrijfactie.</returns>
    /// <remarks>
    /// Roep dit aan nadat <see cref="FieldErrors"/> leeg is teruggekomen: een getalveld dat niet te
    /// lezen is komt hier als <c>null</c> door, dus als "niet vastgelegd", en dat is niet wat de
    /// operator bedoelde. Vroeger kwam het als nul door — een bedrag dat niemand had ingetypt en dat
    /// wél als afspraak in de opslag belandde; zie <see cref="ContractText.TryNumber"/>. De opslag
    /// controleert het verzoek daarna nog een keer — dat is de controle die telt.
    /// </remarks>
    public NewCustomerRequest ToRequest() => new()
    {
        CustomerId = CustomerId?.Trim() ?? string.Empty,
        Name = Name?.Trim() ?? string.Empty,
        IsInternal = string.Equals(EnvironmentKind, InternalEnvironment, StringComparison.Ordinal),
        Environment = NullIfBlank(Environment),
        EnvironmentDetail = NullIfBlank(EnvironmentDetail),
        TelemetryEndpoint = NullIfBlank(TelemetryEndpoint),
        TelemetryDatabase = NullIfBlank(TelemetryDatabase),
        Contract = ToContract(),
        Access = [.. ToAccess()],
    };

    /// <summary>
    /// Het contract, of <c>null</c> als er geen enkel contractveld is ingevuld.
    /// </summary>
    /// <remarks>
    /// <c>null</c> betekent: leg het contract later vast. Dat is een gewone toestand — een klant in
    /// onboarding heeft nog geen contractnummer — en het is beter dan een contractdocument met elf
    /// lege velden, want dan zegt het contractscherm "vastgelegd" over iets wat niet bestaat.
    /// </remarks>
    private ContractEdit? ToContract()
    {
        var empty = string.IsNullOrWhiteSpace(ContractNumber)
                    && string.IsNullOrWhiteSpace(ContractType)
                    && string.IsNullOrWhiteSpace(StartsOn)
                    && string.IsNullOrWhiteSpace(Term)
                    && string.IsNullOrWhiteSpace(NoticePeriod)
                    && string.IsNullOrWhiteSpace(BundledHours)
                    && string.IsNullOrWhiteSpace(HourlyRate)
                    && string.IsNullOrWhiteSpace(Indexation)
                    && string.IsNullOrWhiteSpace(Sla)
                    && string.IsNullOrWhiteSpace(Contact)
                    && string.IsNullOrWhiteSpace(ManagedBy)
                    && string.IsNullOrWhiteSpace(AzureSurcharge);

        if (empty)
        {
            return null;
        }

        ContractText.TryNumber(BundledHours, out var hours);
        ContractText.TryNumber(HourlyRate, out var rate);
        ContractText.TryNumber(AzureSurcharge, out var surcharge);

        return new ContractEdit
        {
            Number = NullIfBlank(ContractNumber),
            Type = NullIfBlank(ContractType),
            StartsOn = NullIfBlank(StartsOn),
            Term = NullIfBlank(Term),
            NoticePeriod = NullIfBlank(NoticePeriod),
            Sla = NullIfBlank(Sla),
            BundledHours = hours,
            HourlyRate = rate,
            Indexation = NullIfBlank(Indexation),
            Contact = NullIfBlank(Contact),
            ManagedBy = NullIfBlank(ManagedBy),
            AzureSurchargePercentage = surcharge,

            // Er is nog geen contract, dus er is niets om op te baseren. Dat is geen ontbrekende
            // controle: de batch schrijft met CreateItem, en wie net eerder was levert een
            // conflict op.
            BasedOnETag = null,
        };
    }

    /// <summary>De ingevulde toegangsregels.</summary>
    private IEnumerable<AccessGrant> ToAccess()
    {
        for (var number = 1; number <= AccessRowCount; number++)
        {
            var row = Row(number);

            if (!row.IsEmpty)
            {
                yield return row.Grant();
            }
        }
    }

    /// <summary>Witruimte wordt <c>null</c>: een leeg veld is geen lege tekst maar geen waarde.</summary>
    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
