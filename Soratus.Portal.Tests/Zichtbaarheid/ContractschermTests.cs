using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Soratus.Portal.Components.Pages;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// Het contractscherm (§3.5) en het aanmaakformulier (§3.9), per rol en in beide richtingen.
/// </summary>
/// <remarks>
/// <para><strong>Elke test heeft zijn spiegel.</strong> Een test die alleen afwezigheid controleert
/// blijft groen als de pagina stukgaat of leeg blijft — dan is er niets, dus ook niet het verboden
/// veld. Waar de spiegel op het scherm bestaat staat hij hier als tweede test; waar hij niet op het
/// scherm bestaat (een etag hoort in géén van beide weergaven te staan) staat er wat er in plaats
/// daarvan wordt bewezen.</para>
///
/// <para><strong>Dit is geen beveiliging.</strong> Zie <see cref="Portaalrendertest"/>: de echte
/// grens ligt in het typesysteem — de klant krijgt <c>CustomerContractView</c> en dat type heeft het
/// opslagpercentage niet — en die kant staat in <c>ContractZichtbaarheidTests</c>, op typeniveau.
/// Wat hier wordt getest is het vangnet daaronder: dat de gegevens die er wél op staan ook
/// werkelijk op het scherm belanden, en dat de operator-only waarden er in geen enkele tooltip
/// alsnog in zijn geslopen.</para>
/// </remarks>
public class ContractschermTests : Portaalrendertest
{
    private static Type Contractpagina =>
        Paginaverzameling.MetRoute("/klant/{Slug}/contract")
        ?? throw new InvalidOperationException(
            "Er staat geen pagina op route '/klant/{Slug}/contract'. Is de route hernoemd, dan " +
            "hoort deze test mee te verhuizen — niet te verdwijnen.");

    private static Type Aanmaakpagina =>
        Paginaverzameling.MetRoute("/klanten/nieuw")
        ?? throw new InvalidOperationException(
            "Er staat geen pagina op route '/klanten/nieuw'. Is de route hernoemd, dan hoort deze " +
            "test mee te verhuizen — niet te verdwijnen.");

    /// <summary>De twee nieuwe schermen van fase 2, elk voor beide rollen.</summary>
    public static TheoryData<Type, bool> SchermenPerRol
    {
        get
        {
            var data = new TheoryData<Type, bool>();

            foreach (var pagina in new[] { Contractpagina, Aanmaakpagina })
            {
                data.Add(pagina, false);
                data.Add(pagina, true);
            }

            return data;
        }
    }

    // ── De contractkaart: lezen tegenover bewerken ──────────────────────────────────────────────

    [Fact]
    public void EenKlantLeestZijnContractAlsPlatteTekstEnNietAlsUitgegrijsdFormulier()
    {
        // §8 is hier expliciet over, en het is de reden dat FieldMode.ReadOnly bestaat: een
        // uitgegrijsd veld zegt "je mag dit niet", platte tekst zegt "dit is een feit". Voor een
        // klant is zijn contract een feit.
        MeldKlantAan();

        var cut = RenderPagina(Contractpagina);

        Assert.Contains(Vasteportaalopslag.Contractnummer, cut.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll(".field--readonly"));

        Assert.True(
            cut.FindAll("input").Count == 0 && cut.FindAll("form").Count == 0,
            "Het contractscherm van een klant bevat een invoerveld of een formulier. §2 geeft de " +
            "klant op contract en toegang lezen; er valt hier niets te wijzigen, en dan hoort er " +
            "geen vak te staan waarin getypt kan worden — ook niet een uitgegrijsd vak. Zie " +
            "FieldMode.ReadOnly.");
    }

