using System.Text.Json.Serialization;

namespace Soratus.Web.Services;

/// <summary>
/// Tool/function definitions advertised to the LLM, plus the strongly-typed
/// shapes their arguments deserialize into.
/// </summary>
public static class ChatTools
{
    /// <summary>
    /// The single lead-capture tool. The schema is intentionally minimal —
    /// the model decides when it has enough; the server validates hard.
    /// </summary>
    public static readonly object SubmitLead = new
    {
        type = "function",
        function = new
        {
            name = "submit_lead",
            description =
                "Sla de contactgegevens van de bezoeker op en stuur Soratus een terugbel-aanvraag. " +
                "Roep dit pas aan nadat de bezoeker expliciet bevestigd heeft dat de gegevens kloppen " +
                "EN akkoord is met eenmalig contact (consent=true). Hallucineer nooit ontbrekende velden.",
            parameters = new
            {
                type = "object",
                required = new[] { "name", "phone", "consent" },
                properties = new
                {
                    name = new
                    {
                        type = "string",
                        description = "Volledige naam zoals de bezoeker hem noemt."
                    },
                    company = new
                    {
                        type = "string",
                        description = "Bedrijfsnaam. Leeg laten als particulier."
                    },
                    phone = new
                    {
                        type = "string",
                        description =
                            "Nederlands telefoonnummer. Geef het door zoals de bezoeker het noemt; " +
                            "de server normaliseert naar +31… formaat."
                    },
                    email = new
                    {
                        type = "string",
                        description = "Optioneel. Een geldig e-mailadres."
                    },
                    note = new
                    {
                        type = "string",
                        description = "Wat de bezoeker wil bespreken, in 1–2 zinnen, in eigen woorden."
                    },
                    consent = new
                    {
                        type = "boolean",
                        description =
                            "True ALLEEN als de bezoeker expliciet ja heeft gezegd op eenmalig telefonisch contact."
                    }
                }
            }
        }
    };

    /// <summary>The full toolset, in the shape OpenAI expects on the request body.</summary>
    public static readonly IReadOnlyList<object> All = new[] { SubmitLead };
}

/// <summary>
/// Shape of the JSON arguments emitted by the LLM when calling <c>submit_lead</c>.
/// Fields are nullable because the LLM may omit optional ones.
/// </summary>
public sealed class LeadToolArgs
{
    [JsonPropertyName("name")]    public string? Name { get; set; }
    [JsonPropertyName("company")] public string? Company { get; set; }
    [JsonPropertyName("phone")]   public string? Phone { get; set; }
    [JsonPropertyName("email")]   public string? Email { get; set; }
    [JsonPropertyName("note")]    public string? Note { get; set; }
    [JsonPropertyName("consent")] public bool Consent { get; set; }
}
