using Soratus.Web.Services;

namespace Soratus.Web.Tests;

public class LeadValidatorTests
{
    // ─── Phone normalization ───────────────────────────────────────────────

    [Theory]
    [InlineData("+31612345678",   "+31612345678")]
    [InlineData("+31 6 12345678", "+31612345678")]
    [InlineData("+31-6-12345678", "+31612345678")]
    [InlineData("0031 6 12345678","+31612345678")]
    [InlineData("0612345678",     "+31612345678")]
    [InlineData("06-12345678",    "+31612345678")]
    [InlineData("06 12 34 56 78", "+31612345678")]
    [InlineData("(06) 12345678",  "+31612345678")]
    [InlineData("020-1234567",    "+31201234567")] // landline
    public void TryNormalizePhone_AcceptsValidNL(string input, string expected)
    {
        var ok = LeadValidator.TryNormalizePhone(input, out var normalized, out _);
        Assert.True(ok);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]                    // empty
    [InlineData("   ")]                 // whitespace
    [InlineData("12345")]               // too short
    [InlineData("061234567")]           // 9 digits but missing one mobile digit (we expect 10 after the 0)
    [InlineData("+44 20 7946 0958")]    // UK
    [InlineData("0012345678901")]       // bogus
    [InlineData("+310123456789")]       // 31 + leading 0 in subscriber → not a real NL number
    public void TryNormalizePhone_RejectsInvalid(string input)
    {
        var ok = LeadValidator.TryNormalizePhone(input, out _, out var error);
        Assert.False(ok);
        Assert.NotEmpty(error);
    }

    // ─── Full Validate flow ────────────────────────────────────────────────

    [Fact]
    public void Validate_HappyPath_ReturnsLead()
    {
        var args = new LeadToolArgs
        {
            Name = "Marcel de Graaf",
            Company = "Soratus B.V.",
            Phone = "06-12345678",
            Email = "marcel@soratus.com",
            Note = "Vraag over AI-agent kosten",
            Consent = true
        };

        var result = LeadValidator.Validate(args);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Lead);
        Assert.Equal("Marcel de Graaf", result.Lead!.Name);
        Assert.Equal("Soratus B.V.", result.Lead.Company);
        Assert.Equal("+31612345678", result.Lead.Phone);
        Assert.Equal("marcel@soratus.com", result.Lead.Email);
        Assert.Equal("Vraag over AI-agent kosten", result.Lead.Note);
        Assert.True(result.Lead.Consent);
    }

    [Fact]
    public void Validate_NoConsent_Fails()
    {
        var args = new LeadToolArgs
        {
            Name = "Marcel",
            Phone = "0612345678",
            Consent = false
        };

        var result = LeadValidator.Validate(args);
        Assert.False(result.IsValid);
        Assert.Contains("consent", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_NameTooShort_Fails()
    {
        var args = new LeadToolArgs
        {
            Name = "M",
            Phone = "0612345678",
            Consent = true
        };

        var result = LeadValidator.Validate(args);
        Assert.False(result.IsValid);
        Assert.Contains("naam", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_BadEmail_Fails()
    {
        var args = new LeadToolArgs
        {
            Name = "Marcel",
            Phone = "0612345678",
            Email = "not-an-email",
            Consent = true
        };

        var result = LeadValidator.Validate(args);
        Assert.False(result.IsValid);
        Assert.Contains("mail", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_OmittedEmail_OK()
    {
        var args = new LeadToolArgs
        {
            Name = "Marcel",
            Phone = "0612345678",
            Email = null,
            Consent = true
        };

        var result = LeadValidator.Validate(args);
        Assert.True(result.IsValid);
        Assert.Null(result.Lead!.Email);
    }

    [Fact]
    public void Validate_EmptyCompany_NormalizesToEmpty()
    {
        var args = new LeadToolArgs
        {
            Name = "Marcel",
            Company = "   ",
            Phone = "0612345678",
            Consent = true
        };

        var result = LeadValidator.Validate(args);
        Assert.True(result.IsValid);
        Assert.Equal("", result.Lead!.Company);
    }

    [Fact]
    public void Validate_BadPhone_FailsWithReadableError()
    {
        var args = new LeadToolArgs
        {
            Name = "Marcel",
            Phone = "asdf",
            Consent = true
        };

        var result = LeadValidator.Validate(args);
        Assert.False(result.IsValid);
        Assert.Contains("telefoon", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("asdf", result.Error);
    }

    [Fact]
    public void Validate_LongNote_GetsTruncated()
    {
        var longNote = new string('a', 800);
        var args = new LeadToolArgs
        {
            Name = "Marcel",
            Phone = "0612345678",
            Note = longNote,
            Consent = true
        };

        var result = LeadValidator.Validate(args);
        Assert.True(result.IsValid);
        Assert.Equal(500, result.Lead!.Note!.Length);
    }

    [Fact]
    public void Validate_ControlCharsInNote_Stripped()
    {
        var args = new LeadToolArgs
        {
            Name = "Marcel",
            Phone = "0612345678",
            Note = "helloworld",
            Consent = true
        };

        var result = LeadValidator.Validate(args);
        Assert.True(result.IsValid);
        Assert.Equal("helloworld", result.Lead!.Note);
    }
}
