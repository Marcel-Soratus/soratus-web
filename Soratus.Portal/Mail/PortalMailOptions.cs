using System.ComponentModel.DataAnnotations;

namespace Soratus.Portal.Mail;

/// <summary>
/// De configuratiesectie <c>PortalMail</c>: waarlangs het maandoverzicht de deur uit gaat.
/// </summary>
/// <remarks>
/// <para><strong>Er staat geen connection string in en er komt er geen.</strong> De marketingsite
/// verstuurt met <c>AzureEmail__ConnectionString</c> als platte app-setting op
/// <c>app-soratus-prod</c>; dat pad is bewezen en blijft staan (zie
/// <c>Soratus.Web/Services/LeadSink.cs</c>). Voor dit portaal is dat de verkeerde vorm. Een
/// connection string voor Azure Communication Services is een sleutel voor het hele
/// communicatieaccount, hij staat in een app-setting die iedereen met <c>Website/Read</c> kan
/// lezen, en hij is niet te roteren zonder de site plat te leggen. Het portaal heeft een managed
/// identity en die kan hetzelfde met minder.</para>
///
/// <para><strong>Waarom een custom role en niet <c>Contributor</c>.</strong> Mail versturen met een
/// identity vraagt <c>Microsoft.Communication/CommunicationServices/Read</c> en <c>.../Write</c>.
/// Dat zijn control-plane-acties, en Microsofts eigen voorbeeld noemt daarvoor <c>Contributor</c>.
/// Die rol geeft er <c>ListKeys/action</c> bij — dus het recht om de connection string op te halen —
/// en <c>Delete</c>. Dan heb je een identity die machtiger is dan het geheim dat je met de identity
/// wilde vermijden. Gemeten in de resource provider; zie <c>docs/agent-portal/fase-4-haalbaarheid.md</c>
/// §3. Het <c>az</c>-blok voor de custom role staat in
/// <c>docs/agent-portal/fase-0-afwijkingen.md</c>, punt 29.</para>
/// </remarks>
public sealed class PortalMailOptions
{
    /// <summary>De naam van de configuratiesectie.</summary>
    public const string SectionName = "PortalMail";

    /// <summary>
    /// De endpoint van het Communication Services-account, bijvoorbeeld
    /// <c>https://acs-soratus-prod.europe.communication.azure.com/</c>. Leeg betekent: niet ingericht.
    /// </summary>
    /// <remarks>
    /// Mag leeg blijven, net als <see cref="Data.PortalDataOptions.AccountEndpoint"/> en om dezelfde
    /// reden: een ontbrekende endpoint is een inrichtingsfout, en een inrichtingsfout die het
    /// opstarten tegenhoudt neemt <c>/healthz</c> mee en rolt de uitrol terug. Wat er in plaats
    /// daarvan gebeurt: het scherm zegt dat mailen niet is ingericht, en er wordt niets verstuurd én
    /// niets vastgelegd.
    /// </remarks>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Het afzenderadres. Vandaag is er precies één geverifieerd adres: <c>DoNotReply@soratus.com</c>.
    /// </summary>
    /// <remarks>
    /// <para>Gemeten op <c>acs-email-soratus-prod/soratus.com</c>: Domain, SPF, DKIM en DKIM2 staan
    /// op <c>Verified</c> en er is één <c>SenderUsername</c>. Een tweede adres — bijvoorbeeld
    /// <c>facturatie@soratus.com</c> — kan pas ná een quotaverhoging worden toegevoegd; de knop
    /// staat in de portal uit zolang het sendlimiet op de standaardwaarde staat. Dat is een
    /// supportverzoek en geen code.</para>
    ///
    /// <para>Daarom is <see cref="ReplyToAddress"/> er wél: een maandoverzicht van <c>DoNotReply</c>
    /// waar je niet op kunt antwoorden is onvriendelijk tegen de enige lezer die een vraag heeft.
    /// </para>
    /// </remarks>
    [EmailAddress(ErrorMessage = "PortalMail:FromAddress is geen e-mailadres.")]
    public string? FromAddress { get; set; }

    /// <summary>
    /// Het adres waar een antwoord van de klant heen gaat, of leeg voor geen <c>Reply-To</c>.
    /// </summary>
    /// <remarks>
    /// Dezelfde vorm als in <c>LeadSink</c>, waar het antwoord naar de lead zelf wordt gerouteerd.
    /// Hier de andere kant op: de klant antwoordt op het maandoverzicht en dat hoort bij een mens
    /// van Soratus te landen en niet in een postbus die niemand leest.
    /// </remarks>
    [EmailAddress(ErrorMessage = "PortalMail:ReplyToAddress is geen e-mailadres.")]
    public string? ReplyToAddress { get; set; }

