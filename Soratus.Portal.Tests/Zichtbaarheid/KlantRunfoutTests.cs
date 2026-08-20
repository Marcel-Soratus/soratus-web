using Bunit;
using Soratus.Agents.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// De typenaam van de uitzondering op een mislukte run is operator-only, en deze tests zijn elkaars
/// spiegel: de klant vindt hem nergens, de operator vindt hem wél.
/// </summary>
/// <remarks>
/// <para><strong>Waarom het spiegelpaar en niet één test.</strong> Gemeten in de echte opslag staan er
/// drie waarden in <c>errorType</c> en alle drie bevatten een naamruimte:
/// <c>SoratusAgent.Sync.ValidationException</c> en <c>SoratusAgent.Mail.ClassificationException</c> op
/// documenten van échte klanten, en <c>System.Net.Http.HttpRequestException</c> bij de interne
/// beheerklant. Een klant doet met een .NET-typenaam niets — hij moet weten dát de run mislukte en of
/// er werk blijft liggen, en dat staat in de foutmelding. Voor de operator ís de naamruimte juist het
/// nuttige deel: <c>Sync.ValidationException</c> is een ander defect dan
/// <c>Mail.ValidationException</c>.</para>
///
/// <para>Een test die alleen de klantkant controleert kan daarom om twee heel verschillende redenen
/// groen staan: omdat de scheiding werkt, of omdat de typenaam nergens meer bestaat. In de uitvoer
/// zien die twee er identiek uit, en de tweede is een verlies: dan is de diagnose weg terwijl de test
/// tevreden is. Daarom staat de operatorkant er als tweede test naast — samen leggen ze niet vast dat
/// er iets ontbreekt, maar dat het op precies één plek staat.</para>
///
/// <para><strong>Waarom afkorten geen oplossing is.</strong> De korte naam na de laatste punt levert
/// <c>ValidationException</c> op. Voor een klant is dat even betekenisloos als de volledige naam, dus
/// het lost niets op; voor de operator gooit het het onderscheid weg dat hij nodig heeft. Dat is het
/// verschil met <c>errorMessage</c>, waar afkappen de informatie <em>verplaatst</em> in plaats van
/// weggooit — de rest blijft operator-only bewaard. <c>Testruns.VerbodenInhoud</c> heeft de korte naam
/// er daarom los in staan: die afkorting hoort de klanttest rood te maken en niet groen.</para>
///
/// <para><strong>Het runtabblad rendert statisch.</strong> Er staat geen <c>@rendermode</c> op de
/// pagina — alleen het logtabblad is een interactief eiland — dus wat er in de eerste render staat,
/// staat in de HTML die de browser krijgt. Deze tests kijken naar de hele markup en niet naar de
/// zichtbare tekst: een <c>title</c> is precies waar de typenaam in zat.</para>
/// </remarks>
public class KlantRunfoutTests : Portaalrendertest
{
    /// <summary>Het agentdetail.</summary>
    private static Type Agentdetail =>
        Paginaverzameling.MetNaam("Soratus.Portal.Components.Pages.Klant.AgentDetail")
        ?? throw new InvalidOperationException(
            "De pagina AgentDetail is niet gevonden. Is hij hernoemd of verplaatst, dan hoort " +
            "deze test mee te verhuizen — hij is de enige die naar de foutvelden van een run " +
            "kijkt.");

    /// <summary>
    /// Rendert het agentdetail met het runtabblad open.
    /// </summary>
    /// <returns>De markup van de pagina.</returns>
    /// <remarks>
    /// Het tabblad komt uit de querystring (<c>?tab=runs</c>) en niet uit een parameter, want zo werkt
    /// de pagina ook: de tabs zijn links met <c>?tab=…</c>, deelbaar en werkend met de terugknop. Zou
    /// deze test het tabblad rechtstreeks zetten, dan meet hij een ingang die geen bezoeker heeft.
    /// </remarks>
    private string RunmarkupNaAanmelden()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(
            $"/klant/{Paginaverzameling.Klantslug}/agents/{Paginaverzameling.Agentnaam}?tab=runs");

