using Soratus.Portal.Data;

namespace Soratus.Portal.Security;

/// <summary>
/// Het bewijs dat de huidige gebruiker de gegevens van één specifieke klant mag lezen, én de
/// verbinding waarlangs dat gebeurt.
/// </summary>
/// <remarks>
/// <para>Dit type is het hart van de autorisatie. Het idee: geen enkele methode in de datalaag
/// neemt een losse <c>string customerId</c> aan — ze nemen allemaal een
/// <see cref="CustomerScope"/>. En een <see cref="CustomerScope"/> kun je niet maken. De
/// constructor is <c>internal</c>, dus buiten deze assembly bestaat hij niet, en binnen de
/// assembly is er precies één plek die hem aanroept: <see cref="CustomerScopeResolver"/>, die
/// eerst kijkt of de gebruiker er recht op heeft.</para>
///
/// <para>Het gevolg is dat autorisatie geen controle meer is maar een eigenschap van het
/// typesysteem:</para>
/// <list type="bullet">
///   <item><description>
///     Een pagina die een scope in handen heeft, kán die niet ongeautoriseerd hebben gekregen. Er
///     is geen andere herkomst.
///   </description></item>
///   <item><description>
///     Een pagina die geen scope heeft, kan de store niet eens aanroepen. De verkeerde aanroep is
///     niet fout, hij is <em>niet te schrijven</em>.
///   </description></item>
/// </list>
///
/// <para>Dat is een wezenlijk sterkere garantie dan een <c>if</c> aan het begin van elke methode,
/// want een vergeten <c>if</c> compileert en een vergeten scope niet.</para>
///
/// <para><strong>De scope draagt de opslaglocatie mee, en dat is geen bijzaak.</strong> Elke klant
/// krijgt uiteindelijk zijn eigen Cosmos-account in zijn eigen resource group. Wie een scope heeft,
/// heeft daarmee een verbinding naar precies één klantopslag en kan er niet omheen: er is geen
/// aanroep waarmee je met de scope van klant A in de opslag van klant B kijkt, want de endpoint zit
/// aan de scope vast. De klantfilter in de query's blijft er als sluitstuk, maar de echte
/// isolatiegrens is deze. In fase 0 wijzen alle klanten naar hetzelfde account; de leescode merkt
/// dat verschil niet, en dat is precies de bedoeling.</para>
///
/// <para>Let ook op wat er níet op staat: geen <c>EnvironmentDetail</c>, geen subscription, geen
/// resource group. Die staan op <see cref="OperatorCustomerScope"/>. Een klantpagina heeft de
/// velden dus niet tot zijn beschikking, in plaats van ze te hebben en te moeten verbergen.</para>
/// </remarks>
public sealed class CustomerScope
{
    /// <summary>
    /// Alleen <see cref="CustomerScopeResolver"/> mag scopes maken. Voeg hier geen publieke
    /// fabrieksmethode aan toe — dan is de hele constructie weg.
    /// </summary>
    internal CustomerScope(
        string customerId,
        string displayName,
        string? environment,
        bool isInternal,
        TelemetryLocation telemetry)
    {
        CustomerId = customerId;
        DisplayName = displayName;
        Environment = environment;
        IsInternal = isInternal;
        Telemetry = telemetry;
    }

    /// <summary>
    /// De slug van de klant, gelijk aan <c>customerId</c> in de telemetriedocumenten.
    /// </summary>
    public string CustomerId { get; }

    /// <summary>De klantnaam, voor de kop van het scherm.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Korte omgevingsaanduiding, bijvoorbeeld <c>West-Europa</c>, of <c>null</c> als die niet is
    /// ingericht. Bewust niet de volledige subscription- en resource-groepnaam.
    /// </summary>
    public string? Environment { get; }

    /// <summary>Of dit de interne beheerklant is.</summary>
    public bool IsInternal { get; }

    /// <summary>
    /// De opslag van deze klant: account-endpoint en database. Zie de opmerkingen bij dit type —
    /// dit is de isolatiegrens, niet een verbindingsdetail.
    /// </summary>
    public TelemetryLocation Telemetry { get; }
}
