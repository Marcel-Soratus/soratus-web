using System.ComponentModel.DataAnnotations;
using Soratus.Portal.Mail;

namespace Soratus.Portal.Tests.Maandoverzicht;

/// <summary>
/// Een leeg optioneel adres is afwezig en niet ongeldig.
/// </summary>
/// <remarks>
/// <para><strong>Dit heeft het portaal plat gelegd.</strong> Er stond een app-setting
/// <c>PortalMail__ReplyToAddress</c> met een lege waarde. De configuratiebinder maakte daar
/// <c>""</c> van, en <c>[EmailAddress]</c> keurt een lege string áf waar hij <c>null</c> doorlaat.
/// Gevolg: een <c>OptionsValidationException</c> bij de eerste keer dat deze instellingen werden
/// opgevraagd — en omdat dat in een achtergronddienst gebeurt, legde die de hele host neer.</para>
///
/// <para>Wat het pijnlijk maakte is de meting eromheen: de uitrol was groen en <c>/healthz</c> gaf
/// 200, want die raakt met opzet geen enkele afhankelijkheid. De app was gestart, had de
/// gezondheidscontrole gehaald, en viel daarna om. Er staat nu een tweede smoke test in de pijplijn
/// die de container wél bouwt.</para>
///
/// <para>De reparatie zit op drie plekken en dat is met opzet: de template zet de sleutel niet meer
/// als hij leeg is, de optieklasse maakt leeg tot afwezig, en deze tests houden die tweede vast. Een
/// lege waarde kan namelijk ook uit een omgevingsvariabele of uit de portaalinstellingen komen, en
/// dan is er geen template die hem tegenhoudt.</para>
/// </remarks>
public class LeegAdresTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EenLeegAntwoordadresIsAfwezigEnGeenFout(string? waarde)
    {
        var opties = new PortalMailOptions { ReplyToAddress = waarde };

        Assert.Null(opties.ReplyToAddress);
        Assert.Empty(Fouten(opties));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EenLeegAfzenderadresIsOokAfwezig(string waarde)
    {
        // Dezelfde val op het andere adres. Dit veld is in de praktijk altijd gevuld, maar een
        // reparatie die alleen het veld repareert waar het misging, laat het volgende geval staan.
        var opties = new PortalMailOptions { FromAddress = waarde };

        Assert.Null(opties.FromAddress);
        Assert.Empty(Fouten(opties));
    }

    [Fact]
    public void EenEchtAdresBlijftStaanEnWordtOntdaanVanRuimte()
    {
        // De spiegel. Zonder deze zou "alles op null zetten" ook groen zijn — en dan verstuurt het
        // portaal nooit meer iets, zonder dat er een fout wordt gemeld.
        var opties = new PortalMailOptions
        {
            FromAddress = "  DoNotReply@soratus.com  ",
            ReplyToAddress = "beheer@soratus.com",
        };

        Assert.Equal("DoNotReply@soratus.com", opties.FromAddress);
        Assert.Equal("beheer@soratus.com", opties.ReplyToAddress);
        Assert.Empty(Fouten(opties));
    }

    [Fact]
    public void EenOnzinnigAdresBlijftEenFout()
    {
        // Verruimen mag de validatie niet uitschakelen. "geen adres" is afwezig; "niet-een-adres" is
        // een inrichtingsfout en hoort dat te blijven.
        var opties = new PortalMailOptions { ReplyToAddress = "beheer.soratus.com" };

        ValidationResult fout = Assert.Single(Fouten(opties));

        Assert.Contains("ReplyToAddress", fout.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>Valideert de opties zoals <c>ValidateDataAnnotations</c> dat doet.</summary>
    /// <param name="opties">De opties.</param>
    /// <returns>De fouten, leeg als er geen zijn.</returns>
    private static List<ValidationResult> Fouten(PortalMailOptions opties)
    {
        var fouten = new List<ValidationResult>();

        Validator.TryValidateObject(opties, new ValidationContext(opties), fouten, validateAllProperties: true);

        return fouten;
    }
}
