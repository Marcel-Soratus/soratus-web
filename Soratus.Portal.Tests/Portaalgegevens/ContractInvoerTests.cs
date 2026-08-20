using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Portaalgegevens;

/// <summary>
/// De contractkaart en het klantformulier zoals een operator ze invult (§3.5, §3.9).
/// </summary>
/// <remarks>
/// <para>Er staan bewust weinig verplichte velden in. Een klant in onboarding heeft nog geen
/// contractnummer, en een verplicht veld levert dan een verzonnen nummer op — dat is erger dan een
/// streepje op de kaart. Wat hier wél wordt tegengehouden is een waarde die niet <em>kan</em>: een
/// datum in een andere vorm sorteert stil verkeerd, en een negatief tarief gaat de factuur in.</para>
///
/// <para>Deze tests staan op de invoer en niet op de opslag. Ze zijn daarmee het enige deel van de
/// schrijfkant dat zonder Cosmos te meten is, en juist het deel dat op de grens fout gaat.</para>
/// </remarks>
public class ContractInvoerTests
{
    // ── Het contract ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenLeegContractKlopt()
    {
        // De klant in onboarding: er is nog niets afgesproken, en dan hoort het formulier niet in de
        // weg te staan.
        Assert.Null(new ContractEdit().Validate());
    }

    [Fact]
    public void EenVolledigIngevuldContractKlopt() =>
        Assert.Null(Contract().Validate());

    [Theory]
    [InlineData("2026-02-01")]
    [InlineData("2026-12-31")]
    [InlineData("2024-02-29")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EenIngangsdatumInIsoVormOfLeegKlopt(string? datum) =>
        Assert.Null((Contract() with { StartsOn = datum }).Validate());

    [Theory]
    [InlineData("01-11-2025", "Nederlandse vorm — sorteert lexicografisch verkeerd")]
    [InlineData("2026-2-1", "geen vaste breedte")]
    [InlineData("2026/02/01", "schuine strepen")]
    [InlineData("1 februari 2026", "tekst")]
    [InlineData("2026-02-30", "die dag bestaat niet")]
    [InlineData("2025-02-29", "geen schrikkeljaar")]
    [InlineData("morgen", "tekst")]
    public void EenIngangsdatumInEenAndereVormWordtGeweigerd(string datum, string waarom)
    {
        // Cosmos vergelijkt tijdvelden als tekst. Op dd-MM-yyyy sorteert een lijst contracten stil
        // verkeerd, en "stil" is hier het hele probleem: er valt niets aan te zien.
        Assert.NotNull((Contract() with { StartsOn = datum }).Validate());
        Assert.False(string.IsNullOrWhiteSpace(waarom));
    }

    [Fact]
    public void EenIngangsdatumMetWitruimteEromheenKlopt() =>
        Assert.Null((Contract() with { StartsOn = " 2026-02-01 " }).Validate());

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public void EenNegatieveUrenbundelWordtGeweigerd(decimal uren) =>
        Assert.NotNull((Contract() with { BundledHours = uren }).Validate());

    [Theory]
    [InlineData(-1)]
    [InlineData(-125)]
    public void EenNegatiefUurtariefWordtGeweigerd(decimal tarief) =>
        Assert.NotNull((Contract() with { HourlyRate = tarief }).Validate());

    [Fact]
    public void EenBundelEnTariefVanNulKloppen()
    {
        // De interne beheerklant: niet gefactureerd, dus nul is de juiste waarde en geen ontbrekende.
        Assert.Null((Contract() with { BundledHours = 0m, HourlyRate = 0m }).Validate());
    }

    [Fact]
    public void EenContractZonderEnkelBedragKlopt()
    {
        // De klant die is aangemeld en waarvan de bedragen nog niet zijn afgesproken. Er is dan
        // niets te controleren, en een verplicht getalveld zou hier een verzonnen bedrag opleveren.
        var leeg = Contract() with
        {
            BundledHours = null,
            HourlyRate = null,
            AzureSurchargePercentage = null,
        };

        Assert.Null(leeg.Validate());
    }

    [Fact]
    public void NietVastgelegdEnNulZijnTweeVerschillendeAfspraken()
    {
        // Het onderscheid dat deze velden nullable maakt, vastgelegd op de plek waar het bestaat.
        // "Nul" is een afspraak die iemand heeft opgeschreven — geen bundel, niet doorbelast, geen
        // beheeropslag — en "niet vastgelegd" is het ontbreken van die afspraak. Bij het
        // opslagpercentage is dat verschil onze marge.
        //
        // Deze test compileert niet meer zodra iemand de velden terugbrengt naar decimal, en dat is
        // de bedoeling: dan is het verschil weg en hoort dat een bouwfout te zijn en niet een
        // stilzwijgende gedragsverandering.
        Assert.NotEqual(Contract() with { HourlyRate = null }, Contract() with { HourlyRate = 0m });
        Assert.NotEqual(Contract() with { BundledHours = null }, Contract() with { BundledHours = 0m });
        Assert.NotEqual(
            Contract() with { AzureSurchargePercentage = null },
            Contract() with { AzureSurchargePercentage = 0m });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(100)]
    public void EenOpslagpercentageBinnenDeGrenzenKlopt(decimal opslag) =>
        Assert.Null((Contract() with { AzureSurchargePercentage = opslag }).Validate());

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(1000)]
    public void EenOpslagpercentageBuitenDeGrenzenWordtGeweigerd(decimal opslag) =>
        Assert.NotNull((Contract() with { AzureSurchargePercentage = opslag }).Validate());

