using System.Globalization;

namespace Soratus.Seed;

/// <summary>
/// Zet een relatieve tijdsaanduiding uit <c>telemetry.json</c> om naar een moment.
/// </summary>
/// <remarks>
/// De demodata staat niet vastgeprikt op de datum van de mockup. Zou hij dat wel doen, dan zou het
/// portaal morgen melden dat elke agent al een dag zwijgt en zou alles op <c>degraded</c> staan.
/// Daarom staat er in het bestand <c>-11m</c> in plaats van een tijdstip, en rekent dit gereedschap
/// dat bij het seeden om naar <c>nu - 11 minuten</c>, in UTC.
///
/// De vorm is <c>[+|-]{getal}{eenheid}...</c>, bijvoorbeeld <c>-8m7s</c>, <c>-1d6h</c> of
/// <c>+12d20h</c>. Eenheden zijn <c>d</c>, <c>h</c>, <c>m</c> en <c>s</c>. Het teken is verplicht:
/// zonder teken moet de lezer raden of <c>11m</c> in het verleden of in de toekomst ligt, en dat is
/// precies het soort gok waar een demoscherm op stukloopt.
/// </remarks>
internal static class RelativeMoment
{
    /// <summary>Rekent een aanduiding om naar een moment ten opzichte van <paramref name="now"/>.</summary>
    /// <param name="text">De aanduiding, bijvoorbeeld <c>-8m7s</c>.</param>
    /// <param name="now">Het moment van seeden, in UTC.</param>
    /// <param name="field">De veldnaam, alleen voor de foutmelding.</param>
    /// <returns>Het berekende moment in UTC.</returns>
    /// <exception cref="SeedManifestException">Als de aanduiding niet te lezen is.</exception>
    internal static DateTimeOffset Resolve(string text, DateTimeOffset now, string field) =>
        now + Parse(text, field);

    /// <summary>Hetzelfde, maar <c>null</c> blijft <c>null</c>.</summary>
    internal static DateTimeOffset? ResolveOptional(string? text, DateTimeOffset now, string field) =>
        string.IsNullOrWhiteSpace(text) ? null : Resolve(text, now, field);

    /// <summary>Leest de aanduiding als een getekende tijdsduur.</summary>
    internal static TimeSpan Parse(string text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new SeedManifestException($"{field}: er staat geen tijdsaanduiding.");
        }

        var span = text.AsSpan().Trim();
        int sign;

        switch (span[0])
        {
            case '-':
                sign = -1;
                break;
            case '+':
                sign = 1;
                break;
            default:
                throw new SeedManifestException(
                    $"{field}: '{text}' begint niet met + of -. Zonder teken is niet te zien of het " +
                    "moment in het verleden of in de toekomst ligt.");
        }

        span = span[1..];

        if (span.IsEmpty)
        {
            throw new SeedManifestException($"{field}: '{text}' bevat alleen een teken.");
        }

        var total = TimeSpan.Zero;
        var index = 0;

        while (index < span.Length)
        {
            var start = index;

            while (index < span.Length && char.IsAsciiDigit(span[index]))
            {
                index++;
            }

            if (start == index)
            {
                throw new SeedManifestException($"{field}: '{text}' heeft een eenheid zonder getal.");
            }

            if (!long.TryParse(span[start..index], NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
            {
                throw new SeedManifestException($"{field}: '{text}' bevat een getal dat niet past.");
            }

            if (index >= span.Length)
            {
                throw new SeedManifestException(
                    $"{field}: '{text}' eindigt op een getal zonder eenheid. Gebruik d, h, m of s.");
            }

            total += span[index] switch
            {
                'd' => TimeSpan.FromDays(amount),
                'h' => TimeSpan.FromHours(amount),
                'm' => TimeSpan.FromMinutes(amount),
                's' => TimeSpan.FromSeconds(amount),
                _ => throw new SeedManifestException(
                    $"{field}: '{text}' gebruikt eenheid '{span[index]}'. Toegestaan zijn d, h, m en s."),
            };

            index++;
        }

        return sign < 0 ? -total : total;
    }
}