        return RenderPagina(Agentdetail).Markup;
    }

    [Fact]
    public void EenKlantZietZijnMislukteRunMetDeFoutmeldingErbij()
    {
        // De onmisbare tegenhanger van de test hieronder: die kijkt of er iets níet staat, en dat is
        // alleen iets waard als er wél iets staat. Faalt deze test, dan zegt de andere niets meer —
        // dan is het tabblad gewoon leeg.
        MeldKlantAan();

        var markup = RunmarkupNaAanmelden();

        Assert.Contains("Mislukt", markup, StringComparison.Ordinal);
        Assert.Contains("r-8f3c", markup, StringComparison.Ordinal);
        Assert.Contains(Testruns.Foutmelding, markup, StringComparison.Ordinal);
        Assert.Contains("4 runs, nieuwste eerst", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EenKlantVindtDeTypenaamVanDeUitzonderingNergensInDeMarkup()
    {
        MeldKlantAan();

        var markup = RunmarkupNaAanmelden();

        var gelekt = Testruns.VerbodenInhoud
            .Where(inhoud => markup.Contains(inhoud, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            gelekt.Length == 0,
            "Het runtabblad van een klant toont inhoud die volgens §2 niet op zijn scherm hoort:\n" +
            $"  {string.Join("\n  ", gelekt)}\n\n" +
            "Dit komt uit RunRecord.ErrorType, of uit een stacktrace in RunRecord.ErrorMessage. Op " +
            "het klantpad hoort de typenaam niet te bestaan: de klant krijgt CustomerRunRow en dat " +
            "type heeft het veld niet. Staat het er tóch, dan is er een pad bijgekomen dat de " +
            "operatorvorm aan een klant laat zien — een cast in RunsTable, een tweede tabel, of een " +
            "viewmodel dat het veld weer heeft.\n\n" +
            "Los dit niet op met een @if, een filter of een afkorting tot de korte typenaam. Een " +
            "ontbrekend veld kan niet lekken; een vergeten @if wel, en een afkorting laat iets " +
            "achter dat voor de klant nog steeds niets betekent en voor de operator geen diagnose " +
            "meer is.");
    }

    [Fact]
    public void EenOperatorZietDeVolledigeTypenaamWelOpHetzelfdeTabblad()
    {
        // De spiegel van de test hierboven, en de reden dat die iets betekent. Zou errorType ook uit
        // het operatorpad verdwijnen, dan blijft de klanttest groen terwijl niemand meer kan zien
        // welk defect een run heeft geveld. Deze test houdt dat tegen.
        MeldOperatorAan();

        var markup = RunmarkupNaAanmelden();

        Assert.Contains(Testruns.Typenaam, markup, StringComparison.Ordinal);
        Assert.Contains(Testruns.TweedeTypenaam, markup, StringComparison.Ordinal);

        // En de melding staat er nog naast. Het type zonder de zin is een halve diagnose: het zegt
        // wat er stuk is en niet wat er misging.
        Assert.Contains(Testruns.Foutmelding, markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HetKlantschermKniptEenMeerregeligeFoutmeldingTerugTotDeEersteRegel()
    {
        // Wat het besluit over errorType niet dekte, en wat bij het lezen alsnog bleek: errorMessage
        // is klantzichtbaar, wordt sinds kort bij het wegschrijven geknipt, en runs blijven 400 dagen
        // staan. Elk rundocument dat er vandaag is heeft die knip dus nooit gezien, en de foutmelding
        // gaat op het klantscherm in de tooltip van de resultaatbadge.
        MeldKlantAan();

        var markup = RunmarkupNaAanmelden();

        // Eerst vaststellen dat de eerste zin het heeft gehaald. Zonder deze assertie zou de test
        // groen kunnen worden doordat de hele melding wegviel in plaats van doordat er is geknipt.
        Assert.Contains(Testruns.EersteRegel, markup, StringComparison.Ordinal);
        Assert.DoesNotContain("/src/Sync/", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HetKlanttypeVanEenRunHeeftGeenVeldMetEenTypenaam()
    {
        // De structurele kant van dezelfde afspraak, en de enige die niet van een gerenderde pagina
        // afhangt. Sterker dan een markuptest: komt er ooit weer een veld met een typenaam op het
        // klantpad, dan valt het hier op ook als er nog geen scherm is dat het toont. En het loopt de
        // hele graaf af en niet één type, want een schone rij helpt niet als het viewmodel eromheen
        // de volle vorm ergens nog meedraagt — in een lijst, in een tuple, of als veld dat "even
        // handig" was.
        var paden = new List<string>();

        Zoek(typeof(CustomerAgentRunsView), nameof(CustomerAgentRunsView), [], paden);

        Assert.True(
            paden.Count == 0,
            "De klantweergave van het runtabblad draagt de typenaam van een uitzondering mee:\n" +
            $"  {string.Join("\n  ", paden)}\n\n" +
            "Gemeten staan daar waarden als SoratusAgent.Sync.ValidationException in, op documenten " +
            "van echte klanten. Dat is onze naamruimtestructuur en een klant doet er niets mee: hij " +
            "moet weten dát de run mislukte en of er werk blijft liggen, en dat staat in " +
            "errorMessage.\n\n" +
            "Zet er geen @if omheen en kort de naam niet af. Het hoort op OperatorRunRow te staan " +
            "en nergens anders; daar heeft het een lezer die er iets aan heeft.");
    }

    [Fact]
    public void DeOperatorweergaveDraagtDieTypenaamWel()
    {
        // De spiegel van de reflectietest. Zonder deze zou de test hierboven ook groen zijn nadat
        // iemand het veld overal heeft weggehaald, en dan meet hij niet de scheiding maar de sloop.
        var paden = new List<string>();

        Zoek(typeof(OperatorAgentRunsView), nameof(OperatorAgentRunsView), [], paden);

        Assert.True(
            paden.Count > 0,
            "De operatorweergave van het runtabblad draagt nergens meer de typenaam van de " +
            "uitzondering. Dat is geen winst in zichtbaarheid maar een verlies: dit is het veld " +
            "waarmee een operator een Sync-defect van een Mail-defect onderscheidt, en de volledige " +
            "boodschap blijft er — anders dan bij errorMessage — nergens anders bewaard.\n\n" +
            "Zie OperatorRunRow.ErrorType, en fase-0-afwijkingen.md §14.");
    }

    /// <summary>
    /// Loopt de eigenschappen van een type af op zoek naar een veld met de typenaam van een
    /// uitzondering, ook in lijsten.
    /// </summary>
    /// <param name="type">Het type dat wordt onderzocht.</param>
    /// <param name="pad">Het pad ernaartoe, voor de foutmelding.</param>
    /// <param name="gezien">Typen die al zijn bekeken, tegen kringetjes.</param>
    /// <param name="treffers">Waar de gevonden paden in komen.</param>
    /// <remarks>
    /// De vraag is "draagt dit een .NET-typenaam", en het antwoord is de veldnaam <c>ErrorType</c> —
    /// hetzelfde woord als in het contract, dus dezelfde naam waarmee iemand het per ongeluk
    /// terugzet. Bewust niet elk veld dat op <c>Type</c> eindigt: <c>DisplayType</c> op een agentrij
    /// is een menselijk label ("Document-intake") en heeft hier niets mee te maken. Een test die daar
    /// ook op aanslaat is binnen een week uitgezet.
    /// </remarks>
    private static void Zoek(Type type, string pad, HashSet<Type> gezien, List<string> treffers)
    {
        // Alleen onze eigen typen aflopen. Een string of een DateTimeOffset draagt geen typenaam van
        // een uitzondering, en de graaf van het framework in gaan levert een oneindige zoektocht op.
        // Generieke typen wél openmaken: IReadOnlyList<OperatorRunRow> is precies de vorm waarin het
        // veld mee zou liften.
        if (type.Assembly != typeof(CustomerRunRow).Assembly
            && type.Assembly != typeof(RunRecord).Assembly)
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
            if (string.Equals(eigenschap.Name, "ErrorType", StringComparison.Ordinal))
            {
                treffers.Add($"{pad}.{eigenschap.Name}");
                continue;
            }

            Zoek(eigenschap.PropertyType, $"{pad}.{eigenschap.Name}", gezien, treffers);
        }
    }
}
