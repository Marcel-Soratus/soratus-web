using Microsoft.Extensions.Configuration;
using Soratus.Portal.Data;
using Soratus.Portal.Security;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Portaalgegevens;

/// <summary>
/// De klantslug: de sleutel waar alles op aansluit.
/// </summary>
/// <remarks>
/// Dezelfde tekenreeks is de partitiesleutel in de portaalopslag, het pad in de URL én
/// <c>customerId</c> in elk telemetriedocument dat een agent wegschrijft. Een slug die door de
/// controle glipt en daarna niet als sleutel kan dienen levert een klant op die bestaat en waarvan de
/// agents onvindbaar zijn.
/// </remarks>
public class KlantIdTests
{
    [Theory]
    [InlineData("bakker")]
    [InlineData("acme-logistiek")]
    [InlineData("vandijk")]
    [InlineData("kl")]
    [InlineData("a1")]
    [InlineData("1e-lijn")]
    [InlineData("soratus")]
    public void EenGewoneSlugKlopt(string slug) =>
        Assert.Null(PortalSlug.Validate(slug));

    [Theory]
    [InlineData(null, "leeg")]
    [InlineData("", "leeg")]
    [InlineData("   ", "witruimte")]
    [InlineData("b", "te kort")]
    [InlineData("Bakker", "hoofdletter")]
    [InlineData("bakker bv", "spatie")]
    [InlineData("bakker.bv", "punt")]
    [InlineData("bakker/bv", "schuine streep — verboden in een Cosmos-id")]
    [InlineData("bakker\\bv", "backslash — verboden in een Cosmos-id")]
    [InlineData("bakker#1", "hekje — verboden in een Cosmos-id")]
    [InlineData("bakker?1", "vraagteken — verboden in een Cosmos-id")]
    [InlineData("bakker_bv", "liggend streepje")]
    [InlineData("-bakker", "begint met een koppelstreepje")]
    [InlineData("bakker-", "eindigt op een koppelstreepje")]
    [InlineData("bäkker", "niet-ascii")]
    [InlineData("$portal", "de gereserveerde partitie")]
    public void EenSlugDieGeenSleutelKanZijnWordtGeweigerd(string? slug, string waarom)
    {
        Assert.NotNull(PortalSlug.Validate(slug));
        Assert.False(string.IsNullOrWhiteSpace(waarom));
    }

    [Fact]
    public void EenSlugVanEenEnkeleMaximaleLengteKlopt()
    {
        Assert.Null(PortalSlug.Validate(new string('a', PortalSlug.MaximumLength)));
        Assert.NotNull(PortalSlug.Validate(new string('a', PortalSlug.MaximumLength + 1)));
        Assert.Null(PortalSlug.Validate(new string('a', PortalSlug.MinimumLength)));
        Assert.NotNull(PortalSlug.Validate(new string('a', PortalSlug.MinimumLength - 1)));
    }

    [Fact]
    public void DeGereserveerdePartitieKanNooitEenKlantZijn()
    {
        // De markering van de eenmalige migratie staat in een eigen partitie. Zou een klantslug
        // daarmee kunnen samenvallen, dan zou die klant de markering kunnen overschrijven en zou de
        // migratie opnieuw gaan lopen — waarna een bewust verwijderde klant terugkomt.
        Assert.NotNull(PortalSlug.Validate(PortalDocumentIds.ReservedPartitionKey));
    }

    [Fact]
    public void ElkeKlantInDeConfiguratieHeeftEenGeldigeSlug()
    {
        // Dit is geen smaakcontrole. De eenmalige migratie slaat een klant met een ongeldige slug
        // over, en daarna is de opslag de bron en de configuratie niet meer — de klantenlijst wordt
        // vervángen en niet samengevoegd. Een klant met een ongeldige slug in appsettings.json
        // verdwijnt dus bij de migratie uit het portaal, met één waarschuwing in de log als enige
        // spoor. Dat hoort hier op te vallen en niet daar.
        var configuratie = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(Broncode.Portaalproject.FullName, "appsettings.json"))
            .Build();

        var opties = new PortalCustomerOptions();
        configuratie.GetSection(PortalCustomerOptions.SectionName).Bind(opties);

        Assert.NotEmpty(opties.Customers);

        var ongeldig = opties.Customers
            .Select(klant => (klant.Id, Reden: PortalSlug.Validate(klant.Id)))
            .Where(paar => paar.Reden is not null)
            .Select(paar => $"'{paar.Id}': {paar.Reden}")
            .ToArray();

        Assert.True(
            ongeldig.Length == 0,
            "In appsettings.json staan klanten met een klant-id dat de controle niet haalt:\n  " +
            string.Join("\n  ", ongeldig) + "\n\n" +
            "Zo'n klant wordt door de eenmalige migratie overgeslagen. Omdat de klantenlijst " +
            "daarna uit de opslag komt en de configuratie niet meer wordt samengevoegd, verdwijnt " +
            "hij dan uit het portaal — inclusief de toegangen die eraan hingen.");
    }
}
