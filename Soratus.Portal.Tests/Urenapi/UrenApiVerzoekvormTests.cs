using System.Reflection;
using System.Text.Json.Serialization;
using Soratus.Portal.Api;
using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Urenapi;

/// <summary>
/// De vorm van het verzoek: vijf velden, en de vier die er nooit op mogen komen.
/// </summary>
/// <remarks>
/// <para>Deze tests bewaken geen gedrag maar een <em>type</em>, en dat is met opzet. De vaste regel uit
/// §5 — alles wat een koppeling inschiet landt als te fiatteren — is hier afgedwongen door afwezigheid:
/// er is geen veld <c>status</c>, dus er is geen waarde die de grens over kan. Dezelfde vorm als
/// <c>CustomerLogLine</c> zonder <c>extra</c> en <c>CustomerRunRow</c> zonder <c>errorType</c>.</para>
///
/// <para>Een test op een afwezig veld faalt op het moment dat iemand het toevoegt, en dat is precies
/// het moment waarop iemand er over na hoort te denken. Zonder deze test is zo'n toevoeging een regel
/// in een record die door elke review heen komt.</para>
/// </remarks>
public sealed class UrenApiVerzoekvormTests
{
    /// <summary>De vijf velden uit §5, en niets erbij.</summary>
    [Fact]
    public void HetVerzoekHeeftPreciesDeVijfVeldenUitDeSpec()
    {
        Assert.Equal(
            ["category", "cid", "hours", "month", "note"],
            Velden(typeof(HourBookingRequest)));
    }

    /// <summary>
    /// <c>status</c>, <c>by</c>, <c>source</c> en de registratietijd staan er niet op, en mogen er
    /// nooit op komen.
    /// </summary>
    /// <param name="veld">De naam die niet mag voorkomen.</param>
    /// <remarks>
    /// Vier velden en vier redenen. <c>status</c>: anders bestaat er een aanroep waarmee een koppeling
    /// zichzelf fiatteert. <c>by</c>: anders kan een aanroeper op naam van iemand anders boeken.
    /// <c>source</c>: anders kan een MCP-regel zich als portaalregel voordoen, en die telt meteen mee.
    /// <c>createdAt</c>/<c>createdBy</c>: anders is het spoor van wie wat wanneer heeft vastgelegd
    /// invoer van buiten in plaats van een vaststelling van het portaal.
    /// </remarks>
    [Theory]
    [InlineData("status")]
    [InlineData("by")]
    [InlineData("source")]
    [InlineData("createdAt")]
    [InlineData("createdBy")]
    [InlineData("date")]
    public void WatErNietOpHetVerzoekMagStaan(string veld)
    {
        Assert.DoesNotContain(veld, Velden(typeof(HourBookingRequest)));
    }

    /// <summary>
    /// Een veld dat niet op het verzoek staat wordt geweigerd en niet overgeslagen.
    /// </summary>
    /// <remarks>
    /// Het gedrag hiervan staat in <c>UrenApiBoekingTests</c>; deze test bewaakt het attribuut zelf,
    /// want zonder dat attribuut is het standaardgedrag van <c>System.Text.Json</c> dat een
    /// meegestuurde <c>"status": "approved"</c> stil wordt overgeslagen. Het verzoek slaagt dan, de
    /// regel landt goed, en de aanroeper heeft geen enkele aanwijzing dat zijn veld is weggegooid.
    /// </remarks>
    [Fact]
    public void EenOnbekendVeldWordtGeweigerd()
    {
        var attribuut = typeof(HourBookingRequest)
            .GetCustomAttribute<JsonUnmappedMemberHandlingAttribute>();

        Assert.NotNull(attribuut);
        Assert.Equal(JsonUnmappedMemberHandling.Disallow, attribuut.UnmappedMemberHandling);
    }

    /// <summary>
    /// Het schrijfpad van de koppeling heeft geen stand, geen bron en geen tweede methode.
    /// </summary>
    /// <remarks>
    /// De tegenhanger van de test hierboven, één laag dieper. Het endpoint kan de stand niet meegeven
    /// omdat er geen parameter voor is; het kan ook niet fiatteren, want er is geen methode voor. Zou
    /// hier ooit een <c>HourEntryStatus</c> of een <c>HourEntrySource</c> in een signatuur verschijnen,
    /// dan is §5 geen eigenschap meer maar een afspraak.
    /// </remarks>
    [Fact]
    public void HetSchrijfpadVanDeKoppelingKentGeenStandEnGeenBron()
    {
        var methoden = typeof(IMcpHoursWriter).GetMethods();

        Assert.Equal(["BookPendingAsync"], methoden.Select(methode => methode.Name).Order().ToArray());

        var soorten = methoden
            .SelectMany(methode => methode.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(HourEntryStatus), soorten);
        Assert.DoesNotContain(typeof(HourEntrySource), soorten);
        Assert.DoesNotContain(typeof(HourEntryStatus?), soorten);
        Assert.DoesNotContain(typeof(HourEntrySource?), soorten);
    }

    /// <summary>
    /// Het antwoord draagt wél de stand en de bron, want de MCP-server kijkt ze na.
    /// </summary>
    /// <remarks>
    /// Niet de spiegel van de test hierboven maar de aanvulling erop. Op het <em>verzoek</em> is een
    /// statusveld een gat; op het <em>antwoord</em> is het het tweede slot: geeft het portaal ooit iets
    /// anders dan <c>pending</c> terug, dan meldt de MCP-server dat als een gebroken §5 in plaats van
    /// als een geslaagde boeking.
    /// </remarks>
    [Fact]
    public void HetAntwoordDraagtDeStandEnDeBron()
    {
        var velden = Velden(typeof(HourBookingResponse));

        Assert.Contains("status", velden);
        Assert.Contains("source", velden);
        Assert.Contains("by", velden);
        Assert.Contains("createdBy", velden);
        Assert.Contains("createdAt", velden);
        Assert.Contains("id", velden);

        // En géén werkdatum: punt 20 van de afwijkingennotitie. Eén moment, één veld.
        Assert.DoesNotContain("date", velden);
    }

    private static string[] Velden(Type type) =>
    [
        .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(eigenschap =>
                eigenschap.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? eigenschap.Name)
            .Order(StringComparer.Ordinal),
    ];
}
