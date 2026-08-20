using Bunit;
using Soratus.Agents.Contracts;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// De vrije context van een logregel is operator-only, en deze tests zijn elkaars spiegel: de klant
/// vindt hem nergens, de operator vindt hem wél.
/// </summary>
/// <remarks>
/// <para><strong>Waarom het spiegelpaar en niet één test.</strong> Het veld <c>extra</c> op een
/// logregel is vrije JSON. De echte agents zetten er koppelingdetails in — een Graph-endpoint, een
/// OAuth-scope, een stacktrace met onze bronpaden, een resource group — en bij de interne klant een
/// lijst met de slugs van andere klanten. §2 van de spec verbiedt dat een klant koppelingdetails
/// ziet; een operator moet ze juist kunnen lezen, want daar zit het antwoord op "waarom is die run
/// mislukt".</para>
///
/// <para>Een test die alleen de klantkant controleert kan om twee heel verschillende redenen groen
/// staan: omdat de scheiding werkt, of omdat <c>Extra</c> nergens meer bestaat. In de uitvoer zien
/// die twee er identiek uit. Zou iemand <c>Extra</c> later ook uit het operatorpad halen, dan
/// blijft zo'n test groen terwijl het portaal onbruikbaar is geworden voor een operator. Daarom
/// staat de operatorkant er als tweede test naast: samen leggen ze niet vast dat er iets
/// ontbreekt, maar dat het op precies één plek staat.</para>
///
/// <para><strong>Geschiedenis, omdat de vorm van de oplossing het punt is.</strong> Deze test
/// faalde toen hij werd geschreven. De logtabel nam op dat moment voor beide rollen
/// <c>LogRecord</c> en klapte elke rij uit naar <c>LogJson.Format</c>; alle zeven waarden uit
/// <see cref="Testlogregels.VerbodenInhoud"/> stonden in de markup van het klantscherm. De
/// reparatie is geen filter en geen <c>@if</c> geworden maar een typeverschil: de klant krijgt
/// <c>CustomerLogLine</c>, en dat type héért <c>Extra</c> niet te hebben — het hééft het niet. Wat
/// er niet is kan niet lekken, ook niet als iemand er over een half jaar een kolom bij zet.</para>
/// </remarks>
public class KlantLogregelTests : Portaalrendertest
{
    /// <summary>Het agentdetail, met het logtabblad als beginstand.</summary>
    private static Type Agentdetail =>
        Paginaverzameling.MetNaam("Soratus.Portal.Components.Pages.Klant.AgentDetail")
        ?? throw new InvalidOperationException(
            "De pagina AgentDetail is niet gevonden. Is hij hernoemd of verplaatst, dan hoort " +
            "deze test mee te verhuizen — hij is de enige die naar de vrije context van een " +
            "logregel kijkt.");

