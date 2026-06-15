using System.Security.Cryptography;

namespace CalendarBooking.Services;

/// <summary>
/// Generates short, opaque, URL-safe public codes for users. Uses the Crockford Base32
/// alphabet (excludes I, L, O, U to stay unambiguous) and a cryptographic RNG. 10 chars over
/// a 32-symbol alphabet is ~10^15 values; callers still check uniqueness against the DB
/// (there's a unique index as the backstop).
/// </summary>
public static class PublicCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // 32 symbols
    public const int Length = 10;

    public static string New()
    {
        // 256 is divisible by 32, so (byte % 32) is unbiased.
        var bytes = RandomNumberGenerator.GetBytes(Length);
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }
        return new string(chars);
    }
}
