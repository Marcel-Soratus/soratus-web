using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// Wat er in de titelbalk van een klantgebruiker terechtkomt.
/// </summary>
/// <remarks>
/// <para><strong>Dit is een blinde vlek in de meetmethode en niet een gat in één pagina.</strong>
/// Het vangnet in <see cref="KlantVangnetTests"/> zoekt verboden woorden in de gerenderde markup van
/// elke pagina, en een <c>&lt;PageTitle&gt;</c> rendert niet in die markup: hij rendert in de
/// <c>HeadOutlet</c>, een heel andere plek in de boom. Het woord "Nieuwe klant" kon dus in de
/// titelbalk van een klant staan terwijl er in <c>cut.Markup</c> niets van te zien was.</para>
///
/// <para>Dat is gemeten en niet bedacht: het aanmaakformulier is tijdelijk stukgemaakt door zijn
/// <c>PageTitle</c> buiten de rolcontrole te zetten, en geen enkele test werd rood. Van de negen
/// mutaties in dit werk was dat de enige die niets raakte, en daarom de enige die iets nieuws
/// opleverde.</para>
///
/// <para><strong>Waarom er twee vastgelegde lijsten in dit bestand staan.</strong> Beide theorieën
/// hieronder slaan een pagina over die niet in hun geval valt — de eerste kijkt alleen naar pagina's
/// die voor een klant niets renderen, de tweede alleen naar pagina's die een titel zetten. Een
/// theorie die alles overslaat is groen en meet niets, en dat is precies het valse groen waar dit
/// portaal al eerder in is gelopen. De lijsten leggen daarom vast wélke pagina's in welk geval
/// vallen. Verhuist een pagina van de ene groep naar de andere, dan is dat een beslissing over
/// zichtbaarheid en gaat er iets rood.</para>
/// </remarks>
public class PaginatitelTests : Portaalrendertest
{
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

    [Theory]
    [MemberData(nameof(Paginas))]
    public void EenPaginaDieVoorEenKlantNietsRendertZetVoorHemOokGeenTitel(Type pagina)
    {
        MeldKlantAan();

        var cut = RenderPagina(pagina);

        if (!string.IsNullOrWhiteSpace(cut.Markup))
        {
            // Deze pagina heeft voor een klant wél inhoud, dus een titel hoort erbij. Wat er in die
            // titel mag staan is de theorie hieronder. Welke pagina's hier langskomen en welke niet
            // staat vast in DePaginasDieVoorEenKlantNietsRenderenStaanVast.
            return;
        }

        var titels = cut.FindComponents<PageTitle>();

        Assert.True(
            titels.Count == 0,
            $"De pagina {pagina.Name} " +
            $"({string.Join(", ", Paginaverzameling.Routes(pagina))}) rendert voor een " +
            $"klantgebruiker geen inhoud, maar zet wel een paginatitel: \"{Titel(cut)}\".\n\n" +
            "Een PageTitle rendert in de HeadOutlet en niet in de markup van de pagina, dus het " +
            "vangnet op verboden woorden ziet hem niet — maar de gebruiker ziet hem wel, in zijn " +
            "tabblad en in zijn geschiedenis. Rendert een pagina voor een rol niets, dan hoort ook " +
            "de titel binnen die rolcontrole te staan: wat niet wordt gerenderd kan niet lekken.\n\n" +
            "NieuweKlant doet dat al — daar staat de PageTitle binnen de rolcontrole, met die reden " +
            "erbij in de opmerking. Eén regel verplaatsen is genoeg.");
    }

    [Theory]
    [MemberData(nameof(Paginas))]
    public void DeTitelDieEenKlantKrijgtBevatGeenOperatorwoord(Type pagina)
    {
        // De tweede helft van dezelfde blinde vlek. Een pagina die voor een klant wél inhoud heeft
        // mag een titel zetten, maar in die titel gelden dezelfde woorden als in de markup — en tot
        // nu toe keek er niets naar.
        MeldKlantAan();

        var titel = Titel(RenderPagina(pagina));

        if (titel.Length == 0)
        {
            return;
        }

        foreach (var woord in KlantVangnetTests.VerbodenWoorden)
        {
            Assert.False(
                titel.Contains(woord, StringComparison.OrdinalIgnoreCase),
                $"De paginatitel die {pagina.Name} aan een klantgebruiker geeft is \"{titel}\" en " +
                $"daarin staat het woord \"{woord}\".\n\n" +
                "Volgens §2 ziet een klant dat woord nergens, en een titel is niet minder zichtbaar " +
                "dan de pagina: hij staat in het tabblad, in de geschiedenis en in een bladwijzer. " +
                "Het vangnet op de markup ziet hem alleen niet, want een PageTitle rendert in de " +
                "HeadOutlet.");
        }
    }

    [Fact]
    public void DePaginasDieVoorEenKlantNietsRenderenStaanVast()
    {
        // Zonder deze lijst kan de eerste theorie elke pagina overslaan en toch groen zijn. Hij legt
        // vast wat een klantgebruiker helemaal niet te zien krijgt; dat is een beslissing en geen
        // toevalligheid.
        Assert.Equal(
            [
                "Soratus.Portal.Components.Pages.NieuweKlant",
                "Soratus.Portal.Components.Pages.Overzicht",
                "Soratus.Portal.Components.Pages.Start",
            ],
            Groep(cut => string.IsNullOrWhiteSpace(cut.Markup)));
    }

