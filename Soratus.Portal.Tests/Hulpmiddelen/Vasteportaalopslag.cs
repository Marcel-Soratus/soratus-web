using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// De portaaleigen opslag in het geheugen: klanten, contracten en toegangen, met echte etags.
/// </summary>
/// <remarks>
/// <para>Dit is geen tweede implementatie in <c>Soratus.Portal</c> — die mag er niet zijn, en de
/// reflectietest in <c>StoreImplementatieTests</c> houdt dat tegen; die kijkt naar de
/// portaalassembly. Dit is de opslag in het <em>testproject</em>, zodat het contractscherm en het
/// aanmaakformulier te renderen zijn zonder Cosmos aan te raken.</para>
///
/// <para><strong>Rijk gevuld, en dat is het punt.</strong> Een lege stub laat elk scherm renderen
/// zonder er iets op te zetten, en dan bewijst een zichtbaarheidstest niets: hij staat groen omdat
/// er niets staat. Er staat hier daarom een volledig contract — alle elf velden van §3.5 plus het
/// opslagpercentage — en een toegangslijst met beide aanduidingen én een regel zonder naam. De
/// waarden zijn met opzet onderscheidend (<c>8,75</c> voor de marge, een eigen naam voor wie
/// wijzigde), zodat een test op "staat dit in de markup" niet per ongeluk op een ander getal
/// aanslaat.</para>
///
/// <para><strong>De gelijktijdigheid is echt en niet gescript.</strong> Elke schrijfactie geeft een
/// nieuwe etag, en een bewerking waarvan de etag niet meer klopt levert een conflict op met het
/// huidige document erbij — dezelfde uitkomst en dezelfde melding als
/// <c>CosmosPortalDataStore</c>. Een test speelt "iemand anders was er eerder" dus door dat te
/// dóen (zie <see cref="EenAndereOperatorWijzigtHetContract"/>) en niet door een uitkomst klaar te
/// zetten. Dat scheelt de val waarin het scherm groen staat op een conflict dat in werkelijkheid
/// nooit zo ontstaat.</para>
///
/// <para>De documenten komen door de omzetting van de productiecode heen; zie
/// <see cref="Documentvorm"/>.</para>
/// </remarks>
/// <remarks>
/// <c>partial</c> en met <see cref="IPortalHoursStore"/> erbij: de urenkant staat in
/// <c>Vasteportaalopslag.Uren.cs</c>. Eén klasse en geen tweede opslag ernaast, want het urenscherm
/// leest de urenregels én het contract uit dezelfde partitie — twee fixtures zouden twee
/// werkelijkheden zijn, en dan is het saldo van de ene niet dat van de andere.
/// </remarks>
internal sealed partial class Vasteportaalopslag : IPortalDataStore, IPortalHoursStore
{
    /// <summary>De klant die standaard gevuld is: dezelfde als in <see cref="Autorisatiebron"/>.</summary>
    public const string Standaardklant = "acme-logistiek";

    /// <summary>Het contractnummer in de vaste gegevens.</summary>
    public const string Contractnummer = "SOR-2026-0142";

    /// <summary>De urenbundel per maand.</summary>
    public const decimal Urenbundel = 12m;

    /// <summary>Het uurtarief buiten de bundel.</summary>
    public const decimal Uurtarief = 137.5m;

    /// <summary>
    /// Het opslagpercentage op de Azure-kosten: onze marge, en operator-only (§2).
    /// </summary>
    /// <remarks>
    /// Met een halve procent erachter, zodat de opgemaakte vorm (<c>8,75</c>) in geen enkel ander
    /// veld van deze gegevens voorkomt. Een test die zoekt of dit getal op het klantscherm staat
    /// kijkt dan echt naar dit veld.
    /// </remarks>
    public const decimal Opslagpercentage = 8.75m;

    /// <summary>Wie het contract voor het laatst heeft gewijzigd: een naam bij Soratus.</summary>
    public const string Wijzigdehet = "Sanne de Wit";

    /// <summary>De volledige omgeving van de standaardklant. Operator-only (§2).</summary>
    public const string Omgevingsdetail = "sub-soratus-acme · rg-acme-prod";

