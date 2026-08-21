using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De gevalideerde Azure-scope van een klant.
/// </summary>
/// <remarks>
/// <para><strong>Wat hier wordt bewezen is de enige laag die er is.</strong> Of een resource group
/// <em>bestaat</em> is niet te controleren — gemeten (punt 30): een resource group die niet bestaat
/// geeft <c>HTTP 200</c> met nul rijen. Juist daarom hoort wat wél te controleren is ook echt
/// gecontroleerd te worden, en hoort elke test hieronder zijn spiegel te hebben: een validatie die
/// alles weigert is even nutteloos als een die alles doorlaat, en de eerste levert een klant op die
/// niet in te richten is.</para>
/// </remarks>
public class AzurescopeTests
{
    private const string Abonnement = "501a66d2-de54-4d4f-9f7c-1fbb55bec17f";

    private const string Goed = $"/subscriptions/{Abonnement}/resourceGroups/MBV";

    [Fact]
    public void EenPadInDeVormVanAzureIsGeldig()
    {
        // De spiegel van elke weigering hieronder. Zonder deze test mag Validate alles afkeuren en is
        // er geen klant meer in te richten — en dan meet de collector nooit iets.
        Assert.Null(AzureScope.Validate(Goed));
        Assert.True(AzureScope.TryParse(Goed, out var scope));
        Assert.Equal(Guid.Parse(Abonnement), scope!.SubscriptionId);
        Assert.Equal("MBV", scope.ResourceGroup);
    }

    [Fact]
    public void LeegIsGeldigEnLevertGeenScope()
    {
        // Punt 15 op de plek waar hij de meting raakt: "niet ingericht" is een geldige toestand. Een
        // verplicht veld zou hier een verzonnen pad opleveren, en een verzonnen pad geeft HTTP 200 met
        // nul rijen — dus het ziet uit als een antwoord.
        foreach (var leeg in new[] { null, "", "   " })
        {
            Assert.Null(AzureScope.Validate(leeg));
            Assert.False(AzureScope.TryParse(leeg, out var scope));
            Assert.Null(scope);
        }
    }

    [Fact]
    public void DeSchrijfwijzeVanDeResourcegroepnaamBlijftZoalsHijIsIngevuld()
    {
        // Gemeten op 21 augustus 2026: /subscriptions/501a66d2-…/resourcegroups/mbv gaf exact dezelfde
        // 112 rijen als /subscriptions/501a66d2-…/resourceGroups/MBV. Het pad is dus
        // hoofdletterongevoelig en de hoofdletter valt weg als storingsoorzaak. Wat er overblijft is de
        // reden om de naam niet aan te raken: deze tekenreeks komt op het scherm te staan als
        // "bevraagd: …", en daar hoort te staan wat er is ingevuld.
        Assert.True(AzureScope.TryParse($"/subscriptions/{Abonnement}/resourcegroups/mbv", out var klein));
        Assert.Equal("mbv", klein!.ResourceGroup);

        Assert.True(AzureScope.TryParse(Goed, out var groot));
        Assert.Equal("MBV", groot!.ResourceGroup);
    }

    [Fact]
    public void DeVasteSegmentenWordenGenormaliseerdEnHetPadIsDeTekstDieDeDeurUitGaat()
    {
        // /resourcegroups/ in, /resourceGroups/ uit. Die twee delen zijn van Azure en niet van de
        // operator, en genormaliseerd zijn twee klantscopes met elkaar te vergelijken. De naam eronder
        // blijft ongemoeid — zie de test hierboven.
        Assert.True(AzureScope.TryParse($"  /Subscriptions/{Abonnement}/RESOURCEGROUPS/mbv/ ", out var scope));
        Assert.Equal($"/subscriptions/{Abonnement}/resourceGroups/mbv", scope!.Path);

        // ToString is gelijk aan Path, en dat is geen gemak maar een grens: een scope die in een
        // logregel belandt hoort dezelfde tekenreeks te zijn als die naar de API gaat. De standaard-
        // ToString van een record zou hier "AzureScope { SubscriptionId = … }" neerzetten.
        Assert.Equal(scope.Path, scope.ToString());
    }

    [Theory]
    [InlineData("501a66d2-de54-4d4f-9f7c-1fbb55bec17f mbv")]
    [InlineData("sub-soratus-acme · rg-acme-prod")]
    public void DeWeergaveTekstenVanDeBestaandeKlantenZijnGeenScope(string envFull)
    {
        // Dit is de reden dat dit veld bestaat. Beide waarden staan vandaag in het veld envFull — de
        // eerste bij de enige echte klant, de tweede bij de demoklanten — en geen van beide is
        // betrouwbaar te ontleden. Een collector die zijn scope hieruit zou afleiden, krijgt bij een
        // tikfout of een verkeerde hoofdletter een leeg antwoord dat als "geen kosten" doorrolt.
        Assert.NotNull(AzureScope.Validate(envFull));
        Assert.False(AzureScope.TryParse(envFull, out var scope));
        Assert.Null(scope);
    }

    [Theory]
    [InlineData("/subscriptions/mbv")]
    [InlineData("/subscriptions/501a66d2/resourceGroups/MBV")]
    [InlineData("/resourceGroups/MBV")]
    [InlineData("/subscriptions/501a66d2-de54-4d4f-9f7c-1fbb55bec17f/resourceGroups")]
    [InlineData("/subscriptions/501a66d2-de54-4d4f-9f7c-1fbb55bec17f/providers/Microsoft.Web/sites/x")]
    public void EenPadDatGeenAbonnementMetEenResourceGroupIsWordtGeweigerd(string tekst)
    {
        Assert.NotNull(AzureScope.Validate(tekst));
        Assert.False(AzureScope.TryParse(tekst, out _));
    }

