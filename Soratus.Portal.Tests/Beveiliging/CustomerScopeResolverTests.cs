using Soratus.Portal.Security;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Beveiliging;

/// <summary>
/// De autorisatie in de datalaag: wie krijgt een scope, en wie niet.
/// </summary>
/// <remarks>
/// <para>Dit is de <em>echte</em> grens, in tegenstelling tot de zichtbaarheidstests op de
/// pagina's. Een klantgebruiker die de URL van een andere klant intikt krijgt hier <c>null</c>, en
/// zonder scope is er geen aanroep naar de store te schrijven — niet omdat het verboden is, maar
/// omdat het argument niet te produceren valt.</para>
///
/// <para>Weigeren en niet-bestaan geven allebei <c>null</c>. Dat is opzet: het onderscheid
/// verklappen zou bevestigen dat de klant achter die URL bestaat.</para>
/// </remarks>
public class CustomerScopeResolverTests
{
    // ── Een klantgebruiker ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EenKlantgebruikerKrijgtEenScopeVoorZijnEigenKlant()
    {
        var resolver = Autorisatiebron.Resolver();

        var scope = await resolver.ResolveAsync(Testprincipals.Klant(), "acme-logistiek");

        Assert.NotNull(scope);
        Assert.Equal("acme-logistiek", scope.CustomerId);
        Assert.Equal("Acme Logistiek", scope.DisplayName);
    }

    [Fact]
    public async Task EenKlantgebruikerKrijgtNullVoorEenVreemdeKlantSlug()
    {
        var resolver = Autorisatiebron.Resolver();

        var scope = await resolver.ResolveAsync(Testprincipals.Klant(), "bakker-bv");

        Assert.Null(scope);
    }

    [Fact]
    public async Task EenVreemdeKlantEnEenNietBestaandeKlantGevenHetzelfdeAntwoord()
    {
        // Zou het verschil zichtbaar zijn, dan is het bestaan van een klant af te lezen aan het
        // antwoord. Dat is zelf een lek, en de reden dat een weigering 404 wordt en geen 403.
        var resolver = Autorisatiebron.Resolver();
        var klant = Testprincipals.Klant();

        Assert.Null(await resolver.ResolveAsync(klant, "bakker-bv"));
        Assert.Null(await resolver.ResolveAsync(klant, "bestaat-niet"));
        Assert.Null(await resolver.ResolveAsync(klant, null));
        Assert.Null(await resolver.ResolveAsync(klant, "   "));
    }

    [Fact]
    public async Task DeKlantSlugIsHoofdletterongevoelig()
    {
        var resolver = Autorisatiebron.Resolver();

        var scope = await resolver.ResolveAsync(Testprincipals.Klant(), "ACME-Logistiek");

        Assert.NotNull(scope);
        Assert.Equal("acme-logistiek", scope.CustomerId);
    }

    [Fact]
    public async Task EenKlantgebruikerKrijgtGeenOperatorrecht()
    {
        var resolver = Autorisatiebron.Resolver();
        var klant = Testprincipals.Klant();

        Assert.Null(await resolver.ResolveOperatorAsync(klant));
        Assert.Null(await resolver.ResolveOperatorAsync(klant, "acme-logistiek"));
    }

    [Fact]
    public async Task EenKlantgebruikerKrijgtAlleenZijnEigenKlantenTerug()
    {
        var resolver = Autorisatiebron.Resolver();

        var eigen = await resolver.ResolveOwnAsync(Testprincipals.Klant());

        Assert.Single(eigen);
        Assert.Equal("acme-logistiek", eigen[0].CustomerId);
    }

    // ── Een operator ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("acme-logistiek")]
    [InlineData("bakker-bv")]
    public async Task EenOperatorKrijgtEenScopeVoorElkeBestaandeSlug(string slug)
    {
        var resolver = Autorisatiebron.Resolver();

        var scope = await resolver.ResolveAsync(Testprincipals.Operator(), slug);

        Assert.NotNull(scope);
        Assert.Equal(slug, scope.CustomerId);
    }

    [Fact]
    public async Task EenOperatorKrijgtGeenScopeVoorEenKlantDieNietBestaat()
    {
        // De rol geeft toegang tot elke klant, niet tot elke tekst in de URL.
        var resolver = Autorisatiebron.Resolver();

        Assert.Null(await resolver.ResolveAsync(Testprincipals.Operator(), "bestaat-niet"));
    }

