using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Options;
using Soratus.Web.Models;

namespace Soratus.Web.Services;

/// <summary>
/// Persists a lead by emailing it to the company inbox via Azure Communication
/// Services Email. Volume is too low to justify a separate datastore; the user
/// receives the lead in their regular mail flow.
/// </summary>
public sealed class LeadSink(
    IOptions<AzureEmailOptions> emailOpt,
    IOptions<CompanyOptions> co,
    ILogger<LeadSink> log)
{
    public async Task SaveAsync(Lead lead, CancellationToken ct)
    {
        var connectionString = emailOpt.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            log.LogWarning(
                "Azure Communication Email not configured. Lead dropped to log: {Name} / {Company} / {Phone}",
                lead.Name, lead.Company, lead.Phone);
            return;
        }

        var client = new EmailClient(connectionString);

        var subject = $"Nieuwe terugbelaanvraag van {lead.Name}";
        var text =
            $"""
             Naam:    {lead.Name}
             Bedrijf: {lead.Company}
             Tel:     {lead.Phone}
             Mail:    {lead.Email ?? "—"}

             Notitie:
             {lead.Note ?? "—"}
             """;

        var html =
            $"""
             <p><strong>Nieuwe terugbelaanvraag</strong></p>
             <table style="font-family: sans-serif; font-size: 14px;">
               <tr><td style="padding: 4px 12px 4px 0; color: #6b7290">Naam</td><td>{System.Net.WebUtility.HtmlEncode(lead.Name)}</td></tr>
               <tr><td style="padding: 4px 12px 4px 0; color: #6b7290">Bedrijf</td><td>{System.Net.WebUtility.HtmlEncode(lead.Company)}</td></tr>
               <tr><td style="padding: 4px 12px 4px 0; color: #6b7290">Telefoon</td><td><a href="tel:{System.Net.WebUtility.HtmlEncode(lead.Phone)}">{System.Net.WebUtility.HtmlEncode(lead.Phone)}</a></td></tr>
               <tr><td style="padding: 4px 12px 4px 0; color: #6b7290">E-mail</td><td>{(string.IsNullOrEmpty(lead.Email) ? "—" : $"<a href=\"mailto:{System.Net.WebUtility.HtmlEncode(lead.Email)}\">{System.Net.WebUtility.HtmlEncode(lead.Email)}</a>")}</td></tr>
             </table>
             <p style="color: #6b7290">Notitie:</p>
             <p>{System.Net.WebUtility.HtmlEncode(lead.Note ?? "—")}</p>
             """;

        var message = new EmailMessage(
            senderAddress: emailOpt.Value.FromAddress,
            recipientAddress: co.Value.Email,
            content: new EmailContent(subject) { PlainText = text, Html = html });

        // Reply-To: if the lead provided an email, route replies straight to them
        if (!string.IsNullOrWhiteSpace(lead.Email))
            message.ReplyTo.Add(new EmailAddress(lead.Email));

        try
        {
            EmailSendOperation send = await client.SendAsync(WaitUntil.Started, message, ct);
            log.LogInformation("Lead email queued, operation id {Id}", send.Id);
        }
        catch (RequestFailedException ex)
        {
            log.LogError(ex, "ACS Email send failed (status {Status})", ex.Status);
            throw new InvalidOperationException("Mail send failed", ex);
        }
    }
}