    [Fact]
    public void EenGuidZonderStreepjesOfMetHaakjesWordtGeweigerd()
    {
        // ARM neemt alleen de vorm met streepjes aan. Guid.TryParse zou "501a66d2de544d4f9f7c…" en
        // "{501a66d2-…}" ook goedkeuren, en dan staat er een pad in de opslag dat de API afwijst —
        // met een 400, en dus zonder bedrag, maandenlang.
        Assert.NotNull(AzureScope.Validate(
            "/subscriptions/501a66d2de544d4f9f7c1fbb55bec17f/resourceGroups/MBV"));
        Assert.NotNull(AzureScope.Validate(
            "/subscriptions/{501a66d2-de54-4d4f-9f7c-1fbb55bec17f}/resourceGroups/MBV"));
    }

    [Theory]
    [InlineData("rg-soratus-prod")]
    [InlineData("MBV")]
    [InlineData("rg_met_underscores")]
    [InlineData("rg.met.punten")]
    [InlineData("rg(met-haakjes)")]
    [InlineData("café")]
    public void DeResourcegroepnamenDieAzureToestaatWordenGoedgekeurd(string naam)
    {
        // De spiegel van de weigeringen hieronder. Azure staat letters, cijfers, _ - . en ronde haakjes
        // toe, en ook unicodeletters — een naam met een accent bestaat daar. Zou die hier afvallen, dan
        // is een klant met zo'n resource group niet in te richten en meet de collector nooit iets.
        Assert.Null(AzureScope.Validate($"/subscriptions/{Abonnement}/resourceGroups/{naam}"));
    }

    [Theory]
    [InlineData("rg met spaties")]
    [InlineData("rg/met-schuine-streep")]
    [InlineData("rg#met-hekje")]
    [InlineData("eindigt.op.een.punt.")]
    public void EenResourcegroepnaamDieNietKanWordtGeweigerd(string naam)
    {
        // Een naam die deze regels schendt bestáát niet in Azure, en een scope die hem noemt geeft dus
        // geen fout maar HTTP 200 met nul rijen. Dat is precies waarom dit hier wordt tegengehouden en
        // niet aan de API wordt overgelaten.
        var pad = $"/subscriptions/{Abonnement}/resourceGroups/{naam}";

        Assert.NotNull(AzureScope.Validate(pad));

        // En TryParse weigert hem óók. Dat die twee hetzelfde zeggen is geen dubbele test maar de eis:
        // de schrijfkant gebruikt Validate en de collector en het facturatiescherm gebruiken TryParse.
        // Zouden ze uiteenlopen, dan staat er "wordt gemeten" bij een klant die niet wordt gemeten — of
        // weigert het formulier een scope die de collector prima aan kan.
        //
        // Gevonden met een mutatie: het weghalen van de naamcontrole uit TryParse maakte niets rood.
        Assert.False(AzureScope.TryParse(pad, out var scope));
        Assert.Null(scope);
    }

    [Theory]
    [InlineData("rg-soratus-prod")]
    [InlineData("MBV")]
    [InlineData("café")]
    public void ValidateEnTryParseZijnHetOverEenGeldigeNaamOokEens(string naam)
    {
        // De spiegel van de test hierboven, en zonder hem is die te halen door TryParse alles te laten
        // weigeren — en dan meet de collector nooit iets.
        var pad = $"/subscriptions/{Abonnement}/resourceGroups/{naam}";

        Assert.Null(AzureScope.Validate(pad));
        Assert.True(AzureScope.TryParse(pad, out _));
    }

    [Fact]
    public void EenResourcegroepnaamVanMeerDanNegentigTekensWordtGeweigerd()
    {
        var kort = new string('a', AzureScope.MaximumResourceGroupLength);
        var lang = new string('a', AzureScope.MaximumResourceGroupLength + 1);

        Assert.Null(AzureScope.Validate($"/subscriptions/{Abonnement}/resourceGroups/{kort}"));
        Assert.NotNull(AzureScope.Validate($"/subscriptions/{Abonnement}/resourceGroups/{lang}"));
    }

    [Fact]
    public void EenGeplakteLapTekstKomtNietInEenFoutmeldingTerecht()
    {
        // De grens op de totale lengte staat er om de meldingen bruikbaar te houden: zonder hem zou een
        // per ongeluk geplakt document als geheel op het scherm van de operator belanden.
        var melding = AzureScope.Validate(new string('x', AzureScope.MaximumScopeLength + 1));

        Assert.NotNull(melding);
        Assert.DoesNotContain("xxxxxxxxxx", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void DeMeldingVerteltWaarDeWaardeTeVindenIs()
    {
        // De enige betrouwbare invoerweg is kopiëren uit Azure. Een melding die dat niet zegt, laat
        // iemand het opnieuw intypen — en dan is de volgende poging even mis.
        var melding = AzureScope.Validate("mbv");

        Assert.NotNull(melding);
        Assert.Contains("Resource-ID", melding, StringComparison.Ordinal);
    }

    [Fact]
    public void DeGrenzenVanHetTypeStaanOpDeGemetenWaarden()
    {
        // Twee constanten met een bron. Negentig komt uit de documentatie van Azure zelf; de totale
        // lengte is die van de twee vaste segmenten, een guid en die negentig.
        Assert.Equal(90, AzureScope.MaximumResourceGroupLength);
        Assert.Equal(157, AzureScope.MaximumScopeLength);
        Assert.Equal(AzureScope.MaximumScopeLength, Goed.Length - 3 + AzureScope.MaximumResourceGroupLength);
    }
}
