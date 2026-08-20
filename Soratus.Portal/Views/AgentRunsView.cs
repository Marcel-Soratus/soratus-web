using Soratus.Agents.Contracts;

namespace Soratus.Portal.Views;

/// <summary>
/// Het tabblad Runs zoals de klant het ziet (§3.3): gestart, duur, resultaat, aantal items, runId.
/// </summary>
/// <remarks>
/// <para><strong>Twee weergaven en niet één, om precies één veld: <c>errorType</c>.</strong> §2 geeft
/// beide rollen leesrecht op de runs van de eigen omgeving, en op alles behalve dat ene veld zijn de
/// twee weergaven identiek. Toch staan ze hier apart, want een gedeeld type met een veld dat de ene
/// rol wel en de andere niet mag zien is precies wat dit portaal twee keer eerder heeft afgeschaft —
/// zie <see cref="CustomerAgentLogsView"/> en <see cref="CustomerAgentsView"/>. Het verschil zit in
/// de vorm van het type en niet in een vlag, een filter of een <c>@if</c>.</para>
///
/// <para>De rijen zijn <see cref="CustomerRunRow"/>, en dat type heeft geen veld met een .NET-typenaam
/// erin. Zie <c>docs/agent-portal/fase-0-afwijkingen.md</c> §14 voor het besluit en de gemeten
/// waarden waarop het rust.</para>
/// </remarks>
public sealed record CustomerAgentRunsView
{
    /// <summary>De technische naam van de agent.</summary>
    public required string AgentName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>De runs, nieuwste eerst.</summary>
    public required IReadOnlyList<CustomerRunRow> Runs { get; init; }

    /// <summary>
    /// Het vervolgtoken voor de volgende (oudere) pagina, of <c>null</c> als dit alles was. Niet
    /// geschikt voor een URL.
    /// </summary>
    public string? ContinuationToken { get; init; }

    /// <summary>Of er nog oudere runs zijn.</summary>
    public bool HasMore => ContinuationToken is not null;

    /// <summary>Of deze agent nog nooit gedraaid heeft.</summary>
    public bool IsEmpty => Runs.Count == 0;
}

/// <summary>
/// Het tabblad Runs zoals de operator het ziet (§3.3).
/// </summary>
/// <remarks>
/// Dit is de variant met <c>errorType</c>: <see cref="Runs"/> bevat <see cref="OperatorRunRow"/>, en
/// die draagt de volledige typenaam van de uitzondering. Dat is geen luxe maar de diagnose —
/// <c>Sync.ValidationException</c> is een ander defect dan <c>Mail.ValidationException</c> — en het
/// is de reden dat het antwoord op het lek een projectie is en geen inkorting bij het wegschrijven:
/// afkorten zou die twee onderscheidbare defecten in één onbruikbaar woord veranderen.
/// </remarks>
public sealed record OperatorAgentRunsView
{
    /// <summary>De technische naam van de agent.</summary>
    public required string AgentName { get; init; }

    /// <summary>Wanneer deze weergave is opgebouwd.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>De runs, nieuwste eerst, met hun diagnose.</summary>
    public required IReadOnlyList<OperatorRunRow> Runs { get; init; }

    /// <inheritdoc cref="CustomerAgentRunsView.ContinuationToken"/>
    public string? ContinuationToken { get; init; }

    /// <summary>Of er nog oudere runs zijn.</summary>
    public bool HasMore => ContinuationToken is not null;

    /// <summary>Of deze agent nog nooit gedraaid heeft.</summary>
    public bool IsEmpty => Runs.Count == 0;
}

