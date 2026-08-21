using System.Net;
using System.Text;
using Soratus.Portal.Data;

namespace Soratus.Portal.Mail;

/// <summary>
/// Aan wie het maandoverzicht gaat.
/// </summary>
/// <param name="Recipients">
/// De e-mailadressen, genormaliseerd naar kleine letters en op alfabet. Nooit leeg — een adressering
/// zonder ontvanger bestaat niet, dat is een weigering.
/// </param>
/// <param name="ContactName">De naam van de contactpersoon voor de aanhef, of <c>null</c>.</param>
/// <remarks>
/// <para><strong>Een eigen type en niet twee losse strings op de opmaakfunctie.</strong> Het bestaan
/// van dit object is het bewijs dat er een geldige ontvanger is; er is geen aanroep waarmee je een
/// mail opmaakt zonder dat de adrescontrole is gedaan, want de parameter is er dan niet. Dezelfde
/// vorm als <see cref="MailSender"/> en als <see cref="Data.PortalDataLocation"/>.</para>
/// </remarks>
public sealed record StatementAddressing(IReadOnlyList<string> Recipients, string? ContactName);

/// <summary>
/// Het maandoverzicht zoals het de deur uit gaat: onderwerp, ontvangers, en beide lichamen.
/// </summary>
/// <remarks>
/// <para><strong>Dit type is de enige vorm waarin tekst naar een klant kan gaan, en hij is alleen
/// door <see cref="StatementMailComposer"/> te maken.</strong> De constructor is <c>internal</c> en
/// er is geen tweede fabriek. Dat is de reden dat "wat kan er in deze mail sluipen" een
/// beantwoordbare vraag is: er is precies één plek waar de velden worden gevuld.</para>
///
/// <para><strong>Een eigen type naast <see cref="OutgoingMail"/> en niet die basis zelf.</strong> De
/// verzendlaag neemt de basis aan, want versturen is voor elk doel hetzelfde. Wat níet hetzelfde is,
/// is wie het leest: op dít type staat een broncodetest die elke foutmelding uit de opmaak weert
/// (punten 13 en 14), en op de operatorvariant staat die met opzet niet. Zou er één type zijn, dan is
/// dat onderscheid alleen nog een afspraak — en de storingsmelding zou het klantpad kunnen nemen.
/// Dezelfde constructie en dezelfde reden als bij <c>AgentRunRow</c> in punt 14.</para>
///
/// <para><strong>Beide lichamen komen uit dezelfde gegevens en niet uit twee opmaakfuncties.</strong>
/// Een HTML-versie en een platte versie die uit elkaar lopen betekent dat de klant met
/// afbeeldingen uit een ander bedrag leest dan de klant zonder. Ze staan daarom naast elkaar in
/// dezelfde methode, met dezelfde regels in dezelfde volgorde.</para>
/// </remarks>
public sealed record StatementMail : OutgoingMail
{
    /// <summary>Alleen de opmaakfunctie maakt dit type.</summary>
    /// <param name="subject">De onderwerpregel.</param>
    /// <param name="recipients">De ontvangers.</param>
    /// <param name="plainText">Het platte lichaam.</param>
    /// <param name="html">Het HTML-lichaam.</param>
    internal StatementMail(
        string subject,
        IReadOnlyList<string> recipients,
        string plainText,
        string html)
        : base(subject, recipients, plainText, html)
    {
    }
}

/// <summary>
/// De uitkomst van het opmaken: een mail, of de reden dat er geen is.
/// </summary>
/// <remarks>
/// Eén type met twee uitkomsten en geen <c>null</c> met een reden ernaast, om dezelfde reden als bij
/// <see cref="Data.PortalWriteResult{T}"/>: een aanroeper die de reden vergeet te lezen krijgt geen
/// mail in handen.
/// </remarks>
public sealed class StatementComposition
{
    private StatementComposition(StatementMail? mail, StatementRefusal refusal)
    {
        Mail = mail;
        Refusal = refusal;
    }

    /// <summary>De mail, of <c>null</c> als er is geweigerd.</summary>
    public StatementMail? Mail { get; }

    /// <summary>Waarom er is geweigerd, of <see cref="StatementRefusal.None"/>.</summary>
    public StatementRefusal Refusal { get; }

    /// <summary>Of er een mail is opgemaakt.</summary>
    public bool IsComposed => Mail is not null;

    /// <summary>Er is een mail.</summary>
    /// <param name="mail">De mail.</param>
    /// <returns>De uitkomst.</returns>
    internal static StatementComposition Composed(StatementMail mail) =>
        new(mail, StatementRefusal.None);

    /// <summary>Er is geen mail.</summary>
    /// <param name="refusal">De reden. Nooit <see cref="StatementRefusal.None"/>.</param>
    /// <returns>De uitkomst.</returns>
    internal static StatementComposition Refused(StatementRefusal refusal) =>
        new(mail: null, refusal);
}

