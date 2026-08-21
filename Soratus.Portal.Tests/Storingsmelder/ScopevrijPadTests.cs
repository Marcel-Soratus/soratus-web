using System.Reflection;
using System.Text.RegularExpressions;
using Soratus.Portal.Alerts;
using Soratus.Portal.Mail;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Storingsmelder;

/// <summary>
/// Twee grenzen die het typesysteem binnen één assembly niet kan afdwingen, en die dus hier staan.
/// </summary>
/// <remarks>
/// <para><strong>De eerste: het scopevrije leespad heeft precies één aanroeper.</strong>
/// <c>AgentScanTarget</c> zegt in zijn documentatie wat het is en vooral wat het níet is — waar en van
/// wie, en geen bewijs dat iemand het mag zien. Maar <c>internal</c> reikt tot in de schermen, dus een
/// pagina zou dat pad kunnen aanroepen en de scopecontrole overslaan. Een type kan dat binnen één
/// assembly niet tegenhouden; deze test wel. Komt er ooit een tweede aanroeper, dan is dat een besluit
/// dat iemand moet nemen en niet iets dat per ongeluk gebeurt.</para>
///
/// <para><strong>De tweede: de melder kan geen klantadres bereiken.</strong> De storingsmelding draagt
/// een stacktrace en een <c>errorType</c> met onze naamruimtestructuur, en dat mag omdat de lezer een
/// operator is (§5: storingsmeldingen aan Soratus). Die garantie leunt erop dat er in <c>Alerts/</c>
/// geen weg naar een e-mailadres van een klant bestaat — en dat is een <em>afwezigheid</em>, precies
/// wat een gedragstest niet kan aantonen.</para>
///
/// <para>Dezelfde vorm en dezelfde reden als <c>StoreImplementatieTests</c> en
/// <c>MailbroncodeTests</c>.</para>
/// </remarks>
public class ScopevrijPadTests
{
    /// <summary>
    /// De bestanden waarin het scopevrije pad mág voorkomen.
    /// </summary>
    /// <remarks>
    /// Het eerste is de definitie zelf plus de twee plekken waar de gewone, scopedragende methoden hem
    /// aanmaken uit een <c>CustomerScope</c>. Het tweede is de enige aanroeper.
    /// </remarks>
    private static readonly string[] Toegestaan =
    [
        "Data/CosmosAgentTelemetryStore.cs",
        "Alerts/AgentFaultSource.cs",
    ];

