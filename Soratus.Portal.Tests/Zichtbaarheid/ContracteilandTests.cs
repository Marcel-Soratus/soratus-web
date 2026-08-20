using AngleSharp.Dom;
using Bunit;
using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// Het bewerkbare deel van het contractscherm: wat er werkelijk wordt weggeschreven, en wat er
/// gebeurt als twee operators dezelfde kaart openhebben.
/// </summary>
/// <remarks>
/// <para><strong>Waarom deze tests op het eiland staan en niet op de pagina.</strong> De
/// gelijktijdigheid zit in het eiland: het houdt de versie vast die op het scherm stond toen de
/// operator begon te typen, en die gaat als <c>ContractEdit.BasedOnETag</c> mee. Dat is niet met een
/// gerenderde pagina te meten maar met een schrijfactie, en daarom wordt hier telkens gekeken naar
/// <see cref="Vasteportaalopslag.Contractbewerkingen"/>: welke etag ging mee, en welke waarden.</para>
///
/// <para><strong>Het conflict wordt gedaan en niet gescript.</strong> Er staat geen uitkomst klaar;
/// er wijzigt werkelijk een tweede operator het document in de opslag, waarna de etag niet meer
/// klopt. Zo meet de test het pad dat in productie bestaat en niet een pad dat alleen in de fixture
/// bestaat. Zie <see cref="Vasteportaalopslag"/>.</para>
///
/// <para>De kern van elke test hieronder is dezelfde vraag: <em>is er een pad waarlangs de wijziging
/// van een ander stil verdwijnt?</em> De etag hoort dat te voorkomen, en een controle die alleen in
/// de gelukkige gevallen wordt uitgevoerd is geen controle.</para>
/// </remarks>
public class ContracteilandTests : Portaalrendertest
{
    /// <summary>Het veld dat de tests wijzigen: tekst, en zichtbaar voor de klant.</summary>
    private const string Sla = "SLA";

    /// <summary>Wat de operator van deze test invult.</summary>
    private const string EigenSla = "Reactie 2 werkuren · herstel dezelfde dag";

    /// <summary>Wat de andere operator intussen invult.</summary>
    private const string AndermansSla = "Reactie 8 werkuren · herstel 3 werkdagen";

    // ── De gewone gang: bewaren met de versie die op het scherm stond ────────────────────────────

