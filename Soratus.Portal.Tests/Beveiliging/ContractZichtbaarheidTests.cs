using System.Reflection;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Beveiliging;

/// <summary>
/// Per veld van de contractkaart: mag een klant het zien (§2, §3.5).
/// </summary>
/// <remarks>
/// <para><strong>Op typeniveau en niet op markup.</strong> Dat is de vorm die in dit portaal werkt
/// en de reden staat in de fase-0-notitie bij §12 en §14: een klanttype dat een veld niet heeft kan
/// het niet lekken, ook niet als iemand er over een half jaar een tooltip bij zet, en ook niet over
/// een serialisatiegrens waar een <c>@if</c> in de Razor niets meer betekent. Een test op markup
/// kijkt naar het laatste station.</para>
///
/// <para>De verboden velden staan hier met naam. Dat is met opzet een lijst en geen regel: welk veld
/// operator-only is volgt uit de rolmatrix en niet uit iets dat aan het veld zelf te zien is. Wat
/// wél mechanisch is, is de tegenhanger — elk veld dat de operator heeft en de klant niet, wordt
/// hier opgesomd, zodat een nieuw veld op het operatortype een test langskomt in plaats van
/// ongemerkt te ontstaan.</para>
/// </remarks>
public class ContractZichtbaarheidTests
{
    /// <summary>
    /// De velden die §2 operator-only maakt, met de regel uit de spec erbij.
    /// </summary>
    private static readonly (string Veld, string Waarom)[] Operatorvelden =
    [
        ("AzureSurchargePercentage",
            "§2: \"Facturatie: Azure per dienst + beheeropslag\" staat voor de klant op nee. Dit is " +
            "onze marge; die hoort niet op het scherm van degene die hem betaalt."),
        ("EnvironmentDetail",
            "§2 maakt infrastructuurdetails operator-only. Dit veld draagt subscription en resource " +
            "group; het is de reden dat CustomerScope het niet heeft en OperatorCustomerScope wel."),
        ("ContractETag",
            "Een etag is een schrijfvoorwaarde. De klant schrijft niet, dus hij heeft er niets aan — " +
            "en een veld dat niemand leest is precies het veld dat later ergens opduikt."),
        ("CustomerETag",
            "Zie ContractETag: een schrijfvoorwaarde op een scherm dat niet schrijft."),
        ("ChangedAt",
            "Wanneer wij het contract hebben aangepast is onze administratie. De klant heeft het " +
            "contract, niet ons wijzigingslog."),
        ("ChangedBy",
            "Zie ChangedAt: dit is de naam van een Soratus-medewerker."),
        ("IsFromConfigurationOnly",
            "Dat de eenmalige migratie nog niet heeft gelopen is onze inrichting en niet de " +
            "administratie van de klant."),
    ];

