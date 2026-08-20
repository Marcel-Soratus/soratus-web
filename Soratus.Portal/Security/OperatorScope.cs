namespace Soratus.Portal.Security;

/// <summary>
/// Het bewijs dat de huidige gebruiker operator is en dus over alle klanten heen mag kijken.
/// </summary>
/// <remarks>
/// <para>Dezelfde constructie als <see cref="CustomerScope"/>, met dezelfde reden: de methoden die
/// over alle klanten gaan — het overzicht, de runtellingen voor de KPI-rij — nemen dit type aan, en
/// een klantgebruiker kan het per definitie niet in handen krijgen. Er is geen aanroep denkbaar
/// waarmee een klantpagina het overzicht van alle klanten opvraagt, want er is geen manier om het
/// argument te produceren.</para>
///
/// <para><strong>De operatorscope bevat een klantscope per klant.</strong> Dat volgt uit wat de rol
/// betekent — een operator mag bij elke klant — en het is bovendien wat het overzicht nodig heeft:
/// nu elke klant zijn eigen Cosmos-account krijgt, is "alle agents ophalen" een fan-out over
/// evenzoveel opslagen, en elke tak van die fan-out heeft een <see cref="CustomerScope"/> nodig met
/// de bijbehorende endpoint. Zo blijft er precies één plek waar scopes ontstaan, ook voor het
/// overzicht, en leest het overzicht per klant langs exact hetzelfde pad als de klantweergave
/// zelf.</para>
///
/// <para>Een klant zonder ingerichte opslag zit hier <em>niet</em> in — er valt niets te lezen. Dat
/// betekent niet dat hij van het scherm verdwijnt: de weergave vult hem aan uit
/// <see cref="ICustomerDirectory"/> en toont hem als "status onbekend". Een overzicht dat een klant
/// weglaat omdat zijn opslag stuk is, verbergt precies datgene waarvoor je het overzicht opent.
/// </para>
/// </remarks>
public sealed class OperatorScope
{
    /// <summary>
    /// Alleen <see cref="CustomerScopeResolver"/> mag scopes maken.
    /// </summary>
    internal OperatorScope(string subject, string? displayName, IReadOnlyList<CustomerScope> customers)
    {
        Subject = subject;
        DisplayName = displayName;
        Customers = customers;
    }

    /// <summary>
    /// De stabiele identificatie van de operator uit het token (<c>oid</c>), voor logregels en
    /// voor het veld "geboekt door" vanaf fase 3.
    /// </summary>
    public string Subject { get; }

    /// <summary>De naam van de operator, als het token die meestuurt.</summary>
    public string? DisplayName { get; }

    /// <summary>
    /// Een leesrecht op elke klant met een ingerichte opslag. Dit is waar het overzicht overheen
    /// fan-out.
    /// </summary>
    public IReadOnlyList<CustomerScope> Customers { get; }
}