    [Fact]
    public void EenOperatorKrijgtHetzelfdeContractAlsBewerkbaarFormulier()
    {
        // De spiegel, en zonder deze zegt de test hierboven niets: een scherm dat helemaal geen
        // formulier meer heeft is ook read-only, maar dan is de contractkaart stuk.
        MeldOperatorAan();

        var cut = RenderPagina(Contractpagina);

        Assert.NotEmpty(cut.FindAll("form"));
        Assert.Contains(
            $"value=\"{Vasteportaalopslag.Contractnummer}\"",
            cut.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietHetOpslagpercentageOpDeAzureKostenNiet()
    {
        // §2: "Facturatie: Azure per dienst + beheeropslag" staat voor de klant op nee. Dit is onze
        // marge; die hoort niet op het scherm van degene die hem betaalt.
        MeldKlantAan();

        var markup = RenderPagina(Contractpagina).Markup;

        Assert.DoesNotContain("8,75", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Opslag op Azure", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EenOperatorZietHetOpslagpercentageWel()
    {
        // De spiegel. Zou het percentage nergens meer staan, dan blijft de test hierboven groen
        // terwijl niemand de marge meer kan invullen — dat is geen winst in zichtbaarheid maar
        // verlies van een functie. Bij errorType (§14) is precies dat gebeurd.
        MeldOperatorAan();

        var markup = RenderPagina(Contractpagina).Markup;

        Assert.Contains("8,75", markup, StringComparison.Ordinal);
        Assert.Contains("Opslag op Azure-kosten", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietZijnEigenUurtariefEnBundelWel()
    {
        // Niet alles is operator-only, en een test die alleen verbiedt zou dat laten verschuiven.
        // §3.5 zet uurtarief, bundel en indexatie op de contractkaart, en §3.7 rekent de extra uren
        // met datzelfde tarief voor. Een klant die zijn eigen tarief niet mag zien kan zijn factuur
        // niet controleren.
        MeldKlantAan();

        var markup = RenderPagina(Contractpagina).Markup;

        Assert.Contains("137,50", markup, StringComparison.Ordinal);
        Assert.Contains("12 uur per maand", markup, StringComparison.Ordinal);
        Assert.Contains("CBS-index per 1 januari", markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EenSchrijfvoorwaardeStaatInGeenEnkeleWeergaveVanHetContract(bool operatorrol)
    {
        // Een etag is een schrijfvoorwaarde en geen gegeven. De klant schrijft niet, dus hij heeft
        // er niets aan; en ook op het operatorscherm hoort hij niet in de markup te staan, want dan
        // staat hij in de paginabron. Het eiland houdt hem in zijn eigen toestand vast — dát hij
        // meegaat bij het bewaren staat in ContracteilandTests, waar de schrijfactie te zien is.
        MeldAanAls(operatorrol);

        var markup = RenderPagina(Contractpagina).Markup;

        Assert.DoesNotContain(Vasteportaalopslag.Etagvingerafdruk, markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantZietDeSubscriptionEnDeResourceGroupNietOpZijnContract()
    {
        // §2 maakt infrastructuurdetails operator-only. De korte aanduiding ("West-Europa") mag wél
        // — dat is volgens fase 0 het enige omgevingsveld dat een klant te zien krijgt — en de
        // grens loopt tussen "in welke regio staat mijn omgeving" en "in welke subscription".
        MeldKlantAan();

        var markup = RenderPagina(Contractpagina).Markup;

        Assert.DoesNotContain("rg-acme-prod", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("sub-soratus", markup, StringComparison.Ordinal);
        Assert.Contains("West-Europa", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeOperatorweergaveVanHetContractDraagtDeVolledigeOmgevingWel()
    {
        // De spiegel van de test hierboven, en hij staat met opzet op het viewmodel en niet op de
        // markup: er is vandaag geen contractscherm dat dit veld rendert. Zonder deze test zou de
        // klanttest ook groen blijven nadat de subscription overal was verdwenen, en dan meet hij
        // niet de scheiding maar de sloop. Zie het rapport bij deze bevinding.
        var weergave = await new VasteContractweergaven(Opslag)
            .BuildContractAsync(await Weergavelaag.Schrijfscope());

        Assert.Equal(Vasteportaalopslag.Omgevingsdetail, weergave.EnvironmentDetail);
    }

    [Fact]
    public void EenKlantZietNietWieHetContractWanneerHeeftGewijzigd()
    {
        // Wanneer wij het contract hebben aangepast is onze administratie, en de naam erbij is die
        // van een Soratus-medewerker. De klant heeft het contract, niet ons wijzigingslog.
        MeldKlantAan();

        var markup = RenderPagina(Contractpagina).Markup;

        Assert.DoesNotContain(Vasteportaalopslag.Wijzigdehet, markup, StringComparison.Ordinal);
        Assert.DoesNotContain("gewijzigd", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EenOperatorZietWelWieHetContractWanneerHeeftGewijzigd()
    {
        MeldOperatorAan();

        var markup = RenderPagina(Contractpagina).Markup;

        Assert.Contains(Vasteportaalopslag.Wijzigdehet, markup, StringComparison.Ordinal);
        Assert.Contains("gewijzigd", markup, StringComparison.Ordinal);
    }

    // ── Geen contract: een gewone toestand en geen storing ──────────────────────────────────────

    [Fact]
    public void EenKlantZonderContractLeestDatErNogNietsIsVastgelegd()
    {
        // Een kaart met elf streepjes suggereert dat er gegevens ontbreken, terwijl er nog niets ís.
        // Een klant in onboarding hoort dat verschil te lezen.
        Opslag = new Vasteportaalopslag(zonderContract: true);

        MeldKlantAan();

        var markup = RenderPagina(Contractpagina).Markup;

        Assert.Contains("nog geen contract", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Vasteportaalopslag.Contractnummer, markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOperatorKanHetContractVanEenKlantZonderContractVastleggen()
    {
        // De spiegel: bij de klant staat de lege staat, bij de operator een leeg formulier met een
        // knop die "vastleggen" zegt in plaats van "bewaren". Dat is de klant in onboarding, en het
        // is de reden dat het schrijfrecht niet aan een ingerichte telemetrie-opslag hangt.
        Opslag = new Vasteportaalopslag(zonderContract: true);

        MeldOperatorAan();

        var markup = RenderPagina(Contractpagina).Markup;

        Assert.Contains("Contract vastleggen", markup, StringComparison.Ordinal);
        Assert.Contains("nog niet vastgelegd", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOperatorLeestDatDeEenmaligeMigratieVoorDezeKlantNogNietHeeftGelopen()
    {
        // Stil laten zou de operator laten denken dat het een gewone klant is, waarna zijn eerste
        // wijziging het klantdocument alsnog aanmaakt. Dat werkt, maar hij hoort te weten wat er
        // gebeurt — en het verklaart waarom er geen wijzigingsgeschiedenis is.
        Opslag = new Vasteportaalopslag(alleenUitConfiguratie: true);

        MeldOperatorAan();

        var markup = RenderPagina(Contractpagina).Markup;

        Assert.Contains("migratie", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EenKlantLeestNietsOverOnzeMigratieOfOnzeInrichting()
    {
        // De spiegel van de vorige test, en de reden dat IsFromConfigurationOnly niet op het
        // klanttype staat: dat de migratie nog niet heeft gelopen is onze inrichting.
        Opslag = new Vasteportaalopslag(alleenUitConfiguratie: true);

        MeldKlantAan();

        var markup = RenderPagina(Contractpagina).Markup;

        Assert.DoesNotContain("migratie", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("configuratie", markup, StringComparison.OrdinalIgnoreCase);
    }

    // ── Het toegangsoverzicht ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EenKlantZietWieErNamensHemToegangHeeftMaarNietWieDatHeeftUitgedeeld()
    {
        // Wie de toegang heeft gegeven is onze administratie; wie er staat is die van de klant.
        MeldKlantAan();

        var cut = RenderPagina(Contractpagina);

        Assert.Contains(Vasteportaalopslag.Beheerderadres, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("3 personen", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(Vasteportaalopslag.Wijzigdehet, cut.Markup, StringComparison.Ordinal);

        // Vier kolommen: adres, naam, aanduiding, en of aanmelden werkt. Niet wanneer het is
        // vastgelegd (dat is onze administratie) en niet de actiekolom (intrekken kan hij niet).
        Assert.Equal(4, cut.FindAll(".data-row-head .data-cell").Count);
    }

    [Fact]
    public void EenOperatorZietErTweeKolommenBijWanneerEnDeIntrekactie()
    {
        // De spiegel van de kolomtelling hierboven. Zonder deze kant zou die telling ook kloppen
        // nadat de operatorkolommen zijn weggevallen.
        MeldOperatorAan();

        var cut = RenderPagina(Contractpagina);

        Assert.Equal(6, cut.FindAll(".data-row-head .data-cell").Count);
        Assert.Contains(Vasteportaalopslag.Wijzigdehet, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantKanGeenToegangIntrekkenEnKrijgtDaarGeenKnopVoor()
    {
        // Alleen Soratus deelt toegang uit — het besluit op de openstaande vraag uit §9. Er staat
        // dus geen uitgegrijsde knop maar een melding: een knop die niets doet belooft dat het wél
        // kan.
        MeldKlantAan();

        var cut = RenderPagina(Contractpagina);

        Assert.Empty(cut.FindAll("button"));
        Assert.DoesNotContain("Intrekken", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Soratus", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOperatorKanEenToegangWelIntrekken()
    {
        MeldOperatorAan();

        var cut = RenderPagina(Contractpagina);

        Assert.Equal(3, cut.FindAll(".row-actions button").Count);
    }

    [Theory]
    [MemberData(nameof(SchermenPerRol))]
    public void DeAanduidingBinnenEenKlantHeetOpGeenEnkelSchermEenRol(Type pagina, bool operatorrol)
    {
        // Twee aanduidingen met identiek recht: "Beheerder klant" en "Lezer" mogen precies
        // hetzelfde — lezen. Het woord rol belooft rechten, en zo'n belofte belandt op een dag als
        // aanname in code: een if op de naam die iets toestaat wat er nooit was.
        //
        // Deze test staat op het gedrag en niet op een tekst. Hij zoekt het woord in álles wat er
        // op het scherm staat: kolomkoppen, uitleg onder een veld, tooltips en de meldingen. Wordt
        // een melding herschreven, dan blijft hij meten; komt er een nieuwe zin bij met "rol" erin,
        // dan valt dat op.
        //
        // Let op wat er níet wordt gemeten: de app-rol uit Entra ID. Dat ís een rol en die mag zo
        // heten. Hij staat in de sticky header (PortalHeader) en niet op deze pagina's, en de
        // uitleg bij een Entra-toestand die het woord gebruikt komt pas in beeld zodra het portaal
        // leesrecht op Entra heeft — zie VasteContractweergaven.
        MeldAanAls(operatorrol);

        var markup = RenderPagina(pagina).Markup;

        Assert.DoesNotMatch(@"(?i)\brol(len)?\b", markup);
    }

    // ── Een klant aanmaken (§3.9): operator-only in zijn geheel ─────────────────────────────────

    [Fact]
    public void EenKlantZietOpDeAanmaakpaginaHelemaalNiets()
    {
        // Niet "de gevoelige blokken vallen weg" maar "de pagina blijft leeg" — ook de PageTitle
        // staat binnen de rolcontrole. "Nieuwe klant" is een woord dat een klantgebruiker nergens
        // mag zien (§2), en wat niet wordt gerenderd kan ook niet lekken.
        MeldKlantAan();

        var markup = RenderPagina(Aanmaakpagina).Markup;

        Assert.True(
            string.IsNullOrWhiteSpace(markup),
            "De pagina /klanten/nieuw rendert iets voor een klantgebruiker:\n" + markup + "\n\n" +
            "Klantbeheer is operator-only. Het beleid staat op de pagina en niet in de markup: dan " +
            "hoort de hele pagina dicht te zitten in plaats van dat er blokken wegvallen.");
    }

    [Fact]
    public void EenKlantKrijgtOpDeAanmaakpaginaOokGeenPaginatitel()
    {
        // Deze test bestaat omdat de vorige hem niet dekt, en dat bleek pas door de pagina
        // tijdelijk stuk te maken: een <PageTitle> buiten de rolcontrole zetten maakte geen enkele
        // test rood. Een PageTitle rendert namelijk niet in de markup van de pagina zelf maar in de
        // HeadOutlet, dus het woord "Nieuwe klant" zou in de titelbalk van de klant staan zonder
        // dat er iets van in cut.Markup te zien is.
        //
        // Vandaar dat hier naar het component wordt gekeken en niet naar de markup. Het is de
        // scherpste vorm van de afspraak dat deze pagina voor een klant helemaal niets rendert —
        // ook geen titel.
        MeldKlantAan();

        var cut = RenderPagina(Aanmaakpagina);

        Assert.Empty(cut.FindComponents<PageTitle>());
    }

    [Fact]
    public void EenOperatorKrijgtOpDeAanmaakpaginaWelEenPaginatitel()
    {
        // De spiegel: zonder deze zou de test hierboven ook groen zijn nadat de titel helemaal is
        // weggehaald, en dan heeft elk tabblad van de operator dezelfde naam.
        MeldOperatorAan();

        var cut = RenderPagina(Aanmaakpagina);

        Assert.NotEmpty(cut.FindComponents<PageTitle>());
    }

    [Fact]
    public void EenOperatorKrijgtHetAanmaakformulierMetAlleVeldenVanParagraaf39()
    {
        // De spiegel, en hij is hier het halve werk: een pagina die stukgaat rendert ook niets, en
        // dan is de test hierboven groen om de verkeerde reden.
        MeldOperatorAan();

        var cut = RenderPagina(Aanmaakpagina);

        Assert.Contains("Nieuwe klant", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Klant-id", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Opslag op Azure-kosten", cut.Markup, StringComparison.Ordinal);

        // Drie vaste toegangsregels, elk met een adres, een naam en een aanduiding. Geen
        // "+ regel"-knop: die kan zonder eiland niet bestaan, en een knop die niets doet is
        // verboden. De labels dragen het regelnummer, want drie keer "E-mailadres" onder elkaar is
        // voor een schermlezer drie keer hetzelfde veld.
        Assert.Equal(3, cut.FindAll("input[type=email]").Count);
        Assert.Equal(3, Labels(cut, "E-mailadres persoon"));
        Assert.Equal(3, Labels(cut, "Aanduiding persoon"));
    }

    [Fact]
    public void ElkeVeldmeldingVanHetAanmaakformulierHoortBijEenVeldDatErOokStaat()
    {
        // Model binding op static SSR bindt met de naam van de [SupplyParameterFromForm]-eigenschap
        // als voorvoegsel, en de sleutel van een veldmelding is het pad daarachter. Lopen die twee
        // uit elkaar, dan komt de melding onder een veld dat niet bestaat — of erger, komt de invoer
        // nooit aan en verdwijnt hij stil bij het versturen.
        //
        // Het voorvoegsel komt hier uit het attribuut en niet als letterlijke tekst: de naam van die
        // eigenschap ís de afspraak, en hem overtypen zou de test langs een hernoeming laten gaan.
        var voorvoegsel = Aanmaakpagina
            .GetProperties()
            .Single(p => p.GetCustomAttribute<SupplyParameterFromFormAttribute>() is not null)
            .Name;

        var fout = new NewCustomerForm
        {
            CustomerId = "Bakker BV",
            Name = "Bakker Logistiek",
            BundledHours = "twaalf",
            HourlyRate = "1.250,50",
            AzureSurcharge = "acht",
        };

        fout.Access2.Name = "Jan Bakker";

        var sleutels = fout.FieldErrors().Keys;

        Assert.NotEmpty(sleutels);

        MeldOperatorAan();

        var markup = RenderPagina(Aanmaakpagina).Markup;

        foreach (var sleutel in sleutels)
        {
            Assert.Contains($"name=\"{voorvoegsel}.{sleutel}\"", markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HetAanmaakformulierBenoemtWatHetNietDoet()
    {
        // De Azure-omgeving aanmaken en de mensen in Entra ID uitnodigen blijven handwerk. Zonder
        // die tweede stap komt er van deze klant niemand binnen, en dat is precies het soort halve
        // toestand dat leesbaar hoort te zijn in plaats van stil.
        MeldOperatorAan();

        var markup = RenderPagina(Aanmaakpagina).Markup;

        Assert.Contains("Entra ID", markup, StringComparison.Ordinal);
        Assert.Contains("handwerk", markup, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Hoeveel veldlabels er met deze tekst beginnen.</summary>
    /// <param name="cut">De gerenderde pagina.</param>
    /// <param name="begin">Het begin van het label, bijvoorbeeld <c>Aanduiding persoon</c>.</param>
    /// <returns>Het aantal.</returns>
    private static int Labels(IRenderedComponent<Bunit.Rendering.ContainerFragment> cut, string begin) =>
        cut.FindAll("label").Count(l => l.TextContent.Contains(begin, StringComparison.Ordinal));

    /// <summary>Meldt de rol aan die de theorie vraagt.</summary>
    /// <param name="operatorrol"><c>true</c> voor een operator, <c>false</c> voor een klant.</param>
    private void MeldAanAls(bool operatorrol)
    {
        if (operatorrol)
        {
            MeldOperatorAan();
        }
        else
        {
            MeldKlantAan();
        }
    }
}
