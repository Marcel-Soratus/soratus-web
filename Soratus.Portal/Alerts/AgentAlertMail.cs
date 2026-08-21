using System.Net;
using System.Text;
using Soratus.Agents.Contracts;
using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Components.Shared;
using Soratus.Portal.Mail;

namespace Soratus.Portal.Alerts;

/// <summary>
/// Een storingsmelding zoals hij de deur uit gaat: naar Soratus en nooit naar een klant.
/// </summary>
/// <remarks>
/// <para><strong>Een eigen type naast <see cref="StatementMail"/> en niet één gedeeld type, en dat is
/// de grens.</strong> Op de opmaak van het maandoverzicht staat een broncodetest die elke foutmelding
/// weert (punten 13 en 14, en §29.4); op de opmaak hiervan staat die met opzet níet, want dit is
/// precies wat een operator wil zien. Zou er één mailtype zijn, dan is het verschil tussen die twee
/// lezers alleen nog een afspraak — en een afspraak is niet wat er tussen een stacktrace en een
/// klantpostbus hoort te staan. Dezelfde constructie en dezelfde reden als bij <c>AgentRunRow</c> in
/// punt 14.</para>
///
/// <para><strong>Alleen <see cref="AgentAlertComposer"/> maakt dit type.</strong> De constructor is
/// <c>internal</c> en er is geen tweede fabriek, en die opmaakfunctie neemt zijn ontvangers uit
/// <see cref="AgentAlertOptions"/> — dus uit configuratie. Er is geen aanroep waarmee een
/// storingsmelding een klantadres krijgt, want er is geen parameter waarin dat adres past.</para>
/// </remarks>
public sealed record AgentAlertMail : OutgoingMail
{
    /// <summary>Alleen de opmaakfunctie maakt dit type.</summary>
    /// <param name="subject">De onderwerpregel.</param>
    /// <param name="recipients">De ontvangers, uit de configuratie.</param>
    /// <param name="plainText">Het platte lichaam.</param>
    /// <param name="html">Het HTML-lichaam.</param>
    internal AgentAlertMail(
        string subject,
        IReadOnlyList<string> recipients,
        string plainText,
        string html)
        : base(subject, recipients, plainText, html)
    {
    }
}

/// <summary>
/// Maakt de storingsmelding op: één mail per host, met een blok per dienst.
/// </summary>
/// <remarks>
/// <para><strong>Deze opmaak is operator-gericht, en dat is een besluit met een bron.</strong> De
/// koppelingentabel bij §5 van de spec zegt het in één regel: <em>storingsmeldingen aan Soratus,
/// maandoverzicht aan de klant</em>. Punt 13 gaat over tekst die "zichtbaar voor een klant" is en punt
/// 14 zegt letterlijk dat de operator de typenaam op het runtabblad hoort te vinden. Beide regels
/// beschermen dus de klant en niet de tekst zelf. Hier is er geen klant, en dan is het weglaten van
/// een <c>errorType</c> of een foutmelding geen zorgvuldigheid maar het weggooien van precies de
/// informatie waarvoor de mail bestaat.</para>
///
/// <para><strong>Getallen en woorden komen uit de bestaande weergavefuncties.</strong>
/// <see cref="AgentText.SilenceWords"/>, <see cref="StatusVisuals.Label"/>,
/// <see cref="TimeFormat.Absolute"/> en <see cref="TimeFormat.Duration"/> — dezelfde die op het scherm
/// staan. Dat is de goedkoopste van twee kwaden: een operator legt de mail naast het scherm, en twee
/// opmaakdefinities laten die twee op een dag verschillen. Dezelfde afweging als bij
/// <c>StatementText</c>, dat om die reden aan de paginamap hangt.</para>
///
/// <para><strong>Er staat geen relatieve tijd in de mail.</strong> Op het scherm is "11 min geleden"
/// het juiste; in een postbus is het onwaar zodra de mail een uur ongelezen blijft. De absolute tijd
/// staat er dus, in de Nederlandse zone met de offset erbij, zoals in de tooltip op het scherm. De
/// stilte staat er als duur en niet als moment, want een duur verandert niet van betekenis.</para>
/// </remarks>
internal static class AgentAlertComposer
{
    /// <summary>
    /// Maakt de melding over één host op.
    /// </summary>
    /// <param name="group">De host met zijn diensten.</param>
    /// <param name="recipients">De ontvangers, uit de configuratie.</param>
    /// <param name="now">Het moment waarop de melding wordt opgemaakt.</param>
    /// <param name="portalBaseUri">Het adres van het portaal, voor de verwijzing per agent.</param>
    /// <param name="repeatAfter">Na hoeveel tijd deze melding zich herhaalt zolang de storing staat.</param>
    /// <returns>De opgemaakte melding.</returns>
    /// <remarks>
    /// Werpt bij een lege groep of zonder ontvanger. Dat zijn geen toestanden maar fouten in de
    /// aanroeper: de melder hoort niet op te maken wat hij niet kan versturen, en een mail zonder
    /// ontvanger bestaat niet.
    /// </remarks>
    internal static AgentAlertMail Compose(
        AgentFaultGroup group,
        IReadOnlyList<string> recipients,
        DateTimeOffset now,
        string portalBaseUri,
        TimeSpan repeatAfter)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(recipients);

