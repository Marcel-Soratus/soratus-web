using System.ComponentModel.DataAnnotations;

namespace Soratus.Support.FirstLine;

/// <summary>
/// Of de eerstelijn draait, en zo niet, waarom niet.
/// </summary>
/// <remarks>
/// <para><strong>Vier toestanden, want drie ervan vragen iets anders van een mens.</strong> "Niet
/// ingericht" vraagt een app-setting, "uitgezet" vraagt een besluit, "ontwikkelmachine" vraagt niets.
/// Zou er één waarde "niet actief" zijn, dan is een vergeten endpoint niet te onderscheiden van een
/// bewuste keuze — en dat is precies het onderscheid dat een operator wil kunnen maken als een klant
/// vraagt waarom er geen antwoord kwam.</para>
///
/// <para><strong>De volgorde van de enum en de volgorde waarin er wordt beslist zijn niet
/// dezelfde,</strong> en dat is opzet. De enum begint bij <see cref="NotConfigured"/>, want de
/// waarde van een niet-gezette enum hoort de veilige te zijn — dezelfde regel als bij
/// <c>SupportGroundKind.Unknown</c> en <c>MailOutboxState.NotConfigured</c>. Beslist wordt er in de
/// volgorde die in <see cref="FirstLineOptions.State"/> staat, en daar staat waarom.</para>
/// </remarks>
public enum FirstLineState
{
    /// <summary>Er staat geen endpoint of geen deployment. Er is niets om aan te roepen.</summary>
    NotConfigured,

    /// <summary>Ingericht, maar uitgezet. De standaardstand.</summary>
    TurnedOff,

    /// <summary>Een ontwikkelmachine. Hier draait de eerstelijn nooit; zie de opmerking.</summary>
    DevelopmentMachine,

    /// <summary>Aangesloten: een vraag van een klant wordt aan het model voorgelegd.</summary>
    Ready,
}

/// <summary>
/// De instellingen van de AI-eerstelijn (§3.8).
/// </summary>
/// <remarks>
/// <para><strong>Er staat geen sleutel in dit type, en er kan er geen bij.</strong> Er is geen
/// <c>ApiKey</c>, geen <c>Secret</c>, geen <c>ConnectionString</c>. De aanroep gaat met de managed
/// identity van het portaal (<c>DefaultAzureCredential</c>, in productie
/// <c>id-soratus-portal</c>) — dezelfde identiteit die Cosmos en de Communication Service al
/// gebruiken. Dat is niet alleen hygiëne: op de Cosmos-accounts van dit portaal is local auth uit,
/// dus er kán daar geen sleutel bestaan, en een tweede authenticatievorm in hetzelfde proces zou de
/// enige zijn die een mens moet roteren.</para>
///
/// <para>De marketingsite (<c>Soratus.Web</c>) gebruikt vandaag hetzelfde Azure OpenAI-account
/// <em>met</em> een api-key in configuratie. Dat pad is met opzet niet gekopieerd; zie §47.5 van de
/// fase-0-afwijkingen, waar dat als bevinding staat met wat er in Azure voor nodig is.</para>
/// </remarks>
public sealed class FirstLineOptions
{
    /// <summary>De sectienaam in configuratie.</summary>
    /// <remarks>
    /// In de stijl van <c>PortalData</c>, <c>PortalMail</c>, <c>PortalCosts</c> en
    /// <c>PortalAlerts</c>, zodat alle app-settings van dit portaal met hetzelfde woord beginnen.
    /// </remarks>
    public const string SectionName = "PortalFirstLine";

