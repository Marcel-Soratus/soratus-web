using System.Reflection;
using Soratus.Portal.Data;
using Soratus.Portal.Security;
using Soratus.Portal.Tests.Hulpmiddelen;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Beveiliging;

/// <summary>
/// De rolmatrix op contract en toegang: klant <em>lezen</em>, operator <em>lezen + bewerken</em>
/// (§2).
/// </summary>
/// <remarks>
/// <para><strong>Een leesrecht is geen schrijfrecht.</strong> Tot fase 2 las dit portaal alleen en
/// was elke scope een leesrecht; nu bestaan er twee soorten bewijs. Deze tests staan op de grens
/// ertussen, en ze staan er in twee lagen.</para>
///
/// <para>De eerste laag kijkt naar het <strong>typesysteem</strong>: kán een klantscope een
/// schrijfmethode aanroepen. Dat is de grens die telt, want een grens die uit een <c>if</c> bestaat
/// kan iemand vergeten en een grens die uit een signatuur bestaat niet. Die laag test niet of de
/// controle klopt maar of hij bestaat — en dat is precies wat een test kan zien en een reviewer
/// niet, want het ontbreken van een parameter valt niet op.</para>
///
/// <para>De tweede laag kijkt naar het <strong>gedrag</strong> van de echte
/// <see cref="ICustomerScopeResolver"/>: krijgt een klantgebruiker het schrijfbewijs werkelijk niet,
/// en — de onmisbare spiegel — krijgt een operator het wél. Zonder die spiegel wordt de eerste laag
/// groen doordat niemand meer iets mag, en dan is de functie stuk terwijl de test tevreden is.
/// </para>
/// </remarks>
public class SchrijfgrensTests
{
    /// <summary>De typen die een schrijfrecht bewijzen.</summary>
    private static readonly Type[] Schrijfbewijzen = [typeof(PortalWriteScope), typeof(CustomerWriteScope)];

    /// <summary>De typen die alleen een leesrecht bewijzen.</summary>
    private static readonly Type[] Leesbewijzen =
    [
        typeof(CustomerScope),
        typeof(OperatorScope),
        typeof(OperatorCustomerScope),
    ];

    // ── Laag 1: het typesysteem ─────────────────────────────────────────────────────────────────

