using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;
using Soratus.Web.Models;

namespace Soratus.Web.Services;

public sealed class LeadSink(
    IOptions<SendGridOptions> sgOpt,
    IOptions<CompanyOptions> co,
    ILogger<LeadSink> log)
{
    public async Task SaveAsync(Lead lead, CancellationToken ct)
    {
        var apiKey = sgOpt.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            log.LogWarning("SendGrid not configured. Lead dropped to log: {Name} / {Company} / {Phone}",
                lead.Name, lead.Company, lead.Phone);
            return;
        }

        var client = new SendGridClient(apiKey);
        var msg = new SendGridMessage
        {
            From = new EmailAddress(sgOpt.Value.FromAddress, sgOpt.Value.FromName),
            Subject = $"Nieuwe terugbelaanvraag van {lead.Name}",
            PlainTextContent = $"""
                Naam:    {lead.Name}
                Bedrijf: {lead.Company}
                Tel:     {lead.Phone}
                Mail:    {lead.Email ?? "—"}

                Notitie:
                {lead.Note ?? "—"}
                """
        };
        msg.AddTo(co.Value.Email);

        var res = await client.SendEmailAsync(msg, ct);
        if (!res.IsSuccessStatusCode)
        {
            log.LogError("SendGrid failed: {Status}", res.StatusCode);
            throw new InvalidOperationException("Mail send failed");
        }
    }
}
