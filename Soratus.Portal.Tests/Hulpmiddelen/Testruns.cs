using Soratus.Agents.Contracts;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Rundocumenten voor de tests, met foutvelden zoals ze er in de echte opslag in staan.
/// </summary>
/// <remarks>
/// <para><strong>Waarom deze data vijandig is.</strong> Gemeten over de echte telemetrie staan er drie
/// verschillende waarden in <c>errorType</c>, en alle drie bevatten een naamruimte:
/// <c>SoratusAgent.Sync.ValidationException</c> en <c>SoratusAgent.Mail.ClassificationException</c> op
/// documenten van échte klanten, en <c>System.Net.Http.HttpRequestException</c> bij de interne
/// beheerklant. Dat is onze mappen- en naamruimtestructuur, en die hoort niet op een klantscherm.</para>
///
/// <para>Zou deze fixture een braaf <c>Http502</c> in <c>errorType</c> zetten, dan zou een
/// zichtbaarheidstest groen staan omdat er niets te lekken viel. De runs hier zijn dus met opzet zo
/// geschreven dat elk fragment dat een klant niet mag zien érgens in de fixture staat; welke daarvan
/// het scherm haalt, is dan een uitkomst van de test en geen aanname van de testdata.</para>
///
/// <para><strong>Eén run heeft een meerregelige foutmelding.</strong> <c>errorMessage</c> is wél
/// klantzichtbaar en de telemetriebibliotheek knipt hem sinds kort af op de eerste regelovergang —
/// maar runs worden 400 dagen bewaard, dus elk document dat er vandaag staat is weggeschreven vóór die
/// knip bestond. Langs een schrijfpad komt een fixture per definitie nooit; de knip die híer wordt
/// gemeten zit in de projectie naar de klant.</para>
/// </remarks>
internal static class Testruns
{
    /// <summary>Het vaste referentiemoment van de tests.</summary>
    private static readonly DateTimeOffset Nu = Testgegevens.Nu;

    /// <summary>De volledige typenaam op een mislukte run van een echte klant.</summary>
    public const string Typenaam = "SoratusAgent.Sync.ValidationException";

    /// <summary>De typenaam op de tweede mislukte run, uit een andere naamruimte.</summary>
    /// <remarks>
    /// Twee verschillende naamruimtes met opzet. Ze zijn samen het bewijs voor de andere helft van het
    /// besluit: afkorten tot de korte typenaam zou <c>Sync.ValidationException</c> en
    /// <c>Mail.ValidationException</c> niet meer onderscheiden, en dat is voor de operator het
    /// weggooien van juist de diagnose die hij nodig heeft.
    /// </remarks>
    public const string TweedeTypenaam = "SoratusAgent.Mail.ValidationException";

    /// <summary>De foutmelding die een klant hóórt te lezen: één zin, in het Nederlands.</summary>
    public const string Foutmelding = "Het boekhoudpakket antwoordde niet binnen 30 seconden.";

    /// <summary>De eerste regel van de meerregelige foutmelding — legitiem en klantleesbaar.</summary>
    public const string EersteRegel = "De voorraadregel kon niet worden weggeschreven.";

    /// <summary>
    /// Fragmenten die volgens §2 nooit op een klantscherm horen te staan.
    /// </summary>
    /// <remarks>
    /// <para>Ze staan hier als lijst omdat de test die ze zoekt niet zelf mag bepalen waar hij naar
    /// kijkt: de fixture zet de inhoud, de lijst benoemt hem, en de test vergelijkt.</para>
    ///
    /// <para><c>ValidationException</c> staat er los in, en dat is niet dubbelop met de volledige
    /// naam. Dit is het fragment dat overblijft na "even afkorten tot de korte typenaam" — de
    /// oplossing die het meest voor de hand ligt en die niets oplost: voor een klant is
    /// <c>ValidationException</c> even betekenisloos als de hele naam, en voor de operator is het het
    /// verlies van de diagnose. Zou iemand die afkorting later invoeren, dan hoort de test daarop
    /// rood te staan en niet groen.</para>
    /// </remarks>
    public static readonly string[] VerbodenInhoud =
    [
        Typenaam,
        TweedeTypenaam,
        "SoratusAgent.",
        "ValidationException",
        "at SoratusAgent",
        "/src/Sync/",
    ];

