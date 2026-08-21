using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// De schrijfkant van de kostenopslag, zonder Cosmos.
/// </summary>
/// <remarks>
/// <para>Legt vast wat er is geclaimd en wat er is weggeschreven, en laat een test de toestand van een
/// eerder gemeten maand zetten. Dat laatste is nodig voor de besparing die de collector doet: een
/// afgesloten maand die al volledig is, wordt niet opnieuw opgevraagd — en dat is de helft van het
/// aanroepbudget, dus het hoort een test te hebben.</para>
///
/// <para><strong>Wat hier met opzet níet gebeurt: iets weigeren dat Cosmos zou weigeren.</strong> De
/// 409 op een tweede claim is er wel, want dat is het gedrag waar het slot op rust. Een etagcontrole is
/// er niet, want de echte implementatie heeft die ook niet — het verbruiksdocument is een upsert, en
/// waarom staat bij <see cref="IAzureCostCollectorStore.WriteAsync"/>.</para>
/// </remarks>
internal sealed class Vastekostenopslag : IAzureCostCollectorStore
{
    private readonly HashSet<DateOnly> _geclaimd = [];

    private readonly Dictionary<(string Klant, string Maand), AzureCostState> _toestanden = [];

    /// <summary>De klanten met hun scope, zoals ze uit de opslag zouden komen.</summary>
    public List<AzureCostTarget> Klanten { get; } = [];

    /// <summary>Alles wat er is weggeschreven, in de volgorde waarin dat gebeurde.</summary>
    public List<AzureCostWrite> Geschreven { get; } = [];

    /// <summary>Elke dag waarvoor een claim is geprobeerd, ook de geweigerde.</summary>
    public List<DateOnly> Claimpogingen { get; } = [];

    /// <summary>Wat er misgaat bij het lezen van de klantenlijst, of <c>null</c>.</summary>
    /// <remarks>
    /// Een onbereikbare opslag hoort geen aanroepen aan Cost Management op te leveren: die kosten
    /// budget en het antwoord zou nergens landen. Dat is te bewijzen door hier iets te laten werpen.
    /// </remarks>
    public Exception? Leesfout { get; set; }

    /// <summary>Wat er misgaat bij het lezen van een opgeslagen toestand, of <c>null</c>.</summary>
    public Exception? Toestandsfout { get; set; }

    /// <summary>Legt vast dat een maand al is gemeten.</summary>
    /// <param name="klant">De klantslug.</param>
    /// <param name="maand">De maand als <c>jjjj-MM</c>.</param>
    /// <param name="toestand">De toestand die er staat.</param>
    public void Toestand(string klant, string maand, AzureCostState toestand) =>
        _toestanden[(klant, maand)] = toestand;

    /// <summary>Zet een klant met een scope in de opslag.</summary>
    /// <param name="klant">De klantslug.</param>
    /// <param name="scope">De scope zoals hij in het document staat.</param>
    public void Klant(string klant, string? scope) =>
        Klanten.Add(new AzureCostTarget(klant, scope));

    /// <inheritdoc />
    public Task<IReadOnlyList<AzureCostTarget>> TargetsAsync(
        CancellationToken cancellationToken = default) =>
        Leesfout is null
            ? Task.FromResult<IReadOnlyList<AzureCostTarget>>([.. Klanten])
            : Task.FromException<IReadOnlyList<AzureCostTarget>>(Leesfout);

    /// <inheritdoc />
    public Task<bool> ClaimAsync(
        DateOnly day,
        int customers,
        CancellationToken cancellationToken = default)
    {
        Claimpogingen.Add(day);

        // Add levert false zodra de dag er al in zit. Dat is precies de 409 van Cosmos: de tweede
        // instantie krijgt te horen dat de run al is geclaimd en doet niets.
        return Task.FromResult(_geclaimd.Add(day));
    }

    /// <inheritdoc />
    public Task<AzureCostState?> StateAsync(
        string customerId,
        string month,
        CancellationToken cancellationToken = default) =>
        Toestandsfout is null
            ? Task.FromResult(
                _toestanden.TryGetValue((customerId, month), out var toestand)
                    ? toestand
                    : (AzureCostState?)null)
            : Task.FromException<AzureCostState?>(Toestandsfout);

    /// <inheritdoc />
    public Task WriteAsync(AzureCostWrite write, CancellationToken cancellationToken = default)
    {
        Geschreven.Add(write);
        _toestanden[(write.CustomerId, write.Month)] = write.State;

        return Task.CompletedTask;
    }
}

/// <summary>
/// Een Cost Management die antwoordt wat de test wil, zonder netwerk.
/// </summary>
/// <remarks>
/// Per klant en maand één antwoord. Wat er niet in staat wordt
/// <see cref="AzureCostAnswerKind.NotAvailable"/> — de veilige kant, en dezelfde kant als bij een
/// niet-gezette enumwaarde in de productiecode.
/// </remarks>
internal sealed class Vastekostenclient : IAzureCostClient
{
    private readonly Dictionary<(string Scope, string Maand), AzureCostAnswer> _antwoorden = [];

    /// <summary>Elke vraag die er is gesteld, in volgorde.</summary>
    public List<(string Scope, string Maand)> Vragen { get; } = [];

    /// <summary>Legt het antwoord op één vraag vast.</summary>
    /// <param name="scope">De scope als pad.</param>
    /// <param name="maand">De maand als <c>jjjj-MM</c>.</param>
    /// <param name="antwoord">Het antwoord.</param>
    public void Antwoord(string scope, string maand, AzureCostAnswer antwoord) =>
        _antwoorden[(scope, maand)] = antwoord;

    /// <summary>Een geslaagd antwoord met regels over een reeks dagen.</summary>
    /// <param name="dienst">De dienstnaam, zoals Azure hem geeft.</param>
    /// <param name="bedrag">Het bedrag over de hele periode.</param>
    /// <param name="dagen">De dagen waarover er bedragen zijn.</param>
    /// <returns>Het antwoord.</returns>
    public static AzureCostAnswer Gemeten(string dienst, decimal bedrag, IEnumerable<DateOnly> dagen) =>
        new(
            AzureCostAnswerKind.Answered,
            [new AzureCostLine { Service = dienst, Amount = bedrag }],
            [.. dagen],
            "EUR",
            Reason: null,
            Calls: 1);

    /// <inheritdoc />
    public Task<AzureCostAnswer> ReadAsync(
        AzureScope scope,
        string month,
        DateOnly observedOn,
        CancellationToken cancellationToken = default)
    {
        Vragen.Add((scope.Path, month));

        return Task.FromResult(
            _antwoorden.TryGetValue((scope.Path, month), out var antwoord)
                ? antwoord
                : new AzureCostAnswer(
                    AzureCostAnswerKind.NotAvailable,
                    [],
                    [],
                    Currency: null,
                    "Niets afgesproken in deze test.",
                    Calls: 1));
    }
}