    /// <summary>
    /// Of het portaal in proefdraaimodus staat: opmaken en tonen wat er zou worden verstuurd, en
    /// niets versturen.
    /// </summary>
    /// <remarks>
    /// <para><strong>De standaard is <c>true</c>, en dat is de belangrijkste regel in dit bestand.
    /// </strong> Een mail is niet terug te halen. De onveilige stand hoort iets te zijn dat iemand
    /// aanzet, niet iets dat je vergeet uit te zetten. Dezelfde vorm als
    /// <c>SORATUS_UREN__DROOGLOOP</c> in de MCP-server — met dit verschil dat die daar de
    /// uitzondering is en hier de standaard, omdat een urenregel te corrigeren is en een verzonden
    /// mail niet.</para>
    ///
    /// <para><strong>Deze vlag geldt voor élk doel en niet alleen voor het maandoverzicht.</strong> Hij
    /// wordt gelezen door <see cref="IMailOutbox"/> — de verzendlaag — en dus ook door de
    /// storingsmelder. Een ontwikkelmachine hoort geen enkele echte mail te versturen, en een tweede
    /// vlag per doel zou betekenen dat je er één kunt vergeten.</para>
    ///
    /// <para>In proefdraaimodus wordt er ook <em>niets vastgelegd</em>. Een verzendbevestiging is de
    /// vastlegging van een feit; een proefdraai is geen feit. Zou hij toch een document schrijven,
    /// dan staat er straks een bevestiging bij een mail die nooit is verstuurd, en dat is precies de
    /// stille onwaarheid met een tijdstempel eronder die dit portaal elders al drie keer heeft
    /// afgewezen.</para>
    /// </remarks>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Het adres van het portaal, voor de verwijzing naar de urenspecificatie in de mail.
    /// </summary>
    /// <remarks>
    /// <para>Uit configuratie en niet uit <c>NavigationManager.BaseUri</c>. Dat laatste is het adres
    /// waarop dít verzoek binnenkwam, en dat kan achter een reverse proxy, in een testomgeving of op
    /// een slotnaam van App Service iets anders zijn dan het adres waar de klant naartoe moet. Een
    /// link in een mail is niet te corrigeren nadat hij is verstuurd.</para>
    ///
    /// <para>De standaardwaarde staat er zodat het adres in een test en op een ontwikkelmachine niet
    /// leeg is. In productie hoort hij in configuratie te staan.</para>
    /// </remarks>
    public string PortalBaseUri { get; set; } = "https://portal.soratus.com";

    /// <summary>
    /// Wat er met een aangeboden bericht zou gebeuren: niets versturen, een proefdraai, of versturen.
    /// </summary>
    /// <returns>De stand van de verzendlaag.</returns>
    /// <remarks>
    /// <para><strong>Deze regel staat hier en niet in <see cref="IMailOutbox"/>, en dat is met opzet.
    /// </strong> Hij bestaat één keer, en zowel de echte verzendlaag als een testdubbel leest hem
    /// hiervandaan. Zou de dubbel zijn eigen stand bepalen, dan meet elke test op de proefdraaimodus
    /// zijn eigen kopie van deze beslissing — en dan blijft hij groen als de echte laag hem omdraait.
    /// Dat is precies het gat dat punt 41 met een mutatie vond: twee stukken code die per ongeluk
    /// hetzelfde doen, dekken elkaars afwezigheid.</para>
    ///
    /// <para>De volgorde van de twee vragen is niet vrij: "niet ingericht" gaat vóór "proefdraai". Een
    /// omgeving zonder endpoint waar iemand <c>DryRun</c> op <c>false</c> heeft gezet, hoort niet te
    /// melden dat hij gaat versturen.</para>
    /// </remarks>
    public MailOutboxState Outbox() =>
        Sender() is null
            ? MailOutboxState.NotConfigured
            : DryRun
                ? MailOutboxState.DryRun
                : MailOutboxState.Ready;

    /// <summary>
    /// De uitgerekende afzender, of <c>null</c> als mailen niet is ingericht.
    /// </summary>
    /// <returns>De afzender, of <c>null</c>.</returns>
    /// <remarks>
    /// Eén methode die de drie voorwaarden samen neemt — endpoint, afzender, en dat ze beide iets
    /// bevatten — zodat er geen aanroeper is die er twee van controleert en de derde vergeet.
    /// </remarks>
    public MailSender? Sender() =>
        string.IsNullOrWhiteSpace(Endpoint) || string.IsNullOrWhiteSpace(FromAddress)
            ? null
            : new MailSender(
                Endpoint.Trim(),
                FromAddress.Trim(),
                string.IsNullOrWhiteSpace(ReplyToAddress) ? null : ReplyToAddress.Trim());
}

/// <summary>
/// Waar een mail vandaan komt: het account, het afzenderadres en het antwoordadres.
/// </summary>
/// <param name="Endpoint">De endpoint van het Communication Services-account.</param>
/// <param name="FromAddress">Het geverifieerde afzenderadres.</param>
/// <param name="ReplyToAddress">Het antwoordadres, of <c>null</c>.</param>
/// <remarks>
/// Een eigen type en geen drie losse strings, om dezelfde reden als
/// <see cref="Data.PortalDataLocation"/>: het bestaan van dit object is het bewijs dat mailen is
/// ingericht. Er is geen aanroep waarmee je verstuurt zonder dat die controle is gedaan, want de
/// parameter is er dan niet.
/// </remarks>
public sealed record MailSender(string Endpoint, string FromAddress, string? ReplyToAddress);
