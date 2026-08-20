using System.ComponentModel.DataAnnotations;

namespace Soratus.Portal.Data;

/// <summary>
/// De configuratiesectie <c>PortalData</c>: waar de portaaleigen gegevens staan.
/// </summary>
/// <remarks>
/// <para><strong>Dit is een andere opslag dan <see cref="PortalTelemetryOptions"/>, en dat is de
/// kern van het ontwerp.</strong> Telemetrie is klantgegevens: die staan in de opslag van de klant
/// en verhuizen mee zodra een klant zijn eigen Cosmos-account krijgt. Klanten, contracten en
/// toegangsregels zijn <em>Soratus-eigen bedrijfsdata</em>. Die staan op precies één plek, en niet
/// per klant. Zie <see cref="PortalDataLocation"/> voor de drie redenen.</para>
///
/// <para>Er staat geen sleutel in en er komt er geen. Op het account staat local auth uit; de
/// verbinding loopt via de managed identity van de app. Het verschil met de telemetrie is dat deze
/// database ook <em>geschreven</em> wordt: dat vraagt <c>Cosmos DB Built-in Data Contributor</c>,
/// gescoopt op deze database en niet op het account. Een schrijfrecht op het account zou het
/// portaal ook de telemetriecontainers laten overschrijven, en dat hoort het portaal niet te
/// kunnen.</para>
///
/// <para><c>AccountEndpoint</c> mag leeg zijn. Dan is de portaalopslag niet ingericht: het portaal
/// start gewoon, valt terug op de klantenlijst uit <c>Portal:Customers</c> en meldt bij elke
/// schrijfpoging waaróm die niet kan. Een ontbrekende endpoint is een inrichtingsfout, en een
/// inrichtingsfout die het opstarten tegenhoudt neemt <c>/healthz</c> mee en rolt de uitrol
/// terug.</para>
/// </remarks>
public sealed class PortalDataOptions
{
    /// <summary>De naam van de configuratiesectie.</summary>
    public const string SectionName = "PortalData";

    /// <summary>
    /// De Cosmos-endpoint van het Soratus-eigen account, bijvoorbeeld
    /// <c>https://cosmos-soratus-prod.documents.azure.com:443/</c>. Leeg betekent: niet ingericht.
    /// </summary>
    public string? AccountEndpoint { get; set; }

    /// <summary>
    /// De databasenaam. <c>platform</c>, en bewust niet <c>telemetry</c>: die database herhaalt
    /// zich per klant, deze niet.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PortalData:Database ontbreekt.")]
    public string Database { get; set; } = "platform";

    /// <summary>
    /// Of het portaal de klanten uit <c>Portal:Customers</c> één keer naar de opslag mag
    /// wegschrijven als die nog leeg is.
    /// </summary>
    /// <remarks>
    /// Dit is de omschakeling van fase 0 naar fase 2 en niets anders. Hij loopt precies één keer
    /// per opslag: er komt een markeerdocument te staan, en zolang dat er staat wordt de
    /// configuratielijst nooit meer geschreven. Zonder die markering zou een klant die iemand
    /// bewust heeft verwijderd bij de volgende herstart terugkomen.
    /// </remarks>
    public bool Bootstrap { get; set; } = true;

    /// <summary>
    /// Hoe vaak de klantenlijst in het geheugen wordt bijgewerkt uit de opslag.
    /// </summary>
    /// <remarks>
    /// <para>Nodig omdat de app op meer dan één instantie kan draaien: een klant die op instantie A
    /// wordt aangemaakt, bestaat voor instantie B pas na een verversing. Een schrijfactie op de
    /// eigen instantie ververst direct — daar hoeft de operator niet op te wachten.</para>
    ///
    /// <para><strong>Een minuut en niet vijf, en de reden is intrekken en niet aanmaken.</strong>
    /// Deze lijst is de autorisatiebron: zolang een instantie hem niet heeft ververst, blijft een
    /// ingetrokken toegang daar geldig. Bij een nieuwe klant is vertraging hinder, bij een
    /// ingetrokken toegang is het een gat. De hele verversing is gemeten op 6,09 RU bij zeven
    /// klanten, dus een minuut kost per instantie zo'n 9000 RU per dag — dat is geen argument om
    /// hier te bezuinigen.</para>
    ///
    /// <para>Dit blijft een <em>venster</em>, geen sluitende garantie. Wil je dat wel, dan hoort de
    /// toegangscontrole bij het aanmelden een lezing te worden in plaats van een opzoeking in het
    /// geheugen. Dat is een grotere ingreep, en hij staat als open punt in het rapport van fase 2.
    /// </para>
    /// </remarks>
    [Range(5, 3600)]
    public int RefreshSeconds { get; set; } = 60;