    /// <summary>
    /// Het begin van elke etag die deze opslag uitdeelt.
    /// </summary>
    /// <remarks>
    /// Cosmos-etags zien er zo uit. Staat als constante hier zodat een test kan zoeken of er ergens
    /// een schrijfvoorwaarde in de markup is beland, zonder een etagwaarde over te typen.
    /// </remarks>
    public const string Etagvingerafdruk = "0x8DC";

    /// <summary>Het adres van de contactpersoon met de aanduiding "Beheerder klant".</summary>
    public const string Beheerderadres = "directie@acme-logistiek.nl";

    /// <summary>Het adres van de derde toegangsregel; die heeft geen naam vastgelegd.</summary>
    public const string Adreszondernaam = "planning@acme-logistiek.nl";

    private readonly Dictionary<string, Klantpartitie> _partities =
        new(StringComparer.OrdinalIgnoreCase);

    private int _versie;

    /// <summary>
    /// Vult de opslag met de standaardklant.
    /// </summary>
    /// <param name="zonderContract">
    /// Laat het contractdocument weg. Dat is de klant in onboarding: een gewone toestand, en de enige
    /// waarin het scherm de lege staat hoort te tonen in plaats van een kaart met streepjes.
    /// </param>
    /// <param name="zonderToegang">
    /// Laat de toegangslijst leeg. Voor de lege staat van het toegangsblok.
    /// </param>
    /// <param name="alleenUitConfiguratie">
    /// Laat het klantdocument weg: de klant staat dan alleen in de configuratie en de eenmalige
    /// migratie heeft voor hem nog niet gelopen. Dat is wat
    /// <c>OperatorContractView.IsFromConfigurationOnly</c> op <c>true</c> zet.
    /// </param>
    /// <param name="contract">
    /// Het contract dat er staat, of <c>null</c> voor <see cref="Volledigcontract"/>. Voor een test
    /// die één veld anders nodig heeft: <c>Volledigcontract() with { … }</c>. Vooral bedoeld voor de
    /// bedragen, waar <c>null</c> ("niet vastgelegd") en nul ("nul afgesproken") twee verschillende
    /// afspraken zijn.
    /// </param>
    public Vasteportaalopslag(
        bool zonderContract = false,
        bool zonderToegang = false,
        bool alleenUitConfiguratie = false,
        ContractEdit? contract = null)
    {
        var partitie = Partitie(Standaardklant);

        if (!alleenUitConfiguratie)
        {
            partitie.Klant = new CustomerDocument
            {
                Id = PortalDocumentIds.Customer,
                PartitionKey = Standaardklant,
                CustomerId = Standaardklant,
                Name = "Acme Logistiek",
                IsInternal = false,
                Environment = "West-Europa",
                EnvironmentDetail = Omgevingsdetail,
                TelemetryEndpoint = Autorisatiebron.StandaardEndpoint,
                TelemetryDatabase = "telemetry",
                CreatedAt = Testgegevens.Nu - TimeSpan.FromDays(120),
                CreatedBy = Wijzigdehet,
                ChangedAt = Testgegevens.Nu - TimeSpan.FromDays(3),
                ChangedBy = Wijzigdehet,
                ETag = NieuweEtag(),
            };
        }

        if (!zonderContract)
        {
            partitie.Contract = Documentvorm.Contract(
                contract ?? Volledigcontract(),
                Standaardklant,
                Wijzigdehet,
                Testgegevens.Nu - TimeSpan.FromDays(3)) with { ETag = NieuweEtag() };
        }

        if (!zonderToegang)
        {
            Leg(partitie, Beheerderadres, "Jan Acme", PortalAccessRoles.Administrator);
            Leg(partitie, Testprincipals.KlantEmail, "Inkoop Acme", PortalAccessRoles.Reader);

            // Zonder naam: het scherm hoort daar een streepje te zetten en geen lege cel. Een lege
            // cel laat de lezer denken dat de pagina niet klaar is.
            Leg(partitie, Adreszondernaam, naam: null, PortalAccessRoles.Reader);
        }
    }

