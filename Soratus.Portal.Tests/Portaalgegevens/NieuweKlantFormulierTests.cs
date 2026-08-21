using Soratus.Portal.Components.Pages;
using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Portaalgegevens;

/// <summary>
/// Het formulier "Nieuwe klant" (§3.9): welke meldingen bij één veld horen, en wat er van de
/// ingevulde tekst in de opslag terechtkomt.
/// </summary>
/// <remarks>
/// <para>Dit formulier gaat als één POST naar de server en daarna als één transactionele batch naar
/// Cosmos. Er is dus precies één moment waarop de tekst uit de browser een klant, een contract en een
/// reeks toegangen wordt, en dat moment staat hier onder de loep. Wat er daarna nog gebeurt is
/// <see cref="NewCustomerRequest.Validate"/>, en die staat in <see cref="ContractInvoerTests"/>.</para>
///
/// <para>De scheiding tussen de twee is de moeite waard om vast te leggen:
/// <see cref="NewCustomerForm.FieldErrors"/> doet alleen wat aan één veld hangt, zodat de melding
/// onder dát veld kan komen; wat over het geheel gaat — een dubbel adres, een ontbrekende naam —
/// komt uit de opslag en staat als blok boven de knop. Twee plekken, en geen twee definities van
/// "klopt dit".</para>
/// </remarks>
public class NieuweKlantFormulierTests
{
    // ── Bedragen: leeg blijft leeg, nul blijft nul ──────────────────────────────────────────────

    [Fact]
    public void EenLeegTariefveldLegtGeenBedragVast()
    {
        // De bevinding waar deze tests uit komen. Een operator die bij het aanmaken van een klant het
        // tarief nog niet weet, legde met dit formulier uurTarief: 0 vast — een bedrag dat hij niet
        // heeft ingetypt, dat als afspraak in de opslag staat en dat in een berekening als nul
        // meetelt. Een klant in onboarding hoort geen tarief te hebben, en niet het tarief nul.
        var contract = Formulier(f => f.ContractNumber = "SOR-2026-0199").ToRequest().Contract;

        Assert.NotNull(contract);
        Assert.Null(contract.HourlyRate);
        Assert.Null(contract.BundledHours);
        Assert.Null(contract.AzureSurchargePercentage);
    }

    [Fact]
    public void EenTariefveldMetNulLegtNulVast()
    {
        // De andere richting, en zonder deze zou de test hierboven ook te halen zijn door elk bedrag
        // weg te gooien. Nul is een afspraak: uren buiten de bundel worden niet doorbelast.
        var contract = Formulier(f =>
        {
            f.HourlyRate = "0";
            f.BundledHours = "0";
            f.AzureSurcharge = "0";
        }).ToRequest().Contract;

        Assert.NotNull(contract);
        Assert.Equal(0m, contract.HourlyRate);
        Assert.Equal(0m, contract.BundledHours);
        Assert.Equal(0m, contract.AzureSurchargePercentage);
    }

    [Fact]
    public void EenTariefMetEenKommaKomtAlsBedragDoor()
    {
        var contract = Formulier(f => f.HourlyRate = "137,50").ToRequest().Contract;

        Assert.NotNull(contract);
        Assert.Equal(137.50m, contract.HourlyRate);
    }

    [Theory]
    [InlineData(nameof(NewCustomerForm.HourlyRate))]
    [InlineData(nameof(NewCustomerForm.BundledHours))]
    [InlineData(nameof(NewCustomerForm.AzureSurcharge))]
    public void EenScheidingstekenVoorDuizendenIsEenMeldingOnderDatVeld(string veld)
    {
        // Onder dát veld, en niet als blok boven de knop: de operator hoort te zien welk van de drie
        // getallen hij moet herstellen. "1.250,50" wordt geweigerd in plaats van stil verkeerd
        // gelezen — zie ContractTextTests.
        var formulier = new NewCustomerForm { CustomerId = "bakker", Name = "Bakker Logistiek" };

        typeof(NewCustomerForm).GetProperty(veld)!.SetValue(formulier, "1.250,50");

        var meldingen = formulier.FieldErrors();

        Assert.True(
            meldingen.ContainsKey(veld),
            $"Het veld {veld} bevat \"1.250,50\" en dat is niet als getal te lezen, maar er staat " +
            $"geen melding onder dat veld. De sleutels die er wel zijn: " +
            $"{string.Join(", ", meldingen.Keys)}.");
    }

    // ── Het contract als geheel ─────────────────────────────────────────────────────────────────

    [Fact]
    public void EenFormulierZonderEnkelContractveldLegtGeenContractAan()
    {
        // null betekent: leg het contract later vast. Dat is beter dan een contractdocument met elf
        // lege velden, want dan zegt het contractscherm "vastgelegd" over iets wat niet bestaat.
        Assert.Null(Formulier().ToRequest().Contract);
    }