    /// <summary>
    /// De uitgerekende opslaglocatie, of <c>null</c> als er geen endpoint is ingericht.
    /// </summary>
    public PortalDataLocation? Location() =>
        string.IsNullOrWhiteSpace(AccountEndpoint) || string.IsNullOrWhiteSpace(Database)
            ? null
            : new PortalDataLocation(AccountEndpoint.Trim(), Database.Trim());
}

/// <summary>
/// De containernaam van de portaaleigen opslag.
/// </summary>
/// <remarks>
/// Vaste waarde en geen knop, om dezelfde reden als <see cref="CosmosContainerNames"/>: een portaal
/// dat in een andere container kijkt leest een lege database in plaats van een fout te melden.
///
/// Eén container voor klant, contract én toegang. Dat is geen zuinigheid maar de reden dat een
/// klant aanmaken atomair kan: ze delen de partitiesleutel, en een <c>TransactionalBatch</c> werkt
/// binnen één partitiesleutel. Zouden ze in drie containers staan, dan bestond er geen enkele
/// manier om ze samen te schrijven.
/// </remarks>
public static class PortalContainerNames
{
    /// <summary>
    /// Klanten, contracten en toegangsregels. Partitiesleutelpad <c>/pk</c>, waarde is de
    /// klantslug. Geen TTL — deze documenten mogen niet verlopen.
    /// </summary>
    public const string Customers = "customers";
}

/// <summary>
/// Waar de portaaleigen gegevens staan: het Soratus-account en de database daarin.
/// </summary>
/// <param name="AccountEndpoint">De endpoint van het Cosmos-account van Soratus.</param>
/// <param name="Database">De databasenaam, <c>platform</c>.</param>
/// <remarks>
/// <para>Een eigen type naast <see cref="TelemetryLocation"/>, en niet hetzelfde type met een
/// andere waarde erin. Het verschil tussen deze twee opslagen is een autorisatiegrens, en die hoort
/// in het typesysteem te zitten: er is geen aanroep waarmee je met een telemetrielocatie in de
/// portaaldata schrijft of omgekeerd, want de parameter past niet.</para>
///
/// <para><strong>Waarom deze gegevens niet in de klantopslag staan.</strong> Drie redenen, in
/// volgorde van gewicht:</para>
/// <list type="number">
///   <item><description>
///     In een klantomgeving heeft de agent-identiteit (<c>id-{k}-agents</c>) schrijfrecht op het
///     Cosmos-account van die klant. De toegangslijst is de autorisatiebron van het portaal. Wie
///     daar een toegangsdocument kan bijschrijven, verleent zichzélf leesrecht. Dat is geen lek
///     maar een rechtenverhoging, en hij zou niet als storing zichtbaar zijn maar als werkende
///     functionaliteit. <strong>Verplaats deze gegevens dus niet "naar waar ze horen".</strong>
///   </description></item>
///   <item><description>
///     De klantenlijst is nodig om élke pagina te renderen, ook het overzicht. Fase 0 heeft
///     vastgelegd dat een onbereikbare klantopslag zichtbaar blijft als "status onbekend" in
///     plaats van te verdwijnen. Staat de registratie zelf in die opslag, dan verdwijnt de klant
///     — en bij een koude start kent het portaal niemand.
///   </description></item>
///   <item><description>
///     Het is Soratus-eigen bedrijfsdata: uurtarief, marge, opslagpercentage. Er is geen scenario
///     waarin een klant dit uit zijn eigen account hoort te kunnen lezen, en wel één waarin dat
///     per ongeluk gebeurt.
///   </description></item>
/// </list>
/// </remarks>
public sealed record PortalDataLocation(string AccountEndpoint, string Database)
{
    /// <summary>De sleutel waarop clients en containercontroles worden gecachet.</summary>
    internal string CacheKey => $"{AccountEndpoint}|{Database}";
}
