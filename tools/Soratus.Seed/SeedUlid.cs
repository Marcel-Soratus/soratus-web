using System.Security.Cryptography;
using System.Text;

namespace Soratus.Seed;

/// <summary>
/// Maakt ULID's in dezelfde vorm als de telemetriebibliotheek: 26 tekens Crockford base32, 48 bit
/// tijd plus 80 bit staart.
/// </summary>
/// <remarks>
/// Eén verschil met de bibliotheek, en dat is opzet: de staart komt niet uit een toevalsgenerator
/// maar uit een hash van de agentnaam en de plek van de regel in het bestand. Twee keer seeden met
/// hetzelfde bestand levert daardoor dezelfde staart op, en de sleutel verandert alleen als het
/// tijdstip verandert. Een lezer van de database ziet daar niets van — de vorm is identiek — maar
/// het maakt het gedrag van dit gereedschap voorspelbaar.
///
/// De sleutels blijven strikt oplopend in tijd, want de eerste tien tekens zijn nog steeds het
/// tijdstip. Dat is de eigenschap waar de live tail in het portaal op leunt.
/// </remarks>
internal static class SeedUlid
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Maakt een ULID voor dit moment en deze plek in het bestand.</summary>
    /// <param name="timestamp">Het moment van de logregel.</param>
    /// <param name="seed">Iets dat deze regel uniek maakt, bijvoorbeeld <c>agentnaam|17</c>.</param>
    /// <returns>Een ULID van 26 tekens.</returns>
    internal static string Create(DateTimeOffset timestamp, string seed)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(seed), hash);

        return Encode(timestamp.ToUnixTimeMilliseconds(), hash[..10]);
    }

    private static string Encode(long milliseconds, ReadOnlySpan<byte> tail)
    {
        Span<char> buffer = stackalloc char[26];

        for (int i = 9; i >= 0; i--)
        {
            buffer[i] = Alphabet[(int)(milliseconds & 0x1F)];
            milliseconds >>= 5;
        }

        ulong high = 0;
        for (int i = 0; i < 5; i++)
        {
            high = (high << 8) | tail[i];
        }

        ulong low = 0;
        for (int i = 5; i < 10; i++)
        {
            low = (low << 8) | tail[i];
        }

        for (int i = 7; i >= 0; i--)
        {
            buffer[10 + i] = Alphabet[(int)(high & 0x1F)];
            high >>= 5;
        }

        for (int i = 7; i >= 0; i--)
        {
            buffer[18 + i] = Alphabet[(int)(low & 0x1F)];
            low >>= 5;
        }

        return new string(buffer);
    }
}
