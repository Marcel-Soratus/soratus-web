using System.Security.Cryptography;

namespace Soratus.Agents.Telemetry.Internal;

/// <summary>
/// Genereert ULID's: 26 tekens Crockford base32, 48 bit tijd plus 80 bit toeval.
/// </summary>
/// <remarks>
/// Zelf geschreven en niet uit een pakket, omdat het twintig regels is en het contract alleen
/// vraagt om een sleutel die stabiel en oplopend in tijd is. Binnen dezelfde milliseconde
/// loopt het toevalsdeel met één op, zodat de sleutels ook dan strikt oplopen — anders zou de
/// live tail in het portaal regels uit dezelfde milliseconde willekeurig door elkaar zetten.
/// </remarks>
internal static class UlidGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private static readonly object Gate = new();
    private static long _lastTimestamp = -1;
    private static readonly byte[] LastRandomness = new byte[10];

    /// <summary>Maakt een nieuwe ULID voor het opgegeven tijdstip.</summary>
    internal static string NewUlid(DateTimeOffset timestamp)
    {
        long ms = timestamp.ToUnixTimeMilliseconds();
        Span<byte> randomness = stackalloc byte[10];

        lock (Gate)
        {
            if (ms == _lastTimestamp)
            {
                // Zelfde milliseconde: het toevalsdeel als big-endian getal met één verhogen.
                for (int i = 9; i >= 0; i--)
                {
                    if (++LastRandomness[i] != 0)
                    {
                        break;
                    }
                }
            }
            else if (ms < _lastTimestamp)
            {
                // De klok liep terug. Blijf op de laatste tijd zitten zodat sleutels oplopend
                // blijven; een niet-oplopende sleutel breekt de sortering in het portaal.
                ms = _lastTimestamp;
                for (int i = 9; i >= 0; i--)
                {
                    if (++LastRandomness[i] != 0)
                    {
                        break;
                    }
                }
            }
            else
            {
                _lastTimestamp = ms;
                RandomNumberGenerator.Fill(LastRandomness);
            }

            LastRandomness.CopyTo(randomness);
        }

        return Encode(ms, randomness);
    }

    private static string Encode(long milliseconds, ReadOnlySpan<byte> randomness)
    {
        Span<char> buffer = stackalloc char[26];

        // 48 bit tijd in tien tekens van vijf bit (de bovenste twee bits van teken 0 zijn nul).
        for (int i = 9; i >= 0; i--)
        {
            buffer[i] = Alphabet[(int)(milliseconds & 0x1F)];
            milliseconds >>= 5;
        }

        // 80 bit toeval in zestien tekens van vijf bit.
        ulong high = 0;
        for (int i = 0; i < 5; i++)
        {
            high = (high << 8) | randomness[i];
        }

        ulong low = 0;
        for (int i = 5; i < 10; i++)
        {
            low = (low << 8) | randomness[i];
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

    /// <summary>
    /// Maakt een korte runId in de vorm <c>r-8f3c1a2b</c>: acht hexadecimale tekens.
    /// </summary>
    /// <remarks>
    /// Kort omdat een runId in het portaal in een monospace kolom staat en door een operator
    /// wordt voorgelezen. Acht tekens is ruim genoeg: de sleutel hoeft alleen binnen één
    /// partitie (<c>agentnaam|dag</c>) uniek te zijn.
    /// </remarks>
    internal static string NewRunId()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return $"r-{Convert.ToHexStringLower(bytes)}";
    }
}
