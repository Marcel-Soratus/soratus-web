using System.Text.RegularExpressions;
using Bunit;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// Het vangnet op tekst: geen enkele pagina toont een klant een woord dat alleen bij de operator
/// hoort.
/// </summary>
/// <remarks>
/// <para>De paginalijst komt uit reflectie over de <c>@page</c>-componenten en niet uit een lijst
/// die iemand bijhoudt. Een nieuwe pagina valt daarmee automatisch onder dit vangnet — ook de
/// pagina waarvan nog niemand heeft nagedacht wat een klant erop mag zien.</para>
///
/// <para><strong>Dit is geen beveiliging</strong>, zie de uitleg bij
/// <see cref="Portaalrendertest"/>. Het is een vangnet tegen ongelukken; de echte grens ligt in de
/// datalaag en bij de autorisatie op de endpoints. Een test die naar markup kijkt, kijkt naar het
/// laatste station.</para>
/// </remarks>
public class KlantVangnetTests : Portaalrendertest
{
    /// <summary>
    /// Woorden die uitsluitend bij de operator horen (§2 van de spec).
    /// </summary>
    /// <remarks>
    /// Hoofdletterongevoelig, want "Fiatteren" in een knop en "te fiatteren" in een tabelkop zijn
    /// allebei mis. Woordgrenzen eromheen, zodat "Koppelingen" wel aanslaat en een woord waar het
    /// toevallig in zit niet.
    /// </remarks>
    public static readonly string[] VerbodenWoorden =
    [
        "fiatteren",
        "Koppelingen",
        "beheeropslag",
        "Nieuwe klant",
        "uren boeken",
    ];

    /// <summary>Elke routeerbare pagina van het portaal.</summary>
    public static TheoryData<Type> Paginas
    {
        get
        {
            var data = new TheoryData<Type>();
            foreach (var pagina in Paginaverzameling.Alle())
            {
                data.Add(pagina);
            }

            return data;
        }
    }

    [Fact]
    public void ErZijnPaginasGevondenOmTeControleren()
    {
        // Zonder deze test blijft alles hieronder groen zodra de reflectie niets meer vindt —
        // bijvoorbeeld omdat de assembly of het routeattribuut verandert. Een vangnet met een gat
        // erin is geen vangnet.
        var paginas = Paginaverzameling.Alle();

        Assert.True(
            paginas.Count > 0,
            "Er is geen enkele @page-component gevonden in Soratus.Portal. Het vangnet op " +
            "zichtbaarheid controleert dan niets meer terwijl het groen blijft. Controleer of " +
            "Paginaverzameling nog naar de juiste assembly kijkt.");
    }

    [Theory]
    [MemberData(nameof(Paginas))]
    public void EenKlantZietNergensEenWoordDatAlleenBijDeOperatorHoort(Type pagina)
    {
        MeldKlantAan();

        var markup = RenderPagina(pagina).Markup;

        foreach (var woord in VerbodenWoorden)
        {
            Assert.False(
                Bevat(markup, woord),
                $"De pagina {pagina.Name} ({string.Join(", ", Paginaverzameling.Routes(pagina))}) " +
                $"toont een klantgebruiker het woord \"{woord}\".\n\n" +
                "Volgens §2 van de spec ziet een klant nooit: te fiatteren regels, " +
                "fiatteer-acties, het boekformulier, koppelingdetails, de Azure-uitsplitsing per " +
                "dienst en de beheeropslag. Dit is een vangnet tegen ongelukken en geen " +
                "beveiliging — de echte grens ligt in de datalaag — maar dit vangnet gaat er hier " +
                "wel doorheen.\n\n" +
                "Hoort het blok bij de operator? Bouw het verschil in het viewmodel, niet in een " +
                "@if: een ontbrekende property kan niet lekken, een vergeten @if wel.");
        }
    }