/// <summary>
/// Eén run in de runlijst, in de vorm die de runtabel leest — plat en direct af te drukken.
/// </summary>
/// <remarks>
/// <para><strong>Dit type is abstract, en dat is het antwoord op één vraag: mag een klant
/// <c>errorType</c> zien?</strong> Nee. Een klant doet niets met een .NET-typenaam: hij moet weten
/// dát de run mislukte en of er werk blijft liggen, en dat staat in <see cref="ErrorMessage"/>. Wat
/// er wél in staat is onze naamruimtestructuur — gemeten in de echte opslag staat er op documenten
/// van échte klanten <c>SoratusAgent.Sync.ValidationException</c> en
/// <c>SoratusAgent.Mail.ClassificationException</c>. Het veld staat daarom niet op dit type maar
/// alleen op <see cref="OperatorRunRow"/>. Wat er niet is kan niet lekken, ook niet als iemand er
/// over een half jaar een tooltip bij zet.</para>
///
/// <para><strong>Waarom niet afkorten tot de korte typenaam.</strong> Dat was de eerste reflex en het
/// is het tegendeel van de oplossing. <c>ValidationException</c> is voor een klant even betekenisloos
/// als de volledige naam, dus het lost niets op; en voor de operator gooit het juist het nuttige deel
/// weg, want <c>Sync.ValidationException</c> en <c>Mail.ValidationException</c> zijn dan niet meer te
/// onderscheiden. Bij <see cref="ErrorMessage"/> verplaatst afkappen de informatie — de rest blijft
/// operator-only bewaard — en hier zou het hem weggooien. Dat asymmetrische verschil is precies
/// waarom dit een projectie is en geen knip bij het schrijven; zie
/// <c>docs/agent-portal/fase-0-afwijkingen.md</c> §14.</para>
///
/// <para><strong>De tabel leest dit basistype en niet de rolvariant.</strong> Er is één
/// <c>RunsTable</c>, want de kolommen, de kolomsporen en de streepjes zijn voor beide rollen
/// hetzelfde; twee tabellen zouden een tweede kopie van hetzelfde <c>RowGrid</c> betekenen, en dat is
/// precies wat afwijking §6 verbiedt. Het enige dat per rol verschilt is de tooltip bij een mislukte
/// run, en die komt uit <see cref="FailureDetail"/>. De tabel kan daardoor geen typenaam tonen die
/// hij niet krijgt: hij vraagt de rij om de tekst in plaats van hem zelf uit velden samen te
/// stellen.</para>
///
/// <para><strong>Een lopende run mist dingen, en dat staat er als "afwezig" en niet als nul.</strong>
/// <see cref="Duration"/>, <see cref="Outcome"/>, <see cref="ItemsProcessed"/> en
/// <see cref="ItemsFailed"/> zijn alle vier nullable, en bij een run die nog loopt zijn ze
/// <c>null</c>. Het document in Cosmos zegt op dat moment <c>durationMs: null</c>,
/// <c>result: "running"</c> en <c>itemsProcessed: 0</c> — dat laatste is een beginstand en geen
/// uitkomst. Zou dit type die nul doorgeven, dan stond er op het scherm "0 ms" en "0 items" bij een
/// run die vrolijk aan het werk is, en dat is niet onvolledig maar onwaar. Met <c>null</c> kan het
/// scherm een streepje zetten. Die beslissing valt één keer, in <see cref="Settled"/>, zodat de twee
/// projecties hem niet elk apart kunnen nemen.</para>
///
/// <para><strong>Waarom dit niet <see cref="AgentRunSummary"/> is.</strong> Dat type beschrijft de
/// laatste <em>afgeronde</em> run in de kop van het scherm; daar bestaat "loopt nog" niet, dus staan
/// <c>Result</c> en <c>ItemsProcessed</c> er terecht als niet-nullable in. Deze lijst bevat juist
/// álle runs, inclusief de lopende. Dat verschil in wat er kán ontbreken is precies het verschil dat
/// een apart type verdient; één type met "soms is dit veld zinloos" zou het bij beide schermen aan de
/// lezer overlaten om dat te weten.</para>
/// </remarks>
public abstract record AgentRunRow
{
    /// <summary>De runId, bijvoorbeeld <c>r-4a91d20c</c>.</summary>
    public required string RunId { get; init; }

    /// <summary>Wanneer de run begon.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Wanneer de run klaar was, of <c>null</c> zolang hij loopt.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>
    /// Hoe lang de run duurde, of <c>null</c> zolang hij loopt. Toon dan een streepje en niet nul.
    /// </summary>
    /// <remarks>
    /// Bewust niet "nu min gestart" voor een lopende run. Dat is de tijd die hij al bezig is, niet
    /// zijn duur, en het zou in dezelfde kolom staan als de duur van de runs eronder — waarmee de
    /// kolom twee verschillende dingen betekent afhankelijk van de rij.
    /// </remarks>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// De afloop, of <c>null</c> zolang de run nog loopt.
    /// </summary>
    /// <remarks>
    /// <c>null</c> en niet <see cref="RunResult.Running"/>: "loopt nog" is geen afloop, en zolang
    /// het als vierde uitkomst in dezelfde opsomming zit, komt het vroeg of laat in een telling
    /// van geslaagd-tegen-mislukt terecht. Zie <see cref="IsRunning"/>.
    /// </remarks>
    public RunResult? Outcome { get; init; }

    /// <summary>
    /// Hoeveel items deze run verwerkte, of <c>null</c> zolang hij loopt.
    /// </summary>
    public int? ItemsProcessed { get; init; }

    /// <summary>
    /// Hoeveel items zijn afgekeurd of mislukt, of <c>null</c> zolang de run loopt.
    /// </summary>
    public int? ItemsFailed { get; init; }

