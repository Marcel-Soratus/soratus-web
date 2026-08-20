using System.Globalization;
using System.Text;

namespace Soratus.Mcp.Uren;

/// <summary>
/// Maakt van een uitkomst de tekst die de aanroeper terugkrijgt.
/// </summary>
/// <remarks>
/// <para>Claude Code toont deze tekst aan een mens, en die mens gaat op grond daarvan wel of niet
/// nog iets doen. Daarom staat in elke geslaagde melding twee dingen: wat er is vastgelegd, én dat
/// het nog gefiatteerd moet worden. Zonder dat tweede denkt de boeker dat hij klaar is, en dan
/// blijven de uren onopgemerkt op <c>pending</c> staan tot iemand zich afvraagt waarom de factuur te
/// laag is.</para>
///
/// <para>Om dezelfde reden staat de tekst hier en niet verspreid door de tool: deze klasse is te
/// testen, en er staat een test op die de woorden nakijkt. "Geboekt" mag, "verwerkt" en "goedgekeurd"
/// niet — die zeggen iets wat niet waar is.</para>
/// </remarks>
internal static class BookingReport
{
    /// <summary>De uren-URL van een klant in het portaal, voor de verwijzing naar het fiatteren.</summary>
    private const string CustomerHoursPath = "klant/{0}/uren";

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>
    /// Schrijft de melding voor deze uitkomst.
    /// </summary>
    /// <param name="outcome">Wat er is gebeurd.</param>
    /// <param name="portal">De basis-URL van het portaal, voor de verwijzing.</param>
    /// <returns>De melding en of dit een fout is voor de aanroeper.</returns>
    /// <exception cref="ArgumentNullException">Een verplichte parameter is <c>null</c>.</exception>
    public static (string Text, bool IsError) Write(BookingOutcome outcome, Uri portal)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(portal);