    [Fact]
    public void HetScopevrijeLeespadHeeftPreciesEenAanroeper()
    {
        var patroon = new Regex(
            @"\bAgentScanTarget\b|\.ScanAsync\s*\(\s*new\s+AgentScanTarget",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        var gevonden = Broncode.Portaalbestanden()
            .Where(bestand => !Toegestaan.Contains(Broncode.RelatiefPad(bestand), StringComparer.Ordinal))
            .Where(bestand => Regels(bestand).Any(regel => patroon.IsMatch(regel.Tekst)))
            .Select(Broncode.RelatiefPad)
            .ToArray();

        Assert.True(
            gevonden.Length == 0,
            "Het scopevrije leespad van de telemetrie wordt aangeroepen buiten de storingsmelder:\n"
            + string.Join("\n", gevonden)
            + "\n\nDat pad bestaat omdat een achtergronddienst geen mens en dus geen CustomerScope "
            + "heeft. Elke andere aanroeper hééft er een — of hoort er een te hebben — en gaat via "
            + "IAgentTelemetryStore. Zou een pagina hier langskomen, dan slaat hij de autorisatie over "
            + "en is de hele scopeconstructie een gebaar. Is een tweede aanroeper werkelijk nodig, dan "
            + "hoort dat een besluit te zijn: zet hem in Toegestaan met de reden erbij.");

        // De spiegel: zonder deze regel blijft de test hierboven groen als het patroon nergens meer
        // voorkomt — bijvoorbeeld doordat het type is hernoemd. Dan meet hij niets meer.
        Assert.Contains(
            "Alerts/AgentFaultSource.cs",
            Broncode.Portaalbestanden()
                .Where(bestand => Regels(bestand).Any(regel => patroon.IsMatch(regel.Tekst)))
                .Select(Broncode.RelatiefPad),
            StringComparer.Ordinal);
    }

    [Fact]
    public void DeMelderRaaktGeenEnkelKlantadresAan()
    {
        // De ontvangers van een storingsmelding komen uit configuratie en nergens anders. Deze lijst is
        // de weg waarlangs een klantadres in het portaal te vinden is; staat er ooit iets van in Alerts/,
        // dan kan de opmaak met een stacktrace erin bij een klant belanden.
        string[] verboden =
        [
            "AccessDocument",
            "GetAccessAsync",
            "StatementRecipients",
            "StatementAddressing",
            "IPortalDataStore",
            "IPortalHoursStore",
            "PortalAccessRoles",
        ];

        var gevonden = Melderbestanden()
            .SelectMany(bestand => Regels(bestand)
                .SelectMany(regel => verboden
                    .Where(woord => regel.Tekst.Contains(woord, StringComparison.Ordinal))
                    .Select(woord => $"{Broncode.RelatiefPad(bestand)}:{regel.Nummer}  {woord}")))
            .ToArray();

        Assert.True(
            gevonden.Length == 0,
            "De storingsmelder raakt een bron van klantadressen aan:\n"
            + string.Join("\n", gevonden)
            + "\n\nDeze mail draagt een agentnaam, een errorType met onze naamruimte en een "
            + "stacktrace. Dat mag, omdat de lezer een operator is (§5 van de spec: storingsmeldingen "
            + "aan Soratus). Die hele redenering leunt erop dat er hier geen weg naar een klantadres "
            + "bestaat. De ontvangers komen uit PortalAlerts:Recipients en nergens anders.");
    }

    [Fact]
    public void DeMelderMaaktGeenKlantmailEnDeMailkantGeenStoringsmelding()
    {
        // Het typeverschil, van beide kanten. StatementMail en AgentAlertMail zijn broertjes onder
        // OutgoingMail en geen van beide is de ander: er is geen pad waarlangs een storingsmelding het
        // klantpad neemt, want dat pad neemt het andere type aan.
        Assert.NotEqual(typeof(StatementMail), typeof(AgentAlertMail));
        Assert.False(typeof(StatementMail).IsAssignableFrom(typeof(AgentAlertMail)));
        Assert.False(typeof(AgentAlertMail).IsAssignableFrom(typeof(StatementMail)));

        Assert.Equal(typeof(OutgoingMail), typeof(StatementMail).BaseType);
        Assert.Equal(typeof(OutgoingMail), typeof(AgentAlertMail).BaseType);

        // En beide zijn alleen door hun eigen opmaakfunctie te maken: geen publieke constructor.
        foreach (var type in new[] { typeof(StatementMail), typeof(AgentAlertMail) })
        {
            Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }
    }

    [Fact]
    public void DeDocumentsoortBotstNietMetEenBestaande()
    {
        // Twee soorten met dezelfde kind-waarde in dezelfde container is een fout die niet zichtbaar is:
        // een query op kind levert dan documenten van het verkeerde type op en de deserialisatie vult de
        // ontbrekende velden met hun standaardwaarde. Deze test bestaat omdat de constante niet naast de
        // andere staat — zie AgentAlertDocumentKeys.
        var bestaand = new[]
            {
                typeof(Portal.Data.PortalDocumentKinds),
                typeof(Portal.Data.AzureCostDocumentKeys),
            }
            .SelectMany(type => type
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(veld => veld.FieldType == typeof(string))
                .Select(veld => (string)veld.GetValue(null)!))
            .Append(StatementDocumentKeys.Kind)
            .ToArray();

        Assert.NotEmpty(bestaand);
        Assert.DoesNotContain(AgentAlertDocumentKeys.Kind, bestaand, StringComparer.Ordinal);
    }

    private static IEnumerable<FileInfo> Melderbestanden() =>
        Broncode.Portaalbestanden()
            .Where(bestand => Broncode.RelatiefPad(bestand)
                .StartsWith("Alerts/", StringComparison.Ordinal));

    private static IEnumerable<(int Nummer, string Tekst)> Regels(FileInfo bestand) =>
        File.ReadAllLines(bestand.FullName)
            .Select((tekst, index) => (Nummer: index + 1, Tekst: tekst))

            // Commentaar en documentatie doen niet mee: daar staat juist de uitleg waarom er geen
            // klantadres wordt gelezen, en die noemt de namen.
            .Where(regel => !regel.Tekst.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(regel => !regel.Tekst.TrimStart().StartsWith("///", StringComparison.Ordinal));
}