    /// <summary>Of de transactie is teruggedraaid.</summary>
    public bool RolledBack { get; init; }

    /// <summary>
    /// De foutmelding, als de run mislukte.
    /// </summary>
    /// <remarks>
    /// Dit veld staat op het basistype en dus op beide rollen, anders dan
    /// <see cref="OperatorRunRow.ErrorType"/>. Het is bedoeld om gelezen te worden door wie de code
    /// niet kent: het contract eist er één Nederlandse zin op één regel, en de
    /// telemetriebibliotheek knipt hem bij het wegschrijven af op de eerste regelovergang. Wat de
    /// klant hier te zien krijgt gaat daarnaast nog door dezelfde knip in de projectie — zie
    /// <see cref="CustomerRunRow.From"/> — want runs worden 400 dagen bewaard en de documenten die
    /// er vandaag al staan hebben die knip nooit gezien.
    /// </remarks>
    public string? ErrorMessage { get; init; }

    /// <summary>De agentversie die deze run draaide.</summary>
    public required string Version { get; init; }

    /// <summary>Waardoor deze run startte.</summary>
    public required TriggerKind Trigger { get; init; }

    /// <summary>Of deze run op dit moment nog loopt.</summary>
    public bool IsRunning => Outcome is null;

    /// <summary>
    /// Wat er bij een mislukte run in de tooltip van de resultaatbadge komt, of <c>null</c> als er
    /// niets te melden is.
    /// </summary>
    /// <remarks>
    /// <para>Het enige lid dat per rol verschilt, en daarmee de enige plek waar de tabel iets vraagt
    /// in plaats van iets samen te stellen. De klant krijgt de foutmelding, de operator die melding
    /// plus de typenaam.</para>
    ///
    /// <para>Bewust een lid op de rij en geen methode in de tabel. Zou de tabel de tekst zelf
    /// samenstellen, dan moet hij naar velden reiken die op het klanttype niet bestaan — en dan is
    /// het typeverschil terug een <c>if</c> in de weergave, met een cast erbij.</para>
    /// </remarks>
    public abstract string? FailureDetail { get; }

    /// <summary>
    /// De vier velden die ervan afhangen of de run nog loopt.
    /// </summary>
    /// <param name="run">De run.</param>
    /// <returns>Duur, afloop en de twee aantallen, of <c>null</c> waar de run nog geen antwoord heeft.</returns>
    /// <remarks>
    /// Eén keer beslist en door beide projecties gebruikt. Zouden ze het elk zelf doen, dan bestaan er
    /// twee definities van "loopt nog" en gaan die schuiven — hetzelfde patroon dat bij de knip op
    /// <c>msg</c> al is misgegaan met drie kopieën van dezelfde regel.
    /// </remarks>
    private protected static (TimeSpan? Duration, RunResult? Outcome, int? ItemsProcessed, int? ItemsFailed)
        Settled(RunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var running = run.Result == RunResult.Running;

        return (
            // Ook de duur gaat op null zolang de run loopt, en niet alleen als het document er geen
            // in heeft staan. Een agent die durationMs alvast meeschrijft op een run die nog bezig
            // is, levert anders een eindduur op iets wat geen einde heeft — en het scherm zet
            // ernaast de tooltip "de run is nog bezig". Dezelfde vraag als bij de andere drie
            // velden, dus hetzelfde antwoord.
            running || run.DurationMs is not { } ms ? null : TimeSpan.FromMilliseconds(ms),
            running ? null : run.Result,
            running ? null : run.ItemsProcessed,
            running ? null : run.ItemsFailed);
    }
}

/// <summary>
/// Eén run zoals de klant hem ziet: alles behalve de typenaam van de uitzondering.
/// </summary>
/// <remarks>
/// Er is geen <c>ErrorType</c> op dit type en dat hoort zo te blijven; <see cref="AgentRunRow"/> legt
/// uit waarom. Wat de klant bij een mislukte run te zien krijgt is <see cref="FailureDetail"/>, en dat
/// is de foutmelding — één zin die zegt wat er misging.
/// </remarks>
public sealed record CustomerRunRow : AgentRunRow
{
    /// <inheritdoc />
    /// <remarks>
    /// Alleen de foutmelding, en niets erachter. Is die leeg, dan komt er <c>null</c> uit en zet het
    /// scherm geen tooltip — beter dan een tooltip die de typenaam als terugvaloptie toont, want dat
    /// was precies het gat: het lek stond niet in de gewone weergave maar in het geval dat de agent
    /// zijn boodschap vergat.
    /// </remarks>
    public override string? FailureDetail =>
        string.IsNullOrWhiteSpace(ErrorMessage) ? null : ErrorMessage;

