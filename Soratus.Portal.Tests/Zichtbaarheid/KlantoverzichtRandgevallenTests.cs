using Bunit;
using Soratus.Portal.Components.Shared;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// De randen van het klantoverzicht (§3.1): de klant die wél agents heeft, maar niets in
/// productie.
/// </summary>
/// <remarks>
/// Zo'n klant komt op dezelfde rang uit als een klant zonder agents — rang 0, want de ernst gaat
/// over productie — en krijgt daarmee hetzelfde neutrale vlak. Het woordlabel mag daar niet
/// meeliften: "Geen agents" is over deze klant onwaar, en een onwaar label op een overzicht is
/// erger dan een leeg vakje. Er staan agents; ze staan alleen ergens anders.
/// </remarks>
public class KlantoverzichtRandgevallenTests : Portaalrendertest
{
    private static Type Overzicht =>
        Paginaverzameling.MetNaam("Soratus.Portal.Components.Pages.Overzicht")
        ?? throw new InvalidOperationException(
            "De pagina Soratus.Portal.Components.Pages.Overzicht bestaat niet. Is hij hernoemd, " +
            "dan hoort deze test mee te verhuizen — niet te verdwijnen.");

    [Fact]
    public void EenKlantMetAlleenAgentsBuitenProductieKrijgtNietHetLabelGeenAgents()
    {
        Weergaven = new VastePortaalweergaven(alleenBuitenProductie: true);
        MeldOperatorAan();

        var cut = RenderPagina(Overzicht);

        // De assertie kijkt naar de badges in de klantrijen en niet naar de hele markup. Dat is
        // met opzet: onderaan de pagina staat de statuslegenda, en die legt rang 0 uit met de
        // algemene klantvariant "Geen agents". De legenda gaat over de vijf statussen en niet over
        // elke contextvariant van een label, dus die tekst hoort daar te blijven staan.
        var badges = cut.FindAll("a.data-row span.badge")
            .Select(badge => badge.TextContent.Trim())
            .ToArray();

        Assert.NotEmpty(badges);

        Assert.All(badges, tekst =>
        {
            Assert.Contains(StatusVisuals.UnknownNonProductionLabel, tekst, StringComparison.Ordinal);
            Assert.DoesNotContain(StatusVisuals.UnknownCustomerLabel, tekst, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EenKlantMetAgentsInProductieKrijgtNooitHetLabelGeenInProductie()
    {
        // De tegenhanger, en de reden dat de test hierboven iets zegt. Zonder dit geval zou een
        // pagina die overal "Geen in productie" neerzet ook groen zijn — en dan is er niets
        // onderscheiden, alleen een label vervangen door een ander.
        MeldOperatorAan();

        var cut = RenderPagina(Overzicht);

        var badges = cut.FindAll("a.data-row span.badge")
            .Select(badge => badge.TextContent.Trim())
            .ToArray();

        Assert.NotEmpty(badges);
        Assert.All(badges, tekst =>
            Assert.DoesNotContain(
                StatusVisuals.UnknownNonProductionLabel, tekst, StringComparison.Ordinal));
    }
}
