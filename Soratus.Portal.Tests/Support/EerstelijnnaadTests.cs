using System.Reflection;
using Soratus.Portal.Support;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Support;

/// <summary>
/// De vorm van de naad naar de AI-eerstelijn, en waarom er geen antwoord zonder bron kan bestaan.
/// </summary>
/// <remarks>
/// <para>Dit is de acceptatie-eis van fase 5: <em>de agent beantwoordt statusvragen, urenvragen en
/// factuurvragen zonder te verzinnen, en escaleert als hij het niet zeker weet.</em> Wat hier wordt
/// gemeten is niet of een model zich gedraagt — er is geen model — maar of de vorm de fout onmogelijk
/// maakt.</para>
///
/// <para><strong>De tests meten de invariant en niet het gevolg.</strong> Dat onderscheid heeft dit
/// project deze week een halve dag gekost: een test die het gevólg meet blijft zes runs groen terwijl
/// de fout er is. De invariant hier is "een bericht van de eerstelijn draagt een grondslag die is
/// aangeboden, of het is een escalatie" — en die wordt op het <em>document</em> gemeten en niet op de
/// bubbel die eruit volgt.</para>
/// </remarks>
public class EerstelijnnaadTests
{
    // ── De vorm zelf ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenAntwoordtypeHeeftGeenEnkelTekstveld()
    {
        // Dit is de kern van het ontwerp en het is met reflectie te meten: zodra iemand er een
        // string-eigenschap op zet, kan een model een zin sturen die het portaal doorschrijft. Dan is
        // "hij kan geen bedrag verzinnen" niet meer waar, en dat is precies de fout die niet te zien
        // is aan het antwoord.
        var tekstvelden = typeof(SupportAnswer)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToArray();

        Assert.True(
            tekstvelden.Length == 0,
            "SupportAnswer heeft een tekstveld: " + string.Join(", ", tekstvelden) + ".\n\n" +
            "Dat is de eis van fase 5 die dan niet meer geldt. Het antwoord van de eerstelijn is een " +
            "verwijzing naar een grondslag en geen tekst; de zin wordt door SupportText samengesteld " +
            "uit gegevens die het portaal zelf heeft opgemaakt. Met een tekstveld hier kan er een " +
            "bedrag in reizen dat nergens op rust, en dat klinkt hetzelfde als een echt antwoord.");
    }

    [Fact]
    public void EenGrondslagIsBuitenHetPortaalNietTeMaken()
    {
        // De tweede helft van dezelfde constructie. Een implementatie van ISupportFirstLine leeft
        // buiten Soratus.Portal; als zij een SupportGround kan construeren, kan zij een bron verzinnen
        // en er een verzonnen feit in zetten.
        var publiek = typeof(SupportGround)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.True(
            publiek.Length == 0,
            "SupportGround heeft een publieke constructor. Daarmee kan een implementatie van " +
            "ISupportFirstLine buiten deze assembly zelf een grondslag maken — met een zelfbedacht " +
            "feit erin — en dan is de subsetcontrole in CosmosSupportStore.Accept waardeloos: die " +
            "vergelijkt op waarde.");
    }

    [Fact]
    public void EenAntwoordZonderGrondslagIsNietTeSchrijven()
    {
        // De compiler is de eerste controle: GroundedIn heeft een verplichte parameter, dus
        // GroundedIn() compileert niet. Wat deze test meet is dat er geen tweede weg is — geen
        // publieke constructor, geen publieke setter, geen with-expressie van buiten.
        var fabrieken = typeof(SupportAnswer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(SupportAnswer))
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Escalate", "GroundedIn"], fabrieken);

        Assert.Empty(typeof(SupportAnswer).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var zetters = typeof(SupportAnswer)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToArray();

        Assert.True(
            zetters.Length == 0,
            "SupportAnswer heeft een publieke setter: " + string.Join(", ", zetters) + ". Dan bestaat " +
            "de toestand 'grondslag weggehaald na constructie' en is de vorm geen garantie meer.");
    }

    [Fact]
    public void DeGrondslagenDieAanDeEerstelijnWordenAangebodenKomenUitDeKlantweergaven()
    {
        // Geen operatorgegeven kan in een grondslag terechtkomen, en dat is niet met een woordenlijst
        // geregeld maar met de bron: de fabrieken nemen klantviewmodellen, en die types hebben de
        // velden niet. Deze test legt dat vast op de signatuur.
        var parameters = typeof(SupportGrounds)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name.StartsWith("From", StringComparison.Ordinal))
            .Select(m => m.GetParameters()[0].ParameterType.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["CustomerAgentsView", "CustomerBillingView", "CustomerHoursView"],
            parameters);
    }