    /// <summary>
    /// Projecteert een rundocument naar de klantvariant.
    /// </summary>
    /// <param name="run">De run.</param>
    /// <returns>De rij zonder typenaam.</returns>
    /// <remarks>
    /// Een expliciete projectie en geen automatische mapping, om dezelfde reden als bij
    /// <see cref="CustomerLogLine.From"/>: komt er morgen een veld bij op <see cref="RunRecord"/>, dan
    /// komt het hier niet stilzwijgend mee. Iemand moet er een regel voor schrijven, en dat is precies
    /// het moment waarop de vraag "mag de klant dit zien" hoort te vallen.
    /// </remarks>
    internal static CustomerRunRow From(RunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var settled = Settled(run);

        return new CustomerRunRow
        {
            RunId = run.Id,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            Duration = settled.Duration,
            Outcome = settled.Outcome,
            ItemsProcessed = settled.ItemsProcessed,
            ItemsFailed = settled.ItemsFailed,
            RolledBack = run.RolledBack,

            // Hier knippen en niet alleen bij het wegschrijven, om dezelfde drie redenen als bij een
            // logregel — en met meer gewicht: runs worden 400 dagen bewaard tegen logs 30. Elk
            // rundocument dat er vandaag staat is weggeschreven vóór de knip bestond, en de
            // foutmelding gaat op het klantscherm in de tooltip van de resultaatbadge. Zie
            // CustomerMessage.
            ErrorMessage = run.ErrorMessage is { } message ? CustomerMessage.FirstLine(message) : null,
            Version = run.Version,
            Trigger = run.Trigger,
        };
    }
}

/// <summary>
/// Eén run zoals de operator hem ziet: met de typenaam van de uitzondering erbij.
/// </summary>
/// <remarks>
/// Dit is de variant die de diagnose draagt. Zonder dit type zou het besluit over <c>errorType</c>
/// niet "operator-only" heten maar "weg", en dan is er niets meer dat een <c>Sync</c>-defect van een
/// <c>Mail</c>-defect onderscheidt.
/// </remarks>
public sealed record OperatorRunRow : AgentRunRow
{
    /// <summary>
    /// Het volledige .NET-type van de uitzondering, als de run mislukte.
    /// </summary>
    /// <remarks>
    /// Volledig, met naamruimte, en met opzet niet ingekort. Voor de operator ís de naamruimte het
    /// nuttige deel. Het contract legt dezelfde regel op aan de schrijfkant vast; zie
    /// <c>docs/agent-portal/agent-contract.md</c>, "Wie leest de foutvelden van een run".
    /// </remarks>
    public string? ErrorType { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// De melding en de typenaam achter elkaar, met de melding vooraan: die zegt wat er misging, het
    /// type zegt wat er stuk is. Vóór dit besluit stond het type er alléén als de melding leeg was —
    /// een terugvaloptie — waardoor de operator hem in de praktijk nooit zag en de klant hem juist
    /// wél zag zodra een agent zijn boodschap vergat. Precies de verkeerde kant op, bij beide rollen.
    /// </remarks>
    public override string? FailureDetail
    {
        get
        {
            var message = string.IsNullOrWhiteSpace(ErrorMessage) ? null : ErrorMessage;
            var type = string.IsNullOrWhiteSpace(ErrorType) ? null : ErrorType;

            return (message, type) switch
            {
                (null, null) => null,
                (null, not null) => type,
                (not null, null) => message,
                _ => $"{message} · {type}",
            };
        }
    }

    /// <summary>
    /// Projecteert een rundocument naar de operatorvariant.
    /// </summary>
    /// <param name="run">De run.</param>
    /// <returns>De rij met de typenaam erbij.</returns>
    /// <remarks>
    /// De foutmelding komt hier onbewerkt door, anders dan bij <see cref="CustomerRunRow.From"/>.
    /// Dezelfde keuze als bij een logregel: de operator hoort te lezen wat er werkelijk in het
    /// document staat, ook als dat een halve pagina diagnostiek is.
    /// </remarks>
    internal static OperatorRunRow From(RunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var settled = Settled(run);

        return new OperatorRunRow
        {
            RunId = run.Id,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            Duration = settled.Duration,
            Outcome = settled.Outcome,
            ItemsProcessed = settled.ItemsProcessed,
            ItemsFailed = settled.ItemsFailed,
            RolledBack = run.RolledBack,
            ErrorType = run.ErrorType,
            ErrorMessage = run.ErrorMessage,
            Version = run.Version,
            Trigger = run.Trigger,
        };
    }
}
