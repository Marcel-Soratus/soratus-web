using Soratus.Portal.Sprints;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// De DevOps-naad in de hand: een <see cref="IDevOpsSprintClient"/> die een vastgelegd antwoord geeft.
/// </summary>
/// <remarks>
/// <para><strong>Dit is de eerste van twee naden in deze lane en de dubbel zit hier en niet hoger.</strong>
/// Wat er tussen deze dubbel en het document zit is productiecode: <see cref="SprintSelection"/> (welke
/// iteratie de sprint is, uit de dátums), en <see cref="SprintCollector"/> (wat er wordt weggeschreven en
/// wat niet). Een dubbel die een <em>document</em> zou opleveren in plaats van een antwoord, zou die twee
/// overslaan — en dan is er niets dat meet dat een mislukte lezing niets wegschrijft.</para>
///
/// <para>Hij houdt bij hoe vaak hij is aangeroepen en met welke scope. Dat eerste is nodig voor de
/// versheidscontrole — die is te meten als "hij is niet opnieuw aangeroepen" en op geen andere manier — en
/// dat tweede voor de vraag of de collector werkelijk het bord van deze klant bevraagt en niet dat van de
/// vorige.</para>
/// </remarks>
internal sealed class Vastesprintbron : IDevOpsSprintClient
{
    private readonly Queue<SprintAnswer> _antwoorden = new();
    private SprintAnswer? _vast;

    /// <summary>De scopes waarmee hij is aangeroepen, in volgorde.</summary>
    public List<DevOpsScope> Aanroepen { get; } = [];

    /// <summary>De dagen waarmee hij is aangeroepen, in volgorde.</summary>
    /// <remarks>
    /// Nodig om te meten dat de collector de <em>Nederlandse</em> dag doorgeeft en niet de UTC-dag. Dat is
    /// een invariant met een gevolg dat maar één keer per maand zichtbaar is — op 1 september tussen
    /// middernacht en twee uur — en zo'n gevolg is niet te testen zonder de doorgegeven waarde te zien.
    /// </remarks>
    public List<DateOnly> Dagen { get; } = [];

    /// <summary>Een uitzondering die bij de volgende aanroep wordt geworpen, of <c>null</c>.</summary>
    /// <remarks>
    /// Voor het pad waarop de client zelf omvalt in plaats van een antwoord met
    /// <see cref="SprintAnswerKind.NotAvailable"/> te geven. Dat is een ander pad in de collector — een
    /// <c>catch</c> in plaats van een <c>if</c> — en beide moeten "niets wegschrijven" opleveren.
    /// </remarks>
    public Exception? Werpt { get; set; }

    /// <summary>Zet één antwoord dat bij elke aanroep terugkomt.</summary>
    /// <param name="antwoord">Het antwoord.</param>
    /// <returns>Deze bron, zodat een test hem in één regel kan opzetten.</returns>
    public Vastesprintbron Antwoordt(SprintAnswer antwoord)
    {
        _vast = antwoord;
        return this;
    }

    /// <summary>Zet een rij antwoorden, één per aanroep.</summary>
    /// <param name="antwoorden">De antwoorden, in volgorde.</param>
    /// <returns>Deze bron.</returns>
    /// <remarks>
    /// <para>Voor tests over twee ronden achter elkaar: eerst een geslaagde lezing, dan een weigering, en
    /// dan hoort de eerste te blijven staan.</para>
    ///
    /// <para>Een eigen naam en geen <c>params</c>-overload van <see cref="Antwoordt(SprintAnswer)"/>. Die
    /// twee zijn bij één argument oplosbaar maar niet leesbaar, en het verschil is wezenlijk: de een geeft
    /// élke aanroep hetzelfde antwoord en de ander een ander antwoord per aanroep. Een test die de
    /// verkeerde kiest, meet iets anders dan hij zegt.</para>
    /// </remarks>
    public Vastesprintbron AntwoordtAchtereenvolgens(params SprintAnswer[] antwoorden)
    {
        ArgumentNullException.ThrowIfNull(antwoorden);

        foreach (var antwoord in antwoorden)
        {
            _antwoorden.Enqueue(antwoord);
        }

        return this;
    }

    /// <inheritdoc />
    public Task<SprintAnswer> ReadAsync(
        DevOpsScope scope,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        Aanroepen.Add(scope);
        Dagen.Add(today);

        if (Werpt is { } fout)
        {
            throw fout;
        }

        if (_antwoorden.Count > 0)
        {
            return Task.FromResult(_antwoorden.Dequeue());
        }

        return Task.FromResult(
            _vast
            ?? throw new InvalidOperationException(
                "Deze bron heeft geen antwoord gekregen. Zet er een met Antwoordt(...) — een dubbel die "
                + "een verzonnen standaardantwoord geeft, maakt een test groen om een reden die de "
                + "testschrijver niet heeft opgeschreven."));
    }