    [Fact]
    public void EenKlantZietZijnEigenLogregelsMetNiveauEnGebeurtenis()
    {
        // De onmisbare tegenhanger van de test hieronder: die kijkt of er iets níet staat, en dat
        // is alleen iets waard als er wél iets staat. Faalt deze test, dan zegt de andere niets
        // meer — dan is het scherm gewoon leeg.
        MeldKlantAan();

        var markup = RenderPagina(Agentdetail).Markup;

        Assert.Contains("delta.opgehaald", markup, StringComparison.Ordinal);
        Assert.Contains("afzender.onbekend", markup, StringComparison.Ordinal);
        Assert.Contains("run.mislukt", markup, StringComparison.Ordinal);
        Assert.Contains("van 3 regels", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantVindtDeVrijeContextNergensInDeMarkup()
    {
        MeldKlantAan();

        var pagina = RenderPagina(Agentdetail);

        // Er is niets om open te klikken op het klantpad, en dat is het ontwerp: geen chevron, geen
        // uitklappaneel, en de rij is een <div>. Toch eerst proberen — een knop die er tóch staat is
        // precies de weg terug naar het lek.
        foreach (var knop in pagina.FindAll("button.data-row--log"))
        {
            knop.Click();
        }

        // De hele markup en niet de zichtbare tekst. Een title, een aria-label of een verborgen
        // attribuut staat net zo goed in de paginabron, en het logtabblad prerendert — wat in de
        // eerste render zit, staat in de HTML die de browser krijgt.
        var markup = pagina.Markup;

        Assert.DoesNotContain("json-disclosure", markup, StringComparison.Ordinal);

        var gelekt = Testlogregels.VerbodenInhoud
            .Where(inhoud => markup.Contains(inhoud, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            gelekt.Length == 0,
            "Het klantscherm toont inhoud die volgens §2 niet op zijn scherm hoort:\n" +
            $"  {string.Join("\n  ", gelekt)}\n\n" +
            "Alles hierboven komt uit LogRecord.Extra. Op het klantpad hoort dat veld niet te " +
            "bestaan: de klant krijgt CustomerLogLine en dat type heeft het niet. Staat het er " +
            "tóch, dan is er een pad bijgekomen dat LogRecord aan een klant laat zien — een " +
            "tweede tabel, een title, of een viewmodel dat het veld weer heeft.\n\n" +
            "Los dit niet op met een @if of een filter. Een ontbrekend veld kan niet lekken, een " +
            "vergeten filter wel.");
    }

    [Fact]
    public void EenOperatorVindtDeVrijeContextWelZodraHijEenRegelUitklapt()
    {
        // De spiegel van de test hierboven, en de reden dat die iets betekent. Zou Extra ook uit
        // het operatorpad verdwijnen, dan blijft de klanttest groen terwijl niemand meer kan zien
        // waarom een run is mislukt. Deze test houdt dat tegen.
        MeldOperatorAan();

        var pagina = RenderPagina(Agentdetail);

        foreach (var rij in pagina.FindAll("button.data-row--log"))
        {
            rij.Click();
        }

        var markup = pagina.Markup;

        Assert.Contains("json-disclosure", markup, StringComparison.Ordinal);

        var ontbreekt = Testlogregels.VerbodenInhoud
            .Where(inhoud => !markup.Contains(inhoud, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            ontbreekt.Length == 0,
            "De operator ziet de vrije context van zijn logregels niet meer. Dit ontbreekt in de " +
            $"uitgeklapte JSON:\n  {string.Join("\n  ", ontbreekt)}\n\n" +
            "Dat is geen winst in zichtbaarheid maar een verlies: hier staat het endpoint, de " +
            "scope en de stacktrace waarmee een operator een mislukte run verklaart. Het hoort " +
            "níet bij de klant en wél hier — zie CustomerLogTable naast LogTable.");
    }

    [Fact]
    public void DeKlantprojectieKniptEenMeerregeligBerichtTerugTotDeEersteRegel()
    {
        // Deze test faalde toen hij werd geschreven, en de vindplaats van de reparatie is dichter
        // bij dan hij toen leek: CustomerLogLine.From, één regel in Soratus.Portal. De bron van het
        // probleem zit in Soratus.Agents.Telemetry — daar ontstaat de tekst en daar knipt de
        // bibliotheek nu bij het wegschrijven — maar die knip zit op het schrijfpad, en langs een
        // schrijfpad komt een fixture per definitie nooit. Dus staat de knip ook in de projectie,
        // en dát is wat hier wordt gemeten.
        //
        // Waarom deze test nodig is naast de vier hierboven: alle zeven termen in
        // Testlogregels.VerbodenInhoud staan in de fixture uitsluitend in extra, en de drie gewone
        // berichten zijn schone eenregelige zinnen. Die tests dekken dus het pad via extra en niets
        // anders, terwijl de projectie het bericht overneemt. In de opslag staat bij
        // bakker-voorraad-sync een regel van 3349 tekens met zestien frames — in msg, en msg is een
        // veld dat de klant hóórt te zien.
        Weergaven = new VastePortaalweergaven(metLangeBerichten: true);
        MeldKlantAan();

        var markup = RenderPagina(Agentdetail).Markup;

        // Eerst vaststellen dat de regel er werkelijk staat, en dat de eerste zin het heeft
        // gehaald. Zonder deze twee zou de test groen kunnen worden doordat de regel buiten beeld
        // viel of doordat het hele bericht is weggevallen, in plaats van doordat er is geknipt.
        Assert.Contains("payload.dump", markup, StringComparison.Ordinal);
        Assert.Contains(
            "De voorraadregel kon niet worden weggeschreven.",
            markup,
            StringComparison.Ordinal);

        var gelekt = new[] { Testlogregels.Bronpad, Testlogregels.Stacktrace }
            .Where(fragment => markup.Contains(fragment, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            gelekt.Length == 0,
            "Het bericht van een logregel zet onze stacktrace op het klantscherm. Dit staat in de " +
            $"markup:\n  {string.Join("\n  ", gelekt)}\n\n" +
            "Dat komt niet uit extra maar uit msg. Wordt deze test rood, dan is er precies één " +
            "ding aan de hand: de knip in de klantprojectie is weg of doet zijn werk niet meer. " +
            "Kijk in CustomerLogLine.From — daar hoort het bericht door CustomerMessage.FirstLine " +
            "te gaan — en in CustomerMessage.FirstLine zelf.\n\n" +
            "Repareer het niet in het scherm. De ellipsis in de berichtcel is beeld; de volledige " +
            "tekst staat in de paginabron, ongeacht wat er te zien is. En kap niet af op lengte: " +
            "dat verminkt een geldig bericht van 1417 tekens en laat een stacktrace nog deels " +
            "door. Zie de test hieronder, die precies dat vastlegt.\n\n" +
            "De bron van de lange berichten zit in Soratus.Agents.Telemetry, dat bij het " +
            "wegschrijven dezelfde knip doet. Die knip haalt de documenten niet die er al staan, " +
            "een agent op een oudere bibliotheekversie niet, en een agent die rechtstreeks naar " +
            "Cosmos schrijft niet. Daarom is deze projectieknip de plek waar het hier over gaat.");
    }

    [Fact]
    public void EenLangKlantberichtOpEenRegelBlijftHelemaalStaan()
    {
        // De tegenhanger, en de belangrijkste van de twee. Zonder deze test kapt iemand later "voor
        // de zekerheid" alsnog op lengte af, en dan gaat er een geldig bericht kapot in plaats van
        // een stacktrace. 1417 tekens is de langste legitieme eerste regel die over de
        // klantzichtbare logregels is gemeten; een grens van 200 of 500 zou hem middenin verminken
        // en tegelijk een stacktrace deels doorlaten. Dat is de gevaarlijkste keuze, want hij lijkt
        // de veilige ruime.
        Weergaven = new VastePortaalweergaven(metLangeBerichten: true);
        MeldKlantAan();

        var markup = RenderPagina(Agentdetail).Markup;
        var zin = Testlogregels.LangeZin;

        Assert.Equal(1417, zin.Length);
        Assert.DoesNotContain('\n', zin);
        Assert.DoesNotContain('\r', zin);

        Assert.True(
            markup.Contains(zin, StringComparison.Ordinal),
            $"Het bericht van {zin.Length} tekens staat niet volledig in de markup. Er is dus " +
            "ergens op lengte afgekapt, en dat verminkt een geldig bericht: dit is één " +
            "doorlopende zin zonder regelovergang, dus er is niets om op te knippen.\n\n" +
            "Kijk in CustomerMessage.MaxLength (8000, ruim boven de gemeten 1417) en in " +
            "CustomerMessage.FirstLine. Een grens in het middengebied is geen veilige keuze maar " +
            "de gevaarlijkste: hij verminkt geldig proza en laat een stacktrace nog deels door.\n\n" +
            $"Het slot dat ontbreekt is: \"{zin[^40..]}\"");

        // En er staat geen markering achter: er is niets afgevallen, dus "(ingekort)" zou een
        // onwaarheid zijn. Precies dít is de assertie die rood wordt als iemand de grens verlaagt
        // in plaats van weghaalt — dan staat het begin er nog wel.
        Assert.DoesNotContain(zin + " … (ingekort)", markup, StringComparison.Ordinal);
        Assert.EndsWith("EINDE-AFSTEMMING.", zin, StringComparison.Ordinal);
    }

    [Fact]
    public void HetKlanttypeVanEenLogregelHeeftGeenVeldMetVrijeJson()
    {
        // De structurele kant van dezelfde afspraak, en de enige die niet van een gerenderde pagina
        // afhangt. Komt er ooit een veld met vrije JSON op CustomerLogLine, dan is dat een besluit
        // over wat een klant mag zien en hoort het hier op te vallen — ook als er nog geen scherm
        // is dat het toont.
        var velden = typeof(CustomerLogLine)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(System.Text.Json.JsonElement)
                || p.PropertyType == typeof(System.Text.Json.JsonElement?)
                || p.PropertyType == typeof(System.Text.Json.JsonDocument))
            .Select(p => $"{p.Name} ({p.PropertyType.Name})")
            .ToArray();

        Assert.True(
            velden.Length == 0,
            "CustomerLogLine heeft een veld met vrije JSON:\n" +
            $"  {string.Join("\n  ", velden)}\n\n" +
            "Vrije JSON is niet te beoordelen op wat erin staat: het contract schrijft er niets " +
            "over voor en de agents zetten er koppelingdetails, bronpaden en klantnamen in. Op het " +
            "klanttype hoort zo'n veld daarom niet te bestaan. Wat de operator wél mag lezen staat " +
            "op LogRecord.");
    }

    [Fact]
    public void GeenKlantviewmodelVanEenLogregelDraagtErgensEenLogRecord()
    {
        // De vorige test kijkt naar één type; deze kijkt naar de hele graaf. Een CustomerLogLine
        // zonder Extra helpt niet als het viewmodel eromheen ergens nog een LogRecord meedraagt —
        // in een lijst, in een tuple, of als veld dat "even handig" was. Deze test maakt "er is
        // geen weg" tot een eigenschap van de typen in plaats van tot een gewoonte.
        var paden = new List<string>();

        foreach (var wortel in new[] { typeof(CustomerAgentLogsView), typeof(CustomerAgentLogTail) })
        {
            Zoek(wortel, wortel.Name, [], paden);
        }

        Assert.True(
            paden.Count == 0,
            "Een klantviewmodel van een logregel draagt een LogRecord mee:\n" +
            $"  {string.Join("\n  ", paden)}\n\n" +
            "LogRecord heeft Extra, en Extra is vrije JSON met koppelingdetails, bronpaden en de " +
            "namen van andere klanten erin. Op het klantpad hoort dat type niet voor te komen — " +
            "ook niet drie lagen diep, want het viewmodel is precies de plek waar de scheiding " +
            "hoort te liggen. Zet er een klanttype tussen, zoals CustomerLogLine.");
    }

    /// <summary>
    /// Loopt de eigenschappen van een type af op zoek naar een <c>LogRecord</c>, ook in lijsten.
    /// </summary>
    /// <param name="type">Het type dat wordt onderzocht.</param>
    /// <param name="pad">Het pad ernaartoe, voor de foutmelding.</param>
    /// <param name="gezien">Typen die al zijn bekeken, tegen kringetjes.</param>
    /// <param name="treffers">Waar de gevonden paden in komen.</param>
    private static void Zoek(Type type, string pad, HashSet<Type> gezien, List<string> treffers)
    {
        if (type == typeof(LogRecord))
        {
            treffers.Add(pad);
            return;
        }

        // Alleen onze eigen typen aflopen. Een string of een DateTimeOffset heeft geen LogRecord in
        // zich, en de graaf van het framework in gaan levert een oneindige zoektocht op. Generieke
        // typen wél openmaken: IReadOnlyList<LogRecord> is precies de vorm waarin hij mee zou
        // liften.
        if (type.Assembly != typeof(CustomerLogLine).Assembly
            && type.Assembly != typeof(LogRecord).Assembly)
        {
            if (!type.IsGenericType)
            {
                return;
            }

            foreach (var argument in type.GetGenericArguments())
            {
                Zoek(argument, $"{pad}<{argument.Name}>", gezien, treffers);
            }

            return;
        }

        if (!gezien.Add(type))
        {
            return;
        }

        foreach (var eigenschap in type.GetProperties())
        {
            Zoek(eigenschap.PropertyType, $"{pad}.{eigenschap.Name}", gezien, treffers);
        }
    }
}
