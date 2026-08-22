using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Portaalgegevens;

/// <summary>
/// De controle op het DevOps-bord aan de schrijfkant, buiten de formulieren om.
/// </summary>
/// <remarks>
/// <para><strong>Dit bestand bestaat om een gat dat een mutatie heeft gevonden.</strong> Het weghalen van
/// de bordcontrole uit <see cref="CustomerEdit.Validate"/> maakte niets rood. De oorzaak is geen
/// ontbrekende regel maar een tweede controle die ervóór staat: het omgevingsblok valideert zelf, zodat de
/// melding ónder het veld komt in plaats van als blok boven de knop, en het komt bij een onbruikbaar bord
/// dus niet eens tot een aanroep aan de opslag. Elke test die via het scherm loopt is daarmee groen zonder
/// de controle in de opslag aan te raken.</para>
///
/// <para><strong>Dat is precies de vorm van gat 2 uit punt 41: twee stukken code die hetzelfde doen dekken
/// elkaars afwezigheid.</strong> En de weggevallen helft is hier de belangrijkste van de twee — hij is de
/// enige die ook geldt voor een aanroeper die het formulier omzeilt. Vandaar deze tests: ze roepen
/// <c>Validate</c> rechtstreeks aan, zonder scherm ertussen.</para>
///
/// <para>Dezelfde reden geldt voor <see cref="NewCustomerRequest.Validate"/>: het aanmaakformulier zet zijn
/// melding ook onder het veld (<c>NewCustomerForm.FieldErrors</c>), dus ook daar is de controle in het
/// verzoek zelf de kant die geen test had.</para>
/// </remarks>
public class BordinvoerTests
{
    /// <summary>Een bewerking met een bord erin.</summary>
    /// <param name="bord">Het bord, of <c>null</c>.</param>
    /// <returns>De bewerking.</returns>
    private static CustomerEdit Bewerking(string? bord) => new()
    {
        Name = "Acme Logistiek",
        DevOpsScope = bord,
    };

    /// <summary>Een aanmaakverzoek met een bord erin.</summary>
    /// <param name="bord">Het bord, of <c>null</c>.</param>
    /// <returns>Het verzoek.</returns>
    private static NewCustomerRequest Verzoek(string? bord) => new()
    {
        CustomerId = "acme-logistiek",
        Name = "Acme Logistiek",
        DevOpsScope = bord,
    };

    [Fact]
    public void DeBewerkingWeigertEenOnbruikbaarBord()
    {
        // De controle die telt: hij geldt ook voor een aanroeper die het formulier omzeilt. Wat er in een
        // klantdocument staat wordt door de sprintcollector bevraagd, en een bord dat geen bord is hoort er
        // niet in te komen.
        Assert.NotNull(Bewerking(Vasteportaalopslag.Omgevingsdetail).Validate());
    }

    [Fact]
    public void HetAanmaakverzoekWeigertEenOnbruikbaarBord()
    {
        Assert.NotNull(Verzoek(Vasteportaalopslag.Omgevingsdetail).Validate());
    }

    [Fact]
    public void EenAzureScopeInHetBordveldWordtDoorDeBewerkingGeweigerd()
    {
        // De twee velden staan naast elkaar op één kaart en hebben beide een pad-achtige vorm. Ze
        // verwisselen is een echte fout en hij hoort ook zonder scherm te vallen.
        Assert.NotNull(Bewerking(Vasteportaalopslag.Standaardscope).Validate());
    }

    [Fact]
    public void EenBruikbaarBordKomtErdoor()
    {
        // De spiegel, en zonder hem is "weigert een onbruikbaar bord" ook waar bij een controle die álles
        // weigert — en dan is er geen klant meer te bewaren.
        Assert.Null(Bewerking(Vasteportaalopslag.Standaardbord).Validate());
        Assert.Null(Verzoek(Vasteportaalopslag.Standaardbord).Validate());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EenLeegBordIsToegestaan(string? bord)
    {
        // Punt 15: leeg betekent "niet ingericht" en dat is een geldige toestand. Een verplicht veld zou
        // hier een verzonnen bord opleveren, en dan is "niet ingericht" niet meer van "verkeerd ingericht"
        // te onderscheiden — twee verschillende handelingen onder één lege pagina.
        Assert.Null(Bewerking(bord).Validate());
        Assert.Null(Verzoek(bord).Validate());
    }

    [Fact]
    public void DeMeldingVanDeBewerkingIsDieVanHetBordEnNietDieVanDeAzureScope()
    {
        // Twee controles achter elkaar in één Validate, en de volgorde is die van de velden op het scherm.
        // Zonder deze test kan de bordmelding stil door de scopemelding worden overschreven — en dan wijst
        // het formulier naar het verkeerde veld.
        var melding = new CustomerEdit
        {
            Name = "Acme Logistiek",
            AzureScope = Vasteportaalopslag.Standaardscope,
            DevOpsScope = "soratus",
        }.Validate();

        Assert.NotNull(melding);
        Assert.Contains("organisatie/project/team", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void DeNaamGaatVoorHetBord()
    {
        // Een lege naam is de enige harde eis van CustomerEdit, en die hoort vóór de scopevelden te vallen:
        // een formulier dat over een bord klaagt terwijl de naam leeg is, laat iemand het verkeerde veld
        // repareren.
        var melding = new CustomerEdit { Name = "  ", DevOpsScope = "soratus" }.Validate();

        Assert.NotNull(melding);
        Assert.Contains("klantnaam", melding, StringComparison.OrdinalIgnoreCase);
    }
}
