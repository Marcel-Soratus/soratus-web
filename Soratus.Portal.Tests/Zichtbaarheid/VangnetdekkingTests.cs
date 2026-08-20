using Bunit;
using Microsoft.AspNetCore.Components;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// Controleert dat het vangnet op zichtbaarheid werkelijk naar een gevulde pagina kijkt.
/// </summary>
/// <remarks>
/// <para><see cref="KlantVangnetTests"/> vindt zijn pagina's met reflectie, dus een nieuw scherm
/// valt er automatisch onder. Automatisch onder het vangnet vallen is echter niet hetzelfde als
/// automatisch gedekt zijn: het vangnet zoekt verboden woorden in markup, en markup die er niet
/// is bevat geen verboden woorden. In fase 1 is dat één keer gebeurd — de route van het
/// agentdetail heet <c>{Agentnaam}</c> met een kleine n, de opzoektabel vergeleek ordinaal met
/// <c>AgentNaam</c>, en de pagina rendeerde daardoor de agent <c>"test"</c>. Het vangnet stond
/// groen over een pagina die een lege staat toonde.</para>
///
/// <para>Deze tests staan tussen die twee in. Ze zeggen niets over wat een klant mag zien; ze
/// zeggen dat het scherm waarop dat wordt gecontroleerd echt gerenderd is. Een nieuw scherm van
/// fase 2 — de contractkaart, toegangsbeheer, klant aanmaken — komt hier vanzelf langs zodra het
/// een <c>@page</c> heeft.</para>
/// </remarks>
public class VangnetdekkingTests : Portaalrendertest
{
    /// <summary>Elke routeerbare pagina van het portaal.</summary>
    public static TheoryData<Type> Paginas
    {
        get
        {
            var data = new TheoryData<Type>();
            foreach (var pagina in Paginaverzameling.Alle())
            {
                data.Add(pagina);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Paginas))]
    public void ElkeRouteparameterKrijgtEenBestaandeWaarde(Type pagina)
    {
        var ontbreekt = Paginaverzameling.ParametersZonderEchteWaarde(pagina);

        Assert.True(
            ontbreekt.Count == 0,
            $"De pagina {pagina.Name} " +
            $"({string.Join(", ", Paginaverzameling.Routes(pagina))}) wordt in de " +
            "zichtbaarheidstests gerenderd met een routeparameter die geen bestaande waarde " +
            $"krijgt:\n  {string.Join("\n  ", ontbreekt)}\n\n" +
            "Zo'n pagina rendeert een 404 of een lege staat, en dan controleert het vangnet op " +
            "verboden woorden een pagina waar niets op staat — groen omdat er niets is, niet " +
            "omdat er niets mag. Vul de naam aan in Paginaverzameling.Waarde, of geef de " +
            "parameter een [Parameter]-property van het type string.");
    }

    [Theory]
    [MemberData(nameof(Paginas))]
    public void EenPaginaMetEenKlantInDeRouteToontDieKlantOokEcht(Type pagina)
    {
        // De positieve kant van de test hierboven, en de enige die niet op reflectie leunt: als de
        // slug is aangekomen én is opgezocht, dan staat de canonieke klantnaam in de markup. Een
        // pagina die de slug niet gebruikt hoort hier niet bij en wordt overgeslagen.
        if (!Paginaverzameling.Routeparameters(pagina)
                .Any(p => string.Equals(p, "Slug", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        MeldOperatorAan();

        var markup = RenderPagina(pagina).Markup;

        Assert.Contains(
            Klantnaam,
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DeDekkingscontroleZietEenParameterDieNietWordtGevuld()
    {
        // Bewijs dat de controle meet wat hij belooft, op nagemaakte pagina's in dit testproject.
        // Zonder dit blijven de theorieën hierboven groen zodra de controle zelf stilvalt, en dan
        // is er een vangnet onder een vangnet dat beide niets doen.
        var onbekend = Paginaverzameling.ParametersZonderEchteWaarde(typeof(PaginaMetOnbekendeParameter));

        Assert.Single(onbekend);
        Assert.Contains("ContractNummer", onbekend[0], StringComparison.Ordinal);
        Assert.Contains(Paginaverzameling.Terugval, onbekend[0], StringComparison.Ordinal);
    }

    [Fact]
    public void DeDekkingscontroleZietEenRouteparameterZonderStringproperty()
    {
        // Het tweede pad naar een blinde pagina: de parameter bestaat wel, maar niet als string.
        // Paginaverzameling vult alleen stringparameters, dus de pagina rendert met de standaard —
        // 0 voor een int — en dat is bijna altijd een lege staat.
        var onbekend = Paginaverzameling.ParametersZonderEchteWaarde(typeof(PaginaMetIntParameter));

        Assert.Single(onbekend);
        Assert.Contains("Maand", onbekend[0], StringComparison.Ordinal);
        Assert.Contains("string", onbekend[0], StringComparison.Ordinal);
    }

    [Fact]
    public void DeDekkingscontroleIsTevredenOverEenPaginaDieWelKlopt()
    {
        // De tegenhanger: een controle die altijd iets vindt is even nutteloos als een die nooit
        // iets vindt.
        Assert.Empty(Paginaverzameling.ParametersZonderEchteWaarde(typeof(PaginaDieKlopt)));
    }

    [Fact]
    public void DeRouteparametersWordenUitHetSjabloonGehaaldInclusiefConstraints()
    {
        Assert.Equal(
            ["Slug", "Maand"],
            Paginaverzameling.Routeparameters(typeof(PaginaMetIntParameter)));
    }

    /// <summary>
    /// De naam van de klant waar de testgebruiker recht op heeft, zoals hij op het scherm hoort te
    /// staan.
    /// </summary>
    private const string Klantnaam = "Acme Logistiek";

    /// <summary>
    /// Een nagemaakte pagina met een routeparameter die de opzoektabel niet kent.
    /// </summary>
    /// <remarks>
    /// Staat in het testproject en niet in het portaal, dus hij komt niet in
    /// <see cref="Paginaverzameling.Alle"/> terecht: die kijkt alleen naar de portaalassembly. Zou
    /// dat veranderen, dan valt deze pagina in de theorieën hierboven en staan ze rood — wat dan
    /// het juiste antwoord is.
    /// </remarks>
    [Route("/klant/{Slug}/contract/{ContractNummer}")]
    private sealed class PaginaMetOnbekendeParameter : ComponentBase
    {
        [Parameter]
        public string? Slug { get; set; }

        [Parameter]
        public string? ContractNummer { get; set; }
    }

    /// <summary>Een nagemaakte pagina met een routeparameter die geen string is.</summary>
    [Route("/klant/{Slug}/uren/{Maand:int}")]
    private sealed class PaginaMetIntParameter : ComponentBase
    {
        [Parameter]
        public string? Slug { get; set; }

        [Parameter]
        public int Maand { get; set; }
    }

    /// <summary>Een nagemaakte pagina waarvan elke routeparameter wordt gevuld.</summary>
    [Route("/klant/{Slug}/agents/{Agentnaam}")]
    private sealed class PaginaDieKlopt : ComponentBase
    {
        [Parameter]
        public string? Slug { get; set; }

        [Parameter]
        public string? Agentnaam { get; set; }
    }
}
