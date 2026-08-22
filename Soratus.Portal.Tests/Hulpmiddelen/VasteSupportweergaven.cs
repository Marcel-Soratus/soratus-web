using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Soratus.Portal.Security;
using Soratus.Portal.Support;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// Het gereedschap van de supporttests: een opslag, een weergavelaag en een balie met een eerstelijn
/// die je zelf kiest.
/// </summary>
/// <remarks>
/// <para><strong>De echte <see cref="SupportProjection"/> en de echte <see cref="SupportDesk"/>, op een
/// <see cref="Vasteportaalopslag"/>.</strong> Dezelfde afweging als bij <c>VasteUrenweergaven</c>: een
/// fixture die de viewmodellen zelf vult, laat elke zichtbaarheidstest groen staan omdat de fixture al
/// filterde en niet omdat de scheiding werkt.</para>
///
/// <para><strong>En de balie krijgt zijn eerstelijn uit een echte servicecontainer.</strong> Dat is
/// niet omslachtig maar het punt: <see cref="SupportDesk"/> haalt de naad met <c>GetService</c> op
/// omdat er in productie geen registratie is, en een test die die opzoeking overslaat meet de
/// toestand "niet aangesloten" niet.</para>
///
/// <para><strong>Deze bouwers staan in <c>Hulpmiddelen/</c> en niet bij de supporttests, en dat is
/// een correctie.</strong> Ze stonden eerst in <c>Support/</c>, en toen hing
/// <c>Portaalrendertest</c> — de gedeelde basis van élke zichtbaarheidstest — af van een map
/// met testgevallen. Dan kan het herschikken van de testbestanden van één onderwerp de basisklasse van
/// een ander onderwerp breken. Dezelfde plek en dezelfde reden als
/// <see cref="VasteUrenweergaven"/> en <see cref="VasteFactuurweergaven"/>.</para>
/// </remarks>
internal static class VasteSupportweergaven
{
    /// <summary>Bouwt de weergavelaag van het supportscherm op deze opslag.</summary>
    /// <param name="opslag">De opslag met de draad en het contract.</param>
    /// <param name="klanten">De klantenlijst, of <c>null</c> voor de standaardlijst.</param>
    /// <returns>De echte projectie.</returns>
    public static ISupportViews Weergaven(
        Vasteportaalopslag opslag,
        IEnumerable<CustomerRecord>? klanten = null)
    {
        ArgumentNullException.ThrowIfNull(opslag);

        return new SupportProjection(
            opslag,
            new VasteContractweergaven(opslag, klanten ?? Autorisatiebron.Standaard()),
            Weergavelaag.Klok);
    }

    /// <summary>
    /// Bouwt de balie met deze eerstelijn, of zonder.
    /// </summary>
    /// <param name="opslag">De opslag.</param>
    /// <param name="eerstelijn">De eerstelijn, of <c>null</c> voor "niet aangesloten".</param>
    /// <param name="klanten">De klantenlijst, of <c>null</c> voor de standaardlijst.</param>
    /// <returns>De echte balie.</returns>
    public static SupportDesk Balie(
        Vasteportaalopslag opslag,
        ISupportFirstLine? eerstelijn = null,
        IEnumerable<CustomerRecord>? klanten = null)
    {
        ArgumentNullException.ThrowIfNull(opslag);

        var lijst = klanten ?? Autorisatiebron.Standaard();
        var diensten = new ServiceCollection();

        if (eerstelijn is not null)
        {
            diensten.AddSingleton(eerstelijn);
        }

        // De agentlijst komt uit VastePortaalweergaven en niet uit de echte projectie op een
        // Vastetelemetriestore. Dat is geen gemakzucht: die store weigert GetAgentsAsync met een
        // NotSupportedException en zegt er zelf bij dat hij alleen het agentdetail bedient. Gemeten:
        // die uitzondering viel in het catch-blok van SupportDesk en leverde een escalatie op, dus
        // elke test over een aangenomen antwoord stond rood om een reden die niets met het onderwerp
        // te maken had.
        //
        // Wat er hierdoor niet wordt gemeten, en dat hoort erbij te staan: dat de agentgrondslagen
        // uit de echte agentprojectie komen. Dat de fabriek een klantviewmodel neemt -- en dus geen
        // omgevingsdetail kan dragen -- staat wel vast, met een reflectietest op de signatuur.
        return new SupportDesk(
            opslag,
            new VastePortaalweergaven(),
            VasteUrenweergaven.Bouw(opslag, lijst),
            VasteFactuurweergaven.Bouw(opslag, lijst),
            diensten.BuildServiceProvider(),
            Weergavelaag.Klok,
            NullLogger<SupportDesk>.Instance);
    }

    /// <summary>Een grondslag zoals het portaal hem zou bouwen, voor een test.</summary>
    /// <param name="kind">De soort.</param>
    /// <param name="key">De aanduiding.</param>
    /// <param name="fact">Het feit.</param>
    /// <returns>De grondslag.</returns>
    /// <remarks>
    /// Dit kán vanuit het testproject omdat de <c>InternalsVisibleTo</c> in
    /// <c>Soratus.Portal.csproj</c> het testproject binnen de vertrouwensgrens zet. Buiten die grens —
    /// en dus voor elke implementatie van <see cref="ISupportFirstLine"/> die niet in het portaal
    /// zelf staat — bestaat deze constructor niet. Dat is de hele grap, en het staat als restrisico in
    /// het rapport: een implementatie die wél in de portaalassembly zou komen, kan er een verzinnen.
    /// </remarks>
    public static SupportGround Grondslag(
        SupportGroundKind kind = SupportGroundKind.Hours,
        string key = "2026-07",
        string fact = "In juli 2026 staan 3 gefiatteerde uren.") =>
        new(kind, key, fact);
}

/// <summary>
/// Een eerstelijn die precies doet wat een test hem opdraagt.
/// </summary>
/// <remarks>
/// <para><strong>Geen mock met verwachtingen maar een functie.</strong> Wat er getest wordt is niet of
/// de naad wordt aangeroepen maar wat het portaal doet met wat er terugkomt — en dat is een uitkomst
/// per aanroep. De functie krijgt het verzoek mee, zodat een test kan antwoorden mét of zonder de
/// aangeboden grondslagen.</para>
/// </remarks>
/// <param name="antwoord">Wat de eerstelijn teruggeeft op een verzoek.</param>
internal sealed class Vasteeerstelijn(Func<SupportEnquiry, SupportAnswer?> antwoord) : ISupportFirstLine
{
    /// <summary>Elk verzoek dat deze eerstelijn heeft gezien, in volgorde.</summary>
    public List<SupportEnquiry> Verzoeken { get; } = [];

    /// <inheritdoc />
    public Task<SupportAnswer?> AnswerAsync(
        SupportEnquiry enquiry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enquiry);

        Verzoeken.Add(enquiry);

        return Task.FromResult(antwoord(enquiry));
    }
}

/// <summary>Een eerstelijn die stukloopt.</summary>
/// <remarks>
/// Bestaat omdat achter deze naad code hangt die wij niet hebben geschreven. Een test die alleen het
/// nette pad meet, meet de helft: de vraag van de klant hoort te blijven staan ook als de naad
/// ontploft.
/// </remarks>
internal sealed class Stukkeeerstelijn : ISupportFirstLine
{
    /// <inheritdoc />
    public Task<SupportAnswer?> AnswerAsync(
        SupportEnquiry enquiry,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "De eerstelijn kreeg HTTP 500 van /v1/messages op /src/Soratus/FirstLine.cs:88");
}