    /// <summary>
    /// De runs van het runtabblad: een lopende, twee mislukte en een geslaagde.
    /// </summary>
    /// <returns>Nieuwste eerst, zoals de opslag ze levert.</returns>
    /// <remarks>
    /// De lopende run staat er met opzet in. Die rij is de enige die de streepjes en de neutrale badge
    /// rendert, en zonder hem is dat pad op het scherm niet te zien.
    /// </remarks>
    public static IReadOnlyList<RunRecord> Runs() =>
    [
        Lopend("r-9a11", Nu - TimeSpan.FromMinutes(1)),
        Mislukt(
            "r-8f3c",
            Nu - TimeSpan.FromMinutes(5),
            Typenaam,
            Foutmelding),
        Mislukt(
            "r-7c04",
            Nu - TimeSpan.FromMinutes(20),
            TweedeTypenaam,
            MeerregeligeFoutmelding),
        Geslaagd("r-77e0", Nu - TimeSpan.FromMinutes(10)),
    ];

    /// <summary>
    /// Een foutmelding met een stacktrace erachter, zoals <c>exception.Message</c> van een
    /// <c>CosmosException</c> hem oplevert.
    /// </summary>
    /// <remarks>
    /// De eerste regel is legitiem Nederlands proza en de bronpaden beginnen daarná. Dat is precies de
    /// vorm die een lengtegrens niet aankan: te krap en het geldige bericht wordt gemangeld, te ruim en
    /// de stacktrace komt er deels alsnog door.
    /// </remarks>
    public static readonly string MeerregeligeFoutmelding =
        EersteRegel
        + "\n   at SoratusAgent.Sync.Validator.Validate(StockLine line) in /src/Sync/Validator.cs:line 88"
        + "\n   at SoratusAgent.Sync.Pipeline.Run(CancellationToken token) in /src/Sync/Pipeline.cs:line 142";

    /// <summary>Een mislukte run met een gekozen typenaam en foutmelding.</summary>
    /// <param name="id">De runId.</param>
    /// <param name="finishedAt">Wanneer de run afliep.</param>
    /// <param name="errorType">De volledige typenaam van de uitzondering.</param>
    /// <param name="errorMessage">De boodschap zoals hij in het document staat.</param>
    /// <returns>De run.</returns>
    public static RunRecord Mislukt(
        string id,
        DateTimeOffset finishedAt,
        string errorType,
        string errorMessage) =>
        Basis(id, finishedAt) with
        {
            Result = RunResult.Failed,
            ItemsProcessed = 14,
            ItemsFailed = 2,
            ErrorType = errorType,
            ErrorMessage = errorMessage,
        };

    /// <summary>Een geslaagde run.</summary>
    /// <param name="id">De runId.</param>
    /// <param name="finishedAt">Wanneer de run afliep.</param>
    /// <returns>De run.</returns>
    public static RunRecord Geslaagd(string id, DateTimeOffset finishedAt) =>
        Basis(id, finishedAt) with
        {
            Result = RunResult.Ok,
            ItemsProcessed = 31,
        };

    /// <summary>Een run die nog loopt: geen eindtijd, geen duur, geen eindstand.</summary>
    /// <param name="id">De runId.</param>
    /// <param name="startedAt">Wanneer de run begon.</param>
    /// <returns>De run.</returns>
    public static RunRecord Lopend(string id, DateTimeOffset startedAt) =>
        Basis(id, startedAt + TimeSpan.FromSeconds(12)) with
        {
            FinishedAt = null,
            DurationMs = null,
            Result = RunResult.Running,
        };

    private static RunRecord Basis(string id, DateTimeOffset finishedAt)
    {
        var started = finishedAt - TimeSpan.FromSeconds(12);

        return new RunRecord
        {
            Id = id,
            PartitionKey = RunRecord.BuildPartitionKey("factuur-intake", started),
            CustomerId = "acme-logistiek",
            AgentName = "factuur-intake",
            StartedAt = started,
            FinishedAt = finishedAt,
            DurationMs = 12_000,
            Result = RunResult.Ok,
            Trigger = TriggerKind.Timer,
            Version = "1.4.2",
        };
    }
}