    // ── Het aannemen: de plek waar de eis wordt afgedwongen ─────────────────────────────────────

    [Fact]
    public void EenNietAangebodenGrondslagWordtNietAangenomen()
    {
        // Het geval waar de hele constructie voor bestaat: een eerstelijn die een grondslag teruggeeft
        // die zij niet in dit verzoek heeft gekregen — een gecachete grondslag van een andere klant,
        // bijvoorbeeld.
        var aangeboden = VasteSupportweergaven.Grondslag(SupportGroundKind.Hours, "2026-07");
        var vreemde = VasteSupportweergaven.Grondslag(SupportGroundKind.Hours, "2026-06");

        var verzoek = new SupportEnquiry { Question = "Hoeveel uren staan er?", Grounds = [aangeboden] };

        Assert.Null(CosmosSupportStore.Accept(verzoek, SupportAnswer.GroundedIn(vreemde)));
        Assert.Same(aangeboden, CosmosSupportStore.Accept(verzoek, SupportAnswer.GroundedIn(aangeboden)));
    }

    [Fact]
    public void GeenAntwoordEnEenEscalatieLeverenBeideGeenGrondslagOp()
    {
        var verzoek = new SupportEnquiry
        {
            Question = "Hoe gaat het?",
            Grounds = [VasteSupportweergaven.Grondslag()],
        };

        Assert.Null(CosmosSupportStore.Accept(verzoek, answer: null));
        Assert.Null(CosmosSupportStore.Accept(
            verzoek,
            SupportAnswer.Escalate(SupportEscalation.NeedsAHuman)));
    }

    [Fact]
    public void EenLeegVerzoekKanGeenAntwoordOpleveren()
    {
        // Een klant zonder agents, zonder uren en zonder gemeten maand. Er is dan niets om op te
        // antwoorden, en dat hoort een escalatie te worden en geen antwoord over niets.
        var verzoek = new SupportEnquiry { Question = "Draait alles?", Grounds = [] };

        Assert.Null(CosmosSupportStore.Accept(
            verzoek,
            SupportAnswer.GroundedIn(VasteSupportweergaven.Grondslag())));
    }

    [Fact]
    public void EenGelijkeGrondslagUitEenAndereInstantieWordtWelAangenomen()
    {
        // Waardegelijkheid en niet referentiegelijkheid, en dat is opzet: bij een naad over een
        // procesgrens komt er nooit dezelfde instantie terug. Deze test legt die keuze vast, want een
        // mutatie naar ReferenceEquals zou hier rood horen te worden en nergens anders.
        var aangeboden = VasteSupportweergaven.Grondslag(SupportGroundKind.Hours, "2026-07", "Drie uur.");
        var gelijk = VasteSupportweergaven.Grondslag(SupportGroundKind.Hours, "2026-07", "Drie uur.");

        Assert.NotSame(aangeboden, gelijk);

        var verzoek = new SupportEnquiry { Question = "?", Grounds = [aangeboden] };

        Assert.NotNull(CosmosSupportStore.Accept(verzoek, SupportAnswer.GroundedIn(gelijk)));
    }

    // ── Het vastleggen: wat er in de draad terechtkomt ──────────────────────────────────────────