    [Fact]
    public void EenFormulierMetAlleenEenOpslagpercentageLegtWelEenContractAan()
    {
        // Eén ingevuld veld is een contract in wording, ook als het het veld is dat de klant nooit
        // ziet. Zou dit veld niet meetellen, dan verdwijnt de marge die de operator net heeft
        // ingetypt zonder melding.
        var contract = Formulier(f => f.AzureSurcharge = "8,75").ToRequest().Contract;

        Assert.NotNull(contract);
        Assert.Equal(8.75m, contract.AzureSurchargePercentage);
    }

    [Fact]
    public void EenNieuwContractGaatZonderVersieDeOpslagIn()
    {
        // Er is nog geen contract, dus er is niets om op te baseren. Dat is geen ontbrekende
        // controle: de batch schrijft met een aanleg, en wie net eerder was levert een conflict op.
        var contract = Formulier(f => f.ContractNumber = "SOR-2026-0199").ToRequest().Contract;

        Assert.NotNull(contract);
        Assert.Null(contract.BasedOnETag);
    }

    [Fact]
    public void WitruimteInEenVeldWordtGeenWaarde()
    {
        var verzoek = Formulier(f =>
        {
            f.CustomerId = "  bakker  ";
            f.Name = "  Bakker Logistiek  ";
            f.Environment = "   ";
            f.ContractNumber = "  SOR-2026-0199  ";
        }).ToRequest();

        Assert.Equal("bakker", verzoek.CustomerId);
        Assert.Equal("Bakker Logistiek", verzoek.Name);
        Assert.Null(verzoek.Environment);
        Assert.Equal("SOR-2026-0199", verzoek.Contract!.Number);
    }

    // ── De klantvelden ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenOnbruikbaarKlantIdIsEenMeldingOnderDatVeld()
    {
        // De slug komt in de URL, in de documentsleutels én in de telemetrie van elke agent terecht.
        // Een hoofdletter of een spatie hoort hier tegengehouden te worden en niet in de opslag te
        // belanden.
        var meldingen = Formulier(f => f.CustomerId = "Bakker BV").FieldErrors();

        Assert.True(meldingen.ContainsKey(nameof(NewCustomerForm.CustomerId)));
    }

    [Fact]
    public void EenIngevuldFormulierHeeftGeenEnkeleVeldmelding()
    {
        // De tegenhanger: een controle die altijd iets vindt is even nutteloos als een die nooit iets
        // vindt.
        var meldingen = Formulier(f =>
        {
            f.ContractNumber = "SOR-2026-0199";
            f.HourlyRate = "137,50";
            f.BundledHours = "12";
            f.AzureSurcharge = "8,75";
            f.Access1.Email = "planning@bakker.nl";
            f.Access1.Name = "Jan Bakker";
        }).FieldErrors();

        Assert.Empty(meldingen);
    }

    [Fact]
    public void DeInterneBeheeromgevingIsEenKeuzeEnGeenStandaard()
    {
        // "Intern" bepaalt of het contract als niet-doorbelast wordt gelezen. Wie niets kiest hoort
        // een gewone klant te krijgen — dat is de veilige kant, want een klant die per ongeluk
        // intern is wordt niet gefactureerd.
        Assert.False(Formulier().ToRequest().IsInternal);

        Assert.True(
            Formulier(f => f.EnvironmentKind = NewCustomerForm.InternalEnvironment)
                .ToRequest().IsInternal);

        Assert.False(
            Formulier(f => f.EnvironmentKind = "iets-anders").ToRequest().IsInternal);
    }

    // ── De toegangsregels ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenLegeToegangsregelWordtOvergeslagen()
    {
        // Drie vaste regels op het formulier, en een klant met één contactpersoon hoort geen twee
        // lege toegangen te krijgen.
        var verzoek = Formulier(f => f.Access1.Email = "planning@bakker.nl").ToRequest();

        Assert.Single(verzoek.Access);
        Assert.Equal("planning@bakker.nl", verzoek.Access[0].Email);
    }