/// <summary>
/// Maakt het maandoverzicht op, of weigert het.
/// </summary>
/// <remarks>
/// <para><strong>Deze klasse rekent niet en leest niet.</strong> Hij neemt bedragen aan die elders
/// zijn uitgerekend, een adressering die elders is gecontroleerd, en een klantnaam. Er is geen
/// optelling, geen aftrekking en geen percentage in dit bestand — er staat een test op — want een
/// tweede plek die een totaal uitrekent is een tweede plek die het anders kan uitrekenen.</para>
///
/// <para><strong>De weigeringen staan hiér en niet bij de verzender.</strong> Dat is de belangrijkste
/// ordening van deze map: alles wat een reden is om níet te versturen wordt vastgesteld vóórdat er
/// een claim wordt geschreven en vóórdat er een netwerkverbinding wordt opgezet. Een weigering laat
/// dus geen spoor achter dat op een halve verzending lijkt.</para>
/// </remarks>
internal static class StatementMailComposer
{
    /// <summary>
    /// Maakt het maandoverzicht op.
    /// </summary>
    /// <param name="customerName">De klantnaam, voor de onderwerpregel en de kop.</param>
    /// <param name="figures">De bedragen, of <c>null</c> als er niets is gemeten.</param>
    /// <param name="addressing">Aan wie het gaat.</param>
    /// <param name="portalBaseUri">Het adres van het portaal, voor de verwijzing naar de specificatie.</param>
    /// <returns>De mail, of de reden dat er geen is.</returns>
    /// <remarks>
    /// De controles staan in de volgorde waarin ze het meest zeggen: eerst of er iets te melden is,
    /// dan of het volledig is, dan of de bedragen bekend zijn. Een operator die "de meting is niet
    /// volledig" leest, weet meer dan een operator die "een bedrag is onbekend" leest, ook al is de
    /// tweede melding op hetzelfde geval van toepassing.
    /// </remarks>
    internal static StatementComposition Compose(
        string customerName,
        MonthlyStatementFigures? figures,
        StatementAddressing addressing,
        string portalBaseUri)
    {
        ArgumentNullException.ThrowIfNull(addressing);

        if (figures is null)
        {
            return StatementComposition.Refused(StatementRefusal.NoFigures);
        }

        if (addressing.Recipients.Count == 0)
        {
            return StatementComposition.Refused(StatementRefusal.NoRecipient);
        }

        if (!figures.AmountsAreComplete)
        {
            return StatementComposition.Refused(StatementRefusal.AmountsIncomplete);
        }

        // Onbekend is niet nul. Drie bedragen moeten er zijn; wat er niet is, wordt geen streepje en
        // geen "onbekend" in de mail maar een weigering. Regel 1 van §9 van het
        // haalbaarheidsrapport, en punt 15 van de fase-0-afwijkingen.
        if (figures.AzureAmount is not { } azure
            || figures.ExtraHoursAmount is not { } extraAmount
            || figures.Total is not { } total)
        {
            return StatementComposition.Refused(StatementRefusal.AmountUnknown);
        }

        var name = MailText.OneLine(customerName, MailText.NameLimit);
        var monthLabel = HourMonths.Label(figures.Month);
        var specification = StatementText.PortalPath(portalBaseUri, figures.CustomerId, figures.Month);

        var plain = PlainText(name, monthLabel, figures, azure, extraAmount, total, addressing, specification);
        var html = Html(name, monthLabel, figures, azure, extraAmount, total, addressing, specification);

        return StatementComposition.Composed(new StatementMail(
            StatementText.Subject(customerName, figures.Month),
            addressing.Recipients,
            plain,
            html));
    }

    /// <summary>Het platte lichaam.</summary>
    private static string PlainText(
        string name,
        string monthLabel,
        MonthlyStatementFigures figures,
        decimal azure,
        decimal extraAmount,
        decimal total,
        StatementAddressing addressing,
        string specification)
    {
        var body = new StringBuilder();

        body.Append(StatementText.Greeting(addressing.ContactName)).Append("\n\n");
        body.Append($"Hierbij het maandoverzicht van {monthLabel} voor {name}.\n\n");

        body.Append($"  Azure-verbruik, door te belasten   {StatementText.Money(azure)}\n");
        body.Append($"  Uren boven bundel                  {StatementText.Money(extraAmount)}\n");
        body.Append($"  Totaal (excl. btw)                 {StatementText.Money(total)}\n\n");

        if (Usage(figures) is { } usage)
        {
            body.Append(usage).Append("\n\n");
        }

        body.Append(Specification).Append('\n');
        body.Append(specification).Append("\n\n");

        body.Append(Closing).Append("\n\n");
        body.Append(Signature).Append('\n');

        return body.ToString();
    }

