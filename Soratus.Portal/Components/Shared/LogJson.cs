using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Soratus.Agents.Contracts;

namespace Soratus.Portal.Components.Shared;

/// <summary>
/// De onderliggende JSON van één logregel, zoals hij in de uitklap onder de rij komt te staan.
/// </summary>
/// <remarks>
/// <para>Dit is presentatie en geen opslagvorm: de velden staan in de volgorde waarin een mens ze
/// leest (wanneer, hoe erg, wie, waarover, wat) en <c>extra</c> wordt platgeslagen in het
/// hoofdobject, precies zoals de mockup het doet. Wie de payload uit dit venster kopieert en in
/// een editor plakt, houdt geldige JSON over.</para>
///
/// <para>De tekst wordt pas opgemaakt als de rij wordt uitgeklapt. Bij vijfhonderd regels met een
/// stacktrace per stuk zou vooraf formatteren megabytes aan tekenreeksen kosten die niemand
/// leest.</para>
///
/// <para><strong>Deze klasse filtert niets, en dat hoort zo — lees dit voordat je hier een filter
/// inbouwt.</strong> <c>extra</c> is vrije JSON die de agentbouwer vult, en alles wat erin staat
/// komt letterlijk in de uitklap. In de echte telemetrie staan daar dingen die een klant volgens §2
/// niet mag zien: een Graph-endpoint, een OAuth-scope, onze bronpaden in een stacktrace, een
/// resource group, en bij de interne beheerklant de slugs van andere klanten. Dat lek is echt en
/// het is vastgelegd in <c>KlantLogregelTests</c>.</para>
///
/// <para>De verleiding is om het hier te repareren, want dit is de laatste plek waar de tekst langs
/// komt. Doe dat niet. Een blokkeerlijst op sleutelnamen in vrije JSON is niet te sluiten: hij dekt
/// de namen die je vandaag kent, en de eerste agent die zijn scope onder <c>auth</c> in plaats van
/// <c>scope</c> zet, lekt er weer langs — met een filter dat de schijn geeft dat er een grens is.
/// Erger nog: een filter hier kan niet weten wíe er kijkt, en rol is geen eigenschap van een
/// tekenreeks.</para>
///
/// <para><strong>Waar de grens werkelijk ligt.</strong> Niet in een leeggemaakt veld maar in een
/// ander type: <c>CustomerAgentLogsView.Lines</c> is een lijst van <c>CustomerLogLine</c> — id,
/// moment, niveau, gebeurtenis, bericht, runId — en dat type heeft geen <c>Extra</c>, geen
/// <c>AgentName</c> en geen <c>CustomerId</c>. Niet leeg dus, maar afwezig. Dat is sterker dan wat
/// hier eerst stond ("een <see cref="LogRecord"/> waarvan <c>Extra</c> leeg is"): een veld dat niet
/// bestaat kun je ook niet per ongeluk weer vullen, en het is dezelfde regel die de rest van het
/// portaal volgt met twee viewmodels per rol in plaats van vlaggen op één.</para>
///
/// <para><strong>Gevolg voor deze klasse: dit is het operatorpad.</strong>
/// <c>OperatorAgentLogsView.Lines</c> blijft <see cref="LogRecord"/> houden, en daar hoort
/// <c>extra</c> ook te staan — een operator die een storing uitzoekt heeft die stacktrace nodig. De
/// klantzijde heeft zijn eigen tabel op <c>CustomerLogLine</c>, zonder uitklap. Richt <c>LogTable</c>
/// dus nooit op een klantscherm: dan komt deze methode weer op vrije JSON uit en staat het lek
/// terug. Uiteindelijk hoort de grens nog verder naar voren, bij het schrijven in
/// <c>Soratus.Agents.Telemetry</c>, zodat zulke velden er niet eens in staan.</para>
/// </remarks>
public static class LogJson
{
    /// <summary>
    /// De veldnamen die dit object zelf zet. Staat er in <c>extra</c> een sleutel met dezelfde
    /// naam, dan wint de logregel — anders levert een agent die per ongeluk <c>msg</c> in zijn
    /// context zet een object met twee keer dezelfde sleutel op, en dat is geen geldige JSON.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "id",
        "ts",
        "level",
        "agentName",
        "customerId",
        "runId",
        "event",
        "msg",
    };

    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,

        // De uitkomst wordt door Blazor als tekstinhoud gerenderd en daar dus HTML-geëscaped.
        // Binnen dit venster mag de JSON daarom leesbaar blijven in plaats van elke aanhaling en
        // elk accentteken als ' te tonen — een stacktrace met < erin leest niemand.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Maakt de uitklaptekst voor één logregel.</summary>
    /// <param name="record">De logregel.</param>
    /// <returns>Geïndenteerde JSON, zonder afsluitende regelovergang.</returns>
    public static string Format(LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, Options))
        {
            writer.WriteStartObject();

            writer.WriteString("id", record.Id);
            writer.WriteString("ts", TimeFormat.Iso(record.Timestamp));
            writer.WriteString("level", Level(record.Level));
            writer.WriteString("agentName", record.AgentName);
            writer.WriteString("customerId", record.CustomerId);

            if (record.RunId is { } runId)
            {
                writer.WriteString("runId", runId);
            }
            else
            {
                // Bewust wél als null in beeld: "deze regel viel buiten een run" is informatie,
                // en een ontbrekend veld ziet eruit als iets dat we vergeten zijn te schrijven.
                writer.WriteNull("runId");
            }

            writer.WriteString("event", record.Event);
            writer.WriteString("msg", record.Message);

            WriteExtra(writer, record.Extra);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>De naam van het niveau zoals hij ook in Cosmos staat.</summary>
    private static string Level(LogLevel level) => level switch
    {
        LogLevel.Warn => "warn",
        LogLevel.Error => "error",
        _ => "info",
    };

    /// <summary>
    /// Zet de vrije context erbij: een object wordt platgeslagen, al het andere komt onder de
    /// sleutel <c>extra</c> te staan.
    /// </summary>
    private static void WriteExtra(Utf8JsonWriter writer, JsonElement? extra)
    {
        if (extra is not { } value || value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return;
        }

        try
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                writer.WritePropertyName("extra");
                value.WriteTo(writer);
                return;
            }

            foreach (var property in value.EnumerateObject())
            {
                if (Reserved.Contains(property.Name))
                {
                    continue;
                }

                property.WriteTo(writer);
            }
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            // Een JsonElement leeft in het document waaruit hij komt. Is dat document al
            // opgeruimd, dan is deze context onleesbaar — maar dan hoort het scherm niet om te
            // vallen op een uitklap die iemand uit nieuwsgierigheid openzet. De reden komt in
            // beeld, zodat de leegte niet als "er was geen context" wordt gelezen.
            //
            // Alleen als we nog in het hoofdobject staan: brak het schrijven halverwege een
            // genest object af, dan zou een extra sleutel op die plek ongeldige JSON opleveren.
            if (writer.CurrentDepth == 1)
            {
                writer.WriteString("extra", "context niet meer beschikbaar");
            }
        }
    }
}