    /// <summary>Elke contractbewerking die deze opslag heeft gekregen, in volgorde.</summary>
    /// <remarks>
    /// Hierop staat de gelijktijdigheidstest: welke etag ging er mee, en welke waarden. Een tweede
    /// poging na een conflict hoort de eigen waarden te dragen met de etag van de ander.
    /// </remarks>
    public List<ContractEdit> Contractbewerkingen { get; } = [];

    /// <summary>Elke klantwijziging die deze opslag heeft gekregen, in volgorde.</summary>
    /// <remarks>
    /// De tegenhanger van <see cref="Contractbewerkingen"/> voor het klantdocument, en om dezelfde
    /// reden: welke etag ging mee, en welke waarden. Zonder deze lijst is niet te zien of het
    /// omgevingsblok de versie van het scherm meestuurt of een verse lezing.
    /// </remarks>
    public List<CustomerEdit> Klantwijzigingen { get; } = [];

    /// <summary>Elke toegang die is vastgelegd, in volgorde.</summary>
    public List<AccessGrant> Toegangverleningen { get; } = [];

    /// <summary>Elke intrekking: het adres en de etag waarop hij is gebaseerd.</summary>
    public List<(string Email, string? BasedOnETag)> Intrekkingen { get; } = [];

    /// <summary>Elk verzoek om een klant aan te maken.</summary>
    public List<NewCustomerRequest> Klantaanmaken { get; } = [];

    /// <summary>Het klantdocument zoals het nu in de opslag staat.</summary>
    /// <param name="klant">De klantslug.</param>
    /// <returns>Het document, of <c>null</c> als deze klant alleen uit de configuratie komt.</returns>
    public CustomerDocument? Klant(string klant = Standaardklant) => Partitie(klant).Klant;

    /// <summary>Het contract zoals het nu in de opslag staat.</summary>
    /// <param name="klant">De klantslug.</param>
    /// <returns>Het document, of <c>null</c> als er geen contract is.</returns>
    public ContractDocument? Contract(string klant = Standaardklant) => Partitie(klant).Contract;

    /// <summary>De toegangen zoals ze nu in de opslag staan, op adres gesorteerd.</summary>
    /// <param name="klant">De klantslug.</param>
    /// <returns>De documenten.</returns>
    public IReadOnlyList<AccessDocument> Toegangen(string klant = Standaardklant) =>
    [
        .. Partitie(klant).Toegangen.Values.OrderBy(t => t.Email, StringComparer.Ordinal)
    ];

    /// <summary>
    /// Iemand anders wijzigt het contract terwijl dit formulier openstaat.
    /// </summary>
    /// <param name="wijziging">Wat die ander verandert.</param>
    /// <param name="klant">De klantslug.</param>
    /// <remarks>
    /// Buiten de scopes om, en dat hoort: dit is niet de gebruiker van het scherm maar een tweede
    /// operator in een ander circuit. De etag schuift op, en daarmee is de etag die het formulier
    /// vasthoudt verouderd — precies zoals in werkelijkheid.
    /// </remarks>
    public void EenAndereOperatorWijzigtHetContract(
        Func<ContractDocument, ContractDocument> wijziging,
        string klant = Standaardklant)
    {
        ArgumentNullException.ThrowIfNull(wijziging);

        var partitie = Partitie(klant);

        if (partitie.Contract is null)
        {
            throw new InvalidOperationException(
                $"Klant {klant} heeft geen contract, dus er valt niets aan te wijzigen. Bouw de " +
                "opslag zonder zonderContract als een test een tweede wijziger nodig heeft.");
        }

        partitie.Contract = wijziging(partitie.Contract) with
        {
            ChangedAt = Testgegevens.Nu,
            ChangedBy = "Ruben Vos",
            ETag = NieuweEtag(),
        };
    }