    /// <summary>
    /// Of de eerstelijn een vraag aan het taalmodel mag voorleggen.
    /// </summary>
    /// <remarks>
    /// <para><strong>De standaard is <c>false</c>, en dat is de belangrijkste regel in dit
    /// bestand.</strong> Dezelfde vorm en dezelfde reden als <c>PortalMailOptions.DryRun</c>, die
    /// standaard <c>true</c> staat: een aanroep aan een taalmodel kost geld en gaat naar een externe
    /// dienst, en de onveilige stand hoort iets te zijn dat iemand aanzet en niet iets dat je vergeet
    /// uit te zetten.</para>
    ///
    /// <para><strong>Het verschil met de proefdraaimodus van de mail is de vórm van de
    /// schakelaar.</strong> Daar staat er een laag die het bericht wél opmaakt en niet verstuurt, en
    /// dat is een zinnige tussenstand: je kunt zien wat er zou zijn gegaan. Hier bestaat die
    /// tussenstand niet. Een eerstelijn die is aangesloten maar niets vraagt, zou op elke vraag
    /// escaleren, en het scherm zou zeggen dat er een agent meekijkt — een storing die zich voordoet
    /// als werkende functionaliteit, en precies wat §46.9 met de ontbrekende registratie afwees.
    /// Deze vlag stuurt daarom niet het gedrag van de eerstelijn maar of hij er <em>is</em>: staat hij
    /// uit, dan wordt <c>ISupportFirstLine</c> niet geregistreerd, leest de klant dat een mens
    /// antwoordt, en is dat waar.</para>
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// Het endpoint van het Azure OpenAI-account, bijvoorbeeld
    /// <c>https://aoai-soratus-prod.openai.azure.com/</c>.
    /// </summary>
    /// <remarks>
    /// Leeg of blanco betekent afwezig en niet ongeldig — dezelfde keuze als bij
    /// <c>PortalMailOptions.FromAddress</c>, en om de reden die daar staat: een app-setting met een
    /// lege waarde bindt als <c>""</c>, en een validatie die daarop afgaat heeft dit portaal al een
    /// keer plat gelegd.
    /// </remarks>
    public string? Endpoint { get; set; }

    /// <summary>De naam van de deployment op dat account, bijvoorbeeld <c>gpt-4o-mini</c>.</summary>
    /// <remarks>
    /// <para><strong>Uit configuratie en niet als letterlijke waarde in code.</strong> §46.9 zegt
    /// het met zoveel woorden: in de supportmap staat geen modelnaam en er hoort er geen in te komen.
    /// Wat hier vastligt is vormvrij — welk model er ook onder deze naad hangt, hij kan geen getal
    /// terugsturen, want er is geen veld waarin een getal past.</para>
    ///
    /// <para>Er staat daarom ook geen standaardwaarde. Een standaardmodel zou een keuze zijn die
    /// niemand heeft gemaakt en die in de kosten van iemand anders landt.</para>
    /// </remarks>
    public string? Deployment { get; set; }

    /// <summary>De api-versie van de chat completions-aanroep.</summary>
    /// <remarks>
    /// <para>Wel een standaardwaarde, anders dan bij <see cref="Deployment"/>: dit is geen keuze over
    /// geld of gedrag maar over het aanroepcontract, en een ontbrekende api-versie levert een 400 op
    /// in plaats van een duidelijke inrichtingsfout.</para>
    ///
    /// <para><strong>Deze waarde is niet gemeten.</strong> Er is geen echte aanroep gedaan (de
    /// identiteit heeft nog geen rol op het account, zie §47.5), dus dat <c>2024-10-21</c> op
    /// <c>aoai-soratus-prod</c> werkt is een aanname. Wat wél is gemeten: de deployment
    /// <c>gpt-4o-mini</c> (model 2024-07-18, DataZoneStandard, capaciteit 50) meldt de capability
    /// <c>jsonObjectResponse: true</c> en meldt géén <c>jsonSchemaResponse</c>. Daarom vraagt
    /// <see cref="AzureOpenAiChooser"/> <c>response_format: json_object</c> en geen json-schema.
    /// </para>
    /// </remarks>
    public string ApiVersion { get; set; } = "2024-10-21";