    [Fact]
    public void DePaginasWaarvanEenKlantDeTitelTeZienKrijgtStaanVast()
    {
        // En zonder deze lijst kan de tweede theorie alles overslaan. Hij legt bovendien iets vast
        // wat de theorie zelf niet kan zeggen: dat er werkelijk titels worden uitgelezen. Zou de
        // uitlezing stilvallen — een andere manier om een titel te zetten, een gewijzigde
        // component — dan staat hier nul en gaat deze test rood in plaats van dat het vangnet
        // stilletjes niets meer meet.
        //
        // Dit is de groep die de tweede theorie meet: inhoud én een titel. Pagina's die voor een
        // klant niets renderen horen hier niet in — die staan onder de eerste theorie, en dat er
        // vandaag twee zijn die tóch een titel zetten is een bevinding en geen groep.
        Assert.Equal(
            [
                "Soratus.Portal.Components.Pages.Error",
                "Soratus.Portal.Components.Pages.Klant.AgentDetail",
                "Soratus.Portal.Components.Pages.Klant.Agents",
                "Soratus.Portal.Components.Pages.Klant.Contract",

                // Fase 3. Het urenscherm rendert voor een klant inhoud — zijn verbruik tegen de
                // bundel — en zet dus een titel ("Uren · Acme Logistiek · Agent Portal"). Wat er
                // niet in staat is een woord uit de fiatteringsstroom; dat wordt door de theorie
                // hierboven gemeten en niet door deze lijst.
                "Soratus.Portal.Components.Pages.Klant.Uren",
                "Soratus.Portal.Components.Pages.NotFound",
            ],
            Groep(cut => Titel(cut).Length > 0 && !string.IsNullOrWhiteSpace(cut.Markup)));
    }

    [Fact]
    public void HetGereedschapLeestDeTitelVanEenPaginaEcht()
    {
        // De onmisbare tegenhanger van de theorie op verboden woorden: die kijkt of er iets níet in
        // een titel staat, en dat is alleen iets waard als er een titel te lezen valt. Een
        // uitlezing die altijd leeg teruggeeft maakt hem groen over elke pagina.
        MeldKlantAan();

        var titel = Titel(RenderPagina(
            Paginaverzameling.MetRoute("/klant/{Slug}/contract")
            ?? throw new InvalidOperationException(
                "Er staat geen pagina op route '/klant/{Slug}/contract'. Deze test heeft een pagina " +
                "nodig die voor een klant een titel zet; is de route hernoemd, kies dan een andere.")));

        Assert.Contains("Contract", titel, StringComparison.Ordinal);
        Assert.Contains("Acme Logistiek", titel, StringComparison.Ordinal);
        Assert.Contains("Agent Portal", titel, StringComparison.Ordinal);
    }

    /// <summary>
    /// De volledige namen van de pagina's die voor een klantgebruiker aan deze voorwaarde voldoen.
    /// </summary>
    /// <param name="voorwaarde">De vraag over de gerenderde pagina.</param>
    /// <returns>De namen, gesorteerd.</returns>
    /// <remarks>
    /// Eén aanmelding en daarna elke pagina renderen. Dat kan hier omdat er niets wordt geschreven:
    /// deze tests kijken alleen naar wat er uitkomt.
    /// </remarks>
    private string[] Groep(Func<IRenderedComponent<Bunit.Rendering.ContainerFragment>, bool> voorwaarde)
    {
        ArgumentNullException.ThrowIfNull(voorwaarde);

        MeldKlantAan();

        return
        [
            .. Paginaverzameling.Alle()
                .Where(pagina => voorwaarde(RenderPagina(pagina)))
                .Select(pagina => pagina.FullName!)
                .OrderBy(naam => naam, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// De tekst van de paginatitel, of een lege tekenreeks als de pagina er geen zet.
    /// </summary>
    /// <param name="cut">De gerenderde pagina.</param>
    /// <returns>De titeltekst.</returns>
    /// <remarks>
    /// <para>Een <c>PageTitle</c> rendert zijn inhoud in de <c>HeadOutlet</c>, dus in de markup van
    /// de pagina staat hij niet en in bUnit is er geen outlet om hem in te vangen. Wat er wél is, is
    /// het component zelf met zijn <c>ChildContent</c>: die wordt hier los gerenderd, en dat levert
    /// precies de tekst op die de browser in de titelbalk zet.</para>
    ///
    /// <para>Meerdere titels worden samengevoegd. In productie wint de laatste, maar voor de vraag
    /// "staat dit woord in wat een klant ziet" is elke titel er één te veel.</para>
    /// </remarks>
    private string Titel(IRenderedComponent<Bunit.Rendering.ContainerFragment> cut)
    {
        ArgumentNullException.ThrowIfNull(cut);

        var titels = cut.FindComponents<PageTitle>();

        return string.Join(
            " ",
            titels
                .Select(titel => titel.Instance.ChildContent)
                .Where(inhoud => inhoud is not null)
                .Select(inhoud => Render(inhoud!).Markup.Trim()));
    }
}