    /// <summary>
    /// Iemand anders legt het contract vast terwijl dit formulier openstaat.
    /// </summary>
    /// <param name="contract">Wat die ander invult.</param>
    /// <param name="klant">De klantslug.</param>
    /// <remarks>
    /// De tegenhanger van <see cref="EenAndereOperatorWijzigtHetContract"/> voor de klant die er nog
    /// geen contract had. Dat geval is het subtiele: het formulier draagt dan geen etag, en "geen
    /// etag" mag geen vrijbrief zijn om over de aanleg van een ander heen te schrijven.
    /// </remarks>
    public void EenAndereOperatorLegtHetContractVast(
        ContractEdit contract,
        string klant = Standaardklant)
    {
        ArgumentNullException.ThrowIfNull(contract);

        Partitie(klant).Contract = Documentvorm.Contract(
            contract,
            klant,
            "Ruben Vos",
            Testgegevens.Nu) with { ETag = NieuweEtag() };
    }

    /// <summary>
    /// Iemand anders wijzigt de omgeving van deze klant terwijl het formulier openstaat.
    /// </summary>
    /// <param name="wijziging">Wat die ander verandert.</param>
    /// <param name="klant">De klantslug.</param>
    /// <remarks>
    /// De tegenhanger van <see cref="EenAndereOperatorWijzigtHetContract"/> voor het klantdocument.
    /// Ook hier buiten de scopes om: dit is een tweede operator in een ander circuit, en de etag
    /// schuift op zodat de etag die het formulier vasthoudt verouderd is.
    /// </remarks>
    public void EenAndereOperatorWijzigtDeKlant(
        Func<CustomerDocument, CustomerDocument> wijziging,
        string klant = Standaardklant)
    {
        ArgumentNullException.ThrowIfNull(wijziging);

        var partitie = Partitie(klant);

        if (partitie.Klant is null)
        {
            throw new InvalidOperationException(
                $"Klant {klant} heeft geen klantdocument, dus er valt niets aan te wijzigen. Bouw " +
                "de opslag zonder alleenUitConfiguratie als een test een tweede wijziger nodig " +
                "heeft, of gebruik EenAndereOperatorLegtDeKlantVast.");
        }

        partitie.Klant = wijziging(partitie.Klant) with
        {
            ChangedAt = Testgegevens.Nu,
            ChangedBy = "Ruben Vos",
            ETag = NieuweEtag(),
        };
    }

    /// <summary>
    /// Iemand anders legt het klantdocument vast terwijl het formulier openstaat.
    /// </summary>
    /// <param name="klant">De klantslug.</param>
    /// <param name="naam">De naam die die ander invult.</param>
    /// <remarks>
    /// Voor de klant die alleen uit de configuratie komt. Dat geval is het subtiele, net als bij het
    /// contract: het formulier draagt dan geen etag, en "geen etag" mag geen vrijbrief zijn om over
    /// de aanleg van een ander heen te schrijven.
    /// </remarks>
    public void EenAndereOperatorLegtDeKlantVast(
        string klant = Standaardklant,
        string naam = "Acme Logistiek BV") =>
        Partitie(klant).Klant = new CustomerDocument
        {
            Id = PortalDocumentIds.Customer,
            PartitionKey = klant,
            CustomerId = klant,
            Name = naam,
            Environment = "West-Europa",
            EnvironmentDetail = Omgevingsdetail,
            TelemetryEndpoint = Autorisatiebron.StandaardEndpoint,
            TelemetryDatabase = "telemetry",
            CreatedAt = Testgegevens.Nu,
            CreatedBy = "Ruben Vos",
            ChangedAt = Testgegevens.Nu,
            ChangedBy = "Ruben Vos",
            ETag = NieuweEtag(),
        };

    /// <summary>Iemand anders trekt een toegang in terwijl de lijst op het scherm staat.</summary>
    /// <param name="email">Het adres.</param>
    /// <param name="klant">De klantslug.</param>
    public void EenAndereOperatorTrektToegangIn(string email, string klant = Standaardklant) =>
        Partitie(klant).Toegangen.Remove(PortalEmail.Normalize(email));