    [Fact]
    public void EenNaamZonderAdresIsEenVergetenVeldEnGeenKeuze()
    {
        // Stil overslaan zou de klant aanmaken zonder de persoon die de operator net intypte.
        var formulier = Formulier(f => f.Access2.Name = "Jan Bakker");

        var meldingen = formulier.FieldErrors();

        Assert.True(meldingen.ContainsKey(NewCustomerForm.EmailField(2)));
        Assert.Contains("e-mailadres", meldingen[NewCustomerForm.EmailField(2)], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EenOngeldigAdresIsEenMeldingOnderDeRegelWaarHetStaat()
    {
        // Onder regel drie en niet onder regel één: met drie identieke labels op het scherm is de
        // regel waar het staat het enige dat de melding bruikbaar maakt.
        var meldingen = Formulier(f => f.Access3.Email = "geen-adres").FieldErrors();

        Assert.True(meldingen.ContainsKey(NewCustomerForm.EmailField(3)));
        Assert.False(meldingen.ContainsKey(NewCustomerForm.EmailField(1)));
    }

    [Fact]
    public void EenOnbekendeAanduidingWordtLezerEnGeenBeheerder()
    {
        // Een waarde die uit onze eigen keuzelijst hoort te komen en dat niet doet, is hier de
        // mildste keuze waard — maar dan wel de minst bevoorrechte. Wie het formulier omzeilt met
        // een verzonnen aanduiding krijgt geen contactpersoonsrol, en wie er "Soratus-operator" in
        // zet wordt dat niet: operator worden gebeurt in Entra.
        var verzoek = Formulier(f =>
        {
            f.Access1.Email = "planning@bakker.nl";
            f.Access1.Designation = "Soratus-operator";
        }).ToRequest();

        Assert.Equal(PortalAccessRoles.Reader, verzoek.Access[0].Role);
    }

    [Fact]
    public void DeStandaardaanduidingIsLezerEnNietDeEersteUitDeLijst()
    {
        // "Beheerder klant" staat bovenaan in de keuzelijst omdat §3.5 hem zo noemt. Wie niets kiest
        // hoort dat niet stil te worden.
        Assert.Equal(PortalAccessRoles.Reader, new NewCustomerForm.AccessRow().Designation);
        Assert.Equal(PortalAccessRoles.Administrator, PortalAccessRoles.All[0]);
    }

    [Fact]
    public void HetPadVanEenToegangsveldIsOpEenPlekVastgelegd()
    {
        // Dit pad is zowel het name-attribuut in de markup als de sleutel van de melding. Zouden die
        // twee elk hun eigen tekenreeks bouwen, dan komt de melding onder een veld dat niet bestaat —
        // of erger, komt de invoer nooit aan.
        Assert.Equal("Access2.Email", NewCustomerForm.EmailField(2));
        Assert.Equal("Access3.Designation", NewCustomerForm.AccessField(3, "Designation"));
    }

    [Fact]
    public void ElkeRegelOpHetFormulierIsEenEigenRegel()
    {
        // Zou Row() twee nummers naar hetzelfde object wijzen, dan overschrijft de tweede persoon de
        // eerste en verdwijnt er een toegang zonder melding.
        var formulier = new NewCustomerForm();

        var regels = Enumerable.Range(1, NewCustomerForm.AccessRowCount)
            .Select(formulier.Row)
            .ToArray();

        Assert.Equal(NewCustomerForm.AccessRowCount, regels.Distinct().Count());
    }

    // ── De Azure-scope: leeg mag, onbruikbaar niet ─────────────────────────────────────────────

    [Fact]
    public void EenLeegScopeveldLevertGeenScopeEnGeenMelding()
    {
        // "Niet ingericht" is een geldige toestand — punt 15 op de plek waar hij de meting raakt. Een
        // verplicht veld zou hier een verzonnen pad opleveren, en een verzonnen pad geeft HTTP 200 met
        // nul rijen: het ziet uit als een antwoord.
        var formulier = Formulier();

        Assert.DoesNotContain(nameof(NewCustomerForm.AzureScope), formulier.FieldErrors().Keys);
        Assert.Null(formulier.ToRequest().AzureScope);
        Assert.Null(formulier.ToRequest().Validate());
    }

    [Fact]
    public void EenIngevuldePadvormKomtOngewijzigdInHetVerzoek()
    {
        // De spiegel. Zonder deze test mag het formulier het veld weggooien en blijft de test hierboven
        // groen — en dan is er geen klant meer in te richten voor de kostenmeting.
        const string scope = "/subscriptions/501a66d2-de54-4d4f-9f7c-1fbb55bec17f/resourceGroups/MBV";

        var verzoek = Formulier(f => f.AzureScope = $"  {scope}  ").ToRequest();

        // Getrimd maar niet herschreven: de schrijfwijze van de resourcegroepnaam is die van de
        // operator, want deze tekenreeks komt op het scherm terug als "bevraagd: …".
        Assert.Equal(scope, verzoek.AzureScope);
        Assert.Null(verzoek.Validate());
    }

    [Fact]
    public void EenOnbruikbareScopeMeldtZichOnderZijnEigenVeld()
    {
        // De weergavetekst van de bestaande klanten hoort hier niet door te komen. "501a66d2-… mbv"
        // staat vandaag bij de echte klant in envFull, en dát is precies de waarde die iemand hier
        // per ongeluk in plakt.
        var formulier = Formulier(f => f.AzureScope = "501a66d2-de54-4d4f-9f7c-1fbb55bec17f mbv");

        Assert.Contains(nameof(NewCustomerForm.AzureScope), formulier.FieldErrors().Keys);

        // En de opslag weigert hem óók, want dat is de controle die telt voor een aanroeper die het
        // formulier omzeilt. Twee plekken, dezelfde functie.
        Assert.NotNull(formulier.ToRequest().Validate());
    }

    /// <summary>
    /// Een formulier met alleen de twee verplichte velden gevuld, en daarna wat de test nodig heeft.
    /// </summary>
    /// <param name="vul">Wat er nog bij moet.</param>
    /// <returns>Het formulier.</returns>
    private static NewCustomerForm Formulier(Action<NewCustomerForm>? vul = null)
    {
        var formulier = new NewCustomerForm { CustomerId = "bakker", Name = "Bakker Logistiek" };

        vul?.Invoke(formulier);

        return formulier;
    }
}
