using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenKSeF.Mobile.E2E.Android.Support;

/// <summary>RFC 6238 TOTP with Keycloak's default policy: HmacSHA1, 6 digits, 30 s.</summary>
public static class Totp
{
    public static long CurrentStep => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

    public static string Code(string base32Secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(DecodeBase32(base32Secret), counter);
        var offset = hash[^1] & 0x0F;
        var value = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7FFFFFFF;
        return (value % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in secret.ToUpperInvariant())
        {
            var index = alphabet.IndexOf(c);
            if (index < 0)
                continue;
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
            }
        }
        return bytes.ToArray();
    }
}