    /// <summary>De methoden van de opslag die iets wijzigen.</summary>
    public static TheoryData<string> Schrijfmethoden
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var methode in Schrijvend())
            {
                data.Add(methode.Name);
            }

            return data;
        }
    }

    [Fact]
    public void ErZijnSchrijfmethodenOmTeControleren()
    {
        // De spiegel van alles hieronder, en hij is niet theoretisch: zolang het portaal alleen las
        // was elke test op deze grens groen zonder dat er een grens was. Zou de schrijfkant
        // verdwijnen of anders heten dan PortalWriteResult<>, dan controleren de theorieën hier
        // niets meer terwijl ze groen blijven.
        var namen = Schrijvend().Select(m => m.Name).ToArray();

        Assert.True(
            namen.Length >= 5,
            "Er zijn minder dan vijf schrijfmethoden gevonden op IPortalDataStore. Fase 2 heeft " +
            "er vijf: CreateCustomerAsync, SaveCustomerAsync, SaveContractAsync, GrantAccessAsync " +
            "en RevokeAccessAsync. Een schrijfmethode is hier een methode die een " +
            "PortalWriteResult<> teruggeeft; heet die uitkomst anders, dan hoort deze test mee te " +
            "veranderen in plaats van stil niets meer te vinden. Gevonden: " +
            string.Join(", ", namen));
    }

    [Theory]
    [MemberData(nameof(Schrijfmethoden))]
    public void ElkeSchrijfmethodeBegintMetEenSchrijfbewijs(string naam)
    {
        var methode = Schrijvend().Single(m => string.Equals(m.Name, naam, StringComparison.Ordinal));
        var eerste = methode.GetParameters().FirstOrDefault();

        Assert.NotNull(eerste);
        Assert.Contains(eerste.ParameterType, Schrijfbewijzen);
    }

    [Theory]
    [MemberData(nameof(Schrijfmethoden))]
    public void GeenSchrijfmethodeNeemtEenLeesbewijs(string naam)
    {
        // Dit is het punt waarom PortalWriteScope naast OperatorScope bestaat. OperatorCustomerScope
        // wordt aan élke operatorpagina gegeven om iets te tónen; zou een schrijfmethode dat type
        // aannemen, dan heeft elke pagina die een klant rendert al een schrijfbewijs in handen en is
        // aan geen enkele signatuur meer te zien welke aanroep de opslag verandert.
        var methode = Schrijvend().Single(m => string.Equals(m.Name, naam, StringComparison.Ordinal));

        var leesparameters = methode.GetParameters()
            .Where(p => Leesbewijzen.Contains(p.ParameterType))
            .Select(p => $"{p.ParameterType.Name} {p.Name}")
            .ToArray();

        Assert.True(
            leesparameters.Length == 0,
            $"IPortalDataStore.{naam} neemt een leesbewijs aan: " +
            $"{string.Join(", ", leesparameters)}.\n\n" +
            "§2 geeft de klant op contract en toegang lezen en de operator lezen + bewerken. Een " +
            "schrijfmethode die met een leesbewijs is aan te roepen maakt van dat verschil een " +
            "afspraak in plaats van een grens. Vraag een PortalWriteScope of een " +
            "CustomerWriteScope; die zijn alleen bij de resolver te krijgen, en die weegt de rol.");
    }

    [Theory]
    [MemberData(nameof(Schrijfmethoden))]
    public void GeenSchrijfmethodeNeemtDeKlantAlsLosseTekenreeks(string naam)
    {
        // Dezelfde regel die de leeskant al volgt: met een string customerId erbij is "mag deze
        // gebruiker bij deze klant" weer iets dat de aanroeper hoort te stellen, en dan kan hij het
        // vergeten. RevokeAccessAsync neemt wél een string — dat is het e-mailadres van de regel
        // die wordt ingetrokken, en dat is een gegeven en geen recht.
        string[] verdacht = ["customerid", "customer", "cid", "slug", "klantid", "tenant"];

        var methode = Schrijvend().Single(m => string.Equals(m.Name, naam, StringComparison.Ordinal));

        var gevonden = methode.GetParameters()
            .Where(p => p.ParameterType == typeof(string))
            .Where(p => verdacht.Contains(p.Name?.ToLowerInvariant()))
            .Select(p => p.Name!)
            .ToArray();

        Assert.True(
            gevonden.Length == 0,
            $"IPortalDataStore.{naam} krijgt de klant als losse tekenreeks mee " +
            $"({string.Join(", ", gevonden)}) in plaats van uit de scope. De partitiesleutel waarin " +
            "geschreven wordt hoort uit het bewijs te komen, niet uit een argument dat de aanroeper " +
            "zelf samenstelt.");
    }

    [Fact]
    public void EenSchrijfbewijsIsNietUitEenLeesbewijsTeHalen()
    {
        // Zou een leesbewijs een schrijfbewijs kunnen opleveren — als property, als methode of via
        // een conversie — dan is de scheiding hierboven een formaliteit: elke pagina die iets mag
        // tonen heeft dan een pad naar iets mogen wijzigen.
        foreach (var lees in Leesbewijzen)
        {
            var paden = Paden(lees).ToArray();

            Assert.True(
                paden.Length == 0,
                $"{lees.Name} biedt een pad naar een schrijfbewijs: {string.Join(", ", paden)}. " +
                "Daarmee is het verschil tussen lezen en bewerken weg voor iedereen die het " +
                "leesbewijs al heeft.");
        }
    }

    [Fact]
    public void EenSchrijfbewijsMagWelNaarEenLeesbewijsLeiden()
    {
        // De andere richting is juist de bedoeling en staat hier zodat de test hierboven niet per
        // ongeluk "geen enkel pad tussen scopes" gaat betekenen. Wie mag bewerken mag lezen; dat
        // volgt uit de rolmatrix.
        Assert.NotEmpty(Paden(typeof(PortalWriteScope), Leesbewijzen));
        Assert.NotEmpty(Paden(typeof(CustomerWriteScope), Schrijfbewijzen));
    }

    [Fact]
    public void HetSchrijfbewijsErftNietVanEenLeesbewijs()
    {
        // Zou het dat wel doen, dan accepteert elke leesmethode stilzwijgend ook een schrijfbewijs
        // en omgekeerd is aan de signatuur niet meer te zien wat er nodig is.
        foreach (var schrijf in Schrijfbewijzen)
        {
            foreach (var lees in Leesbewijzen)
            {
                Assert.False(lees.IsAssignableFrom(schrijf));
                Assert.False(schrijf.IsAssignableFrom(lees));
            }
        }
    }

    [Fact]
    public void DeOpslagVraagtOveralEenScopeEnNooitEenLosseKlantSlug()
    {
        // Dezelfde regel als bij de telemetriestore, nu voor de portaalgegevens. Ook de leesmethoden
        // vallen hieronder: wie geen scope heeft kan hier niets.
        var methoden = typeof(IPortalDataStore).GetMethods();

        Assert.NotEmpty(methoden);

        foreach (var methode in methoden)
        {
            var eerste = methode.GetParameters().FirstOrDefault();

            Assert.NotNull(eerste);
            Assert.True(
                eerste.ParameterType.Name.EndsWith("Scope", StringComparison.Ordinal),
                $"IPortalDataStore.{methode.Name} begint niet met een scope maar met " +
                $"{eerste.ParameterType.Name}. Elke methode hoort met een scope te beginnen: dat is " +
                "wat autorisatie hier tot een eigenschap van het typesysteem maakt in plaats van " +
                "tot een vergeten if.");
        }
    }

    [Fact]
    public void HetKlantcontractIsMetEenKlantbewijsNietAlsOperatorweergaveOpTeVragen()
    {
        // De overloads van IContractViews zijn de hele grens: een CustomerScope levert het
        // klanttype, een CustomerWriteScope het operatortype. Er is dus geen aanroep te schrijven
        // die met een leesbewijs de operatorweergave oplevert — niet omdat het verboden is, maar
        // omdat de overload niet bestaat.
        var metLeesbewijs = typeof(IContractViews)
            .GetMethods()
            .Where(m => m.GetParameters().Any(p => Leesbewijzen.Contains(p.ParameterType)))
            .Select(m => Uitgepakt(m.ReturnType))
            .ToArray();

        Assert.NotEmpty(metLeesbewijs);
        Assert.All(metLeesbewijs, t => Assert.Equal(typeof(CustomerContractView), t));

        var metSchrijfbewijs = typeof(IContractViews)
            .GetMethods()
            .Where(m => m.GetParameters().Any(p => Schrijfbewijzen.Contains(p.ParameterType)))
            .Select(m => Uitgepakt(m.ReturnType))
            .ToArray();

        Assert.NotEmpty(metSchrijfbewijs);
        Assert.All(metSchrijfbewijs, t => Assert.Equal(typeof(OperatorContractView), t));
    }

    // ── Laag 2: het gedrag van de resolver ──────────────────────────────────────────────────────

    [Fact]
    public async Task EenKlantgebruikerKrijgtGeenSchrijfbewijs()
    {
        var resolver = Autorisatiebron.Resolver();

        Assert.Null(await resolver.ResolveWriteAsync(Testprincipals.Klant()));
    }

    [Fact]
    public async Task EenKlantgebruikerKrijgtGeenSchrijfbewijsOpZijnEigenKlant()
    {
        // Juist deze: hij mag die klant lezen. Het contract van zijn eigen omgeving is dus zichtbaar
        // en niet wijzigbaar, en dat verschil hoort uit de resolver te komen en niet uit een
        // verborgen knop.
        var resolver = Autorisatiebron.Resolver();

        Assert.NotNull(await resolver.ResolveAsync(Testprincipals.Klant(), EigenKlant));
        Assert.Null(await resolver.ResolveWriteAsync(Testprincipals.Klant(), EigenKlant));
    }

    [Fact]
    public async Task EenGebruikerZonderRolKrijgtGeenSchrijfbewijs()
    {
        var resolver = Autorisatiebron.Resolver();

        Assert.Null(await resolver.ResolveWriteAsync(Testprincipals.ZonderRol()));
        Assert.Null(await resolver.ResolveWriteAsync(Testprincipals.ZonderRol(), EigenKlant));
    }

    [Fact]
    public async Task EenBezoekerDieNietIsAangemeldKrijgtGeenSchrijfbewijs()
    {
        var resolver = Autorisatiebron.Resolver();

        Assert.Null(await resolver.ResolveWriteAsync(Testprincipals.Anoniem()));
        Assert.Null(await resolver.ResolveWriteAsync(Testprincipals.Anoniem(), EigenKlant));
        Assert.Null(await resolver.ResolveWriteAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task EenOperatorKrijgtHetSchrijfbewijsWel()
    {
        // De spiegel. Zonder deze test kan alles hierboven groen worden doordat niemand meer iets
        // mag, en dan is contractbeheer stuk terwijl de suite tevreden is. Dat patroon heeft in de
        // vorige fase een echt lek gevonden.
        var resolver = Autorisatiebron.Resolver();

        var portaal = await resolver.ResolveWriteAsync(Testprincipals.Operator());
        var klant = await resolver.ResolveWriteAsync(Testprincipals.Operator(), EigenKlant);

        Assert.NotNull(portaal);
        Assert.NotNull(klant);
        Assert.Equal(EigenKlant, klant.CustomerId);
        Assert.Equal("Acme Logistiek", klant.DisplayName);
    }

    [Fact]
    public async Task EenOperatorKrijgtGeenSchrijfbewijsOpEenKlantDieNietBestaat()
    {
        // Anders legt een getypte slug een contract vast in een partitiesleutel die niemand ooit
        // heeft ingericht, en staat er een contract in een partitie die geen klant is.
        var resolver = Autorisatiebron.Resolver();

        Assert.Null(await resolver.ResolveWriteAsync(Testprincipals.Operator(), "bestaat-niet"));
        Assert.Null(await resolver.ResolveWriteAsync(Testprincipals.Operator(), customerId: null));
        Assert.Null(await resolver.ResolveWriteAsync(Testprincipals.Operator(), " "));
    }

    [Fact]
    public async Task DeSlugUitDeUrlKomtCanoniekTerugInHetSchrijfbewijs()
    {
        // De opzoektabel vergelijkt hoofdletterongevoelig, dus ACME-Logistiek resolvet. Wat er dan
        // in de partitiesleutel belandt moet de canonieke vorm zijn: anders staan de documenten van
        // één klant onder twee partitiesleutels, en die zijn in Cosmos twee klanten.
        var resolver = Autorisatiebron.Resolver();

        var scope = await resolver.ResolveWriteAsync(Testprincipals.Operator(), "ACME-Logistiek");

        Assert.NotNull(scope);
        Assert.Equal(EigenKlant, scope.CustomerId);
    }

    [Fact]
    public async Task EenKlantZonderIngerichteOpslagIsWelTeBewerkenEnNietTeLezen()
    {
        // De klant in onboarding: zijn Azure-omgeving staat nog niet, dus er valt niets te lezen —
        // maar juist zijn contract is wat je aan het invullen bent. Zou het schrijfrecht op het
        // leesrecht leunen, dan was dat contract niet vast te leggen tot de omgeving stond, en dat
        // is de omgekeerde volgorde van hoe onboarding gaat.
        var zonderOpslag = Autorisatiebron.ZonderOpslag();
        var resolver = Autorisatiebron.ResolverZonderOpslag(zonderOpslag);

        Assert.Null(await resolver.ResolveAsync(Testprincipals.Operator(), zonderOpslag.Id));
        Assert.NotNull(await resolver.ResolveWriteAsync(Testprincipals.Operator(), zonderOpslag.Id));
    }

    [Fact]
    public async Task DeWijzigingKrijgtEenNaamOpZijnNaam()
    {
        // Gaat als changedBy en grantedBy mee op elk document. Niet omdat er een audittrail is,
        // maar omdat "dit heeft iemand vorige week veranderd" iets anders is dan "dit stond hier al".
        var resolver = Autorisatiebron.Resolver();

        var scope = await resolver.ResolveWriteAsync(Testprincipals.Operator());

        Assert.NotNull(scope);
        Assert.Equal("Marcel de Graaf", scope.Actor);
    }

    // ── Hulpmiddelen ────────────────────────────────────────────────────────────────────────────

    /// <summary>De klant waar de testklantgebruiker recht op heeft.</summary>
    private const string EigenKlant = "acme-logistiek";

    /// <summary>
    /// De methoden van de opslag die iets wijzigen: die een <see cref="PortalWriteResult{T}"/>
    /// teruggeven.
    /// </summary>
    /// <remarks>
    /// Op de uitkomst en niet op de naam. Een lijst met namen zou "SaveContractAsync" bevatten en de
    /// methode missen die iemand er volgende maand bij zet.
    /// </remarks>
    private static IEnumerable<MethodInfo> Schrijvend() =>
        typeof(IPortalDataStore)
            .GetMethods()
            .Where(m => Uitgepakt(m.ReturnType) is { IsGenericType: true } t
                && t.GetGenericTypeDefinition() == typeof(PortalWriteResult<>));

    /// <summary>Haalt het type uit een <c>Task&lt;T&gt;</c>.</summary>
    private static Type Uitgepakt(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>)
            ? type.GetGenericArguments()[0]
            : type;

    /// <summary>
    /// De leden van een type die naar een van de doeltypen leiden: properties, methoden en
    /// conversies.
    /// </summary>
    private static IEnumerable<string> Paden(Type type, Type[]? doelen = null)
    {
        var zoek = doelen ?? Schrijfbewijzen;

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (zoek.Contains(property.PropertyType))
            {
                yield return $"{type.Name}.{property.Name}";
            }
        }

        foreach (var methode in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
        {
            if (zoek.Contains(Uitgepakt(methode.ReturnType)))
            {
                yield return $"{type.Name}.{methode.Name}()";
            }
        }
    }
}
