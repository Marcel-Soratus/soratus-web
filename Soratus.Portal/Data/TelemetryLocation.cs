namespace Soratus.Portal.Data;

/// <summary>
/// Waar de telemetrie van één klant staat: een Cosmos-account en een database daarin.
/// </summary>
/// <param name="AccountEndpoint">
/// De endpoint van het Cosmos-account, bijvoorbeeld
/// <c>https://cosmos-soratus-prod.documents.azure.com:443/</c>.
/// </param>
/// <param name="Database">De naam van de database, doorgaans <c>telemetry</c>.</param>
/// <remarks>
/// <para>Dit type bestaat omdat elke klant in de eindsituatie zijn <em>eigen</em> Cosmos-account
/// krijgt, in zijn eigen resource group. Dat is de echte isolatiegrens: een verkeerd geraden
/// klant-slug komt niet bij de gegevens van een ander, niet omdat er een filter overheen zit maar
/// omdat de verbinding er niet is. Een <c>WHERE customerId = ...</c> beschermt tegen een fout in de
/// query; een aparte opslag beschermt ook tegen een fout in de query.</para>
///
/// <para>In fase 0 bestaat er één account en wijzen alle klanten daarheen. De leescode weet dat
/// niet en hoeft er niets van te weten: hij krijgt een locatie mee via
/// <c>CustomerScope.Telemetry</c> en gebruikt die. Als er straks vijf accounts zijn, verandert er
/// alleen configuratie.</para>
///
/// <para>Er staat geen sleutel in, en die komt er ook niet. Op de accounts staat local auth uit;
/// verbinden gebeurt uitsluitend met de managed identity van de app en een data-plane
/// roltoewijzing.</para>
/// </remarks>
public sealed record TelemetryLocation(string AccountEndpoint, string Database)
{
    /// <summary>
    /// De sleutel waarop clients en containercontroles worden gecachet.
    /// </summary>
    internal string CacheKey => $"{AccountEndpoint}|{Database}";
}