    /// <summary>De operator-only velden als theoriegegevens.</summary>
    public static TheoryData<string, string> VerbodenVelden
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (veld, waarom) in Operatorvelden)
            {
                data.Add(veld, waarom);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(VerbodenVelden))]
    public void DeKlantweergaveVanHetContractDraagtHetVeldNiet(string veld, string waarom)
    {
        Assert.Null(typeof(CustomerContractView).GetProperty(veld));
        Assert.False(string.IsNullOrWhiteSpace(waarom));
    }

    [Theory]
    [MemberData(nameof(VerbodenVelden))]
    public void DeOperatorweergaveVanHetContractDraagtHetVeldWel(string veld, string waarom)
    {
        // De spiegel, en hij is hier het halve werk. Zonder deze kant is elke test hierboven ook
        // groen als het veld nergens meer bestaat — en dan is de contractkaart stuk terwijl de
        // zichtbaarheid klopt. Bij errorType (§14) was dit precies het geval: het veld stond alleen
        // in de tooltip als de melding leeg was, dus de operator zag het nooit en de klant juist wel.
        Assert.NotNull(typeof(OperatorContractView).GetProperty(veld));
        Assert.False(string.IsNullOrWhiteSpace(waarom));
    }

    [Fact]
    public void ElkVerschilTussenDeTweeWeergavenIsBewustOpgesomd()
    {
        // Dit is de mechanische helft. De lijst hierboven zegt wat er niet mag; deze test zegt dat
        // er niets anders is. Zet iemand een veld op het operatortype dat de klant niet heeft, dan
        // is dat een beslissing over zichtbaarheid — en die hoort iemand bewust te nemen in plaats
        // van hem stilzwijgend mee te laten liften.
        string[] opgesomd = [.. Operatorvelden.Select(veld => veld.Veld)];

        var alleenOperator = Namen(typeof(OperatorContractView))
            .Except(Namen(typeof(CustomerContractView)), StringComparer.Ordinal)
            .Except(BewustAnders, StringComparer.Ordinal)
            .Except(opgesomd, StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            alleenOperator.Length == 0,
            "OperatorContractView draagt velden die CustomerContractView niet heeft en die " +
            "nergens zijn verantwoord: " + string.Join(", ", alleenOperator) + ".\n\n" +
            "Elk verschil tussen de twee weergaven is een beslissing over wat een klant mag zien. " +
            "Hoort het veld bij de operator, zet het dan in VerbodenVelden met de regel uit §2 " +
            "erbij. Hoort de klant het ook te zien, zet het dan op beide typen.");
    }

    [Fact]
    public void DeKlantweergaveHeeftGeenVeldenDieDeOperatorMist()
    {
        // De andere richting: een veld dat alleen de klant heeft is bijna altijd een vergissing —
        // de operator ziet hetzelfde scherm plus meer. De twee meldingen zijn de uitzondering en
        // staan als zodanig benoemd.
        var alleenKlant = Namen(typeof(CustomerContractView))
            .Except(Namen(typeof(OperatorContractView)), StringComparer.Ordinal)
            .Except(BewustAnders, StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            alleenKlant.Length == 0,
            "CustomerContractView draagt velden die OperatorContractView niet heeft: " +
            string.Join(", ", alleenKlant) + ". De operator ziet hetzelfde scherm plus meer; een " +
            "veld dat alleen de klant heeft is bijna altijd een vergissing.");
    }

    [Fact]
    public void DeToegangsregelVanDeKlantDraagtGeenEtagEnGeenUitgever()
    {
        // Intrekken is operator-only, dus de klant heeft de etag van de regel niet nodig. En wie de
        // toegang heeft uitgedeeld is onze administratie.
        Assert.Null(typeof(CustomerAccessRow).GetProperty("ETag"));
        Assert.Null(typeof(CustomerAccessRow).GetProperty("GrantedBy"));
        Assert.Null(typeof(CustomerAccessRow).GetProperty("GrantedAt"));

        Assert.NotNull(typeof(OperatorAccessRow).GetProperty("ETag"));
        Assert.NotNull(typeof(OperatorAccessRow).GetProperty("GrantedBy"));
        Assert.NotNull(typeof(OperatorAccessRow).GetProperty("GrantedAt"));
    }

    [Fact]
    public void HetTariefDeIndexatieEnDeBundelStaanOokOpDeKlantweergave()
    {
        // Niet alles is operator-only, en een test die alleen verbiedt zou dat laten verschuiven.
        // §2 geeft de klant lezen op contract; §3.5 zet uurtarief en indexatie op de contractkaart,
        // en §3.7 rekent de extra uren voor hem uit met datzelfde tarief. Een klant die zijn eigen
        // tarief niet mag zien kan zijn factuur niet controleren.
        Assert.NotNull(typeof(CustomerContractView).GetProperty("HourlyRate"));
        Assert.NotNull(typeof(CustomerContractView).GetProperty("Indexation"));
        Assert.NotNull(typeof(CustomerContractView).GetProperty("BundledHours"));
        Assert.NotNull(typeof(CustomerContractView).GetProperty("Sla"));
        Assert.NotNull(typeof(CustomerContractView).GetProperty("Contact"));
    }

    [Fact]
    public void DeKlantweergaveDraagtDeUitlegWaaromErNietsTeWijzigenValt()
    {
        // §8: read-only is platte tekst en geen uitgegrijsd veld, en de openstaande vraag uit §9 is
        // met "alleen Soratus" beantwoord. Er hoort dus een melding te staan in plaats van een knop
        // die niets doet — een knop die niets doet belooft dat het wél kan.
        Assert.NotNull(typeof(CustomerContractView).GetProperty("ReadOnlyNotice"));
        Assert.False(string.IsNullOrWhiteSpace(ContractNotice.ReadOnly));
    }

    [Fact]
    public void DeAanduidingBinnenDeKlantBelooftGeenRechten()
    {
        // Beide aanduidingen mogen precies hetzelfde: lezen. Er is geen klantaanduiding waarmee
        // iemand iets kan wijzigen. Dat staat als tekst op het scherm, want anders is "Beheerder
        // klant" een naam die een bevoegdheid belooft die niet bestaat.
        //
        // De melding staat op béide typen, en dat is geen verdubbeling: de klant is juist de lezer
        // die het woord op zichzelf betrekt. Het klantscherm haalde de tekst eerder rechtstreeks
        // uit de constante in de Razor; dan staat de belofte buiten het bereik van de compiler en
        // is een melding die op één van de twee schermen ontbreekt niet meer op te merken.
        Assert.NotNull(typeof(OperatorContractView).GetProperty("AccessLabelNotice"));
        Assert.NotNull(typeof(CustomerContractView).GetProperty("AccessLabelNotice"));
        Assert.Contains("leesrecht", ContractNotice.AccessLabelsAreEqual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Soratus", ContractNotice.AccessLabelsAreEqual, StringComparison.Ordinal);
    }

    [Fact]
    public void HetPortaalBeweertNietDatIemandKanAanmelden()
    {
        // Toegang bestaat uit twee toestanden: vastgelegd bij ons en actief in Entra. Het portaal
        // kent de tweede niet, en dat staat er dan ook. Beide rollen krijgen die melding — ook de
        // klant, want als een collega in de lijst staat en niet kan inloggen is dit de uitleg.
        Assert.NotNull(typeof(CustomerContractView).GetProperty("AccessStateNotice"));
        Assert.NotNull(typeof(OperatorContractView).GetProperty("AccessStateNotice"));

        Assert.Equal(AccessEntraState.Unknown, default(AccessEntraState));
    }

    /// <summary>
    /// De velden waarvan het verschil tussen de twee weergaven geen zichtbaarheidsvraag is.
    /// </summary>
    /// <remarks>
    /// <para>De melding waarom de klant niets kan wijzigen (die heeft de operator niet nodig), de
    /// keuzelijst van aanduidingen voor het formulier, en de toegangslijst — die is op beide typen
    /// een lijst van een ander rijtype, en dat verschil staat in
    /// <see cref="DeToegangsregelVanDeKlantDraagtGeenEtagEnGeenUitgever"/>.</para>
    ///
    /// <para><c>AccessLabelNotice</c> staat hier bewust <em>niet</em> in: die melding staat op beide
    /// typen en is dus geen verschil. Hem hier zetten zou precies verbergen wat er gegarandeerd moet
    /// worden.</para>
    /// </remarks>
    private static readonly string[] BewustAnders = ["ReadOnlyNotice", "Roles", "Access"];

    private static IEnumerable<string> Namen(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(p => p.Name);
}