    [Fact]
    public void ErIsGeenEtagwaardeWaarmeeDeControleWordtOvergeslagen()
    {
        // De etag is een schrijfvoorwaarde en geen invoerveld: geen waarde ervan maakt het contract
        // ongeldig, en geen waarde ervan zet de gelijktijdigheidscontrole uit. null betekent "er was
        // nog geen contract" — dan wordt het aangemaakt, en was iemand anders net eerder, dan is dat
        // óók een conflict. Zie SchrijfuitkomstTests voor die kant.
        Assert.Null((Contract() with { BasedOnETag = null }).Validate());
        Assert.Null((Contract() with { BasedOnETag = "\"0x8DC1\"" }).Validate());
        Assert.Null((Contract() with { BasedOnETag = string.Empty }).Validate());
        Assert.Null((Contract() with { BasedOnETag = "*" }).Validate());
    }

    // ── De klantvelden ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenKlantwijzigingZonderNaamWordtGeweigerd() =>
        Assert.NotNull(new CustomerEdit { Name = "  " }.Validate());

    [Fact]
    public void EenKlantwijzigingMetNaamKlopt() =>
        Assert.Null(new CustomerEdit { Name = "Bakker Logistiek" }.Validate());

    [Fact]
    public void DeSlugStaatNietOpHetWijzigingsformulier()
    {
        // Hem wijzigen zou elk bestaand telemetriedocument stil laten verwijzen naar een klant die
        // niet meer zo heet. Moet hij toch anders, dan is dat een nieuwe klant en een migratie.
        Assert.Null(typeof(CustomerEdit).GetProperty("CustomerId"));
        Assert.Null(typeof(CustomerEdit).GetProperty("Id"));
        Assert.Null(typeof(CustomerEdit).GetProperty("Slug"));
    }

    // ── Een klant aanmaken ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenNieuweKlantMetAlleenEenIdEnEenNaamKlopt()
    {
        // Zonder contract en zonder toegangen: dat is de klant die vandaag wordt aangemeld en morgen
        // een contract krijgt.
        Assert.Null(new NewCustomerRequest { CustomerId = "bakker", Name = "Bakker Logistiek" }.Validate());
    }

    [Fact]
    public void EenNieuweKlantZonderNaamWordtGeweigerd() =>
        Assert.NotNull(new NewCustomerRequest { CustomerId = "bakker", Name = "  " }.Validate());

