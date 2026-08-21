using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Soratus.Portal.Data;
using Soratus.Portal.Mail;
using Soratus.Portal.Security;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Maandoverzicht;

/// <summary>
/// Eigenschappen van de mailkant die niet aan gedrag te meten zijn, maar aan de code zelf.
/// </summary>
/// <remarks>
/// <para>Drie van de vier controles hieronder bewaken een <em>afwezigheid</em>, en dat is precies wat
/// een gedragstest niet kan: dat de mailkant nergens een bedrag uitrekent, dat er nergens een
/// foutmelding in een mail belandt en dat de rolgrens een typeverschil is en geen <c>@if</c>. Een
/// gedragstest zou dat per geval moeten aantonen en blijft groen zodra iemand een pad toevoegt dat
/// de test niet kent.</para>
///
/// <para>Dezelfde vorm en dezelfde reden als <c>UrencomponentTests</c> en <c>AanmeldpadTests</c> in
/// de MCP-server: kijken naar wat er überhaupt te doen valt in plaats van naar het laatste station.
/// </para>
/// </remarks>
public class MailbroncodeTests
{
    /// <summary>De bedragen en de lokale namen ervoor, zoals ze in deze map voorkomen.</summary>
    private const string Bedragen =
        "AzureAmount|ExtraHoursAmount|Total|UsedHours|BundledHours|ExtraHours|azure|extraAmount|total";

