using System.Reflection;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Beveiliging;

/// <summary>
/// Wat er op de sprintweergave van de operator staat en niet op die van de klant (§2, §3.4).
/// </summary>
/// <remarks>
/// <para><strong>§2 geeft de sprint aan béide rollen</strong> — "Sprint (DevOps): ✓ read-only" staat er
/// twee keer — en zet in dezelfde tabel "Koppelingen (MCP/DevOps-details)" dicht voor de klant. Dit bestand
/// is dus het antwoord op de vraag wat er dan tóch operator-only is, en het legt dat antwoord vast als een
/// <em>typeverschil</em>: de klantvorm heeft de velden niet, in plaats van ze te hebben en te verbergen.
/// Een vergeten <c>@if</c> lekt; een ontbrekende property compileert niet.</para>
///
/// <para>Dezelfde vorm en dezelfde mechaniek als <see cref="ContractZichtbaarheidTests"/>: een opgesomde
/// lijst met de regel uit de spec erbij, en een tegenhanger die eist dat er niets buiten die lijst valt.
/// Die tweede is het halve werk — zonder hem is elke test hier ook groen als het veld nergens meer
/// bestaat.</para>
/// </remarks>
public class SprintZichtbaarheidTests
{
    /// <summary>De velden van de weergave die §2 operator-only maakt, met de regel erbij.</summary>
    private static readonly (string Veld, string Waarom)[] Operatorvelden =
    [
        ("DevOpsScope",
            "§2 zet \"Koppelingen (MCP/DevOps-details)\" voor de klant op nee, en dit veld ís die " +
            "koppeling: organisatie, project en team. Het is bovendien het enige gereedschap tegen een " +
            "tikfout in een teamnaam die per ongeluk een ánder bestaand team raakt — en dat gereedschap " +
            "is voor degene die het bord vastlegt."),
        ("QueriedScope",
            "Het bord waartegen de lezing werkelijk is gedaan. Staat naast DevOpsScope en mag ervan " +
            "verschillen; dat verschil is het antwoord op de vraag waarom er nog een sprint van een " +
            "ander team op het scherm staat. Zelfde koppelingsdetail, zelfde grens."),
        ("ScopeNotice",
            "De mededeling dat er geen bord is vastgelegd of dat het niet te gebruiken is. Die noemt het " +
            "contractscherm en het blok Omgeving — beheerhandelingen van Soratus, niet van de klant. Wat " +
            "de klant nodig heeft staat in StateNotice."),
        ("Failure",
            "Waarom de laatste ophaling niets opleverde. Zo'n tekst noemt een rolverlening of een " +
            "identiteit; zie SprintDocument.Failure. De klant hoort niet te weten met welke API wij " +
            "vechten — hij hoort te weten dat er nog niets is opgehaald."),
        ("Undated",
            "De iteraties zonder datums mét hun pad. Dát er werk buiten elke periode valt hoort de klant " +
            "te weten (UndatedCount); wélke iteraties dat zijn is boordhygiëne die Soratus repareert, en " +
            "een pad is een DevOps-detail."),
        ("Overlapping",
            "De iteraties die vandaag allemaal bevatten. Zelfde reden als Undated: de handeling erachter " +
            "is de periodes in DevOps corrigeren, en die namen zijn voor wie dat doet."),
        ("DatedCount",
            "Hoeveel iteraties er datums hebben. Dit verklaart het verschil tussen \"er zijn vijf " +
            "sprints en vandaag valt in geen ervan\" en \"er is er één en die is afgelopen\" — een " +
            "diagnose over de inrichting van het bord en niet over het werk van de klant."),
    ];

    /// <summary>De velden van een work item die §2 operator-only maakt, met de regel erbij.</summary>
    private static readonly (string Veld, string Waarom)[] Operatorvelden_Item =
    [
        ("CreatedBy",
            "§3.4 vraagt aan de klant de hérkomst — \"aangemaakt door agent of handmatig\" — en dat is " +
            "een andere vraag dan \"door wie\". De naam van de aanmaker is een koppelingsdetail én de " +
            "naam van een medewerker; het antwoord op de vraag die §3.4 wél stelt staat in Origin."),
        ("CreatedByAddress",
            "Het adres van de aanmaker. Zie CreatedBy, plus: dit is het gegeven waarop de herkomst " +
            "vergelijkt, dus het is precies het gereedschap van de operator als een item op " +
            "\"onbekend\" staat."),
        ("AssignedToAddress",
            "Het adres van de toegewezen persoon. De weergavenaam krijgt de klant wél — zonder \"wie " +
            "werkt waaraan\" is een sprintweergave geen sprintweergave — maar een adres is een " +
            "contactgegeven dat niemand hier heeft gevraagd."),
    ];

    /// <summary>De weergavevelden als theoriegegevens.</summary>
    public static TheoryData<string, string> VerbodenVelden => Data(Operatorvelden);

    /// <summary>De itemvelden als theoriegegevens.</summary>
    public static TheoryData<string, string> VerbodenItemvelden => Data(Operatorvelden_Item);

    private static TheoryData<string, string> Data((string Veld, string Waarom)[] velden)
    {
        var data = new TheoryData<string, string>();

        foreach (var (veld, waarom) in velden)
        {
            data.Add(veld, waarom);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(VerbodenVelden))]
    public void DeKlantweergaveVanDeSprintDraagtHetVeldNiet(string veld, string waarom)
    {
        Assert.Null(typeof(CustomerSprintView).GetProperty(veld));
        Assert.False(string.IsNullOrWhiteSpace(waarom));
    }

