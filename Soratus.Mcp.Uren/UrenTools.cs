using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Soratus.Mcp.Uren;

/// <summary>
/// De tool die deze server aanbiedt: <c>uren.boeken</c>.
/// </summary>
/// <remarks>
/// Eén tool, met de parameters uit §5 van de spec:
/// <c>uren.boeken({ klant, maand, uren, categorie, omschrijving })</c>.
///
/// <para><strong>De tool heet <c>uren_boeken</c> en niet <c>uren.boeken</c>, en dat kan niet
/// anders.</strong> De Messages-API van Anthropic eist dat een toolnaam op
/// <c>^[a-zA-Z0-9_-]{1,64}$</c> past. Claude Code voegt er zijn eigen voorvoegsel voor
/// (<c>mcp__soratus-uren__…</c>) en stuurt het geheel als toolnaam mee; een punt levert daar een
/// <c>400</c> op bij élke prompt in de sessie, niet pas bij het aanroepen van deze tool. De
/// MCP-specificatie zelf verbiedt de punt niet, dus dit is een clientgrens en geen protocolgrens —
/// maar het is de client waar deze server voor bestaat. De naam uit §5 blijft in de titel en de
/// beschrijving staan, zodat hij vindbaar is.</para>
///
/// <para><strong>De parameternamen zijn Nederlands, en dat is geen afwijking van de conventie.</strong>
/// De conventie in deze repo is: Engelse identifiers, Nederlandse documentatie. Deze namen zijn
/// echter geen identifiers die wij kiezen — het MCP-SDK gebruikt de parameternaam letterlijk als
/// veldnaam in het JSON-schema van de tool, en dat schema is de publieke vorm die §5 vastlegt. Ze
/// hernoemen zou de vorm uit de spec veranderen.</para>
/// </remarks>
[McpServerToolType]
internal sealed class UrenTools(
    PortalUrenClient client,
    IOptions<UrenOptions> options,
    TimeProvider clock,
    ILogger<UrenTools> logger)
{
    /// <summary>
    /// De naam waaronder de tool wordt aangeboden.
    /// </summary>
    /// <remarks>
    /// Staat als constante zodat er een test op kan die de naam tegen
    /// <c>^[a-zA-Z0-9_-]{1,64}$</c> houdt. Zonder die test is de fout die dit had kunnen worden — een
    /// <c>400</c> op elke prompt in de sessie — pas te zien nadat iemand de server heeft aangesloten.
    /// </remarks>
    internal const string ToolName = "uren_boeken";

    private readonly UrenOptions _options = options.Value;

    /// <summary>
    /// Boekt uren voor een klant in het Soratus Agent Portal.
    /// </summary>
    /// <param name="klant">De klant, als slug.</param>
    /// <param name="maand">De maand als <c>jjjj-MM</c>.</param>
    /// <param name="uren">Het aantal uren.</param>
    /// <param name="categorie">De categorie.</param>
    /// <param name="omschrijving">Eén zin over wat er is gedaan.</param>
    /// <param name="cancellationToken">Afbreken.</param>
    /// <returns>De melding voor de aanroeper.</returns>
    [McpServerTool(
        // Zie de opmerking bij de klasse: een punt in de naam breekt de Messages-API.
        Name = ToolName,
        Title = "Uren boeken in het Soratus Agent Portal (uren.boeken)",
        // Deze annotaties zijn geen decoratie; een client gebruikt ze om te bepalen of hij mag
        // doorpakken zonder te vragen. Niet read-only (er wordt iets vastgelegd), niet destructief
        // (er wordt niets overschreven of verwijderd) en niet idempotent — twee keer boeken levert
        // twee regels op. Zie de opmerking over idempotentie in docs/agent-portal/mcp-uren.md.
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true)]
    [Description(
        "Boekt uren voor een klant in het Soratus Agent Portal. De regel landt ALTIJD als 'te " +
        "fiatteren' en telt pas mee in het maandtotaal en in de facturatie nadat een operator van " +
        "Soratus hem in het portaal heeft gefiatteerd. Meld dat altijd aan de gebruiker en zeg nooit " +
        "dat de uren verwerkt, goedgekeurd of gefactureerd zijn. Alleen een Soratus-operator kan " +
        "hiermee boeken; een klantaccount wordt door het portaal geweigerd. Bij een fout wordt er " +
        "niets vastgelegd — herstel de melding en boek opnieuw.")]
    public async Task<CallToolResult> BoekenAsync(
        [Description(
            "De klant, als slug in kleine letters zoals hij in de portaal-URL staat (/klant/<slug>/…), " +
            "bijvoorbeeld 'bakker'. Niet de bedrijfsnaam.")]
        string klant,
        [Description(
            "De maand waarop de uren worden geboekt, als jjjj-MM, bijvoorbeeld '2026-08'. Mag niet in " +
            "de toekomst liggen.")]
        string maand,
        [Description(
            "Het aantal uren, groter dan nul en maximaal 200 per regel, met maximaal twee decimalen. " +
            "Een correctie naar beneden gaat niet via deze koppeling.")]
        decimal uren,
        [Description(
            "De categorie. Op het moment van schrijven: Ontwikkeling, Beheer, Support of Advies. Dit " +
            "is een voorbeeld en geen gezag — het portaal beslist, en de afwijzing noemt de geldige " +
            "waarden. 'Correctie' kan hier niet: dat is voorbehouden aan een handmatige correctie in " +
            "het portaal.")]
        string categorie,
        [Description(
            "Eén zin in het Nederlands over wat er is gedaan. De klant leest dit op zijn specificatie, " +
            "dus geen bestandspaden, klassenamen, endpoints of namen van andere klanten. Eén regel; " +
            "meer regels worden geweigerd en niet afgeknipt.")]
        string omschrijving,
        CancellationToken cancellationToken)
    {
        var input = new HourBookingInput(klant, maand, uren, categorie, omschrijving);

        // Alleen de vorm. Of deze klant en deze categorie bestaan, weet het portaal; die vraag wordt
        // daar gesteld en de afwijzing komt hier ongewijzigd terug. Zie HourBookingValidation voor
        // waarom een gekopieerde categorielijst hier niet mag en in de beschrijving hierboven wel.
        IReadOnlyList<string> errors = HourBookingValidation.Check(
            input,
            _options.AllowedCustomers,
            clock.GetUtcNow());

        if (errors.Count > 0)
        {
            logger.LogInformation(
                "Boeking voor '{Klant}' in '{Maand}' is hier afgewezen op {Aantal} punt(en); er is niets verstuurd.",
                klant,
                maand,
                errors.Count);

            return Result(new BookingOutcome.Refused(errors, Sent: false));
        }

        HourBookingRequest request = HourBookingValidation.ToRequest(input);
        BookingOutcome outcome = await client.BookAsync(request, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Boeking voor '{Klant}' in '{Maand}': {Uitkomst}.",
            request.CustomerId,
            request.Month,
            outcome.GetType().Name);

        return Result(outcome);
    }

    /// <summary>
    /// Bouwt het toolresultaat: de tekst voor de mens en de stand voor de machine.
    /// </summary>
    /// <remarks>
    /// Beide, en niet één van de twee. De tekst is wat een mens leest en waarin staat dat de regel nog
    /// gefiatteerd moet worden; <see cref="BookingState"/> is dezelfde mededeling als veld, zodat een
    /// aanroeper die alleen naar <c>isError</c> kijkt niet uit <c>false</c> kan concluderen dat het
    /// klaar is. Ze komen uit dezelfde <see cref="BookingOutcome"/>, dus ze kunnen niet uiteenlopen.
    /// </remarks>
    private CallToolResult Result(BookingOutcome outcome)
    {
        Uri portal = _options.PortalBaseAddress!;
        (string text, bool isError) = BookingReport.Write(outcome, portal);

        return new CallToolResult
        {
            IsError = isError,
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = BookingState.From(outcome, portal).ToJson(),
        };
    }
}
