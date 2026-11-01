using System.Security.Cryptography;
using System.Text;
using HearthGate.Server.Config;
using Microsoft.Extensions.Caching.Memory;

namespace HearthGate.Server.Security;

// Owns: self-hosted proof-of-work challenges for anonymous forms (no third-party CAPTCHA).
// The client must find a nonce so that SHA-256(salt + nonce) starts with N zero bits.
public sealed class ProofOfWorkService(ConfigProvider config, IMemoryCache cache)
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    public sealed record Challenge(string Salt, int Bits, long Expires, string Signature);

    public Challenge Create()
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var bits = Math.Clamp(config.Current.Security.ProofOfWorkBits, 8, 24);
        var expires = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        return new Challenge(salt, bits, expires, Sign(salt, bits, expires));
    }

    public bool Verify(Challenge? c, string? nonce)
    {
        if (c is null || string.IsNullOrEmpty(nonce) || nonce.Length > 32) return false;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(c.Salt, c.Bits, c.Expires)), Encoding.ASCII.GetBytes(c.Signature ?? ""))) return false;
        if (c.Expires < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
        if (cache.TryGetValue("pow." + c.Salt, out _)) return false; // replay
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(c.Salt + nonce));
        if (!HasLeadingZeroBits(hash, c.Bits)) return false;
        cache.Set("pow." + c.Salt, true, TimeSpan.FromMinutes(15));
        return true;
    }

    private static bool HasLeadingZeroBits(byte[] hash, int bits)
    {
        for (var i = 0; i < bits; i++)
            if ((hash[i / 8] & (0x80 >> (i % 8))) != 0) return false;
        return true;
    }

    private static string Sign(string salt, int bits, long expires) =>
        Convert.ToHexString(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes($"{salt}|{bits}|{expires}")));
}
