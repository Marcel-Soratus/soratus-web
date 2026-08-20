using Soratus.Agents.Contracts;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Beveiliging;

/// <summary>
/// Een klant die iets opvraagt van een agent buiten productie krijgt <c>null</c> — op alle vijf de
/// ingangen, en niet alleen op het detail.
/// </summary>
/// <remarks>
/// <para>Dat <c>BuildAgentDetailAsync</c> voor een acceptatie-agent <c>null</c> geeft beschermt
/// alleen de kop van het scherm. De tabbladen zijn losse aanroepen met de agentnaam erin; zonder
/// eigen controle zou de logweergave van een acceptatie-agent op te vragen zijn terwijl het detail
/// 404 geeft — en dan is het bestaan van die agent alsnog vast te stellen. Elke klant-overload
/// controleert daarom zelf dat de agent bestaat, van deze klant is én in productie draait.</para>
///
/// <para>Dat is geen dubbel werk maar vijf keer dezelfde vraag op vijf plekken waar hij gesteld
/// kan worden. Deze tests lopen ze allemaal langs, want een vergeten controle op één van de vijf is
/// precies het soort gat dat niemand vindt door naar het scherm te kijken.</para>
///
/// <para>Alle vijf geven hetzelfde antwoord, en dat is de tweede helft van het besluit: zou de
/// logweergave "bestaat niet" en het detail "geen toegang" zeggen, dan verklapt het verschil dat er
/// een acceptatie-agent is.</para>
/// </remarks>
public class AgentDetailZichtbaarheidTests
{
    private const string Agent = "factuur-intake";

    [Fact]
    public async Task EenKlantKrijgtGeenDetailVanEenAgentBuitenProductie()
    {
        var (paginas, _) = Buiten();

        Assert.Null(await paginas.BuildAgentDetailAsync(await Weergavelaag.Klantscope(), Agent));
    }

    [Fact]
    public async Task EenKlantKrijgtGeenLogregelsVanEenAgentBuitenProductie()
    {
        var (_, tabbladen) = Buiten();

        Assert.Null(await tabbladen.BuildLogsAsync(
            await Weergavelaag.Klantscope(),
            Agent,
            new LogQuery()));
    }

    [Fact]
    public async Task EenKlantKrijgtGeenRunsVanEenAgentBuitenProductie()
    {
        var (_, tabbladen) = Buiten();

        Assert.Null(await tabbladen.BuildRunsAsync(await Weergavelaag.Klantscope(), Agent));
    }

    [Fact]
    public async Task EenKlantKrijgtGeenConfiguratieVanEenAgentBuitenProductie()
    {
        var (_, tabbladen) = Buiten();

        Assert.Null(await tabbladen.BuildConfigurationAsync(await Weergavelaag.Klantscope(), Agent));
    }

    [Fact]
    public async Task EenKlantKrijgtGeenTailVanEenAgentBuitenProductie()
    {
        // De tail is de ingang die het makkelijkst wordt vergeten: hij zit in een lus achter een
        // schakelaar en niemand kijkt er ooit naar. Juist daarom staat hij hier.
        var (_, tabbladen) = Buiten();

        Assert.Null(await tabbladen.TailLogsAsync(
            await Weergavelaag.Klantscope(),
            Agent,
            new LogQuery(),
            LogCursor.From(Testgegevens.Nu)));
    }

    [Fact]
    public async Task EenOperatorKrijgtDieAgentWelTeZien()
    {
        // De onmisbare tegenhanger. Zonder deze test zou een weergavelaag die overal null
        // teruggeeft alle vijf de tests hierboven laten slagen, en dan meten ze niets meer dan dat
        // er niets werkt.
        var store = new Vastetelemetriestore()
            .MetOmgeving(AgentEnvironment.Acceptance)
            .MetLogregels(Testlogregels.Klantregels());

        var (paginas, tabbladen) = Weergavelaag.Beide(store);
        var scope = await Weergavelaag.Operatorscope();

        Assert.NotNull(await paginas.BuildAgentDetailAsync(scope, Agent));
        Assert.NotNull(await tabbladen.BuildLogsAsync(scope, Agent, new LogQuery()));
        Assert.NotNull(await tabbladen.BuildRunsAsync(scope, Agent));
        Assert.NotNull(await tabbladen.BuildConfigurationAsync(scope, Agent));
        Assert.NotNull(await tabbladen.TailLogsAsync(
            scope,
            Agent,
            new LogQuery(),
            LogCursor.From(Testgegevens.Nu - TimeSpan.FromHours(1))));
    }