    /// <summary>Hoe lang er op het model wordt gewacht.</summary>
    /// <remarks>
    /// <para>Aan de andere kant van deze wachttijd zit een mens die op een verzendknop heeft gedrukt
    /// en naar een pagina kijkt die aan het laden is. Twintig seconden is de grens waarna wachten
    /// duurder is dan escaleren: de vraag staat dan al in de draad (<c>SupportDesk</c> legt hem vóór
    /// deze aanroep vast), dus een tijdslimiet kost de klant een AI-antwoord en niet zijn vraag.</para>
    ///
    /// <para>Er is geen tweede poging. Vaste stelregel van dit project, en hier extra: een tweede
    /// aanroep kan een tweede bubbel opleveren, en twee antwoorden op één vraag is verwarrender dan
    /// geen antwoord.</para>
    /// </remarks>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Wat de stand van de eerstelijn is.
    /// </summary>
    /// <param name="isDevelopment">Of dit een ontwikkelomgeving is.</param>
    /// <returns>De stand.</returns>
    /// <remarks>
    /// <para><strong>Eén plek die dit besluit neemt.</strong> Dezelfde vorm en dezelfde reden als
    /// <c>PortalMailOptions.Outbox()</c>: zou de registratie dit zelf uitrekenen en een test er een
    /// eigen kopie van hebben, dan meet elke test zijn eigen versie van deze beslissing en blijft hij
    /// groen als de echte omdraait (punt 41).</para>
    ///
    /// <para><strong>De ontwikkelmachine gaat vóór alle andere redenen, en dat is een andere volgorde
    /// dan bij de mail.</strong> Daar gaat "niet ingericht" voorop omdat een omgeving zonder endpoint
    /// niet hoort te melden dat hij gaat versturen. Hier is de vraag een andere: op een
    /// ontwikkelmachine draait de eerstelijn nooit, wat er ook in de configuratie staat, dus zou
    /// "niet ingericht" of "uitgezet" hier voorop staan, dan wijst de melding een handeling aan die
    /// niets verandert. Daarna komt "niet ingericht" vóór "uitgezet", precies zoals bij de mail.
    /// </para>
    ///
    /// <para><strong>Waarom de eerstelijn niet in Development draait, en de reden is een andere dan
    /// bij de collectors.</strong> <c>AzureCostCollector</c> en <c>SprintCollector</c> staan daar uit
    /// omdat een lokale run met de identiteit van een ontwikkelaar bij Azure gaat meten of in de
    /// partitie van een echte klant gaat schrijven. Hier gebeurt geen van beide: er wordt niets
    /// weggeschreven en er wordt niets van een klant gelezen dat de ontwikkelaar niet al op zijn
    /// scherm heeft. Wat hier de reden is, is <em>geld en een externe dienst</em>. Elke keer dat
    /// iemand lokaal op de verzendknop van het supportformulier drukt, gaat er een aanroep naar
    /// <c>aoai-soratus-prod</c> uit de capaciteit van productie — en anders dan bij de collectors
    /// hangt dat niet aan een klok die je kunt vergeten, maar aan een handeling die je juist aan het
    /// uitproberen bent. Een tweede reden erbij: de vraag en de feiten verlaten dan het proces vanaf
    /// een laptop, en wat er de deur uit gaat hoort te horen bij een omgeving waarvan we weten wie
    /// erin kijkt (§47.6).</para>
    /// </remarks>
    public FirstLineState State(bool isDevelopment) =>
        isDevelopment
            ? FirstLineState.DevelopmentMachine
            : CompletionsUri() is null
                ? FirstLineState.NotConfigured
                : Enabled
                    ? FirstLineState.Ready
                    : FirstLineState.TurnedOff;

    /// <summary>
    /// Het volledige adres van de chat completions-aanroep, of <c>null</c> als het niet is ingericht.
    /// </summary>
    /// <returns>Het adres, of <c>null</c>.</returns>
    /// <remarks>
    /// <para>Eén methode die de drie voorwaarden samen neemt — endpoint, deployment, en dat het
    /// endpoint een absoluut http(s)-adres is — zodat er geen aanroeper is die er twee van
    /// controleert en de derde vergeet. Dezelfde vorm als <c>PortalMailOptions.Sender()</c>.</para>
    ///
    /// <para>De deploymentnaam wordt ge-escapet. Hij komt uit onze eigen configuratie en niet van een
    /// gebruiker, dus dit is een vangnet en geen verdediging — dezelfde tweede laag als in de
    /// sprintlane, en om dezelfde reden: hij staat er voor de dag dat deze waarde ergens anders
    /// vandaan komt.</para>
    /// </remarks>
    public Uri? CompletionsUri()
    {
        if (string.IsNullOrWhiteSpace(Endpoint) || string.IsNullOrWhiteSpace(Deployment))
        {
            return null;
        }

        if (!Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var basis)
            || (basis.Scheme != Uri.UriSchemeHttps && basis.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var relative =
            $"openai/deployments/{Uri.EscapeDataString(Deployment.Trim())}/chat/completions"
            + $"?api-version={Uri.EscapeDataString(ApiVersion?.Trim() ?? string.Empty)}";

        return Uri.TryCreate(basis, relative, out var full) ? full : null;
    }
}
