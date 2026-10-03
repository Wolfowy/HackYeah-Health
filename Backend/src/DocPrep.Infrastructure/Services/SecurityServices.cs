using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using DocPrep.Application.Abstractions;

namespace DocPrep.Infrastructure.Services;

internal sealed class PatientDataProtector : IPatientDataProtector
{
    private readonly byte[] hmacKey; private readonly byte[] encryptionKey;
    public PatientDataProtector(IConfiguration configuration)
    {
        hmacKey = ReadKey(configuration["Security:PatientHmacKey"], "PatientHmacKey");
        encryptionKey = ReadKey(configuration["Security:EncryptionKey"], "EncryptionKey");
    }
    public string CorrelationKey(string pesel)
    {
        var normalized = NormalizePesel(pesel); return Convert.ToHexString(HMACSHA256.HashData(hmacKey, Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }
    public string Protect(string value)
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var plain = Encoding.UTF8.GetBytes(value); var cipher = new byte[plain.Length]; var tag = new byte[16];
        using var aes = new AesGcm(encryptionKey, 16); aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    public string Unprotect(string value)
    {
        var all = Convert.FromBase64String(value); var nonce = all[..12]; var tag = all[12..28]; var cipher = all[28..]; var plain = new byte[cipher.Length];
        using var aes = new AesGcm(encryptionKey, 16); aes.Decrypt(nonce, cipher, tag, plain); return Encoding.UTF8.GetString(plain);
    }
    private static byte[] ReadKey(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Security:{name} is missing.");
        var bytes = Convert.FromBase64String(value); if (bytes.Length != 32) throw new InvalidOperationException($"Security:{name} must contain a base64-encoded 256-bit key."); return bytes;
    }
    private static string NormalizePesel(string value)
    {
        var result = new string(value.Where(char.IsDigit).ToArray());
        if (result.Length != 11) throw new ArgumentException("PESEL must contain 11 digits.", nameof(value));
        var weights = new[] { 1, 3, 7, 9, 1, 3, 7, 9, 1, 3 }; var checksum = (10 - result.Take(10).Select((c, i) => (c - '0') * weights[i]).Sum() % 10) % 10;
        if (checksum != result[10] - '0') throw new ArgumentException("PESEL checksum is invalid.", nameof(value)); return result;
    }
}

internal sealed class CredentialService : ICredentialService
{
    public string GenerateLinkToken() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public string GenerateVisitCode() => Base64Url(RandomNumberGenerator.GetBytes(9)).ToUpperInvariant();
    public string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()))).ToLowerInvariant();
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class RedisPatientSessionStore(IDistributedCache cache, ICredentialService credentials, IClock clock) : IPatientSessionStore
{
    public async Task<string> Create(Guid visitId, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var token = credentials.GenerateLinkToken(); var hash = credentials.Hash(token); var ttl = expiresAt - clock.UtcNow;
        if (ttl <= TimeSpan.Zero) throw new InvalidOperationException("Cannot create an expired session.");
        await cache.SetStringAsync($"patient-session:{hash}", visitId.ToString(), new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt }, ct);
        var reverseKey = $"patient-session-visit:{visitId}"; var hashes = JsonSerializer.Deserialize<List<string>>(await cache.GetStringAsync(reverseKey, ct) ?? "[]") ?? [];
        hashes.Add(hash); await cache.SetStringAsync(reverseKey, JsonSerializer.Serialize(hashes), new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt }, ct); return token;
    }
    public async Task<Guid?> Resolve(string token, CancellationToken ct) => Guid.TryParse(await cache.GetStringAsync($"patient-session:{credentials.Hash(token)}", ct), out var id) ? id : null;
    public async Task RevokeForVisit(Guid visitId, CancellationToken ct)
    {
        var key = $"patient-session-visit:{visitId}"; var hashes = JsonSerializer.Deserialize<List<string>>(await cache.GetStringAsync(key, ct) ?? "[]") ?? [];
        foreach (var hash in hashes) await cache.RemoveAsync($"patient-session:{hash}", ct); await cache.RemoveAsync(key, ct);
    }
}
