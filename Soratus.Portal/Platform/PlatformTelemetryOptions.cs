using System.ComponentModel.DataAnnotations;

namespace Soratus.Portal.Platform;

/// <summary>
/// De configuratiesectie <c>PlatformTelemetry</c>: waar de telemetrie van het platform zélf staat.
/// </summary>
/// <remarks>
/// <para><strong>Een eigen database, en dat is een veiligheidsgrens en geen ordening.</strong> Het
/// portaal is het ding waar klanten op inloggen. Kan het in de telemetriedatabase schrijven, dan kan
/// een gecompromitteerd portaal telemetrie <em>verzinnen</em> — een agent die "alles in orde" meldt
/// terwijl hij stilstaat. Dat is een ergere eigenschap dan de zichtbaarheid die deze sectie komt
/// brengen. Vandaar dat het leesrecht op <c>telemetry</c> blijft zoals het was (Cosmos Data Reader,
/// accountbreed) en er schrijfrecht bij komt op precies deze ene database en op niets anders.</para>
///
/// <para><strong>Waarom een database en niet een container of een partitie.</strong> Cosmos schaalt
/// zijn dataplane-rollen per account, database of container en <em>nooit</em> per partitie. Een
/// gereserveerde partitie zoals <c>$portal</c> — de vorm die de dagclaim van de kostencollector en de
/// markeringen van de storingsmelder gebruiken — is dus geen rechtengrens: schrijfrecht op de
/// container <c>agents</c> in <c>telemetry</c> is schrijfrecht op de registratie van élke klantagent.
/// Zelfde argument als waarom de urenregels in <c>customers</c> bleven en waarom de MBV-telemetrie
/// een eigen account kreeg: de grens ligt waar de rol hem kan leggen.</para>
///
/// <para><strong>Deze sectie voedt beide kanten, en dat is de reden dat hij bestaat.</strong> Hij
/// zegt waar het portaal zijn eigen agents <em>naartoe schrijft</em> (via de sleutels die
/// <c>Soratus.Agents.Telemetry</c> zelf leest, zie <see cref="PlatformAgents"/>) en waar de interne
/// beheerklant ze <em>vandaan leest</em> (zie <c>CustomerDirectory</c>). Zouden dat twee
/// configuraties zijn, dan bestaat de toestand "het portaal publiceert netjes en het scherm kijkt in
/// de verkeerde database" — en dan staat er geen fout, maar een leeg overzicht. Dat is precies de
/// klasse storing die zich voordoet als werkende functionaliteit.</para>
///
/// <para>Er staat géén <c>ValidateOnStart</c> op, om dezelfde reden als bij <c>PortalDataOptions</c>,
/// <c>PortalMailOptions</c>, <c>AzureCostOptions</c> en <c>AgentAlertOptions</c>: een verkeerd
/// ingerichte telemetrie is een inrichtingsfout, en een inrichtingsfout die het opstarten tegenhoudt
/// neemt <c>/healthz</c> mee en rolt daarmee de uitrol terug.</para>
///
/// <para>Er staat geen sleutel in en die komt er niet: op het account staat local auth uit.</para>
/// </remarks>
public sealed class PlatformTelemetryOptions
{
    /// <summary>De naam van de configuratiesectie.</summary>
    public const string SectionName = "PlatformTelemetry";

    /// <summary>
    /// Of het portaal zijn eigen beheeragents publiceert.
    /// </summary>
    /// <remarks>
    /// Standaard aan, om dezelfde reden als bij <c>AzureCostOptions.Enabled</c> en
    /// <c>AgentAlertOptions.Enabled</c>: een vlag die standaard uit staat is een storing die zich
    /// voordoet als werkende functionaliteit. Wat er zonder ingerichte <see cref="AccountEndpoint"/>
    /// gebeurt is niet "uit" maar "niet ingericht", en dat is een aparte, luidruchtige toestand — zie
    /// <see cref="PlatformAgents.AddSoratusPlatformAgents"/>.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// De Cosmos-endpoint waar de telemetrie van het platform staat, of leeg voor "niet ingericht".
    /// </summary>
    /// <remarks>
    /// Vandaag hetzelfde account als de klanttelemetrie en de portaalgegevens; de grens is de
    /// database en niet het account, want daar kan de rol worden gelegd. Staat hier niets, dan
    /// publiceert het portaal zijn eigen agents niet en meldt het dat één keer bij het opstarten.
    /// </remarks>
    public string? AccountEndpoint { get; set; }

    /// <summary>De database met de telemetrie van het platform.</summary>
    /// <remarks>
    /// Uitdrukkelijk niet <c>telemetry</c> (daar staat de klanttelemetrie, waar het portaal alleen
    /// mag lezen) en niet <c>platform</c> (daar staan klanten, contracten, uren en toegang, die niet
    /// verlopen — telemetrie verloopt wel, en de bewaartermijn is een eigenschap van de container).
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PlatformTelemetry:Database ontbreekt.")]
    public string Database { get; set; } = "platform-telemetry";

    /// <summary>
    /// De slug van de klant waarop de beheeragents worden gepubliceerd.
    /// </summary>
    /// <remarks>
    /// De interne beheerklant van §4. Dit moet dezelfde slug zijn als in de klantenlijst, want dat
    /// is de klant waaronder <c>/klant/{slug}/agents</c> ze laat zien. Het staat hier als één waarde
    /// zodat <c>CustomerDirectory</c> en de publicatiekant hem uit dezelfde plek halen.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PlatformTelemetry:CustomerId ontbreekt.")]
    public string CustomerId { get; set; } = "soratus";

    /// <summary>Of er een bruikbare opslaglocatie is ingericht.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AccountEndpoint)
        && !string.IsNullOrWhiteSpace(Database)
        && !string.IsNullOrWhiteSpace(CustomerId);
}
