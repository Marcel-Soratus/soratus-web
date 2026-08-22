using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Support;

/// <summary>
/// Wat er in de supportkant van de broncode niet mag staan.
/// </summary>
/// <remarks>
/// <para>Deze tests kijken naar tekst en niet naar gedrag, en dat is met opzet: ze dekken de gevallen
/// waarin een <em>toekomstige</em> wijziging het ontwerp ongedaan zou maken zonder dat een gedragstest
/// rood wordt. Dezelfde soort controle als <c>MailbroncodeTests</c> bij de mailkant.</para>
/// </remarks>
public class SupportbroncodeTests
{
    /// <summary>De bestanden van de supportkant: de map <c>Support/</c> en de vier razorbestanden.</summary>
    private static IReadOnlyList<FileInfo> Bestanden() =>
    [
        .. Broncode.Portaalbestanden()
            .Where(f =>
            {
                var pad = Broncode.RelatiefPad(f);

                return pad.StartsWith("Support/", StringComparison.Ordinal)
                    || pad.Contains("/Klant/Support", StringComparison.Ordinal)
                    || pad.Contains("/Klant/CustomerSupport", StringComparison.Ordinal)
                    || pad.Contains("/Klant/OperatorSupport", StringComparison.Ordinal);
            }),
    ];

    [Fact]
    public void ErZijnSupportbestandenGevonden()
    {
        // Zonder deze test staat alles hieronder groen zodra de zoektocht niets meer vindt — bij een
        // hernoemde map, bijvoorbeeld. Een controle met een gat erin is geen controle.
        var bestanden = Bestanden();

        Assert.True(
            bestanden.Count >= 8,
            "Er zijn maar " + bestanden.Count + " supportbestanden gevonden. De controles hieronder "
            + "meten dan bijna niets terwijl ze groen blijven. Controleer of de map nog Support/ heet.");
    }

    [Fact]
    public void ErStaatGeenMarkupStringInDeSupportkant()
    {
        // De tekst in een bubbel komt van een klant. Blazor escapet een @-expressie; een MarkupString
        // doet dat niet, en dan is één regel genoeg om van de draad een injectiepunt te maken.
        Verboden("MarkupString");
    }

    [Fact]
    public void ErKomtGeenUitzonderingstekstInEenBericht()
    {
        // Punt 13 en 14 van de fase-0-afwijkingen: een reden die als tekst reist komt uit een
        // catch-blok. In deze map reizen redenen als enum, en dit is de controle dat dat zo blijft.
        //
        // Let op wat er níet onder valt: het loggen van een uitzondering. Dat mag en het hoort —
        // SupportDesk logt de uitzondering van de naad met de volledige stacktrace. De controle
        // hieronder gaat over de tekst van een bericht, en die is te onderscheiden doordat elke
        // berichttekst uit SupportText komt.
        foreach (var bestand in Bestanden())
        {
            var pad = Broncode.RelatiefPad(bestand);

            if (pad is "Support/SupportDesk.cs")
            {
                // De enige plek die een uitzondering aanraakt, en hij zet hem in een logregel. Wat
                // daar niet mag is de melding als tekst gebruiken.
                var inhoud = Code(bestand);

                Assert.DoesNotContain("exception.Message", inhoud, StringComparison.Ordinal);
                Assert.DoesNotContain("exception.ToString", inhoud, StringComparison.Ordinal);
                continue;
            }

            var tekst = Code(bestand);

            // ".Message" staat er niet bij, en dat is gemeten en niet bedacht: OperatorSupport.razor
            // heeft een lusvariabele "message" en las dus als een treffer. Een controle die op elke
            // eigenschap die Message heet afgaat, is binnen een week weg omdat hij te vaak onterecht
            // rood staat. Wat er wél in staat zijn de vormen die uit een catch-blok komen.
            foreach (var woord in new[]
            {
                "StackTrace",
                "Exception.Message",
                "exception.Message",
                "ex.Message",
                "ErrorCode",
            })
            {
                Assert.False(
                    tekst.Contains(woord, StringComparison.Ordinal),
                    $"In {pad} staat \"{woord}\". Punt 13 en 14 van de fase-0-afwijkingen gaan over "
                    + "precies die klasse fout: tekst uit een catch-blok die bij een klant belandt. In "
                    + "deze map reizen redenen als enum. Moet er iets gelogd worden, dan hoort dat in "
                    + "SupportDesk en niet in een berichttekst.");
            }
        }
    }

