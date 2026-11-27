using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0108Tests
{
    private static string CookiePair(HttpResponseMessage response, string name) =>
        response.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith(name + "=", StringComparison.Ordinal))
            .Split(';', 2)[0];

    private static Task<object?> ReadFactAsync(NpgsqlConnectionStringBuilder runtime, string sql) =>
        LicensingPostgreSqlFixture.ReadScalarAsync(runtime, sql);

    private static async Task<string> SetupSecretAsync(HttpClient browser, string csrf)
    {
        using var setup = await StartAsync(browser, csrf);
        return setup.RootElement.GetProperty("setupSecret").GetString()!;
    }

    private static async Task AssertNoPlaintextAsync(
        NpgsqlConnectionStringBuilder runtime, string[] values, P0107Tests.CapturedLogs logs)
    {
        var stored = (string)(await ReadFactAsync(runtime, """
            SELECT concat((SELECT string_agg(row_to_json(t)::text, '') FROM admin_totp t),
                          (SELECT string_agg(row_to_json(r)::text, '') FROM admin_recovery_codes r),
                          (SELECT string_agg(row_to_json(a)::text, '') FROM provider_audit_events a),
                          (SELECT string_agg(row_to_json(c)::text, '') FROM bootstrap_enrollment_credentials c))
            """))!;
        Assert.True(stored.Length > 0);
        foreach (var value in values)
        {
            Assert.True(!stored.Contains(value, StringComparison.Ordinal), "Persisted data contained a plaintext credential.");
            Assert.True(!logs.Events.Any(entry => entry.Contains(value, StringComparison.Ordinal)), "Diagnostic output contained a plaintext credential.");
        }
    }

    private static async Task AssertSeparatedKeysAsync(
        NpgsqlConnectionStringBuilder runtime, string rootKey, string setupSecret, string recoveryCode)
    {
        var cipher = (byte[])(await ReadFactAsync(runtime, "SELECT encrypted_secret FROM admin_totp"))!;
        var adminId = (Guid)(await ReadFactAsync(runtime, "SELECT admin_id FROM admin_totp"))!;
        var root = Convert.FromBase64String(rootKey);
        var encryption = HKDF.DeriveKey(HashAlgorithmName.SHA256, root, 32, info: "intelibill-admin/totp-encryption/v1"u8.ToArray());
        var recovery = HKDF.DeriveKey(HashAlgorithmName.SHA256, root, 32, info: "intelibill-admin/recovery-hmac/v1"u8.ToArray());

        using var aes = new AesGcm(encryption, 16);
        using var wrongPurpose = new AesGcm(recovery, 16);
        var plaintext = new byte[20];
        aes.Decrypt(cipher.AsSpan(0, 12), cipher.AsSpan(28), cipher.AsSpan(12, 16), plaintext, adminId.ToByteArray());
        Assert.True(CryptographicOperations.FixedTimeEquals(DecodeAuthenticatorSecret(setupSecret), plaintext));
        Assert.Throws<AuthenticationTagMismatchException>(() =>
            wrongPurpose.Decrypt(cipher.AsSpan(0, 12), cipher.AsSpan(28), cipher.AsSpan(12, 16), plaintext, adminId.ToByteArray()));

        var raw = recoveryCode.Replace("-", "", StringComparison.Ordinal);
        var digest = Convert.ToHexString(HMACSHA256.HashData(recovery, Encoding.ASCII.GetBytes($"{adminId:N}:{raw}")));
        await using var databaseConnection = new NpgsqlConnection(runtime.ConnectionString);
        await databaseConnection.OpenAsync();
        await using var check = new NpgsqlCommand("SELECT EXISTS (SELECT FROM admin_recovery_codes WHERE digest = @digest)", databaseConnection);
        check.Parameters.AddWithValue("digest", digest);
        Assert.True(await check.ExecuteScalarAsync() is true, "Recovery digest must use its own derived key.");
    }

    // Independent authenticator calculation, using a decoded manual setup key.
    private static string AuthenticatorCode(string setup, DateTimeOffset now)
    {
        var secret = DecodeAuthenticatorSecret(setup);
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, now.ToUnixTimeSeconds() / 30);
#pragma warning disable CA5350
        var mac = HMACSHA1.HashData(secret, counter);
#pragma warning restore CA5350
        var truncated = BinaryPrimitives.ReadUInt32BigEndian(mac.AsSpan(mac[^1] & 15)) & 0x7FFFFFFFu;
        return (truncated % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeAuthenticatorSecret(string setup)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        return Enumerable.Range(0, setup.Length * 5 / 8).Select(index =>
        {
            var bit = index * 8;
            var character = bit / 5;
            var value = alphabet.IndexOf(setup[character], StringComparison.Ordinal) << 10
                | alphabet.IndexOf(setup[character + 1], StringComparison.Ordinal) << 5;
            if (character + 2 < setup.Length)
                value |= alphabet.IndexOf(setup[character + 2], StringComparison.Ordinal);
            return (byte)(value >> (7 - bit % 5));
        }).ToArray();
    }
}