    [Fact]
    public void EenNieuweKlantMetEenOnbruikbaarIdWordtGeweigerd()
    {
        var melding = new NewCustomerRequest { CustomerId = "Bakker BV", Name = "Bakker" }.Validate();

        Assert.NotNull(melding);
        Assert.Contains("klant-id", melding, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeMeldingVanHetContractKomtDoorNaarHetFormulier()
    {
        // Eén melding voor het hele formulier, dus de melding van het onderliggende deel moet
        // doorkomen. Anders staat er "er is iets niet goed" bij een fout die de gebruiker had kunnen
        // herstellen.
        var verzoek = Nieuw() with { Contract = Contract() with { StartsOn = "01-11-2025" } };

        Assert.Contains("ingangsdatum", verzoek.Validate(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeMeldingVanEenToegangKomtDoorNaarHetFormulier()
    {
        var verzoek = Nieuw() with { Access = [new AccessGrant { Email = "geen-adres" }] };

        Assert.Contains("e-mailadres", verzoek.Validate(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HetzelfdeAdresTweeKeerInHetAanmaakformulierWordtGeweigerd()
    {
        // Twee documenten met dezelfde sleutel in één transactionele batch laat Cosmos mislukken op
        // de tweede bewerking. Dan is de hele klant niet aangemaakt om een reden die niet in het
        // formulier staat.
        var verzoek = Nieuw() with
        {
            Access =
            [
                new AccessGrant { Email = "Planning@Bakker.nl" },
                new AccessGrant { Email = "planning@bakker.nl", Role = PortalAccessRoles.Administrator },
            ],
        };

        var melding = verzoek.Validate();

        Assert.NotNull(melding);
        Assert.Contains("planning@bakker.nl", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void TweeVerschillendeAdressenInHetAanmaakformulierKloppen()
    {
        var verzoek = Nieuw() with
        {
            Access =
            [
                new AccessGrant { Email = "planning@bakker.nl" },
                new AccessGrant { Email = "directie@bakker.nl", Role = PortalAccessRoles.Administrator },
            ],
        };

        Assert.Null(verzoek.Validate());
    }

    [Fact]
    public void MeerToegangenDanInEenBatchPassenWordtGeweigerd()
    {
        // Cosmos laat honderd bewerkingen in één transactionele batch. Een grens die stil wordt
        // overschreden levert een 400 op de laatste regel — en dan is de klant niet aangemaakt
        // zonder dat het formulier weet waarom.
        var teveel = Nieuw() with
        {
            Access = [.. Enumerable.Range(0, 91).Select(i => new AccessGrant { Email = $"n{i}@bakker.nl" })],
        };

        Assert.NotNull(teveel.Validate());

        var netAan = Nieuw() with
        {
            Access = [.. Enumerable.Range(0, 90).Select(i => new AccessGrant { Email = $"n{i}@bakker.nl" })],
        };

        Assert.Null(netAan.Validate());
    }

    [Fact]
    public void DeGrensOpDeBatchLaatRuimteVoorDeKlantEnHetContract()
    {
        // De batch bestaat uit het klantdocument, eventueel het contract en één document per
        // toegang. De grens die de invoercontrole hanteert moet dus mét die twee erbij onder de
        // honderd blijven en niet er precies op. De grens wordt hier opgezocht in plaats van
        // ingetypt: een getal dat je overtypt uit de productiecode test zichzelf.
        var grens = Enumerable.Range(0, 120)
            .Last(aantal => (Nieuw() with
            {
                Contract = Contract(),
                Access = [.. Enumerable.Range(0, aantal).Select(i => new AccessGrant { Email = $"n{i}@bakker.nl" })],
            }).Validate() is null);

        Assert.True(
            grens + 2 <= 100,
            $"De invoercontrole laat {grens} toegangen door. Met het klantdocument en het contract " +
            $"erbij zijn dat {grens + 2} bewerkingen in één transactionele batch, en Cosmos laat er " +
            "honderd. Een volle batch mislukt dan op de laatste bewerking, en dan is de klant niet " +
            "aangemaakt om een reden die niet in het formulier staat.");
    }

    private static NewCustomerRequest Nieuw() =>
        new() { CustomerId = "bakker", Name = "Bakker Logistiek" };

    private static ContractEdit Contract() => new()
    {
        Number = "SOR-2026-003",
        Type = "Agent-abonnement + doorontwikkeling",
        StartsOn = "2026-02-01",
        Term = "24 maanden",
        NoticePeriod = "2 maanden",
        Sla = "Reactie 4 werkuren · herstel 1 werkdag",
        BundledHours = 8m,
        HourlyRate = 125m,
        Indexation = "CBS-index per 1 januari",
        Contact = "Jan Bakker",
        ManagedBy = "Soratus — accountteam",
        AzureSurchargePercentage = 8m,
    };
}
