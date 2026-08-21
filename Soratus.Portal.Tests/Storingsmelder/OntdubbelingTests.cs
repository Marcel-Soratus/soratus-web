using Soratus.Agents.Contracts;
using Soratus.Portal.Alerts;
using Soratus.Portal.Data;
using Soratus.Portal.Mail;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Storingsmelder;

/// <summary>
/// Wanneer een storing opnieuw gemeld mag worden.
/// </summary>
/// <remarks>
/// <see cref="AgentAlertDecision.Judge"/> is een pure functie zonder klok, dus dit is de plek waar het
/// herhaalvenster te meten is zonder zes uur te wachten. De melder erboven wordt in
/// <c>MelderTests</c> gemeten; hier staat de regel zelf.
/// </remarks>
public class OntdubbelingTests
{
    private static readonly TimeSpan Venster = TimeSpan.FromHours(6);

    [Fact]
    public void ZonderMarkeringIsHetDeEersteMelding() =>
        Assert.Equal(
            AlertDue.First,
            AgentAlertDecision.Judge(marker: null, AgentStatus.Failed, Testgegevens.Nu, Venster));

    [Fact]
    public void BinnenHetVensterGaatErNietsUit()
    {
        // Dit is de kern. ShouldAlert levert voor Failed elke aanroep true, dus zonder deze regel mailt
        // een melder die elke minuut draait zestig keer per uur over dezelfde mislukte run.
        var markering = Markering(AgentStatus.Failed, Testgegevens.Nu - TimeSpan.FromMinutes(1));

        Assert.Equal(
            AlertDue.Suppressed,
            AgentAlertDecision.Judge(markering, AgentStatus.Failed, Testgegevens.Nu, Venster));
    }

    [Fact]
    public void NaHetVensterWordtErOpnieuwGemeld()
    {
        // Eén melding per storing is óók fout: een storing die drie dagen duurt en één keer is gemeld,
        // is een storing waarvan niemand meer weet dat hij er is.
        var markering = Markering(AgentStatus.Failed, Testgegevens.Nu - Venster - TimeSpan.FromMinutes(1));

        Assert.Equal(
            AlertDue.Repeat,
            AgentAlertDecision.Judge(markering, AgentStatus.Failed, Testgegevens.Nu, Venster));
    }

    [Fact]
    public void PreciesOpDeGrensGaatErNogNietsUit()
    {
        // Groter dan en niet groter-of-gelijk, dezelfde vorm als AgentStatusCalculator. Deze test staat
        // er omdat het de enige grens is die met een tekenfout de andere kant op valt.
        var markering = Markering(AgentStatus.Failed, Testgegevens.Nu - Venster);

        Assert.Equal(
            AlertDue.Suppressed,
            AgentAlertDecision.Judge(markering, AgentStatus.Failed, Testgegevens.Nu, Venster));
    }

    [Theory]
    [InlineData(AgentStatus.Degraded, AgentStatus.Failed)]
    [InlineData(AgentStatus.Failed, AgentStatus.Degraded)]
    public void EenVeranderdeStatusMeldtMeteen(AgentStatus gemeld, AgentStatus nu)
    {
        // Beide kanten op. Degraded naar Failed is een verergering en wachten zou die informatie zes uur
        // oud maken; Failed naar Degraded is een ander beeld, en de operator hoort niet uit een oude
        // mail te concluderen wat er nú aan de hand is.
        var markering = Markering(gemeld, Testgegevens.Nu - TimeSpan.FromMinutes(1));

        Assert.Equal(AlertDue.Changed, AgentAlertDecision.Judge(markering, nu, Testgegevens.Nu, Venster));
    }

    [Fact]
    public void EenAfgeslotenMarkeringGeldtAlsGeenMarkering()
    {
        // Een storing die weg was en terugkomt is een nieuwe storing, ook al is het dezelfde agent en
        // dezelfde status. Zonder deze regel zou een agent die gisteren omviel, herstelde en vandaag
        // opnieuw omvalt pas na het venster worden gemeld.
        var markering = Markering(AgentStatus.Failed, Testgegevens.Nu - TimeSpan.FromMinutes(1))
            with { ClearedAt = Testgegevens.Nu - TimeSpan.FromSeconds(30) };

        Assert.Equal(
            AlertDue.First,
            AgentAlertDecision.Judge(markering, AgentStatus.Failed, Testgegevens.Nu, Venster));
    }

    [Fact]
    public void EenKlokDieAchteruitIsGezetMeldtNietsExtra()
    {
        // Een negatief verschil valt op de veilige kant. De andere kant is een mail per ronde zolang de
        // klok achterloopt.
        var markering = Markering(AgentStatus.Failed, Testgegevens.Nu + TimeSpan.FromHours(12));

        Assert.Equal(
            AlertDue.Suppressed,
            AgentAlertDecision.Judge(markering, AgentStatus.Failed, Testgegevens.Nu, Venster));
    }

    [Fact]
    public void DeMarkeringStaatInDeGereserveerdePartitieEnNietBijDeKlant()
    {
        // Dit is Soratus-eigen boekhouding over onze eigen meldingen; de klant heeft er niets mee te
        // maken en ziet hem nergens. En het levert de goedkope lezing op: alle markeringen in één
        // partitie, dus één query per ronde in plaats van een cross-partition query.
        var markering = Markering(AgentStatus.Failed, Testgegevens.Nu);

        Assert.Equal(PortalDocumentIds.ReservedPartitionKey, markering.PartitionKey);
        Assert.StartsWith("$", markering.PartitionKey, StringComparison.Ordinal);
    }

    [Fact]
    public void DeStandaardwaardeVanDeUitkomstIsOnbekend()
    {
        // Een document dat is aangemaakt en waarop daarna niets meer is geschreven — omdat het proces
        // omviel tussen de claim en de verzending — zegt "onbekend" en niet "aangenomen".
        var markering = new AgentAlertDocument
        {
            Id = "x",
            PartitionKey = PortalDocumentIds.ReservedPartitionKey,
            CustomerId = "acme",
            AgentName = "a",
            Status = AgentStatus.Failed,
            NotifiedAt = Testgegevens.Nu,
            FirstNotifiedAt = Testgegevens.Nu,
        };

        Assert.Equal(MailDelivery.Unknown, markering.Delivery);
    }

    private static AgentAlertDocument Markering(AgentStatus status, DateTimeOffset gemeldOp) => new()
    {
        Id = AgentAlertDocumentKeys.Id("acme-logistiek", "factuur-intake"),
        PartitionKey = PortalDocumentIds.ReservedPartitionKey,
        CustomerId = "acme-logistiek",
        AgentName = "factuur-intake",
        Status = status,
        NotifiedAt = gemeldOp,
        FirstNotifiedAt = gemeldOp,
        ETag = "etag-1",
    };
}