    [Fact]
    public async Task EenAangenomenAntwoordKrijgtDeTekstVanDeGrondslagEnGeenAndere()
    {
        var opslag = new Vasteportaalopslag();
        var scope = await Weergavelaag.Klantscope();
        var grondslag = VasteSupportweergaven.Grondslag(
            SupportGroundKind.Hours,
            "2026-07",
            "In juli 2026 staan 3 u gefiatteerde uren op een bundel van 12 u.");

        var verzoek = new SupportEnquiry { Question = "Hoeveel uren in juli?", Grounds = [grondslag] };

        await opslag.RecordFirstLineAsync(scope, verzoek, SupportAnswer.GroundedIn(grondslag));

        var bericht = Assert.Single(opslag.Supportberichten());

        Assert.Equal(SupportAuthor.FirstLine, bericht.Author);
        Assert.Equal(grondslag.Fact, bericht.Text);
        Assert.Equal(SupportGroundKind.Hours, bericht.GroundKind);
        Assert.Equal("2026-07", bericht.GroundKey);
        Assert.Null(bericht.Escalation);
    }

    [Fact]
    public async Task EenVerzonnenGrondslagLeidtTotEenEscalatieEnNietTotEenAntwoord()
    {
        // Het geval dat de eis draagt: er kwam een antwoord, en er komt géén antwoord in de draad.
        // Gemeten op het document en niet op het scherm — de bubbel is het gevolg, dit is de invariant.
        var opslag = new Vasteportaalopslag();
        var scope = await Weergavelaag.Klantscope();

        var verzoek = new SupportEnquiry
        {
            Question = "Wat is mijn factuur?",
            Grounds = [VasteSupportweergaven.Grondslag(SupportGroundKind.Invoice, "2026-07")],
        };

        var verzonnen = VasteSupportweergaven.Grondslag(
            SupportGroundKind.Invoice,
            "2026-07",
            "Over juli 2026 staat € 0,00 door te belasten.");

        await opslag.RecordFirstLineAsync(scope, verzoek, SupportAnswer.GroundedIn(verzonnen));

        var bericht = Assert.Single(opslag.Supportberichten());

        Assert.Equal(SupportEscalation.AnswerNotUsable, bericht.Escalation);
        Assert.Null(bericht.GroundKind);
        Assert.Null(bericht.GroundKey);
        Assert.Equal(SupportText.Handoff(), bericht.Text);
        Assert.DoesNotContain("0,00", bericht.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EenEscalatieDraagtDeRedenAlsEnumEnNooitInDeTekst()
    {
        var opslag = new Vasteportaalopslag();
        var scope = await Weergavelaag.Klantscope();
        var verzoek = new SupportEnquiry { Question = "Kan de bundel omhoog?", Grounds = [] };

        await opslag.RecordFirstLineAsync(
            scope,
            verzoek,
            SupportAnswer.Escalate(SupportEscalation.NeedsAHuman));

        var bericht = Assert.Single(opslag.Supportberichten());

        Assert.Equal(SupportEscalation.NeedsAHuman, bericht.Escalation);

        // Alle vier de redenen leveren dezelfde zin op. Dat is het bewuste verlies uit
        // SupportEscalation: mag het model uit vier zinnen kiezen, dan mag het vier verschillende
        // dingen beweren over wat wij van deze klant weten.
        foreach (var reden in Enum.GetValues<SupportEscalation>())
        {
            Assert.DoesNotContain(
                SupportText.EscalationLabel(reden),
                bericht.Text,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── De balie: de volgorde, en wat er gebeurt als de naad ontbreekt of ontploft ──────────────

    [Fact]
    public async Task ZonderEerstelijnStaatDeVraagErEnKomtErGeenEnkeleBubbelBij()
    {
        var opslag = new Vasteportaalopslag();
        var balie = VasteSupportweergaven.Balie(opslag);
        var scope = await Weergavelaag.Klantscope();

        Assert.Equal(SupportFirstLineState.NotConfigured, balie.FirstLine());

        var uitkomst = await balie.AskAsync(
            scope,
            new SupportQuestion { Author = "Jan Bakker", Text = "Draaien mijn agents?" });

        Assert.True(uitkomst.IsSaved);

        // Precies één bericht: de vraag. Geen escalatiebubbel, want er is niets geëscaleerd — een
        // bericht met het merkteken van een agent die niet bestaat, zou een agent suggereren die er is.
        var bericht = Assert.Single(opslag.Supportberichten());
        Assert.Equal(SupportAuthor.Customer, bericht.Author);
    }

    [Fact]
    public async Task EenEerstelijnDieOntploftVerliestDeVraagNietEnLevertEenEscalatie()
    {
        var opslag = new Vasteportaalopslag();
        var balie = VasteSupportweergaven.Balie(opslag, new Stukkeeerstelijn());
        var scope = await Weergavelaag.Klantscope();

        var uitkomst = await balie.AskAsync(
            scope,
            new SupportQuestion { Author = "Jan Bakker", Text = "Hoe staat mijn factuur?" });

        Assert.True(uitkomst.IsSaved);

        var berichten = opslag.Supportberichten();

        Assert.Equal(2, berichten.Count);
        Assert.Equal(SupportAuthor.Customer, berichten[0].Author);
        Assert.Equal(SupportAuthor.FirstLine, berichten[1].Author);
        Assert.Equal(SupportEscalation.AnswerNotUsable, berichten[1].Escalation);

        // En de melding van de uitzondering staat nergens in de draad. Dat is punt 13 en 14 van de
        // fase-0-afwijkingen in deze map: de tekst uit een catch-blok hoort niet bij een klant.
        foreach (var bericht in berichten)
        {
            Assert.DoesNotContain("/src/", bericht.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("HTTP 500", bericht.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("FirstLine.cs", bericht.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DeVraagWordtVastgelegdVoordatDeEerstelijnHemZiet()
    {
        // De volgorde is het ontwerp, en dat is hier te meten: op het moment dat de eerstelijn wordt
        // aangeroepen, staat de vraag al in de opslag. Dezelfde regel als §29.1 van de
        // fase-0-afwijkingen — de claim gaat vóór de mail — en om dezelfde reden: de duurste fout
        // bepaalt de ordening, en dat is hier een vraag die verdwijnt.
        var opslag = new Vasteportaalopslag();
        var aantalBijAanroep = -1;

        var eerstelijn = new Vasteeerstelijn(_ =>
        {
            aantalBijAanroep = opslag.Supportberichten().Count;
            return SupportAnswer.Escalate(SupportEscalation.NotSure);
        });

        var balie = VasteSupportweergaven.Balie(opslag, eerstelijn);
        var scope = await Weergavelaag.Klantscope();

        await balie.AskAsync(
            scope,
            new SupportQuestion { Author = "Jan Bakker", Text = "Draait de voorraad-sync?" });

        Assert.Equal(1, aantalBijAanroep);
    }

    [Fact]
    public async Task DeEerstelijnKrijgtDeGrondslagenVanDrieVraagsoortenEnGeenSleutel()
    {
        // De drie vraagsoorten uit de acceptatie-eis: statusvragen, urenvragen en factuurvragen.
        var opslag = new Vasteportaalopslag();
        var eerstelijn = new Vasteeerstelijn(_ => SupportAnswer.Escalate(SupportEscalation.NotSure));
        var balie = VasteSupportweergaven.Balie(opslag, eerstelijn);
        var scope = await Weergavelaag.Klantscope();

        await balie.AskAsync(
            scope,
            new SupportQuestion { Author = "Jan Bakker", Text = "Alles goed?" });

        var verzoek = Assert.Single(eerstelijn.Verzoeken);

        Assert.NotEmpty(verzoek.Grounds);
        Assert.Contains(verzoek.Grounds, g => g.Kind == SupportGroundKind.Hours);

        // En wat er níet op het verzoek staat: geen klantslug, geen scope, geen verbinding.
        var velden = typeof(SupportEnquiry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Grounds", "Question"], velden);
    }

    [Fact]
    public async Task EenAangenomenAntwoordVanDeBalieKomtAlsBubbelInDeDraad()
    {
        // De tegenhanger van de tests hierboven, en zonder deze zou "er komt geen antwoord door" ook
        // groen staan als er nóóit een antwoord doorkomt.
        var opslag = new Vasteportaalopslag();
        var eerstelijn = new Vasteeerstelijn(verzoek =>
            SupportAnswer.GroundedIn(verzoek.Grounds.First(g => g.Kind == SupportGroundKind.Hours)));

        var balie = VasteSupportweergaven.Balie(opslag, eerstelijn);
        var scope = await Weergavelaag.Klantscope();

        Assert.Equal(SupportFirstLineState.Available, balie.FirstLine());

        await balie.AskAsync(
            scope,
            new SupportQuestion { Author = "Jan Bakker", Text = "Hoeveel uren heb ik verbruikt?" });

        var berichten = opslag.Supportberichten();

        Assert.Equal(2, berichten.Count);
        Assert.Equal(SupportAuthor.FirstLine, berichten[1].Author);
        Assert.Equal(SupportGroundKind.Hours, berichten[1].GroundKind);
        Assert.Null(berichten[1].Escalation);
    }

    // ── De standaardwaarden van de drie opsommingen ─────────────────────────────────────────────

    [Fact]
    public void DeEersteWaardeVanElkeOpsommingIsDeVeilige()
    {
        // Dit is de invariant die geen enkele andere test raakt, en die het meest stil kan omvallen:
        // de standaardwaarde van een niet-gezette enum is de eerste, en een document met een leeg of
        // hernoemd veld leest daarop uit. Dezelfde regel als bij StatementSendState in de mailkant, en
        // daar staat waarom: stond "verstuurd" op nul, dan zou een onleesbaar document lezen als
        // "verstuurd".
        //
        // Hier zijn de drie gevallen:
        //   SupportAuthor      -> Unknown. Niet Soratus, want dan komt de tekst van een klant met onze
        //                         stem terug naar die klant. En niet Customer, want dan komt ons
        //                         antwoord terug als zijn vraag.
        //   SupportGroundKind  -> Unknown. Niet Hours of Invoice, want dan wijst de bronregel van een
        //                         beschadigd bericht naar de verkeerde plek.
        //   SupportEscalation  -> NotSure. De enige van de vier die geen bewering doet.
        Assert.Equal(0, (int)SupportAuthor.Unknown);
        Assert.Equal(0, (int)SupportGroundKind.Unknown);
        Assert.Equal(0, (int)SupportEscalation.NotSure);
    }

    [Fact]
    public async Task EenEerstelijnberichtMetZowelEenGrondslagAlsEenEscalatieLeestAlsEscalatie()
    {
        // Onze schrijfkant zet er nooit beide, dus dit document kan alleen uit een beschadigd of
        // vreemd document komen -- en dan is "hij wist het niet" de veilige lezing en "hier is je
        // antwoord" de gevaarlijke. Zonder deze test is de volgorde van die twee takken in de
        // projectie niet gemeten, en een omgekeerde volgorde levert een bewering op.
        var opslag = new Vasteportaalopslag();
        var weergaven = VasteSupportweergaven.Weergaven(opslag);
        var scope = await Weergavelaag.Klantscope();

        opslag.ZetSupportbericht(SupportdraadTests.Bericht(
            "In juli 2026 staan 3 u gefiatteerde uren.",
            SupportAuthor.FirstLine,
            kind: SupportGroundKind.Hours,
            key: "2026-07",
            escalatie: SupportEscalation.NotSure,
            wie: null));

        var weergave = await weergaven.BuildThreadAsync(
            scope,
            SupportThreadQuery.Newest(),
            SupportFirstLineState.Available);

        var bubbel = Assert.Single(weergave.Bubbles);

        Assert.IsType<SupportHandoffBubble>(bubbel);
    }
}