    /// <summary>Het HTML-lichaam. Elke ingevoegde waarde gaat door <see cref="WebUtility.HtmlEncode(string)"/>.</summary>
    /// <remarks>
    /// De inline stijlen zijn met de hand en klein gehouden, en het zijn dezelfde grijstinten die
    /// <c>LeadSink</c> in de marketingsite gebruikt. Geen stylesheet en geen afbeeldingen: een
    /// postbus is geen browser, en de tekst hoort ook te werken als er niets wordt geladen.
    /// </remarks>
    private static string Html(
        string name,
        string monthLabel,
        MonthlyStatementFigures figures,
        decimal azure,
        decimal extraAmount,
        decimal total,
        StatementAddressing addressing,
        string specification)
    {
        var body = new StringBuilder();

        body.Append("<div style=\"font-family: sans-serif; font-size: 14px; color: #0a0d1a;\">");
        body.Append("<p>").Append(Encode(StatementText.Greeting(addressing.ContactName))).Append("</p>");
        body.Append("<p>Hierbij het maandoverzicht van ")
            .Append(Encode(monthLabel))
            .Append(" voor ")
            .Append(Encode(name))
            .Append(".</p>");

        body.Append("<table style=\"font-size: 14px; border-collapse: collapse;\">");
        body.Append(Row("Azure-verbruik, door te belasten", StatementText.Money(azure), strong: false));
        body.Append(Row("Uren boven bundel", StatementText.Money(extraAmount), strong: false));
        body.Append(Row("Totaal (excl. btw)", StatementText.Money(total), strong: true));
        body.Append("</table>");

        if (Usage(figures) is { } usage)
        {
            body.Append("<p style=\"color: #575d75;\">").Append(Encode(usage)).Append("</p>");
        }

        body.Append("<p>").Append(Encode(Specification)).Append("<br />");
        body.Append("<a href=\"").Append(Encode(specification)).Append("\">")
            .Append(Encode(specification))
            .Append("</a></p>");

        body.Append("<p>").Append(Encode(Closing)).Append("</p>");
        body.Append("<p style=\"color: #575d75;\">").Append(Encode(Signature)).Append("</p>");
        body.Append("</div>");

        return body.ToString();
    }

    /// <summary>Eén regel in de bedragentabel.</summary>
    private static string Row(string label, string amount, bool strong)
    {
        var weight = strong ? " font-weight: 600;" : string.Empty;
        var border = strong ? " border-top: 1px solid #e3e5ee;" : string.Empty;

        return "<tr><td style=\"padding: 4px 24px 4px 0; color: #575d75;"
            + border
            + "\">"
            + Encode(label)
            + "</td><td style=\"padding: 4px 0; text-align: right; font-variant-numeric: tabular-nums;"
            + weight
            + border
            + "\">"
            + Encode(amount)
            + "</td></tr>";
    }

    /// <summary>
    /// De regel over de uren tegenover de bundel, of <c>null</c> als die niet te schrijven is.
    /// </summary>
    /// <remarks>
    /// <para>Drie uitkomsten en geen twee. Is de bundel niet vastgelegd (punt 19), dan staat er geen
    /// regel in plaats van "0 uur bundel" — dat laatste zou een afspraak melden die niet bestaat.
    /// Zijn de gebruikte uren onbekend, dan ook geen regel: het bedrag is dan al bekend en de mail
    /// mag door, maar er hoort niet een getal te staan dat we niet hebben gemeten.</para>
    ///
    /// <para><strong>Er wordt hier niets opgeteld of afgetrokken.</strong> De drie getallen komen
    /// alle drie uit <see cref="MonthlyStatementFigures"/>. Zou deze regel <c>UsedHours -
    /// BundledHours</c> uitrekenen om "boven bundel" te tonen, dan bestaat er een tweede definitie
    /// van dat getal, en dan kan de zin het bedrag eronder tegenspreken.</para>
    /// </remarks>
    private static string? Usage(MonthlyStatementFigures figures)
    {
        if (figures.BundledHours is not { } bundled || figures.UsedHours is not { } used)
        {
            return null;
        }

        var over = figures.ExtraHours;

        return over is { } extra && extra > 0
            ? $"Uren deze maand: {StatementText.Hours(used)} van de {StatementText.Hours(bundled)} "
                + $"in de bundel, dus {StatementText.Hours(extra)} boven bundel."
            : $"Uren deze maand: {StatementText.Hours(used)} van de {StatementText.Hours(bundled)} "
                + "in de bundel.";
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    /// <summary>De verwijzing naar de specificatie in het portaal.</summary>
    /// <remarks>
    /// Een verwijzing en geen bijlage met de regels erin. Zie
    /// <see cref="StatementText.PortalPath(string,string,string)"/>: de omschrijving van een
    /// urenregel is vrije tekst die uit een koppeling kan komen, en die hoort achter een aanmelding
    /// te blijven waar een mens hem kan lezen en corrigeren.
    /// </remarks>
    private const string Specification =
        "De urenspecificatie achter dit bedrag staat in het portaal:";

    private const string Closing =
        "Vragen over dit overzicht? Antwoord op deze mail, dan kijken we ernaar.";

    private const string Signature = "Met vriendelijke groet, Soratus";
}