    [Fact]
    public void ErStaatGeenRolvoorwaardeInDeWeergavecomponenten()
    {
        // Het rolverschil is een typeverschil. Een @if op een rol in de markup is afgekeurd: een
        // vergeten @if lekt, een ontbrekende property niet.
        foreach (var naam in new[]
        {
            "Components/Pages/Klant/CustomerSupport.razor",
            "Components/Pages/Klant/OperatorSupport.razor",
            "Components/Pages/Klant/SupportThread.razor",
        })
        {
            var bestand = Bestanden().Single(f =>
                string.Equals(Broncode.RelatiefPad(f), naam, StringComparison.Ordinal));

            var inhoud = Code(bestand);

            foreach (var woord in new[] { "IsInRole", "AuthorizeView", "isOperator", "PortalRoles" })
            {
                Assert.False(
                    inhoud.Contains(woord, StringComparison.Ordinal),
                    $"In {naam} staat \"{woord}\". Het rolverschil van dit scherm hoort in het "
                    + "viewmodel te zitten en niet in de markup: CustomerSupportView heeft geen "
                    + "escalatieredenen en OperatorSupportView heeft geen eerstelijnstoestand.");
            }
        }
    }

    [Fact]
    public void HetMerktekenStaatAlleenInDeBubbelcomponentEnAltijdNaastEenBron()
    {
        // §3.8: elke AI-bubbel toont het merkteken én de bron. Deze controle dekt het geval dat iemand
        // het merkteken ergens anders neerzet — in een kaartkop, in een tooltip — waar geen bronregel
        // naast staat. Het merkteken hoort in de twee AI-takken van SupportThread.razor en nergens
        // anders in de markup.
        var metMerkteken = Bestanden()
            .Where(f => f.Extension == ".razor")
            .Where(f => Code(f).Contains("SupportText.FirstLineBadge", StringComparison.Ordinal))
            .Select(Broncode.RelatiefPad)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Components/Pages/Klant/SupportThread.razor"], metMerkteken);

        // En de letterlijke tekst staat nergens in de markup: alleen de constante. Twee schrijfwijzen
        // van hetzelfde merkteken gaan schuiven.
        foreach (var bestand in Bestanden().Where(f => f.Extension == ".razor"))
        {
            Assert.DoesNotContain("AI · eerstelijn", Code(bestand), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DeMailkantWeetNietsVanDeSupportdraad()
    {
        // §29.4, punt 3: de urenspecificatie staat niet in de mail, omdat een mail de enige plek is
        // waar vrije tekst buiten het bereik van een operator komt. Een supportbericht is precies zulke
        // tekst — van een klant, en van ons. Vandaag raakt geen mailpad de draad, en deze controle houdt
        // dat zo: wie er ooit een mailmelding bij wil, komt hier langs en leest waarom.
        var mailmap = new DirectoryInfo(Path.Combine(Broncode.Portaalproject.FullName, "Mail"));

        Assert.True(mailmap.Exists, "De map Mail/ bestaat niet meer; deze controle meet dan niets.");

        foreach (var bestand in mailmap.EnumerateFiles("*.*", SearchOption.AllDirectories)
            .Where(f => f.Extension is ".cs" or ".razor"))
        {
            var inhoud = Code(bestand);

            Assert.False(
                inhoud.Contains("Portal.Support", StringComparison.Ordinal)
                || inhoud.Contains("SupportMessage", StringComparison.Ordinal)
                || inhoud.Contains("ISupportStore", StringComparison.Ordinal),
                $"{bestand.Name} in Mail/ verwijst naar de supportkant. Een supportbericht is vrije "
                + "tekst van een klant en van ons; in een postbus staat die tekst definitief, en dat is "
                + "de reden dat §29.4 de urenspecificatie uit de mail heeft gehouden. Wil er echt een "
                + "mailmelding bij een nieuw bericht, dan hoort in dezelfde wijziging te staan wat er "
                + "wél en niet in die mail komt.");
        }
    }

    [Fact]
    public void DeOpslagWijzigtGeenBestaandBericht()
    {
        // Een draad is een verslag. "Dit hebben jullie mij geantwoord" is een vraag die maanden later
        // komt, en een antwoord dat achteraf te wijzigen is maakt van dat verslag een bewering zonder
        // bron.
        var store = Bestanden().Single(f =>
            string.Equals(Broncode.RelatiefPad(f), "Support/CosmosSupportStore.cs", StringComparison.Ordinal));

        var inhoud = Code(store);

        foreach (var aanroep in new[] { "ReplaceItemAsync", "UpsertItemAsync", "DeleteItemAsync", "PatchItemAsync" })
        {
            Assert.False(
                inhoud.Contains(aanroep, StringComparison.Ordinal),
                $"CosmosSupportStore roept {aanroep} aan. Er hoort geen pad te zijn dat een bestaand "
                + "bericht aanraakt: alle drie de schrijfmethoden doen CreateItemAsync, en een botsing "
                + "op de afgeleide sleutel is precies het antwoord dat we willen.");
        }

        Assert.Contains("CreateItemAsync", inhoud, StringComparison.Ordinal);
    }

    [Fact]
    public void DeEchteOpslagLeestNieuwsteEerstEnDraaitDeDraadTerugOmNaarOudsteEerst()
    {
        // Dit gat is door de mutatieronde gevonden en het is structureel. De gedragstest op de ordening
        // draait op Vasteportaalopslag, en die heeft zijn eigen kopie van de ordening; een mutatie die
        // page.Reverse() uit CosmosSupportStore haalde, maakte niets rood. De echte opslag is zonder
        // Cosmos niet te oefenen, dus dit is wat er wél te meten valt: dat de twee helften van die
        // ordening er staan.
        //
        // Dat is zwakker dan gedrag en het staat zo in het rapport. Het dekt de mutatie -- iemand die
        // een van de twee weghaalt -- en niet de fout die erin blijft zitten.
        var store = Bestanden().Single(f =>
            string.Equals(
                Broncode.RelatiefPad(f),
                "Support/CosmosSupportStore.cs",
                StringComparison.Ordinal));

        var inhoud = Code(store);

        Assert.Contains("ORDER BY c.id DESC", inhoud, StringComparison.Ordinal);
        Assert.Contains("page.Reverse();", inhoud, StringComparison.Ordinal);
        Assert.Contains("c.id < @before", inhoud, StringComparison.Ordinal);
    }

    private static void Verboden(string woord)
    {
        foreach (var bestand in Bestanden())
        {
            Assert.False(
                Code(bestand).Contains(woord, StringComparison.Ordinal),
                $"In {Broncode.RelatiefPad(bestand)} staat \"{woord}\".");
        }
    }

    /// <summary>
    /// De code van een bestand zonder commentaar.
    /// </summary>
    /// <param name="bestand">Het bestand.</param>
    /// <returns>De inhoud met de commentaarblokken eruit.</returns>
    /// <remarks>
    /// <para><strong>Deze functie bestaat omdat de eerste versie van deze controles op de hele
    /// bestandsinhoud zocht, en drie van hen daardoor rood stonden op hun eigen toelichting.</strong>
    /// In <c>SupportThread.razor</c> staat met zoveel woorden "er staat geen MarkupString in dit
    /// bestand", en in <c>CosmosSupportStore.cs</c> staat "er is geen ReplaceItemAsync in deze klasse".
    /// Dat is precies de documentatie die dit project wil hebben, en een controle die haar verbiedt is
    /// de verkeerde kant op.</para>
    ///
    /// <para>Het alternatief -- de opmerkingen herschrijven zodat het woord er niet in staat -- is
    /// afgewezen: dan is de reden niet meer te vinden door wie het woord zoekt.</para>
    ///
    /// <para>Dit is geen ontleder en hoeft dat niet te zijn. Hij haalt <c>@* *@</c>-blokken,
    /// <c>/* */</c>-blokken en regels die met <c>//</c> beginnen weg. Wat hij mist -- een <c>//</c>
    /// halverwege een regel met code ervoor -- is een geval dat in deze bestanden niet voorkomt en dat
    /// bij een vals-negatief hoogstens iets doorlaat; nooit iets verbiedt.</para>
    /// </remarks>
    private static string Code(FileInfo bestand)
    {
        var inhoud = File.ReadAllText(bestand.FullName);

        // De regels met commentaar eruit vóór de blokken, en dat is gemeten en niet bedacht: in
        // CosmosSupportStore staat in een XML-opmerking het indexpad "/*" van Cosmos, en een zoektocht
        // naar blokcommentaar zag daar een openend blok. Het gevolg was erger dan een valse treffer —
        // de rest van het bestand viel weg, en dan meet deze controle niets meer terwijl hij groen kan
        // staan. Regels eerst, dan blokken.
        inhoud = string.Join(
            "\n",
            inhoud
                .Split('\n')
                .Where(regel => !regel.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        inhoud = Verwijder(inhoud, "@*", "*@");

        return Verwijder(inhoud, "/*", "*/");
    }

    /// <summary>
    /// Haalt de blokken tussen twee markeringen weg.
    /// </summary>
    /// <param name="inhoud">De inhoud.</param>
    /// <param name="open">De openende markering.</param>
    /// <param name="sluit">De sluitende markering.</param>
    /// <returns>De inhoud zonder die blokken.</returns>
    /// <remarks>
    /// <para><strong>Een openende markering zonder sluitende kapt het bestand niet af.</strong> Dat was
    /// de eerste versie en die was gevaarlijk: een niet-gesloten markering — een "/*" in een string of
    /// in een opmerking — liet de rest van het bestand verdwijnen, en dan slaagt elke controle
    /// hieronder omdat er niets meer staat. Nu wordt in dat geval alleen de markering zelf weggehaald.
    /// Dat is de goede kant om fout te zitten: hoogstens laat deze functie iets door, nooit verbiedt
    /// hij iets en nooit stopt hij met meten.</para>
    /// </remarks>
    private static string Verwijder(string inhoud, string open, string sluit)
    {
        for (var start = inhoud.IndexOf(open, StringComparison.Ordinal);
            start >= 0;
            start = inhoud.IndexOf(open, StringComparison.Ordinal))
        {
            var eind = inhoud.IndexOf(sluit, start + open.Length, StringComparison.Ordinal);

            inhoud = eind < 0
                ? inhoud[..start] + " " + inhoud[(start + open.Length)..]
                : inhoud[..start] + inhoud[(eind + sluit.Length)..];
        }

        return inhoud;
    }
}