    [Fact]
    public void EenWijzigingGaatMetDeVersieMeeDieOpHetSchermStond()
    {
        // De basis waar alles hieronder op leunt. Niet een verse lezing vlak vóór het schrijven: dan
        // zou je de wijziging van de ander binnenhalen en er precies overheen schrijven, en is de
        // controle een ritueel.
        var versieOpHetScherm = Opslag.Contract()!.ETag;

        MeldOperatorAan();

        var cut = Eiland();

        Vul(cut, Sla, EigenSla);
        Bewaar(cut);

        var bewerking = Assert.Single(Opslag.Contractbewerkingen);

        Assert.Equal(versieOpHetScherm, bewerking.BasedOnETag);
        Assert.Equal(EigenSla, bewerking.Sla);
        Assert.Equal(EigenSla, Opslag.Contract()!.Sla);
        Assert.Contains("Bewaard", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenContractDatNogNietBestondWordtAangelegdZonderVersie()
    {
        // null betekent "dit contract wordt aangemaakt" en niet "sla de controle over". Dat tweede
        // staat in de test hieronder.
        Opslag = new Vasteportaalopslag(zonderContract: true);

        MeldOperatorAan();

        var cut = Eiland();

        Vul(cut, "Contractnummer", "SOR-2026-0199");
        Bewaar(cut);

        var bewerking = Assert.Single(Opslag.Contractbewerkingen);

        Assert.Null(bewerking.BasedOnETag);
        Assert.Equal("SOR-2026-0199", Opslag.Contract()!.Number);
    }

    // ── Twee operators op dezelfde kaart ────────────────────────────────────────────────────────

    [Fact]
    public void EenConflictLaatDeWijzigingVanDeAnderStaanEnToontWatErVeranderde()
    {
        // Het verschil tussen "opslaan is mislukt, probeer opnieuw" en een scherm dat kan tonen wát
        // er veranderde. Zonder dat laatste is het enige dat de operator kan doen zijn eigen invoer
        // nogmaals versturen, en dan is de laatste schrijver alsnog de winnaar.
        var versieOpHetScherm = Opslag.Contract()!.ETag;

        MeldOperatorAan();

        var cut = Eiland();

        Vul(cut, Sla, EigenSla);

        Opslag.EenAndereOperatorWijzigtHetContract(document => document with { Sla = AndermansSla });

        Bewaar(cut);

        // De poging is gedaan met de versie van het scherm, en niet met een verse lezing.
        var bewerking = Assert.Single(Opslag.Contractbewerkingen);
        Assert.Equal(versieOpHetScherm, bewerking.BasedOnETag);

        // En er is niets overschreven: in de opslag staat nog wat de ander erin zette.
        Assert.Equal(AndermansSla, Opslag.Contract()!.Sla);

        // Het scherm zegt wat er is gebeurd, veld voor veld, en niet alleen dat het mislukte.
        Assert.Contains("intussen", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AndermansSla, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("was", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenTweedePogingLegtDeEigenVersieVastMetDeVersieVanDeAnder()
    {
        // De uitweg. De etag schuift op naar die van de ander, dus een tweede klik op Bewaren legt
        // de eigen waarden alsnog vast — maar pas nadat de operator heeft gezien wat hij
        // overschrijft, en dat is het omgekeerde van stil overschrijven.
        MeldOperatorAan();

        var cut = Eiland();

        Vul(cut, Sla, EigenSla);

        Opslag.EenAndereOperatorWijzigtHetContract(document => document with { Sla = AndermansSla });

        var versieVanDeAnder = Opslag.Contract()!.ETag;

        Bewaar(cut);
        Bewaar(cut);

        Assert.Equal(2, Opslag.Contractbewerkingen.Count);

        var tweede = Opslag.Contractbewerkingen[1];

        Assert.Equal(versieVanDeAnder, tweede.BasedOnETag);
        Assert.Equal(EigenSla, tweede.Sla);
        Assert.Equal(EigenSla, Opslag.Contract()!.Sla);
    }

    [Fact]
    public void GeenEnkelePogingGaatZonderDeVersieDieOpHetSchermStond()
    {
        // De vraag achter alle tests hierboven, in één assertie over het hele verloop: er hoort geen
        // schrijfactie te bestaan die zonder versie langs de controle glipt zolang er een contract
        // is. Ook niet de tweede poging na een conflict, en ook niet na een derde wijziging van
        // iemand anders.
        MeldOperatorAan();

        var cut = Eiland();

        Vul(cut, Sla, EigenSla);

        Opslag.EenAndereOperatorWijzigtHetContract(document => document with { Sla = AndermansSla });
        Bewaar(cut);

        Opslag.EenAndereOperatorWijzigtHetContract(document => document with { Term = "36 maanden" });
        Bewaar(cut);

        Bewaar(cut);

        Assert.Equal(3, Opslag.Contractbewerkingen.Count);

        Assert.All(
            Opslag.Contractbewerkingen,
            bewerking => Assert.False(
                bewerking.BasedOnETag is null,
                "Er is een contract weggeschreven zonder de versie waarop het formulier is " +
                "gebaseerd, terwijl er wél een contract stond. Dat is precies het pad waarlangs " +
                "twee operators elkaar stil overschrijven: zonder etag doet de opslag een aanleg " +
                "en niet een vervanging, en dan wint de laatste verzender."));

        // En elke poging draagt de eigen waarde. Zou een poging de waarde van de ander meesturen,
        // dan had het scherm zijn wijziging binnengehaald in plaats van hem te tonen.
        Assert.All(
            Opslag.Contractbewerkingen,
            bewerking => Assert.Equal(EigenSla, bewerking.Sla));
    }

    [Fact]
    public void EenTweedeAanlegVanHetzelfdeContractIsOokEenConflict()
    {
        // Het subtiele geval: het formulier draagt geen etag, want er was nog geen contract. "Geen
        // etag" mag geen vrijbrief zijn om over de aanleg van een ander heen te schrijven — er is
        // geen waarde van BasedOnETag waarmee je de controle overslaat.
        Opslag = new Vasteportaalopslag(zonderContract: true);

        MeldOperatorAan();

        var cut = Eiland();

        Vul(cut, "Contractnummer", "SOR-2026-0199");

        Opslag.EenAndereOperatorLegtHetContractVast(
            Vasteportaalopslag.Volledigcontract() with { Number = "SOR-2026-0200" });

        Bewaar(cut);

        var bewerking = Assert.Single(Opslag.Contractbewerkingen);

        Assert.Null(bewerking.BasedOnETag);
        Assert.Equal("SOR-2026-0200", Opslag.Contract()!.Number);
        Assert.Contains("intussen", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeVersieVanDeAnderOvernemenSchrijftNiets()
    {
        // De andere uitweg uit een conflict, en hij hoort niets te doen behalve het formulier
        // bijzetten. Een knop die "overnemen" heet en stil iets wegschrijft is de omgekeerde
        // verrassing van stil overschrijven, en even ongewenst.
        MeldOperatorAan();

        var cut = Eiland();

        Vul(cut, Sla, EigenSla);

        Opslag.EenAndereOperatorWijzigtHetContract(document => document with { Sla = AndermansSla });

        Bewaar(cut);

        Assert.Single(Opslag.Contractbewerkingen);

        Overnemen(cut);

        Assert.Single(Opslag.Contractbewerkingen);
        Assert.Equal(AndermansSla, Opslag.Contract()!.Sla);
        Assert.Equal(AndermansSla, Waarde(cut, Sla));

        // De verschillenkaart is weg: er is niets meer te vergelijken.
        Assert.DoesNotContain("intussen", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    // ── Nul is een afspraak en leeg is er geen ──────────────────────────────────────────────────

    [Fact]
    public void EenAfgesprokenNulBlijftNulAlsErEenAnderVeldWordtBewaard()
    {
        // Een opslagpercentage van nul is een afspraak die iemand heeft opgeschreven: op deze klant
        // zit geen beheeropslag. Zou het formulier die nul als leeg tonen, dan verandert een
        // operator die het contract opent en één ander veld bewaart die afspraak stil in "niet
        // vastgelegd" — zonder één toetsaanslag op dat veld, en zonder dat de wijzigingslijst er
        // iets over zegt. Bij dit veld is het verschil onze marge.
        Opslag = new Vasteportaalopslag(
            contract: Vasteportaalopslag.Volledigcontract() with { AzureSurchargePercentage = 0m });

        MeldOperatorAan();

        var cut = Eiland();

        Vul(cut, Sla, EigenSla);
        Bewaar(cut);

        var bewerking = Assert.Single(Opslag.Contractbewerkingen);

        Assert.Equal(0m, bewerking.AzureSurchargePercentage);
        Assert.Equal(0m, Opslag.Contract()!.AzureSurchargePercentage);
    }

    [Fact]
    public void EenNietVastgelegdBedragBlijftLeegAlsErEenAnderVeldWordtBewaard()
    {
        // De andere richting, en de bevinding waar dit paar uit komt: een leeg tariefveld werd als
        // nul weggeschreven. Dan staat er een bedrag in de opslag dat niemand heeft ingetypt, dat
        // als afspraak leest en dat in een berekening als nul meetelt.
        Opslag = new Vasteportaalopslag(
            contract: Vasteportaalopslag.Volledigcontract() with
            {
                HourlyRate = null,
                AzureSurchargePercentage = null,
            });

        MeldOperatorAan();

        var cut = Eiland();

        Vul(cut, Sla, EigenSla);
        Bewaar(cut);

        var bewerking = Assert.Single(Opslag.Contractbewerkingen);

        Assert.Null(bewerking.HourlyRate);
        Assert.Null(bewerking.AzureSurchargePercentage);
        Assert.Null(Opslag.Contract()!.HourlyRate);
    }

    [Fact]
    public void HetVerschilTussenNulEnLeegStaatOokInDeVerschillenkaart()
    {
        // Zou de kaart nul en leeg als dezelfde tekst tonen, dan meldt hij bij een conflict geen
        // verschil waar er wel een is — en dan is de operator die op Bewaren klikt niet gewaarschuwd
        // dat hij een afspraak weggooit.
        Opslag = new Vasteportaalopslag(
            contract: Vasteportaalopslag.Volledigcontract() with { AzureSurchargePercentage = 0m });

        MeldOperatorAan();

        var cut = Eiland();

        Vul(cut, Sla, EigenSla);

        Opslag.EenAndereOperatorWijzigtHetContract(
            document => document with { AzureSurchargePercentage = null });

        Bewaar(cut);

        Assert.Contains("Opslag op Azure-kosten", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("leeg", cut.Markup, StringComparison.Ordinal);
    }

    // ── Toegang: vastleggen en intrekken ────────────────────────────────────────────────────────

    [Fact]
    public void EenToegangIntrekkenGaatMetDeVersieVanDieRijMee()
    {
        // Zo wordt er niets verwijderd wat intussen is veranderd. Twee klikken, want het document
        // wordt echt verwijderd en er blijft geen spoor.
        MeldOperatorAan();

        var cut = Eiland();

        var rij = Opslag.Toegangen()[0];

        Intrekken(cut, 0);

        var intrekking = Assert.Single(Opslag.Intrekkingen);

        Assert.Equal(rij.Email, intrekking.Email);
        Assert.Equal(rij.ETag, intrekking.BasedOnETag);
        Assert.False(
            intrekking.BasedOnETag is null,
            "De intrekking ging zonder de versie van de rij zoals hij op het scherm stond. Dan " +
            "verwijdert het portaal een toegang die iemand anders intussen kan hebben gewijzigd.");

        Assert.Equal(2, Opslag.Toegangen().Count);
    }

    [Fact]
    public void EenIngetrokkenToegangDieAlWegWasLevertEenMeldingEnGeenStilteOp()
    {
        // Iemand anders was net eerder. Het scherm hoort dat te zeggen: de lijst waar de operator
        // naar kijkt is dan niet meer waar, en een gelukte-melding over een regel die al weg was
        // laat hem denken dat híj hem heeft ingetrokken.
        MeldOperatorAan();

        var cut = Eiland();

        var rij = Opslag.Toegangen()[0];

        Opslag.EenAndereOperatorTrektToegangIn(rij.Email);

        Intrekken(cut, 0);

        Assert.Contains("geen toegang (meer)", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, Opslag.Toegangen().Count);
    }

    [Fact]
    public void EenAdresDatAlToegangHeeftLevertEenMeldingEnGeenTweedeRegel()
    {
        MeldOperatorAan();

        var cut = Eiland();

        Vul(cut, "E-mailadres", Vasteportaalopslag.Beheerderadres);
        BewaarToegang(cut);

        Assert.Contains("heeft al toegang", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, Opslag.Toegangen().Count);
    }

    [Fact]
    public void EenNieuwAdresKomtInDeLijstMetDeAantekeningDatEntraNogHandwerkIs()
    {
        // Toegang vastleggen is niet uitnodigen. Zwijgen daarover levert de vraag op waarom iemand
        // die in de lijst staat niet binnenkomt — en dat is precies de halve toestand die leesbaar
        // hoort te zijn.
        MeldOperatorAan();

        var cut = Eiland();

        Vul(cut, "E-mailadres", "Nieuwe.Collega@Acme-Logistiek.NL");
        BewaarToegang(cut);

        var toegang = Assert.Single(Opslag.Toegangverleningen);

        // Het scherm normaliseert vóór de aanroep, en dat is de bedoeling: twee schrijfwijzen van
        // hetzelfde adres zouden twee toegangen zijn, waarvan er één intrekken niets doet. De
        // schrijfkant normaliseert nog een keer — één vorm, en niet twee.
        Assert.Equal("nieuwe.collega@acme-logistiek.nl", toegang.Email);

        Assert.Contains(
            "nieuwe.collega@acme-logistiek.nl",
            Opslag.Toegangen().Select(t => t.Email),
            StringComparer.Ordinal);

        Assert.Equal(4, Opslag.Toegangen().Count);
        Assert.Contains("Entra ID", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenOnbruikbaarAdresKomtNietBijDeOpslagTerecht()
    {
        // Dezelfde controle als de schrijfkant, maar vóór de aanroep, zodat de melding onder het
        // veld komt en niet als blok boven de knop.
        MeldOperatorAan();

        var cut = Eiland();

        Vul(cut, "E-mailadres", "geen-adres");
        BewaarToegang(cut);

        Assert.Empty(Opslag.Toegangverleningen);
        Assert.Equal(3, Opslag.Toegangen().Count);
        Assert.Contains("e-mailadres", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    // ── Gereedschap ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Rendert het eiland voor de eigen klant, zoals de pagina het aanroept.</summary>
    /// <returns>Het gerenderde eiland.</returns>
    /// <remarks>
    /// Over de grens gaat precies één string: de klantslug. Het eiland autoriseert zichzelf opnieuw
    /// met de aangemelde gebruiker, en dat is ook hier het geval — er wordt geen scope ingeduwd.
    /// </remarks>
    private IRenderedComponent<ContractPanel> Eiland() =>
        Render<ContractPanel>(parameters => parameters.Add(p => p.CustomerId, EigenKlant));

    /// <summary>Typt een waarde in het veld met dit label.</summary>
    private static void Vul(IRenderedComponent<ContractPanel> cut, string label, string waarde) =>
        Veld(cut, label).Input(waarde);

    /// <summary>Wat er nu in het veld met dit label staat.</summary>
    private static string? Waarde(IRenderedComponent<ContractPanel> cut, string label) =>
        Veld(cut, label).GetAttribute("value");

    /// <summary>
    /// Het invoerveld dat bij dit label hoort.
    /// </summary>
    /// <remarks>
    /// Via het <c>for</c>-attribuut van het label en niet via een positie in de kaart. Een test die
    /// het derde invoerveld pakt, breekt zodra er een veld bij komt en meet daarna stilletjes iets
    /// anders. Bijkomend: dit meet ook dat het label werkelijk aan zijn veld is gekoppeld — dat is
    /// wat klikken op het label laat werken.
    /// </remarks>
    private static IElement Veld(IRenderedComponent<ContractPanel> cut, string label)
    {
        ArgumentNullException.ThrowIfNull(cut);

        var labels = cut.FindAll("label")
            .Where(l => l.TextContent.Trim().StartsWith(label, StringComparison.Ordinal))
            .ToArray();

        if (labels.Length != 1)
        {
            throw new InvalidOperationException(
                $"Er zijn {labels.Length} labels die met \"{label}\" beginnen op het contracteiland, " +
                "en deze test heeft er precies één nodig. Is het label hernoemd, dan hoort de test " +
                "mee te veranderen; zijn het er twee, dan kies een preciezere tekst.");
        }

        var id = labels[0].GetAttribute("for")
            ?? throw new InvalidOperationException(
                $"Het label \"{label}\" heeft geen for-attribuut, dus het hoort bij geen enkel veld. " +
                "Klikken op het label focust dan niets en een schermlezer noemt het veld niet.");

        return cut.Find("#" + id);
    }

    /// <summary>Dient de contractkaart in.</summary>
    private static void Bewaar(IRenderedComponent<ContractPanel> cut) =>
        Formulier(cut, "Contract").Submit();

    /// <summary>Dient het toegangsformulier in.</summary>
    private static void BewaarToegang(IRenderedComponent<ContractPanel> cut) =>
        Formulier(cut, "Toegang vastleggen").Submit();

    /// <summary>
    /// Neemt de versie van de ander over: de knop, en dan de bevestiging.
    /// </summary>
    /// <remarks>
    /// Twee klikken, want dat is wat ConfirmAction doet — en de bevestiging staat met opzet niet op
    /// de plek van de knop die hem opriep.
    /// </remarks>
    private static void Overnemen(IRenderedComponent<ContractPanel> cut)
    {
        ArgumentNullException.ThrowIfNull(cut);

        cut.FindAll("button")
            .First(b => b.TextContent.Contains("overnemen", StringComparison.OrdinalIgnoreCase))
            .Click();

        cut.Find(".confirm .btn--primary").Click();
    }

    /// <summary>Trekt de toegang op deze rij in: de knop, en dan de bevestiging.</summary>
    private static void Intrekken(IRenderedComponent<ContractPanel> cut, int rij)
    {
        ArgumentNullException.ThrowIfNull(cut);

        cut.FindAll(".row-actions button")[rij].Click();
        cut.Find(".confirm .btn--confirm").Click();
    }

    private static IElement Formulier(IRenderedComponent<ContractPanel> cut, string kaart)
    {
        ArgumentNullException.ThrowIfNull(cut);

        return cut.Find($"section[aria-label=\"{kaart}\"] form");
    }
}