    [Theory]
    [MemberData(nameof(VerbodenVelden))]
    public void DeOperatorweergaveVanDeSprintDraagtHetVeldWel(string veld, string waarom)
    {
        // De spiegel, en hij is het halve werk. Zonder deze kant is elke test hierboven ook groen als het
        // veld nergens meer bestaat — en dan is de sprintweergave stuk terwijl de zichtbaarheid klopt. Bij
        // errorType (punt 14) was dit precies het geval: het veld stond alleen in de tooltip als de melding
        // leeg was, dus de operator zag het nooit en de klant juist wel.
        Assert.NotNull(typeof(OperatorSprintView).GetProperty(veld));
        Assert.False(string.IsNullOrWhiteSpace(waarom));
    }

    [Theory]
    [MemberData(nameof(VerbodenItemvelden))]
    public void DeKlantrijVanEenWorkItemDraagtHetVeldNiet(string veld, string waarom)
    {
        Assert.Null(typeof(CustomerSprintRow).GetProperty(veld));
        Assert.False(string.IsNullOrWhiteSpace(waarom));
    }

    [Theory]
    [MemberData(nameof(VerbodenItemvelden))]
    public void DeOperatorrijVanEenWorkItemDraagtHetVeldWel(string veld, string waarom)
    {
        Assert.NotNull(typeof(OperatorSprintRow).GetProperty(veld));
        Assert.False(string.IsNullOrWhiteSpace(waarom));
    }

    [Fact]
    public void ElkVerschilTussenDeTweeWeergavenIsBewustOpgesomd()
    {
        // De mechanische helft. De lijst hierboven zegt wat er niet mag; deze test zegt dat er niets anders
        // is. Zet iemand een veld op het operatortype dat de klant niet heeft, dan is dat een beslissing
        // over zichtbaarheid — en die hoort iemand bewust te nemen in plaats van hem stilzwijgend mee te
        // laten liften.
        Assert.Equal(
            [],
            Alleen(typeof(OperatorSprintView), typeof(CustomerSprintView), Operatorvelden));
    }

    [Fact]
    public void ElkVerschilTussenDeTweeRijenIsBewustOpgesomd()
    {
        Assert.Equal(
            [],
            Alleen(typeof(OperatorSprintRow), typeof(CustomerSprintRow), Operatorvelden_Item));
    }

    [Fact]
    public void DeKlantweergaveHeeftEenVeldDatDeOperatorNietHeeftEnDatIsBenoemd()
    {
        // De andere richting, en hier is hij níet leeg — daarom staat deze test er als eigen uitspraak en
        // niet als spiegel.
        //
        // UndatedCount staat alleen op de klantvorm: de klant krijgt het áántal iteraties zonder datums en
        // de operator de lijst zelf, en uit een lijst is een aantal te lezen. Twee vormen van hetzelfde
        // gegeven, en de klantvorm is de smallere — precies zoals bedoeld.
        //
        // UndatedNotice staat op beide en is dus geen verschil.
        Assert.Equal(
            ["UndatedCount"],
            Alleen(typeof(CustomerSprintView), typeof(OperatorSprintView), []));
    }

    [Fact]
    public void DeKlantrijHeeftGeenVeldenDieDeOperatorMist()
    {
        // Een veld dat alleen de klant heeft is op een rij bijna altijd een vergissing: de operator ziet
        // dezelfde rij plus meer.
        Assert.Equal([], Alleen(typeof(CustomerSprintRow), typeof(OperatorSprintRow), []));
    }

    [Fact]
    public void DeKlantweergaveDraagtGeenEnkelVeldDatEenAdresKanBevatten()
    {
        // Een tweede net onder het eerste, en met een andere maas: de lijst hierboven somt op wat we
        // kennen, en deze test zoekt op naam naar wat er bij kan komen. Een veld dat "Address", "Email" of
        // "UniqueName" in zijn naam heeft, hoort op geen enkel klanttype van dit scherm te staan — ook niet
        // als iemand vergeet hem hierboven op te sommen.
        string[] verdacht = ["Address", "Email", "Adres", "UniqueName", "Mail"];

        foreach (var type in new[] { typeof(CustomerSprintView), typeof(CustomerSprintRow) })
        {
            foreach (var naam in Namen(type))
            {
                Assert.DoesNotContain(
                    verdacht,
                    deel => naam.Contains(deel, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    /// <summary>De namen die op het ene type staan en niet op het andere, minus de opgesomde.</summary>
    /// <param name="type">Het type met de extra velden.</param>
    /// <param name="ander">Het type waar ze niet op staan.</param>
    /// <param name="opgesomd">De verantwoorde velden.</param>
    /// <returns>De namen die nergens zijn verantwoord.</returns>
    private static string[] Alleen(
        Type type,
        Type ander,
        (string Veld, string Waarom)[] opgesomd) =>
    [
        .. Namen(type)
            .Except(Namen(ander), StringComparer.Ordinal)
            .Except(opgesomd.Select(veld => veld.Veld), StringComparer.Ordinal)
            .OrderBy(naam => naam, StringComparer.Ordinal),
    ];

    /// <summary>De namen van de publieke eigenschappen van een type.</summary>
    /// <param name="type">Het type.</param>
    /// <returns>De namen.</returns>
    private static IEnumerable<string> Namen(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(p => p.Name);
}
