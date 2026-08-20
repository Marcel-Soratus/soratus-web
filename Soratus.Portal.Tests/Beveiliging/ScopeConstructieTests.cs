using System.Reflection;
using System.Text.RegularExpressions;
using Soratus.Portal.Security;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Beveiliging;

/// <summary>
/// Zet de constructie vast waar de autorisatie van het portaal op leunt: een scope is niet te
/// maken, behalve door de resolver.
/// </summary>
/// <remarks>
/// <para>Het idee is dat autorisatie geen controle is maar een eigenschap van het typesysteem. Een
/// pagina die een <see cref="CustomerScope"/> in handen heeft kan die niet ongeautoriseerd hebben
/// gekregen, want er is geen andere herkomst. Dat argument valt om zodra er een tweede plek is die
/// er een kan maken — en dan valt het stil om, zonder dat er iets rood wordt.</para>
///
/// <para>Daarom staan hier twee soorten test. De eerste kijkt naar het typesysteem: is de
/// constructor niet publiek. De tweede kijkt naar de broncode: staat de aanroep echt maar op één
/// plek. Die tweede is nodig omdat <c>internal</c> de <em>hele</em> assembly dekt: elke andere
/// klasse in <c>Soratus.Portal</c> kan vandaag <c>new CustomerScope(...)</c> schrijven en daarmee
/// een autorisatiebewijs uit het niets maken.</para>
/// </remarks>
public class ScopeConstructieTests
{
    /// <summary>
    /// De scopetypen die alleen door de resolver gemaakt mogen worden.
    /// </summary>
    /// <remarks>
    /// <para>Met opzet géén handmatige lijst, om dezelfde reden als bij
    /// <c>Paginaverzameling</c>: een lijst die je zelf bijhoudt vergeet iemand aan te vullen, en dan
    /// valt precies het nieuwste scopetype buiten de regels — het type waar nog niemand over heeft
    /// nagedacht. Fase 2 voegde er twee toe (<see cref="PortalWriteScope"/> en
    /// <see cref="CustomerWriteScope"/>) en die vielen hier stil buiten.</para>
    ///
    /// <para>De vorm is de afspraak: een scope is een publiek type in
    /// <c>Soratus.Portal.Security</c> waarvan de naam op <c>Scope</c> eindigt.</para>
    /// </remarks>
    public static IReadOnlyList<Type> Alle { get; } =
    [
        .. typeof(CustomerScope).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsPublic: true, IsAbstract: false })
            .Where(t => string.Equals(
                t.Namespace, typeof(CustomerScope).Namespace, StringComparison.Ordinal))
            .Where(t => t.Name.EndsWith("Scope", StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
    ];

    /// <summary>De scopetypen als theoriegegevens.</summary>
    public static TheoryData<Type> Scopetypen
    {
        get
        {
            var data = new TheoryData<Type>();
            foreach (var scopetype in Alle)
            {
                data.Add(scopetype);
            }

            return data;
        }
    }

    /// <summary>De namen van de scopetypen, voor de tests die de broncode doorzoeken.</summary>
    public static TheoryData<string> Scopenamen
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var scopetype in Alle)
            {
                data.Add(scopetype.Name);
            }

            return data;
        }
    }

    [Fact]
    public void ErZijnScopetypenGevondenOmTeControleren()
    {
        // Zonder deze test blijft alles hieronder groen zodra de reflectie niets meer vindt —
        // bijvoorbeeld omdat de naamgeving of de naamruimte verandert. De twee polen staan er
        // expliciet in: het leesbewijs van een klant en het schrijfbewijs van een operator. Vindt
        // de reflectie die twee, dan kijkt hij naar het juiste.
        Assert.Contains(typeof(CustomerScope), Alle);
        Assert.Contains(typeof(PortalWriteScope), Alle);

        Assert.True(
            Alle.Count >= 5,
            "Er zijn minder dan vijf scopetypen gevonden in Soratus.Portal.Security. Fase 2 heeft " +
            "er vijf: CustomerScope, CustomerWriteScope, OperatorCustomerScope, OperatorScope en " +
            "PortalWriteScope. Zijn er typen weggevallen uit de opsomming, dan controleren de " +
            "tests hieronder minder dan ze beweren. Gevonden: " +
            string.Join(", ", Alle.Select(t => t.Name)));
    }

    /// <summary>
    /// Het enige bestand dat een scope mag maken, ten opzichte van de projectmap.
    /// </summary>
    private const string EnigeToegestaneBestand = "Security/CustomerScopeResolver.cs";

    // ── Het typesysteem ─────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Scopetypen))]
    public void EenScopeIsBuitenDeAssemblyNietTeConstrueren(Type scopetype)
    {
        var publieke = scopetype.GetConstructors(BindingFlags.Instance | BindingFlags.Public);

        Assert.True(
            publieke.Length == 0,
            $"{scopetype.Name} heeft een publieke constructor. Daarmee kan elke aanroeper een " +
            "autorisatiebewijs uit het niets maken en is de hele constructie weg: een pagina die " +
            "een scope heeft, kan hem dan wél ongeautoriseerd hebben gekregen. Houd de " +
            "constructor internal en laat CustomerScopeResolver de enige plek zijn die hem " +
            "aanroept.");
    }

    [Theory]
    [MemberData(nameof(Scopetypen))]
    public void EenScopeHeeftGeenPubliekeFabrieksmethodeDieHemAlsnogMaakt(Type scopetype)
    {
        // Een statische fabrieksmethode zou de internal constructor omzeilen zonder dat de test
        // hierboven iets merkt.
        var fabrieken = scopetype
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(m => scopetype.IsAssignableFrom(m.ReturnType))
            .Select(m => m.Name)
            .ToArray();

        Assert.True(
            fabrieken.Length == 0,
            $"{scopetype.Name} heeft een publieke statische methode die zichzelf teruggeeft " +
            $"({string.Join(", ", fabrieken)}). Dat is een tweede herkomst voor een " +
            "autorisatiebewijs en haalt de garantie onderuit die het internal-zijn van de " +
            "constructor moet geven.");
    }

    [Fact]
    public void DeKlantscopeDraagtGeenOperatorvelden()
    {
        // Wat de klant niet mag zien staat niet als null op het type — het staat er niet. Een
        // ontbrekende property kan niet lekken, ook niet als iemand een @if vergeet.
        Assert.Null(typeof(CustomerScope).GetProperty("EnvironmentDetail"));
        Assert.Null(typeof(CustomerScope).GetProperty("Subscription"));
        Assert.Null(typeof(CustomerScope).GetProperty("ResourceGroup"));
    }

    [Fact]
    public void DeOperatorklantscopeErftNietVanDeKlantscope()
    {
        // Zou hij dat wel doen, dan accepteert elke methode die een CustomerScope vraagt
        // stilzwijgend ook de operatorvariant, en is aan de signatuur niet meer te zien wat een
        // methode nodig heeft.
        Assert.False(typeof(CustomerScope).IsAssignableFrom(typeof(OperatorCustomerScope)));
    }

    // ── De broncode ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Scopenamen))]
    public void EenScopeWordtInHetHelePortaalMaarOpEenPlekGemaakt(string scopetype)
    {
        // internal dekt de hele assembly. Vandaag roept alleen de resolver de constructor aan; de
        // documentatie belooft dat ook. Deze test zet die belofte vast, want anders schrijft er
        // over een half jaar iemand een handige helper en is de garantie stil verdwenen.
        var patroon = new Regex($@"\bnew\s+{Regex.Escape(scopetype)}\s*\(", RegexOptions.Compiled);

        var vindplaatsen = new List<string>();

        foreach (var bestand in Broncode.Portaalbestanden())
        {
            var pad = Broncode.RelatiefPad(bestand);
            var inhoud = File.ReadAllText(bestand.FullName);

            foreach (Match _ in patroon.Matches(inhoud))
            {
                vindplaatsen.Add(pad);
            }
        }

        var buitenDeResolver = vindplaatsen
            .Where(p => !string.Equals(p, EnigeToegestaneBestand, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            buitenDeResolver.Length == 0,
            $"'new {scopetype}(' staat buiten {EnigeToegestaneBestand}, namelijk in: " +
            $"{string.Join(", ", buitenDeResolver)}.\n\n" +
            "Een scope is het bewijs dat iemand deze gegevens mag lezen. De hele autorisatie van " +
            "het portaal leunt erop dat dat bewijs maar op één plek ontstaat — daar waar eerst " +
            "wordt gekeken of de gebruiker er recht op heeft. Wie hem ergens anders maakt, maakt " +
            "een autorisatiebewijs uit het niets: de code compileert, de pagina werkt, en de " +
            "controle is overgeslagen zonder dat iemand dat kan zien.\n\n" +
            "Heb je een scope nodig? Vraag hem aan ICustomerScopeResolver. Kun je dat niet, dan " +
            "hoort de aanroeper hem door te geven.");
    }

    [Theory]
    [MemberData(nameof(Scopenamen))]
    public void DeResolverMaaktDeScopeOokDaadwerkelijk(string scopetype)
    {
        // Zonder deze tegenhanger blijft de test hierboven groen als iemand de constructie
        // helemaal weghaalt — of als het zoekpatroon nergens meer op past. Dat is niet theoretisch:
        // een scopetype dat door niemand wordt gemaakt is een recht dat niet aan te vragen is, en
        // dan staat het scherm eromheen stil zonder dat de zichtbaarheidstests iets merken. Dit is
        // ook de spiegel van de klanttest bij de schrijfgrens: de operator moet het bewijs wél
        // kunnen krijgen, anders is de functie stuk terwijl de grens klopt.
        var pad = Path.Combine(Broncode.Portaalproject.FullName, EnigeToegestaneBestand);
        var inhoud = File.ReadAllText(pad);

        Assert.Matches($@"\bnew\s+{Regex.Escape(scopetype)}\s*\(", inhoud);
    }
}
