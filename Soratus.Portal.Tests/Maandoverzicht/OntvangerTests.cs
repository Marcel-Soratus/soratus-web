using Soratus.Portal.Data;
using Soratus.Portal.Mail;

namespace Soratus.Portal.Tests.Maandoverzicht;

/// <summary>
/// Aan wie het maandoverzicht gaat, en wanneer er niemand is.
/// </summary>
/// <remarks>
/// De ontvanger is de plek waar een mail de verkeerde kant op kan gaan, en anders dan bij een scherm
/// is dat niet terug te draaien. Deze tests meten twee dingen: dat alleen de contactpersonen worden
/// geadresseerd, en dat één onbruikbaar adres de hele verzending tegenhoudt.
/// </remarks>
public class OntvangerTests
{
    [Fact]
    public void AlleenDeContactpersonenWordenGeadresseerd()
    {
        var (adressering, weigering) = StatementRecipients.Resolve(
        [
            Toegang("directie@acme.nl", "Jan Acme", PortalAccessRoles.Administrator),
            Toegang("inkoop@acme.nl", "Inkoop", PortalAccessRoles.Reader),
            Toegang("stage@acme.nl", null, PortalAccessRoles.Reader),
        ]);

        Assert.Equal(StatementRefusal.None, weigering);
        Assert.NotNull(adressering);
        Assert.Equal(["directie@acme.nl"], adressering.Recipients);
        Assert.Equal("Jan Acme", adressering.ContactName);
    }

    [Fact]
    public void EenKlantZonderContactpersoonLevertGeenOntvangerOp()
    {
        var (adressering, weigering) = StatementRecipients.Resolve(
            [Toegang("inkoop@acme.nl", "Inkoop", PortalAccessRoles.Reader)]);

        Assert.Null(adressering);
        Assert.Equal(StatementRefusal.NoRecipient, weigering);
    }

    [Fact]
    public void BijTweeContactpersonenGaanBeidenMeeEnKrijgtDeAanhefGeenNaam()
    {
        var (adressering, _) = StatementRecipients.Resolve(
        [
            Toegang("financien@acme.nl", "Marieke", PortalAccessRoles.Administrator),
            Toegang("directie@acme.nl", "Jan", PortalAccessRoles.Administrator),
        ]);

        Assert.NotNull(adressering);
        Assert.Equal(["directie@acme.nl", "financien@acme.nl"], adressering.Recipients);
        Assert.Null(adressering.ContactName);
    }

    [Fact]
    public void EenOnbruikbaarAdresHoudtDeHeleVerzendingTegen()
    {
        // De duurdere van de twee keuzes, en de juiste. Versturen naar wat wél klopt levert een
        // bevestiging op die "verstuurd" zegt terwijl de persoon voor wie het overzicht bedoeld was
        // niets heeft gekregen.
        var (adressering, weigering) = StatementRecipients.Resolve(
        [
            Toegang("directie@acme.nl", "Jan", PortalAccessRoles.Administrator),
            Toegang("kapot", "Marieke", PortalAccessRoles.Administrator),
        ]);

        Assert.Null(adressering);
        Assert.Equal(StatementRefusal.RecipientInvalid, weigering);
    }

    [Theory]
    [InlineData("jan@acme.nl", true)]
    [InlineData("jan.bakker@acme-logistiek.nl", true)]
    [InlineData("jan+facturen@acme.nl", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("jan", false)]
    [InlineData("jan@acme", false)]
    [InlineData("@acme.nl", false)]
    [InlineData("jan@@acme.nl", false)]
    [InlineData("jan@acme.nl.", false)]
    [InlineData("jan..bakker@acme.nl", false)]
    [InlineData("jan bakker@acme.nl", false)]
    [InlineData("Jan <jan@acme.nl>", false)]
    [InlineData("jan@acme.nl, iemand@elders.nl", false)]
    [InlineData("jan@acme.nl;iemand@elders.nl", false)]
    [InlineData("jan@acme.nl\niemand@elders.nl", false)]
    [InlineData("jan@acme.nl\r\nBcc: iemand@elders.nl", false)]
    public void WatAlsOntvangerBruikbaarIs(string adres, bool bruikbaar) =>
        Assert.Equal(bruikbaar, StatementRecipients.IsUsable(adres));

    [Fact]
    public void EenAbsurdLangAdresIsGeenAdres()
    {
        var lang = new string('a', 250) + "@acme.nl";

        Assert.False(StatementRecipients.IsUsable(lang));
    }

    private static AccessDocument Toegang(string email, string? naam, string rol) => new()
    {
        Id = PortalDocumentIds.Access(email),
        PartitionKey = "acme-logistiek",
        CustomerId = "acme-logistiek",
        Email = email,
        Name = naam,
        Role = rol,
    };
}