    [Fact]
    public async Task HetOperatorrechtDraagtEenLeesrechtPerKlantMee()
    {
        var resolver = Autorisatiebron.Resolver();

        var scope = await resolver.ResolveOperatorAsync(Testprincipals.Operator());

        Assert.NotNull(scope);
        Assert.Equal(2, scope.Customers.Count);
        Assert.Equal(
            new[] { "acme-logistiek", "bakker-bv" },
            scope.Customers.Select(c => c.CustomerId).OrderBy(c => c, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task DeVolledigeOmgevingsaanduidingZitAlleenOpDeOperatorscope()
    {
        // EnvironmentDetail bestaat niet op CustomerScope. Een klantpagina kan het veld dus niet
        // renderen, ook niet per ongeluk — dat is het verschil tussen een type en een @if.
        var resolver = Autorisatiebron.Resolver();

        var operatorScope = await resolver.ResolveOperatorAsync(
            Testprincipals.Operator(), "acme-logistiek");

        Assert.NotNull(operatorScope);
        Assert.Equal("sub-soratus-acme · rg-acme-prod", operatorScope.EnvironmentDetail);
        Assert.Null(typeof(CustomerScope).GetProperty("EnvironmentDetail"));
    }

    [Fact]
    public async Task EenOperatorKrijgtGeenEigenKlantenlijstWantDaarIsHetOverzichtVoor()
    {
        var resolver = Autorisatiebron.Resolver();

        var eigen = await resolver.ResolveOwnAsync(Testprincipals.Operator());

        Assert.Empty(eigen);
    }

    // ── Zonder rol en zonder aanmelding ─────────────────────────────────────────────────────

    [Fact]
    public async Task EenGebruikerZonderAppRolKrijgtNiets()
    {
        var resolver = Autorisatiebron.Resolver(
            new Soratus.Portal.Security.CustomerRecord
            {
                Id = "acme-logistiek",
                Name = "Acme Logistiek",
                Access = [new CustomerAccessRecord { Email = "niemand@example.com" }],
            });

        var zonderRol = Testprincipals.ZonderRol();

        Assert.Null(await resolver.ResolveAsync(zonderRol, "acme-logistiek"));
        Assert.Null(await resolver.ResolveOperatorAsync(zonderRol));
        Assert.Empty(await resolver.ResolveOwnAsync(zonderRol));
    }

    [Fact]
    public async Task EenNietAangemeldeBezoekerKrijgtNiets()
    {
        var resolver = Autorisatiebron.Resolver();
        var anoniem = Testprincipals.Anoniem();

        Assert.Null(await resolver.ResolveAsync(anoniem, "acme-logistiek"));
        Assert.Null(await resolver.ResolveAsync(null, "acme-logistiek"));
        Assert.Null(await resolver.ResolveOperatorAsync(anoniem));
        Assert.Null(await resolver.ResolveOperatorAsync(null));
        Assert.Empty(await resolver.ResolveOwnAsync(anoniem));
    }

    // ── Een klant zonder ingerichte opslag ──────────────────────────────────────────────────

    [Fact]
    public async Task EenKlantZonderIngerichteOpslagLevertGeenScopeOp()
    {
        // Een scope zonder verbinding zou een bewijs zijn van iets dat niet kan. Dat de klant
        // desondanks op het overzicht blijft staan regelt de weergave, uit de klantenlijst.
        var resolver = Autorisatiebron.ResolverZonderOpslag(Autorisatiebron.ZonderOpslag());

        Assert.Null(await resolver.ResolveAsync(Testprincipals.Klant(), "cordaan-zorg"));
        Assert.Null(await resolver.ResolveAsync(Testprincipals.Operator(), "cordaan-zorg"));
        Assert.Null(await resolver.ResolveOperatorAsync(Testprincipals.Operator(), "cordaan-zorg"));
    }

    [Fact]
    public async Task EenKlantZonderOpslagZitNietInHetOperatorrechtMaarWelInDeKlantenlijst()
    {
        var resolver = Autorisatiebron.ResolverZonderOpslag(Autorisatiebron.ZonderOpslag());
        var directory = Autorisatiebron.Klantenlijst(
            [Autorisatiebron.ZonderOpslag()], standaardEndpoint: null);

        var scope = await resolver.ResolveOperatorAsync(Testprincipals.Operator());

        Assert.NotNull(scope);
        Assert.Empty(scope.Customers);
        Assert.Single(directory.All);
    }

    // ── Annulering ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EenGeannuleerdVerzoekLevertGeenScopeMaarEenAnnulering()
    {
        var resolver = Autorisatiebron.Resolver();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            resolver.ResolveAsync(Testprincipals.Operator(), "acme-logistiek", cts.Token));
    }
}