        if (group.Faults.Count == 0)
        {
            throw new ArgumentException(
                "Er is een storingsmelding opgemaakt over een host zonder storingen.",
                nameof(group));
        }

        if (recipients.Count == 0)
        {
            throw new ArgumentException(
                "Er is een storingsmelding opgemaakt zonder ontvanger. Zonder ontvanger bestaat een "
                + "mail niet; de melder hoort dat vóór het opmaken vast te stellen.",
                nameof(recipients));
        }

        var customer = MailText.OneLine(group.CustomerName, MailText.NameLimit);

        return new AgentAlertMail(
            Subject(group, customer),
            recipients,
            PlainText(group, customer, now, portalBaseUri, repeatAfter),
            Html(group, customer, now, portalBaseUri, repeatAfter));
    }

    /// <summary>
    /// De onderwerpregel.
    /// </summary>
    /// <remarks>
    /// <para>De ernstigste status staat vooraan en de klantnaam erachter: een operator die zijn
    /// postbuslijst doorloopt hoort in de eerste woorden te zien wat het is. Bij meer dan één dienst
    /// staat het aantal erin en niet de namen — drie agentnamen maken de regel onleesbaar, en de namen
    /// staan in de eerste regels van het bericht.</para>
    ///
    /// <para>Geen vaste voorvoegsels als <c>[ALERT]</c>. Dat is de vorm waarop mensen een filterregel
    /// maken, en een filterregel op een storingsmelding is precies wat je niet wil.</para>
    /// </remarks>
    private static string Subject(AgentFaultGroup group, string customer)
    {
        var worst = group.Faults.Max(fault => fault.Status);
        var label = StatusVisuals.Label(worst);

        return group.Faults.Count == 1
            ? $"{label}: {group.Faults[0].AgentName} bij {customer}"
            : $"{label}: {group.Faults.Count} diensten in één host bij {customer}";
    }

    /// <summary>Het platte lichaam.</summary>
    private static string PlainText(
        AgentFaultGroup group,
        string customer,
        DateTimeOffset now,
        string portalBaseUri,
        TimeSpan repeatAfter)
    {
        var body = new StringBuilder();

        body.Append(Opening(group, customer)).Append("\n\n");

        foreach (var fault in group.Faults)
        {
            body.Append($"{StatusVisuals.Glyph(fault.Status)} {fault.AgentName}  ")
                .Append($"[{StatusVisuals.Label(fault.Status)}]\n");

            foreach (var (label, value) in Facts(fault, portalBaseUri))
            {
                body.Append($"    {label,-18}{value}\n");
            }

            body.Append('\n');
        }

        body.Append(Host(group, now)).Append("\n\n");
        body.Append(Repeat(repeatAfter)).Append('\n');

        return body.ToString();
    }

    /// <summary>Het HTML-lichaam. Elke ingevoegde waarde gaat door <see cref="WebUtility.HtmlEncode(string)"/>.</summary>
    /// <remarks>
    /// Dezelfde regels in dezelfde volgorde als het platte lichaam, uit dezelfde bron. Een operator die
    /// de platte versie leest hoort niets te missen: sommige postbussen op een telefoon tonen die.
    /// </remarks>
    private static string Html(
        AgentFaultGroup group,
        string customer,
        DateTimeOffset now,
        string portalBaseUri,
        TimeSpan repeatAfter)
    {
        var body = new StringBuilder();

        body.Append("<div style=\"font-family: sans-serif; font-size: 14px; color: #0a0d1a;\">");
        body.Append("<p>").Append(Encode(Opening(group, customer))).Append("</p>");

        foreach (var fault in group.Faults)
        {
            body.Append("<p style=\"margin-bottom: 4px;\"><strong>")
                .Append(Encode($"{StatusVisuals.Glyph(fault.Status)} {fault.AgentName}"))
                .Append("</strong> · ")
                .Append(Encode(StatusVisuals.Label(fault.Status)))
                .Append("</p>");

            body.Append("<table style=\"font-size: 13px; border-collapse: collapse;\">");

            foreach (var (label, value) in Facts(fault, portalBaseUri))
            {
                body.Append("<tr><td style=\"padding: 2px 24px 2px 0; color: #575d75; ")
                    .Append("vertical-align: top;\">")
                    .Append(Encode(label))
                    .Append("</td><td style=\"padding: 2px 0; white-space: pre-wrap; ")
                    .Append("font-family: monospace;\">")
                    .Append(Encode(value))
                    .Append("</td></tr>");
            }

            body.Append("</table>");
        }

        body.Append("<p style=\"color: #575d75;\">").Append(Encode(Host(group, now))).Append("</p>");
        body.Append("<p style=\"color: #575d75;\">").Append(Encode(Repeat(repeatAfter))).Append("</p>");
        body.Append("</div>");

        return body.ToString();
    }

    /// <summary>De eerste zin: wat er is en bij wie.</summary>
    private static string Opening(AgentFaultGroup group, string customer) =>
        group.Faults.Count == 1
            ? $"Bij {customer} staat één dienst niet goed."
            : $"Bij {customer} staan {group.Faults.Count} diensten niet goed. Ze draaien in "
                + "hetzelfde proces — hun starttijd is gelijk — dus dit is vermoedelijk één oorzaak.";

    /// <summary>
    /// De feiten per agent, in de volgorde waarin een operator ze afgaat.
    /// </summary>
    /// <remarks>
    /// <para>Eén lijst voor beide lichamen. Zou elk lichaam zijn eigen feiten samenstellen, dan kan de
    /// HTML-versie een veld tonen dat de platte versie niet heeft — en dan hangt het van de postbus af
    /// wat een operator te zien krijgt.</para>
    ///
    /// <para>Wat er niet in staat wordt weggelaten en niet als streepje getoond. Een regel "Foutmelding
    /// —" bij een agent die geen foutmelding heeft, is een regel die zegt dat er iets ontbreekt waar
    /// niets hoort te staan.</para>
    /// </remarks>
    private static IEnumerable<(string Label, string Value)> Facts(
        AgentFault fault,
        string portalBaseUri)
    {
        yield return ("Klant", $"{fault.CustomerName} ({fault.CustomerId})");
        yield return ("Type", fault.DisplayType);
        yield return ("Versie", fault.Version);
        yield return ("Zwijgt", AgentText.SilenceWords(fault.Silence));

        if (fault.LastRun is { } run)
        {
            yield return ("Laatste run", $"{run.Result} · {TimeFormat.Absolute(run.StartedAt)}");

            if (run.DurationMs is { } duration)
            {
                yield return ("Duur", TimeFormat.Duration(TimeSpan.FromMilliseconds(duration)));
            }

            yield return ("RunId", run.Id);

            if (run.RolledBack)
            {
                yield return ("Teruggedraaid", "ja");
            }

            // Hier staat de volledige typenaam en de volledige melding. Dat is punt 14 met de andere
            // lezer ervoor: "Sync.ValidationException" en "Mail.ValidationException" zijn twee
            // verschillende defecten, en de korte naam gooit juist het nuttige deel weg.
            if (run.ErrorType is { Length: > 0 } type)
            {
                yield return ("Fouttype", type);
            }

            if (run.ErrorMessage is { Length: > 0 } message)
            {
                yield return ("Foutmelding", message);
            }
        }
        else
        {
            yield return ("Laatste run", "geen afgeronde run");
        }

        yield return (
            "In het portaal",
            $"{portalBaseUri.TrimEnd('/')}/klant/{fault.CustomerId}/agents/{fault.AgentName}");
    }

    /// <summary>
    /// De regel over de host, met het diagnostische paar uit punt 42 erbij.
    /// </summary>
    /// <remarks>
    /// <para>Deze regel staat er omdat hij een vraag beantwoordt die een operator anders in twee
    /// schermen moet opzoeken: is dit proces net opnieuw gestart, of staat het er al dagen? Punt 42
    /// noemt dat het diagnostische paar — schuift <c>startedAt</c> na elke stilte op, dan wordt het
    /// proces telkens uitgeladen (op een App Service is dat de instelling Always On); blijft hij staan
    /// terwijl de hartslag stokt, dan is er iets mis met het proces zelf.</para>
    /// </remarks>
    private static string Host(AgentFaultGroup group, DateTimeOffset now) =>
        $"Het proces draait sinds {TimeFormat.Absolute(group.StartedAt)} "
        + $"({AgentText.SilenceWords(now - group.StartedAt)}). Schuift die tijd bij elke melding mee, "
        + "dan wordt het proces telkens opnieuw gestart en is dat de storing; blijft hij staan terwijl "
        + "de hartslag stokt, dan is er iets mis in het proces zelf.";

    /// <summary>De slotregel over de herhaling.</summary>
    /// <remarks>
    /// Dat er wordt herhaald hoort in de mail te staan. Zonder die regel weet een lezer niet of het
    /// stilvallen van de meldingen betekent dat de storing over is of dat de melder is gestopt — en dat
    /// is precies het verschil dat hij nodig heeft.
    /// </remarks>
    private static string Repeat(TimeSpan repeatAfter) =>
        $"Zolang dit zo blijft komt deze melding elke {AgentText.SilenceWords(repeatAfter)} terug. "
        + "Verandert de status, dan komt er meteen een nieuwe. Bij herstel volgt er geen bericht; dat "
        + "staat op het scherm.";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
