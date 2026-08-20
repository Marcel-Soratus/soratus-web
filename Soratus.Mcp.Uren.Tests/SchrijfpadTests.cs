using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Soratus.Mcp.Uren;

namespace Soratus.Mcp.Uren.Tests;

/// <summary>
/// De veiligheidseigenschap van deze server: er kan geen gefiatteerde regel de deur uit.
/// </summary>
/// <remarks>
/// <para>§5 van de spec legt vast dat alles wat een agent of koppeling inschiet als <em>te
/// fiatteren</em> landt en pas meetelt na akkoord van Soratus. Die regel wordt hier niet met een
/// waarde afgedwongen maar met de <em>afwezigheid van een veld</em>: op
/// <see cref="HourBookingRequest"/> staat geen <c>status</c>, geen <c>by</c> en geen
/// <c>source</c>.</para>
///
/// <para>Dat is dezelfde vorm die het portaal twee keer heeft gekozen —
/// <c>CustomerLogLine</c> zonder <c>extra</c>, <c>CustomerRunRow</c> zonder <c>errorType</c> — en om
/// dezelfde reden: een veld dat op <c>"pending"</c> is vastgezet, is een veld dat iemand over een
/// half jaar met een goede bedoeling van buiten instelbaar maakt. Wat er niet is, kan die grens niet
/// over.</para>
///
/// <para>Deze tests kijken daarom naar het <em>type</em> en niet naar het gedrag. Ze zouden ook
/// slagen als het gedrag ergens kapot was — dat is precies waarom ze bestaan naast de andere tests
/// en niet in plaats daarvan.</para>
/// </remarks>
public class SchrijfpadTests
{
    /// <summary>
    /// Namen die niet op het verzoek mogen staan, met per naam de reden.
    /// </summary>
    /// <remarks>
    /// De lijst is bewust ruim en dekt zowel de C#-naam als de naam op de draad. Een veld dat
    /// <c>Goedkeuring</c> heet en <c>status</c> serialiseert zou anders langs de ene helft glippen.
    /// </remarks>
    private static readonly string[] VerbodenFragmenten =
    [
        "status",
        "approve",
        "approved",
        "fiat",
        "goedkeur",
        "by",
        "bookedby",
        "source",
        "bron",
    ];

    [Fact]
    public void HetVerzoekHeeftGeenStatusveld()
    {
        PropertyInfo[] eigenschappen = typeof(HourBookingRequest).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

        foreach (PropertyInfo eigenschap in eigenschappen)
        {
            string csharpNaam = eigenschap.Name.ToLowerInvariant();
            string draadNaam = (eigenschap
                .GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? eigenschap.Name)
                .ToLowerInvariant();

            foreach (string verboden in VerbodenFragmenten)
            {
                Assert.False(
                    csharpNaam == verboden || draadNaam == verboden,
                    $"HourBookingRequest heeft een veld '{eigenschap.Name}' ({draadNaam}). " +
                    "Status, bron en 'geboekt door' worden door het portaal gezet en horen niet op " +
                    "het verzoek te staan: dan bestaat er een pad waarlangs een andere waarde de " +
                    "deur uit kan.");
            }
        }
    }

    [Fact]
    public void HetVerzoekSerialiseertPreciesVijfVelden()
    {
        var verzoek = new HourBookingRequest
        {
            CustomerId = "bakker",
            Month = "2026-08",
            Hours = 3.5m,
            Category = "Ontwikkeling",
            Note = "Koppeling met de voorraadservice afgemaakt.",
        };

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(verzoek));

        string[] namen = [.. document.RootElement.EnumerateObject().Select(static p => p.Name).Order()];

        // Precies deze vijf, want dit is de vorm uit §5. Een zesde veld erbij is een wijziging van
        // het contract met het portaal en hoort niet ongemerkt te kunnen gebeuren.
        Assert.Equal(["category", "cid", "hours", "month", "note"], namen);
    }

    [Fact]
    public void EenGoedgekeurdAntwoordWordtNietAlsBoekingGemeld()
    {
        // Het spiegelbeeld van de test hierboven. Zou het portaal ondanks alles een gefiatteerde
        // regel teruggeven, dan mag deze server dat niet als geslaagde boeking melden — dan denkt de
        // boeker dat er nog een mens naar kijkt terwijl het bedrag al meetelt.
        var antwoord = new HourBookingResponse
        {
            Id = "hourEntry-mcp-1",
            CustomerId = "bakker",
            Month = "2026-08",
            Hours = 3.5m,
            Status = "approved",
            Source = "mcp",
        };

        (string tekst, bool isFout) = BookingReport.Write(
            new BookingOutcome.Suspect(antwoord, "test"),
            new Uri("https://portal.soratus.com"));

        Assert.True(isFout);
        Assert.Contains("LET OP", tekst, StringComparison.Ordinal);
        Assert.DoesNotContain("Vastgelegd als TE FIATTEREN", tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingIsDeEnigeStatusDieDeServerVerwacht()
    {
        // Deze constante wordt niet verstuurd maar nagekeken. Verandert hij, dan verandert de
        // controle in PortalUrenClient mee, en dan hoort dat op te vallen.
        Assert.Equal("pending", HourEntryContract.PendingStatus);
        Assert.Equal("mcp", HourEntryContract.Source);
    }
}