    /// <summary>Een geslaagd antwoord met een huidige sprint.</summary>
    /// <param name="sprint">De sprint.</param>
    /// <param name="items">De work items.</param>
    /// <param name="undated">De iteraties zonder datums.</param>
    /// <param name="datedCount">Hoeveel iteraties er datums hebben.</param>
    /// <returns>Het antwoord.</returns>
    public static SprintAnswer Sprint(
        DevOpsIteration sprint,
        IReadOnlyList<SprintWorkItem>? items = null,
        IReadOnlyList<DevOpsIteration>? undated = null,
        int datedCount = 1)
    {
        ArgumentNullException.ThrowIfNull(sprint);

        return new SprintAnswer(
            SprintAnswerKind.Answered,
            new SprintChoice(SprintState.Current, sprint, undated ?? [], [], datedCount),
            items ?? [],
            Reason: null,
            Calls: 4);
    }

    /// <summary>Een geslaagd antwoord zonder huidige sprint.</summary>
    /// <param name="state">De toestand.</param>
    /// <param name="undated">De iteraties zonder datums.</param>
    /// <param name="overlapping">De overlappende iteraties.</param>
    /// <param name="datedCount">Hoeveel iteraties er datums hebben.</param>
    /// <returns>Het antwoord.</returns>
    public static SprintAnswer Geen(
        SprintState state,
        IReadOnlyList<DevOpsIteration>? undated = null,
        IReadOnlyList<DevOpsIteration>? overlapping = null,
        int datedCount = 0) =>
        new(
            SprintAnswerKind.Answered,
            new SprintChoice(state, null, undated ?? [], overlapping ?? [], datedCount),
            [],
            Reason: null,
            Calls: 1);

    /// <summary>Een lezing die er niet was: geen recht, geen bord, of een tijdslimiet.</summary>
    /// <param name="reason">Waarom.</param>
    /// <returns>Het antwoord.</returns>
    public static SprintAnswer Niets(string reason) =>
        new(SprintAnswerKind.NotAvailable, default, [], reason, Calls: 1);

    /// <summary>Een lezing die er wél was en niet te gebruiken viel.</summary>
    /// <param name="reason">Waarom.</param>
    /// <returns>Het antwoord.</returns>
    public static SprintAnswer Onleesbaar(string reason) =>
        new(SprintAnswerKind.Unreadable, default, [], reason, Calls: 2);
}

/// <summary>
/// De schrijfkant van de sprintcollector in het geheugen.
/// </summary>
/// <remarks>
/// <para>Een eigen dubbel en niet <see cref="Vasteportaalopslag"/>, en dat volgt uit de interfaces:
/// <see cref="ISprintCollectorStore"/> neemt een klantslug en geen scope, want de collector heeft geen
/// mens. Die twee bij elkaar zetten zou betekenen dat de leeskant van de fixture een slug als ingang
/// krijgt — en dan is de eigenschap die de leeskant juist heeft ("de partitiesleutel komt uit de scope")
/// in de fixture niet meer waar.</para>
///
/// <para>Hij bewaart élke schrijfactie in volgorde. Dat is het verschil tussen "de laatste lezing klopt"
/// en "er is precies één keer geschreven", en de tweede vraag is degene die de regel van punt 39 meet: bij
/// een mislukte lezing hoort er <em>niets</em> te worden weggeschreven.</para>
/// </remarks>
internal sealed class Vastesprintopslag : ISprintCollectorStore
{
    private readonly Dictionary<string, DateTimeOffset> _gelezen =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>De klanten die deze opslag teruggeeft.</summary>
    public List<SprintTarget> Klanten { get; } = [];

    /// <summary>Elke schrijfactie, in volgorde.</summary>
    public List<SprintWrite> Schrijfacties { get; } = [];

    /// <summary>Hoe vaak het tijdstip van de vorige lezing is opgevraagd.</summary>
    public int Puntlezingen { get; private set; }

    /// <summary>Een uitzondering die de klantenlijst werpt, of <c>null</c>.</summary>
    public Exception? KlantenlijstWerpt { get; set; }

    /// <summary>Een uitzondering die de puntlezing werpt, of <c>null</c>.</summary>
    public Exception? PuntlezingWerpt { get; set; }

    /// <summary>Zet het tijdstip van de vorige lezing van een klant.</summary>
    /// <param name="klant">De klantslug.</param>
    /// <param name="moment">Het tijdstip.</param>
    public void Gelezen(string klant, DateTimeOffset moment) => _gelezen[klant] = moment;

    /// <inheritdoc />
    public Task<IReadOnlyList<SprintTarget>> TargetsAsync(
        CancellationToken cancellationToken = default) =>
        KlantenlijstWerpt is { } fout
            ? throw fout
            : Task.FromResult<IReadOnlyList<SprintTarget>>([.. Klanten]);

    /// <inheritdoc />
    public Task<DateTimeOffset?> ReadAtAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        Puntlezingen++;

        if (PuntlezingWerpt is { } fout)
        {
            throw fout;
        }

        // Expliciet DateTimeOffset? en niet een ternair met null erin: dat laatste is voor de
        // typeafleiding van Task.FromResult niet op te lossen (CS0411), en een cast in een assertie is
        // lastiger te lezen dan een if.
        return Task.FromResult(
            _gelezen.TryGetValue(customerId, out var moment) ? moment : (DateTimeOffset?)null);
    }

    /// <inheritdoc />
    public Task WriteAsync(SprintWrite write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);

        Schrijfacties.Add(write);
        _gelezen[write.CustomerId] = write.ReadAt;

        return Task.CompletedTask;
    }
}
