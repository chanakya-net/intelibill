using System.Text;
using System.Text.Json;
using Intelibill.Application.Common.Licensing.Offline;

namespace Intelibill.Integration.Tests;

public sealed partial class P0501LicensingTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// tests/contracts/licensing/offline-permit-v1.json against the production codec, verifier and
    /// policy. The vectors were signed once by a throwaway fixture key; only its public half exists.
    /// </summary>
    [Fact]
    public void Shared_fixture_pins_canonical_bytes_signatures_and_deadlines()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "offline-permit-v1.json")));
        var root = document.RootElement;
        var keyId = root.GetProperty("key").GetProperty("keyId").GetString()!;
        var publicKey = Convert.FromBase64String(root.GetProperty("key").GetProperty("publicKeySpki").GetString()!);
        Assert.Equal(root.GetProperty("key").GetProperty("fingerprint").GetString(), OfflinePermitSignatures.Fingerprint(publicKey));
        byte[]? Retained(string id) => id == keyId ? publicKey : null;

        var grantVector = root.GetProperty("grant");
        var grant = grantVector.GetProperty("fields").Deserialize<GrantSnapshotV1>(Web)!;
        var grantToken = grantVector.GetProperty("token").GetString()!;
        Assert.Equal(OfflinePermitContractV1.GrantType, grantVector.GetProperty("type").GetString());
        Assert.Equal(grantVector.GetProperty("canonical").GetString(), Encoding.UTF8.GetString(OfflinePermitContractV1.Encode(grant)));
        var grantPayload = OfflinePermitSignatures.Verify(grantToken, OfflinePermitContractV1.GrantType, publicKey);
        Assert.Equal(grant, OfflinePermitContractV1.DecodeGrant(Issued(grantPayload)));

        var permitVector = root.GetProperty("permit");
        var permit = permitVector.GetProperty("fields").Deserialize<OfflineLicensePermitV1>(Web)!;
        var token = permitVector.GetProperty("token").GetString()!;
        var scope = permitVector.GetProperty("scope").Deserialize<OfflinePermitScope>(Web)!;
        Assert.Equal(OfflinePermitContractV1.PermitType, permitVector.GetProperty("type").GetString());
        Assert.Equal(permitVector.GetProperty("canonical").GetString(), Encoding.UTF8.GetString(OfflinePermitContractV1.Encode(permit)));
        Assert.Equal(permit.GrantSha256, OfflinePermitContractV1.Digest(grantToken));
        foreach (var at in permitVector.GetProperty("validAt").EnumerateArray())
            Assert.Equal(permit, Issued(OfflinePermitSignatures.VerifyPermit(token, scope, at.GetDateTimeOffset(), Retained)));
        foreach (var at in permitVector.GetProperty("invalidAt").EnumerateArray())
            Assert.Equal("OfflinePermit.OutsideValidity", Code(OfflinePermitSignatures.VerifyPermit(token, scope, at.GetDateTimeOffset(), Retained)));

        var validAt = permit.NotBefore;
        foreach (var wrong in permitVector.GetProperty("wrongScope").EnumerateArray())
        {
            var expected = wrong.EnumerateObject().Single() switch
            {
                { Name: "shopId" } member => scope with { ShopId = member.Value.GetGuid() },
                { Name: "userId" } member => scope with { UserId = member.Value.GetGuid() },
                { Name: "sessionId" } member => scope with { SessionId = member.Value.GetGuid() },
                { Name: "deviceId" } member => scope with { DeviceId = member.Value.GetString()! },
                var member => scope with { LeaseId = member.Value.GetGuid() },
            };
            Assert.Equal("OfflinePermit.ScopeMismatch", Code(OfflinePermitSignatures.VerifyPermit(token, expected, validAt, Retained)));
        }

        foreach (var vector in root.GetProperty("negative").EnumerateArray())
            Assert.True(vector.GetProperty("error").GetString() ==
                Code(OfflinePermitSignatures.VerifyPermit(vector.GetProperty("token").GetString(), scope, validAt, Retained)),
                vector.GetProperty("name").GetString());

        AssertDeadlineVectors(root);
    }

    private void AssertDeadlineVectors(JsonElement root)
    {
        var encoding = root.GetProperty("encoding");
        Assert.Equal(TimeSpan.FromHours(encoding.GetProperty("freshnessHours").GetInt32()), OfflineLicencePermitPolicy.Freshness);
        Assert.Equal(TimeSpan.FromHours(encoding.GetProperty("snapshotLifetimeHours").GetInt32()), OfflineLicencePermitPolicy.SnapshotLifetime);
        var every = new OfflinePermitCaps(default, default, default, default, default, default, default, default,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Assert.Equal(encoding.GetProperty("tieOrder").EnumerateArray().Select(x => x.GetString()),
            OfflineLicencePermitPolicy.Deadlines(every).Select(x => x.Reason));

        var count = 0;
        foreach (var vector in root.GetProperty("deadlines").EnumerateArray())
        {
            var name = vector.GetProperty("name").GetString();
            var caps = vector.GetProperty("caps").Deserialize<OfflinePermitCaps>(Web)!;
            var window = OfflineLicencePermitPolicy.Evaluate(caps, vector.GetProperty("issuedAt").GetDateTimeOffset());
            if (vector.TryGetProperty("error", out var error))
            {
                Assert.True(error.GetString() == Code(window), name);
            }
            else
            {
                var issued = Issued(window);
                Assert.True((vector.GetProperty("issuedAt").GetDateTimeOffset(), vector.GetProperty("validUntil").GetDateTimeOffset(),
                    vector.GetProperty("limitingReason").GetString()) == (issued.NotBefore, issued.ValidUntil, issued.LimitingReason),
                    $"{name}: {issued}");
            }
            count++;
        }
        _output.WriteLine("Contract fixture: {0} deadline vectors, {1} negative signature vectors verified",
            count, root.GetProperty("negative").GetArrayLength());
        Assert.True(count >= 19);
    }
}