    [Fact]
    public async Task EenAgentDieNietBestaatGeeftDezelfdeVijfKerenNull()
    {
        // Hetzelfde antwoord als bij een agent buiten productie, en dat is het punt: het scherm
        // hoort de twee niet uit elkaar te houden, want dan verklapt het verschil dat de agent
        // bestaat.
        var store = new Vastetelemetriestore().ZonderAgent();
        var (paginas, tabbladen) = Weergavelaag.Beide(store);
        var scope = await Weergavelaag.Klantscope();

        Assert.Null(await paginas.BuildAgentDetailAsync(scope, Agent));
        Assert.Null(await tabbladen.BuildLogsAsync(scope, Agent, new LogQuery()));
        Assert.Null(await tabbladen.BuildRunsAsync(scope, Agent));
        Assert.Null(await tabbladen.BuildConfigurationAsync(scope, Agent));
        Assert.Null(await tabbladen.TailLogsAsync(
            scope,
            Agent,
            new LogQuery(),
            LogCursor.From(Testgegevens.Nu)));
    }

    [Fact]
    public async Task EenAgentVanEenAndereNaamGeeftOokNull()
    {
        // De naam komt uit de URL en is dus niet te vertrouwen. Een registratie die er wél is maar
        // een andere naam draagt hoort niet mee te liften op het feit dat er íets is.
        var store = new Vastetelemetriestore().MetLogregels(Testlogregels.Klantregels());
        var (paginas, tabbladen) = Weergavelaag.Beide(store);
        var scope = await Weergavelaag.Klantscope();

        Assert.Null(await paginas.BuildAgentDetailAsync(scope, "een-andere-agent"));
        Assert.Null(await tabbladen.BuildLogsAsync(scope, "een-andere-agent", new LogQuery()));
        Assert.Null(await tabbladen.BuildRunsAsync(scope, "een-andere-agent"));
        Assert.Null(await tabbladen.BuildConfigurationAsync(scope, "een-andere-agent"));
        Assert.Null(await tabbladen.TailLogsAsync(
            scope,
            "een-andere-agent",
            new LogQuery(),
            LogCursor.From(Testgegevens.Nu)));
    }

    [Fact]
    public async Task ElkeIngangDoetZijnEigenControleEnVertrouwtNietOpDieVanHetDetail()
    {
        // Vijf aanroepen, vijf keer een registratie opgevraagd. Zou er ergens één ontbreken, dan is
        // dat de ingang die de zichtbaarheid van een ander overneemt — en dat werkt alleen zolang
        // niemand die ingang los aanroept. Het logtabblad is een eigen circuit en doet precies dat.
        var store = new Vastetelemetriestore().MetOmgeving(AgentEnvironment.Production);
        var (paginas, tabbladen) = Weergavelaag.Beide(store);
        var scope = await Weergavelaag.Klantscope();

        await paginas.BuildAgentDetailAsync(scope, Agent);
        await tabbladen.BuildLogsAsync(scope, Agent, new LogQuery());
        await tabbladen.BuildRunsAsync(scope, Agent);
        await tabbladen.BuildConfigurationAsync(scope, Agent);
        await tabbladen.TailLogsAsync(scope, Agent, new LogQuery(), LogCursor.From(Testgegevens.Nu));

        Assert.Equal(5, store.Registratieverzoeken);
    }

    private static (Soratus.Portal.Views.IPortalViews Paginas, Soratus.Portal.Views.IAgentDetailViews Tabbladen)
        Buiten() =>
        Weergavelaag.Beide(new Vastetelemetriestore()
            .MetOmgeving(AgentEnvironment.Acceptance)
            .MetLogregels(Testlogregels.Klantregels()));
}