    [Theory]
    [MemberData(nameof(Paginas))]
    public void ElkePaginaRendertOokEchtIetsVoorEenOperator(Type pagina)
    {
        // De onmisbare tegenhanger van de test hierboven. Een pagina die stukgaat of leeg blijft
        // toont ook geen verboden woorden, en dan is afwezigheid geen bewijs meer van iets. Deze
        // test bewijst dat de pagina's werkelijk iets renderen, zodat de stilte bij de klant een
        // keuze is en geen storing.
        //
        // Voor een klantgebruiker geldt dit met opzet níet: een operator-only pagina hoort voor
        // hem juist helemaal leeg te zijn. Zie OperatorZichtbaarheidTests, waar per pagina staat
        // welke onderdelen er voor wie horen te zijn.
        MeldOperatorAan();

        var markup = RenderPagina(pagina).Markup;

        Assert.True(
            !string.IsNullOrWhiteSpace(markup) || IsDoorgestuurd(),
            $"De pagina {pagina.Name} " +
            $"({string.Join(", ", Paginaverzameling.Routes(pagina))}) rendert niets voor een " +
            "operator en stuurt ook niet door. Zolang dat zo is, zegt het vangnet op verboden " +
            "woorden niets: dat blijft groen zolang er niets staat.");
    }

    [Fact]
    public void GeenTweePaginasDelenDezelfdeRoute()
    {
        // De Blazor-router bouwt zijn routetabel bij het opstarten en werpt op een dubbele route.
        // Dat is geen zichtbaarheidsprobleem maar het breekt élke pagina tegelijk, en het is met
        // reflectie te zien zonder de app te starten.
        var dubbel = Paginaverzameling.Alle()
            .SelectMany(p => Paginaverzameling.Routes(p).Select(r => (Route: r, Pagina: p)))
            .GroupBy(x => x.Route, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} → {string.Join(" én ", g.Select(x => x.Pagina.FullName))}")
            .ToArray();

        Assert.True(
            dubbel.Length == 0,
            "Twee of meer pagina's staan op dezelfde route:\n" + string.Join("\n", dubbel) +
            "\n\nDe Router in Routes.razor bouwt zijn routetabel uit de assembly en werpt bij het " +
            "eerste verzoek een InvalidOperationException over ambigue routes. Het portaal geeft " +
            "dan op elke URL een fout, ook op /healthz-vreemde paden — de uitrolpijplijn ziet het " +
            "pas als de smoke test faalt. Haal de overbodige @page weg.");
    }

    [Fact]
    public void HetVangnetVindtEenVerbodenWoordAlsHetErStaat()
    {
        // Bewijs dat de zoekfunctie meet wat hij belooft. Zonder deze test weet je niet of de
        // groene tests hierboven groen zijn omdat er niets staat, of omdat er niet wordt gekeken.
        Assert.True(Bevat("<button>Fiatteren</button>", "fiatteren"));
        Assert.True(Bevat("<th>+ 3 u te fiatteren</th>", "fiatteren"));
        Assert.True(Bevat("<h2>Koppelingen</h2>", "Koppelingen"));
        Assert.True(Bevat("<span>Beheeropslag 8%</span>", "beheeropslag"));
        Assert.True(Bevat("<a>Nieuwe klant</a>", "Nieuwe klant"));
        Assert.True(Bevat("<legend>Uren boeken</legend>", "uren boeken"));
    }

    [Fact]
    public void HetVangnetSlaatNietAanOpEenWoordWaarHetToevalligInZit()
    {
        // Woordgrenzen, zodat "koppeling" in een gewone zin geen vals alarm geeft en de test niet
        // binnen een week wordt weggehaald omdat hij te vaak onterecht rood staat.
        Assert.False(Bevat("<p>ontkoppelingen</p>", "Koppelingen"));
        Assert.False(Bevat("<p>affiatteren-achtig</p>", "fiatteren"));
    }

    /// <summary>
    /// Of deze markup dit woord bevat, hoofdletterongevoelig en op woordgrenzen.
    /// </summary>
    /// <param name="markup">De gerenderde markup.</param>
    /// <param name="woord">Het woord of de woordgroep.</param>
    /// <returns><c>true</c> als het erin staat.</returns>
    private static bool Bevat(string markup, string woord) =>
        Regex.IsMatch(
            markup,
            $@"\b{Regex.Escape(woord).Replace(@"\ ", @"\s+", StringComparison.Ordinal)}\b",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(5));
}