        return outcome switch
        {
            BookingOutcome.Booked booked => (Booked(booked.Entry, portal), false),
            BookingOutcome.DryRun dry => (DryRun(dry.Request), false),
            BookingOutcome.Refused refused => (Refused(refused), true),
            BookingOutcome.Unavailable unavailable => (Unavailable(unavailable, portal), true),
            BookingOutcome.Suspect suspect => (Suspect(suspect, portal), true),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Onbekende uitkomst."),
        };
    }

    private static string Booked(HourBookingResponse entry, Uri portal)
    {
        var text = new StringBuilder();

        text.AppendLine("Vastgelegd als TE FIATTEREN. Nog niet meegeteld.");
        text.AppendLine();
        Field(text, "klant", entry.CustomerId);
        Field(text, "maand", entry.Month);
        Field(text, "uren", entry.Hours is { } hours ? Hours(hours) : null);
        Field(text, "categorie", entry.Category);
        Field(text, "omschrijving", entry.Note);
        Field(text, "bron", entry.Source);
        Field(text, "geboekt door", entry.BookedBy);
        Field(text, "status", "te fiatteren (pending)");
        Field(text, "regel", entry.Id);
        text.AppendLine();
        text.AppendLine(
            "Deze regel telt NIET mee in het maandtotaal en NIET in de facturatie. Het maandtotaal is " +
            "de som van de gefiatteerde regels; een operator van Soratus moet deze regel in het portaal " +
            "eerst fiatteren. Zeg dat tegen degene voor wie je boekt — de boeking is hiermee niet af.");

        if (entry.CustomerId is { Length: > 0 } customer)
        {
            text.AppendLine();
            text.Append("Fiatteren: ").Append(HoursUrl(portal, customer, entry.Month));
        }

        return text.ToString().TrimEnd();
    }

    private static string DryRun(HourBookingRequest request)
    {
        var text = new StringBuilder();

        text.AppendLine("PROEFDRAAI — er is NIETS geboekt en er is geen verzoek naar het portaal gestuurd.");
        text.AppendLine();
        text.AppendLine("Dit zou zijn verstuurd:");
        Field(text, "klant", request.CustomerId);
        Field(text, "maand", request.Month);
        Field(text, "uren", Hours(request.Hours));
        Field(text, "categorie", request.Category);
        Field(text, "omschrijving", request.Note);
        text.AppendLine();
        text.Append(
            $"Zet {UrenConfiguration.DryRunKey} op false om echt te boeken. Ook dan landt de regel als " +
            "te fiatteren.");

        return text.ToString();
    }

    private static string Refused(BookingOutcome.Refused refused)
    {
        var text = new StringBuilder();

        text.AppendLine(refused.Sent
            ? "NIET geboekt. Het portaal heeft de boeking geweigerd en niets vastgelegd."
            : "NIET geboekt. De boeking is hier afgewezen; er is niets naar het portaal gestuurd.");
        text.AppendLine();

        foreach (string reason in refused.Reasons)
        {
            text.Append("- ").AppendLine(reason);
        }

        text.AppendLine();
        text.Append("Herstel wat er misgaat en boek opnieuw. Er is niets achtergebleven om op te ruimen.");

        return text.ToString();
    }

    private static string Unavailable(BookingOutcome.Unavailable unavailable, Uri portal)
    {
        var text = new StringBuilder();

        text.AppendLine(unavailable.MayHaveLanded
            ? "ONBEKEND of er geboekt is. Ga er niet van uit dat het is gelukt, en ook niet dat het is mislukt."
            : "NIET geboekt. Er is niets vastgelegd.");
        text.AppendLine();
        text.AppendLine(unavailable.Reason);

        if (unavailable.MayHaveLanded)
        {
            text.AppendLine();
            text.AppendLine(
                "Kijk in het portaal of de regel er staat vóór je het opnieuw probeert. Deze koppeling " +
                "kent geen idempotentiesleutel, dus een tweede poging levert een tweede regel op — die " +
                "landt wel als te fiatteren, dus hij komt niet ongezien op een factuur, maar iemand moet " +
                "hem afwijzen.");
            text.Append("Nakijken: ").Append(portal);
        }

        return text.ToString().TrimEnd();
    }

    private static string Suspect(BookingOutcome.Suspect suspect, Uri portal)
    {
        var text = new StringBuilder();

        text.AppendLine("LET OP — het portaal gaf een antwoord dat niet aan de vaste regel voldoet.");
        text.AppendLine();
        text.AppendLine(suspect.Reason);
        text.AppendLine();
        Field(text, "klant", suspect.Entry.CustomerId);
        Field(text, "maand", suspect.Entry.Month);
        Field(text, "uren", suspect.Entry.Hours is { } hours ? Hours(hours) : null);
        Field(text, "status", suspect.Entry.Status ?? "(geen)");
        Field(text, "bron", suspect.Entry.Source ?? "(geen)");
        Field(text, "regel", suspect.Entry.Id);
        text.AppendLine();
        text.Append("Nakijken: ").Append(HoursUrl(portal, suspect.Entry.CustomerId, suspect.Entry.Month));

        return text.ToString().TrimEnd();
    }

    private static void Field(StringBuilder text, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // Een leeg veld belooft dat er ooit een waarde komt. Bij een antwoord van het portaal
            // zegt een ontbrekend veld iets anders: het portaal heeft het niet meegestuurd. Dan is
            // de regel weglaten eerlijker dan een streepje neerzetten.
            return;
        }

        text.Append("  ").Append(label.PadRight(13)).AppendLine(value);
    }

    private static string Hours(decimal hours) =>
        hours.ToString("0.##", Dutch) + " u";

    private static string HoursUrl(Uri portal, string? customer, string? month)
    {
        if (string.IsNullOrWhiteSpace(customer))
        {
            return portal.ToString();
        }

        string path = string.Format(CultureInfo.InvariantCulture, CustomerHoursPath, customer);
        var url = new Uri(portal, path);

        return string.IsNullOrWhiteSpace(month)
            ? url.ToString()
            : $"{url}?maand={Uri.EscapeDataString(month)}";
    }
}