    [Fact]
    public void DeMailkantRekentNergensMetEenBedrag()
    {
        // Een tweede plek die een bedrag berekent, is een tweede plek die het anders kan berekenen —
        // en dan kan de mail een ander bedrag noemen dan het scherm. Deze test is grof: hij verbiedt
        // elke rekenkundige operator naast de naam van een bedrag. Dat vangt "azure + extraAmount"
        // en het vangt ook een onschuldige aanpassing, en dat is hier de bedoeling: het besluit dat
        // deze map niet rekent hoort een gesprek te kosten en geen commit.
        var patroon = new Regex(
            $@"\b({Bedragen})\b\s*[-+*/]|[-+*/]\s*\b({Bedragen})\b",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        var gevonden = Mailbestanden()
            .SelectMany(bestand => Regels(bestand)
                .Where(regel => patroon.IsMatch(regel.Tekst))
                .Select(regel => $"{Broncode.RelatiefPad(bestand)}:{regel.Nummer}  {regel.Tekst.Trim()}"))
            .ToArray();

        Assert.True(
            gevonden.Length == 0,
            "Er wordt in Soratus.Portal/Mail met een bedrag gerekend:\n"
            + string.Join("\n", gevonden)
            + "\n\nDe bedragen komen uit IMonthlyStatementFigures en worden doorgegeven zoals ze "
            + "zijn. Hoort hier een berekening, dan hoort ze aan de kostenkant.");
    }

    [Fact]
    public void ErStaatGeenFoutmeldingOpEenPadNaarEenMail()
    {
        // Punt 13 en 14: tekst die door onze eigen systemen is geschreven en bij een klant belandt.
        // Deze test kijkt naar de twee bestanden die de mail samenstellen. De verzender mag een
        // uitzondering wél lezen — hij logt hem — en staat daarom niet in deze lijst.
        var patroon = new Regex(
            @"\bex(ception)?\.(Message|StackTrace|ToString)|\bErrorCode\b",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(5));

        // MailText.cs staat erbij sinds de verzendlaag is geëxtraheerd: de knip op de eerste regel is
        // gedeeld met de storingsmelder, en dat is precies de plek waar iemand ooit een foutmelding
        // "even" zou kunnen laten meeliften naar het klantpad. Alerts/ staat er níet bij en hoort er
        // niet bij: die opmaak gaat naar Soratus en mág een stacktrace dragen (§5 van de spec, punt 43).
        var opmaak = new[] { "Mail/StatementMail.cs", "Mail/StatementText.cs", "Mail/MailText.cs" };

        var gevonden = Mailbestanden()
            .Where(bestand => opmaak.Contains(Broncode.RelatiefPad(bestand), StringComparer.Ordinal))
            .SelectMany(bestand => Regels(bestand)
                .Where(regel => patroon.IsMatch(regel.Tekst))
                .Select(regel => $"{Broncode.RelatiefPad(bestand)}:{regel.Nummer}  {regel.Tekst.Trim()}"))
            .ToArray();

        Assert.True(
            gevonden.Length == 0,
            "De opmaak van de mail raakt een foutmelding aan:\n"
            + string.Join("\n", gevonden)
            + "\n\nEen Exception.Message van een dienstverlener hoort op een operatorscherm en in een "
            + "logregel, nooit in een postbus van een klant.");
    }

    [Fact]
    public void ErStaatGeenRolvoorwaardeInDeMarkup()
    {
        var kaart = Mailbestanden()
            .Single(bestand => Broncode.RelatiefPad(bestand) == "Mail/MonthlyStatementCard.razor");

        var patroon = new Regex(
            @"@if\s*\(\s*!?\s*(is|_is)?[Oo]perator",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        // Razorcommentaar eraf. De toelichting bovenaan het bestand legt uit waarom er géén
        // `@if (isOperator)` in staat, en die zin bevat dus letterlijk het patroon. Deze test viel
        // daarop om bij de eerste run — precies het soort valse meting waar dit project er tien van
        // heeft gehad, en hier zichtbaar geworden in plaats van weggemoffeld.
        var zonderCommentaar = new Regex(
                @"@\*.*?\*@",
                RegexOptions.Singleline,
                TimeSpan.FromSeconds(5))
            .Replace(File.ReadAllText(kaart.FullName), string.Empty);

        Assert.DoesNotMatch(patroon, zonderCommentaar);

        // De spiegel: zonder deze regel blijft de test hierboven groen als het commentaar per
        // ongeluk de hele markup blijkt te zijn.
        Assert.Contains("CustomerWriteScope", zonderCommentaar, StringComparison.Ordinal);
    }

    [Fact]
    public void DeKaartNeemtAlleenHetSchrijfrechtAanEnGeenViewmodel()
    {
        // De rolgrens is een typeverschil. De enige parameter van dit component is een type dat een
        // klantgebruiker niet kan produceren; er bestaat dus geen klantpagina die hem kan renderen.
        var parameters = typeof(MonthlyStatementCard)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(eigenschap => eigenschap.GetCustomAttribute<ParameterAttribute>() is not null)
            .ToArray();

        var enige = Assert.Single(parameters);

        Assert.Equal(nameof(MonthlyStatementCard.Scope), enige.Name);
        Assert.Equal(typeof(CustomerWriteScope), Nullable.GetUnderlyingType(enige.PropertyType)
            ?? enige.PropertyType);
    }

    [Fact]
    public void GeenEnkelTypeOpHetMailpadDraagtDeFiatteringsstroom()
    {
        // Dezelfde lijst en dezelfde reden als UrencomponentTests: de acceptatie van fase 3 is dat de
        // klant niets van die stroom ziet, en een mail is de makkelijkste plek om die eis alsnog te
        // breken. "status" staat er met opzet niet bij; zie de toelichting daar.
        string[] verboden = ["pending", "approv", "reject", "etag", "fiat"];

        var gevonden = new[]
            {
                typeof(MonthlyStatementFigures),
                typeof(StatementMail),
                typeof(StatementAddressing),
            }
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(eigenschap => $"{type.Name}.{eigenschap.Name}"))
            .Where(lid => verboden.Any(woord => lid.Contains(woord, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            gevonden.Length == 0,
            "Een type op het mailpad draagt een lid over de fiatteringsstroom:\n"
            + string.Join("\n", gevonden));
    }

    [Fact]
    public void DeDocumentsoortBotstNietMetEenBestaande()
    {
        // Twee soorten met dezelfde kind-waarde in dezelfde container is een fout die niet zichtbaar
        // is: een query op kind levert dan documenten van het verkeerde type op en de deserialisatie
        // vult de ontbrekende velden met hun standaardwaarde. Deze test bestaat omdat de constante
        // niet naast de andere vier staat — zie StatementDocumentKeys.
        var bestaand = typeof(PortalDocumentKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(veld => (string)veld.GetValue(null)!)
            .ToArray();

        Assert.NotEmpty(bestaand);
        Assert.DoesNotContain(StatementDocumentKeys.Kind, bestaand, StringComparer.Ordinal);
    }

    [Fact]
    public void DeProefdraaimodusStaatStandaardAan()
    {
        // De onveilige stand hoort iets te zijn dat iemand aanzet en niet iets dat je vergeet uit te
        // zetten. Een mail is niet terug te halen.
        Assert.True(new PortalMailOptions().DryRun);
    }

    [Fact]
    public void ZonderEndpointOfAfzenderIsErGeenAfzender()
    {
        Assert.Null(new PortalMailOptions().Sender());
        Assert.Null(new PortalMailOptions { Endpoint = "https://x" }.Sender());
        Assert.Null(new PortalMailOptions { FromAddress = "a@b.nl" }.Sender());
        Assert.NotNull(new PortalMailOptions
        {
            Endpoint = "https://x",
            FromAddress = "a@b.nl",
        }.Sender());
    }

    private static IEnumerable<FileInfo> Mailbestanden() =>
        Broncode.Portaalbestanden()
            .Where(bestand => Broncode.RelatiefPad(bestand)
                .StartsWith("Mail/", StringComparison.Ordinal));

    private static IEnumerable<(int Nummer, string Tekst)> Regels(FileInfo bestand) =>
        File.ReadAllLines(bestand.FullName)
            .Select((tekst, index) => (Nummer: index + 1, Tekst: tekst))

            // Commentaar en documentatie doen niet mee: daar staat juist de uitleg waarom er niet
            // gerekend wordt, en die noemt de namen van de bedragen.
            .Where(regel => !regel.Tekst.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(regel => !regel.Tekst.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .Where(regel => !regel.Tekst.TrimStart().StartsWith("*", StringComparison.Ordinal));
}