    // ── Lezen ───────────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<ContractDocument?> GetContractAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult(Partitie(scope.CustomerId).Contract);
    }

    /// <inheritdoc />
    public Task<ContractDocument?> GetContractAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult(Partitie(scope.CustomerId).Contract);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccessDocument>> GetAccessAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult(Toegangen(scope.CustomerId));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccessDocument>> GetAccessAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult(Toegangen(scope.CustomerId));
    }

    /// <inheritdoc />
    public Task<CustomerDocument?> GetCustomerAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult(Partitie(scope.CustomerId).Klant);
    }

    // ── Schrijven ───────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<PortalWriteResult<CustomerDocument>> CreateCustomerAsync(
        PortalWriteScope scope,
        NewCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(request);

        Klantaanmaken.Add(request);

        if (request.Validate() is { } melding)
        {
            return Task.FromResult(PortalWriteResult<CustomerDocument>.Invalid(melding));
        }

        var partitie = Partitie(request.CustomerId);

        if (partitie.Klant is { } bestaand)
        {
            return Task.FromResult(PortalWriteResult<CustomerDocument>.Conflict(
                $"Er bestaat al een klant met id {request.CustomerId}.",
                bestaand));
        }

        // Alles of niets: klant, contract en toegangen in één keer. Zie
        // IPortalDataStore.CreateCustomerAsync — ze delen de partitiesleutel, en binnen één
        // partitiesleutel schrijft Cosmos alles of niets. Er bestaat dus geen halve klant, ook
        // niet in deze opslag.
        partitie.Klant = new CustomerDocument
        {
            Id = PortalDocumentIds.Customer,
            PartitionKey = request.CustomerId,
            CustomerId = request.CustomerId,
            Name = request.Name,
            IsInternal = request.IsInternal,
            Environment = request.Environment,
            EnvironmentDetail = request.EnvironmentDetail,
            TelemetryEndpoint = request.TelemetryEndpoint,
            TelemetryDatabase = request.TelemetryDatabase,
            CreatedAt = Testgegevens.Nu,
            CreatedBy = scope.Actor,
            ETag = NieuweEtag(),
        };

        if (request.Contract is { } contract)
        {
            partitie.Contract = Documentvorm.Contract(
                contract,
                request.CustomerId,
                scope.Actor,
                Testgegevens.Nu) with { ETag = NieuweEtag() };
        }

        foreach (var toegang in request.Access)
        {
            partitie.Toegangen[PortalEmail.Normalize(toegang.Email)] = Documentvorm.Toegang(
                toegang,
                request.CustomerId,
                scope.Actor,
                Testgegevens.Nu) with { ETag = NieuweEtag() };
        }

        return Task.FromResult(PortalWriteResult<CustomerDocument>.Saved(partitie.Klant));
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<CustomerDocument>> SaveCustomerAsync(
        CustomerWriteScope scope,
        CustomerEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(edit);

        Klantwijzigingen.Add(edit);

        if (edit.Validate() is { } melding)
        {
            return Task.FromResult(PortalWriteResult<CustomerDocument>.Invalid(melding));
        }

        var partitie = Partitie(scope.CustomerId);

        // Twee kanten van dezelfde controle, precies zoals bij het contract hieronder. De tweede —
        // een bewerking zonder etag op een klant die inmiddels wél een document heeft — stond hier
        // niet, en dat was een afwijking van de echte opslag: UpsertAsync doet zonder etag een
        // CreateItemAsync, en die loopt op een 409 als het document er al staat. Zonder deze regel
        // overschrijft de fixture stil waar productie een conflict geeft, en dat is precies het
        // geval van de klant die alleen uit de configuratie komt.
        if (Verouderd(partitie.Klant?.ETag, edit.BasedOnETag)
            || (edit.BasedOnETag is null && partitie.Klant is not null))
        {
            return Task.FromResult(PortalWriteResult<CustomerDocument>.Conflict(
                Conflictmelding("klant"),
                partitie.Klant));
        }

        partitie.Klant = new CustomerDocument
        {
            Id = PortalDocumentIds.Customer,
            PartitionKey = scope.CustomerId,
            CustomerId = scope.CustomerId,
            Name = edit.Name,

            // Wat er staat gaat vóór wat de bewerking meestuurt; alleen bij een nieuw document telt
            // de bewerking. Zie SaveCustomerAsync in CosmosPortalDataStore.
            IsInternal = partitie.Klant?.IsInternal ?? edit.IsInternal,
            Environment = edit.Environment,
            EnvironmentDetail = edit.EnvironmentDetail,
            TelemetryEndpoint = edit.TelemetryEndpoint,
            TelemetryDatabase = edit.TelemetryDatabase,
            CreatedAt = partitie.Klant?.CreatedAt ?? Testgegevens.Nu,
            CreatedBy = partitie.Klant?.CreatedBy,
            ChangedAt = Testgegevens.Nu,
            ChangedBy = scope.Actor,
            ETag = NieuweEtag(),
        };

        return Task.FromResult(PortalWriteResult<CustomerDocument>.Saved(partitie.Klant));
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<ContractDocument>> SaveContractAsync(
        CustomerWriteScope scope,
        ContractEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(edit);

        Contractbewerkingen.Add(edit);

        if (edit.Validate() is { } melding)
        {
            return Task.FromResult(PortalWriteResult<ContractDocument>.Invalid(melding));
        }

        var partitie = Partitie(scope.CustomerId);

        // Twee kanten van dezelfde controle. Een etag die niet meer klopt is de gewone botsing; een
        // bewerking zonder etag op een contract dat er inmiddels wél is, is de andere — dan dachten
        // twee operators beide dat zij het contract aanlegden. Zie UpsertAsync in de echte opslag:
        // er is geen waarde van BasedOnETag waarmee je de controle overslaat.
        if (Verouderd(partitie.Contract?.ETag, edit.BasedOnETag)
            || (edit.BasedOnETag is null && partitie.Contract is not null))
        {
            return Task.FromResult(PortalWriteResult<ContractDocument>.Conflict(
                Conflictmelding("contract"),
                partitie.Contract));
        }

        partitie.Contract = Documentvorm.Contract(
            edit,
            scope.CustomerId,
            scope.Actor,
            Testgegevens.Nu) with { ETag = NieuweEtag() };

        return Task.FromResult(PortalWriteResult<ContractDocument>.Saved(partitie.Contract));
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<AccessDocument>> GrantAccessAsync(
        CustomerWriteScope scope,
        AccessGrant grant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(grant);

        Toegangverleningen.Add(grant);

        if (grant.Validate() is { } melding)
        {
            return Task.FromResult(PortalWriteResult<AccessDocument>.Invalid(melding));
        }

        var partitie = Partitie(scope.CustomerId);
        var email = PortalEmail.Normalize(grant.Email);

        if (partitie.Toegangen.TryGetValue(email, out var bestaand))
        {
            return Task.FromResult(PortalWriteResult<AccessDocument>.Conflict(
                $"{email} heeft al toegang tot {scope.DisplayName}, als {bestaand.Role}. Wijzig de " +
                "bestaande regel of trek hem in.",
                bestaand));
        }

        var document = Documentvorm.Toegang(grant, scope.CustomerId, scope.Actor, Testgegevens.Nu)
            with { ETag = NieuweEtag() };

        partitie.Toegangen[email] = document;

        return Task.FromResult(PortalWriteResult<AccessDocument>.Saved(document));
    }

    /// <inheritdoc />
    public Task<PortalWriteResult<AccessDocument>> RevokeAccessAsync(
        CustomerWriteScope scope,
        string email,
        string? basedOnETag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var genormaliseerd = PortalEmail.Normalize(email);

        Intrekkingen.Add((genormaliseerd, basedOnETag));

        if (PortalEmail.Validate(genormaliseerd) is { } melding)
        {
            return Task.FromResult(PortalWriteResult<AccessDocument>.Invalid(melding));
        }

        var partitie = Partitie(scope.CustomerId);

        if (!partitie.Toegangen.TryGetValue(genormaliseerd, out var huidig))
        {
            return Task.FromResult(PortalWriteResult<AccessDocument>.Conflict(
                $"{genormaliseerd} heeft geen toegang (meer) tot {scope.DisplayName}. Iemand anders " +
                "heeft die mogelijk net ingetrokken.",
                current: null));
        }

        if (Verouderd(huidig.ETag, basedOnETag))
        {
            return Task.FromResult(PortalWriteResult<AccessDocument>.Conflict(
                $"De toegang van {genormaliseerd} is intussen gewijzigd. Bekijk de regel opnieuw " +
                "voordat je hem intrekt.",
                huidig));
        }

        // Echt verwijderen en niet als ingetrokken markeren: "wie mag hierbij" is de aanwezigheid
        // van een document. Zie AccessDocument.
        partitie.Toegangen.Remove(genormaliseerd);

        return Task.FromResult(PortalWriteResult<AccessDocument>.Saved(huidig));
    }

    /// <summary>Het volledig ingevulde contract van de standaardklant, als bewerking.</summary>
    /// <returns>De bewerking, zonder etag: dit is het contract zoals het is aangelegd.</returns>
    /// <remarks>
    /// Staat als methode en niet als document, zodat hij door dezelfde omzetting gaat als een
    /// bewerking uit het formulier. Ook bruikbaar in een test die wil weten wat er stond.
    /// </remarks>
    public static ContractEdit Volledigcontract() => new()
    {
        Number = Contractnummer,
        Type = "Agent-abonnement + doorontwikkeling",
        StartsOn = "2025-11-01",
        Term = "24 maanden",
        NoticePeriod = "2 maanden",
        Sla = "Reactie 4 werkuren · herstel 1 werkdag",
        BundledHours = Urenbundel,
        HourlyRate = Uurtarief,
        Indexation = "CBS-index per 1 januari",
        Contact = "Inkoop Acme",
        ManagedBy = "Soratus — accountteam",
        AzureSurchargePercentage = Opslagpercentage,
        BasedOnETag = null,
    };

    /// <summary>De melding die de echte opslag bij een botsing geeft.</summary>
    private static string Conflictmelding(string wat) =>
        $"Dit {wat} is intussen door iemand anders gewijzigd. Je wijziging is niet opgeslagen. " +
        "Vergelijk hem met de huidige versie en probeer het opnieuw.";

    /// <summary>Of de etag van de aanroeper niet meer klopt met wat er staat.</summary>
    private static bool Verouderd(string? huidig, string? basedOn) =>
        basedOn is not null && huidig is not null && !string.Equals(huidig, basedOn, StringComparison.Ordinal);

    private void Leg(Klantpartitie partitie, string email, string? naam, string aanduiding) =>
        partitie.Toegangen[PortalEmail.Normalize(email)] = Documentvorm.Toegang(
            new AccessGrant { Email = email, Name = naam, Role = aanduiding },
            Standaardklant,
            Wijzigdehet,
            Testgegevens.Nu - TimeSpan.FromDays(40)) with { ETag = NieuweEtag() };

    private Klantpartitie Partitie(string klant)
    {
        if (!_partities.TryGetValue(klant, out var partitie))
        {
            partitie = new Klantpartitie();
            _partities[klant] = partitie;
        }

        return partitie;
    }

    /// <summary>
    /// Een nieuwe etag, in de vorm die Cosmos ze uitdeelt.
    /// </summary>
    /// <remarks>
    /// Oplopend, zodat een falende assertie leesbaar is: etag 3 komt na etag 2. In Cosmos is de
    /// waarde ondoorzichtig; wat ervan gebruikt wordt is alleen of hij gelijk is.
    /// </remarks>
    private string NieuweEtag() => $"\"{Etagvingerafdruk}{++_versie:0000}\"";

    /// <summary>Eén partitiesleutel: het klantdocument, het contract en de toegangen.</summary>
    /// <remarks>
    /// Dat ze in één object zitten is niet cosmetisch. De drie documenten delen de partitiesleutel,
    /// en dat is precies wat een klant aanmaken transactioneel maakt.
    /// </remarks>
    private sealed class Klantpartitie
    {
        public CustomerDocument? Klant { get; set; }

        public ContractDocument? Contract { get; set; }

        public Dictionary<string, AccessDocument> Toegangen { get; } = new(StringComparer.Ordinal);
    }
}
