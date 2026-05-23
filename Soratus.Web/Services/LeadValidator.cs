using System.Net.Mail;
using System.Text.RegularExpressions;
using Soratus.Web.Models;

namespace Soratus.Web.Services;

/// <summary>
/// Hard validator for lead-capture tool arguments. The LLM proposes; the
/// server disposes. On failure the chat widget feeds the error back to the
/// model as a tool-result, and the model asks the visitor to correct it.
/// </summary>
public static class LeadValidator
{
    // NL phone after stripping all non-digit and leading +
    //   +31 6 12345678   → 31612345678  (11 digits, starts with 31 + 6-9)
    //   0031 6 12345678  → 31612345678  (idem)
    //   06-12345678      → 612345678    (9 digits, starts with 6-9)
    //   020-1234567      → 201234567    (9 digits)
    private static readonly Regex DigitsOnly = new(@"[^\d]", RegexOptions.Compiled);

    /// <summary>
    /// Validates the arguments and produces either a <see cref="Lead"/> ready for
    /// <c>LeadSink.SaveAsync</c>, or a human-readable error suitable for showing
    /// the LLM ("Telefoonnummer ongeldig: ...").
    /// </summary>
    public static ValidationResult Validate(LeadToolArgs args)
    {
        if (!args.Consent)
            return ValidationResult.Fail(
                "consent ontbreekt: vraag de bezoeker expliciet of Soratus eenmalig contact mag opnemen, " +
                "en roep deze tool pas opnieuw aan met consent=true.");

        var name = (args.Name ?? "").Trim();
        if (name.Length < 2 || name.Length > 80)
            return ValidationResult.Fail("naam ontbreekt of is te kort/lang (2–80 tekens).");

        if (!TryNormalizePhone(args.Phone, out var phone, out var phoneError))
            return ValidationResult.Fail($"telefoon ongeldig: {phoneError}");

        string? email = null;
        if (!string.IsNullOrWhiteSpace(args.Email))
        {
            var raw = args.Email.Trim();
            if (!MailAddress.TryCreate(raw, out var parsed) || !raw.Contains('.'))
                return ValidationResult.Fail($"e-mail ongeldig: '{raw}'.");
            email = parsed!.Address;
        }

        var company = string.IsNullOrWhiteSpace(args.Company)
            ? ""
            : args.Company.Trim().Length > 120
                ? args.Company.Trim()[..120]
                : args.Company.Trim();

        var note = string.IsNullOrWhiteSpace(args.Note)
            ? null
            : SanitizeNote(args.Note);

        var lead = new Lead(
            Name: name,
            Company: company,
            Phone: phone,
            Email: email,
            Note: note,
            Consent: true);

        return ValidationResult.Ok(lead);
    }

    /// <summary>
    /// Normalize to <c>+31XXXXXXXXX</c>. Accepts +31, 0031, or leading 0 forms,
    /// with arbitrary whitespace / dashes / dots / parens.
    /// </summary>
    public static bool TryNormalizePhone(string? raw, out string normalized, out string error)
    {
        normalized = "";
        error = "";

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "leeg";
            return false;
        }

        var digits = DigitsOnly.Replace(raw, "");

        if (digits.StartsWith("0031")) digits = digits[2..];           // 0031… → 31…
        else if (digits.StartsWith('0')) digits = "31" + digits[1..];   // 0612… → 31612…
        // already 31… → leave

        // Now we expect exactly 11 digits starting with 31 + a leading 1–9 area/mobile.
        if (digits.Length != 11 || !digits.StartsWith("31") || digits[2] is < '1' or > '9')
        {
            error = $"'{raw}' is geen geldig Nederlands nummer";
            return false;
        }

        normalized = "+" + digits;
        return true;
    }

    private static string SanitizeNote(string s)
    {
        // Strip control chars except newline, cap length.
        var clean = new string(s.Where(c => c == '\n' || !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > 500 ? clean[..500] : clean;
    }

    public sealed record ValidationResult(bool IsValid, Lead? Lead, string? Error)
    {
        public static ValidationResult Ok(Lead lead) => new(true, lead, null);
        public static ValidationResult Fail(string error) => new(false, null, error);
    }
}
