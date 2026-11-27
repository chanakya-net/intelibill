using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intelibill.Application.Common.Licensing.Offline;

/// <summary>
/// The verified entitlement a new invoice lease was reserved under, frozen at reservation. Later
/// projection reads, session touches or snapshots never replace it.
/// </summary>
public sealed record GrantSnapshotV1(
    int V, Guid GrantId, Guid ShopId, Guid AccountId, Guid AffiliationRequestId, long AccountRevision, long ShopRevision,
    Guid AccountEvidenceId, Guid ShopEvidenceId, DateTimeOffset AuthorityVerifiedAt, DateTimeOffset AuthorityValidUntil,
    Guid UserId, Guid SessionId, string DeviceId, Guid LeaseId, long MembershipAuthorityVersion, long SessionScopeVersion,
    DateTimeOffset DeviceVerifiedAt, DateTimeOffset AuthDeadline, DateTimeOffset CapturedAt);

/// <summary>
/// Bounded permission to issue offline invoices from one lease on one device. Valid over
/// <c>[NotBefore, ValidUntil)</c>; the revisions and verification time are the original grant's.
/// </summary>
public sealed record OfflineLicensePermitV1(
    int V, Guid PermitId, Guid GrantId, string GrantSha256, Guid ShopId, Guid AccountId, Guid UserId, Guid SessionId,
    string DeviceId, Guid LeaseId, Guid SnapshotId, long AccountRevision, long ShopRevision, long CurrentAccountRevision,
    long CurrentShopRevision, DateTimeOffset AuthorityVerifiedAt, DateTimeOffset DeviceVerifiedAt,
    DateTimeOffset SnapshotCompletedAt, DateTimeOffset IssuedAt, DateTimeOffset NotBefore, DateTimeOffset ValidUntil,
    string LimitingReason, Guid? TillSessionId);

/// <summary>Version 1 canonical bytes: fixed member order, lowercase hyphenated GUIDs, UTC microsecond instants.</summary>
public static class OfflinePermitContractV1
{
    public const int Version = 1;
    public const string Section = "OfflineLicensePermit";
    public const string GrantType = "intelibill-offline-grant+v1";
    public const string PermitType = "intelibill-offline-permit+v1";
    public const int MaxTokenLength = 8192;
    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        MaxDepth = 4,
    };

    /// <summary>PostgreSQL keeps microseconds; signed instants use the same precision.</summary>
    public static DateTimeOffset Instant(DateTimeOffset value)
    {
        var ticks = value.UtcTicks;
        return new DateTimeOffset(ticks - ticks % 10, TimeSpan.Zero);
    }

    public static string Digest(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    public static byte[] Encode(GrantSnapshotV1 grant) => Write(json =>
    {
        json.WriteNumber("v", grant.V);
        json.WriteString("grantId", Id(grant.GrantId));
        json.WriteString("shopId", Id(grant.ShopId));
        json.WriteString("accountId", Id(grant.AccountId));
        json.WriteString("affiliationRequestId", Id(grant.AffiliationRequestId));
        json.WriteNumber("accountRevision", grant.AccountRevision);
        json.WriteNumber("shopRevision", grant.ShopRevision);
        json.WriteString("accountEvidenceId", Id(grant.AccountEvidenceId));
        json.WriteString("shopEvidenceId", Id(grant.ShopEvidenceId));
        json.WriteString("authorityVerifiedAt", At(grant.AuthorityVerifiedAt));
        json.WriteString("authorityValidUntil", At(grant.AuthorityValidUntil));
        json.WriteString("userId", Id(grant.UserId));
        json.WriteString("sessionId", Id(grant.SessionId));
        json.WriteString("deviceId", grant.DeviceId);
        json.WriteString("leaseId", Id(grant.LeaseId));
        json.WriteNumber("membershipAuthorityVersion", grant.MembershipAuthorityVersion);
        json.WriteNumber("sessionScopeVersion", grant.SessionScopeVersion);
        json.WriteString("deviceVerifiedAt", At(grant.DeviceVerifiedAt));
        json.WriteString("authDeadline", At(grant.AuthDeadline));
        json.WriteString("capturedAt", At(grant.CapturedAt));
    });

    public static byte[] Encode(OfflineLicensePermitV1 permit) => Write(json =>
    {
        json.WriteNumber("v", permit.V);
        json.WriteString("permitId", Id(permit.PermitId));
        json.WriteString("grantId", Id(permit.GrantId));
        json.WriteString("grantSha256", permit.GrantSha256);
        json.WriteString("shopId", Id(permit.ShopId));
        json.WriteString("accountId", Id(permit.AccountId));
        json.WriteString("userId", Id(permit.UserId));
        json.WriteString("sessionId", Id(permit.SessionId));
        json.WriteString("deviceId", permit.DeviceId);
        json.WriteString("leaseId", Id(permit.LeaseId));
        json.WriteString("snapshotId", Id(permit.SnapshotId));
        json.WriteNumber("accountRevision", permit.AccountRevision);
        json.WriteNumber("shopRevision", permit.ShopRevision);
        json.WriteNumber("currentAccountRevision", permit.CurrentAccountRevision);
        json.WriteNumber("currentShopRevision", permit.CurrentShopRevision);
        json.WriteString("authorityVerifiedAt", At(permit.AuthorityVerifiedAt));
        json.WriteString("deviceVerifiedAt", At(permit.DeviceVerifiedAt));
        json.WriteString("snapshotCompletedAt", At(permit.SnapshotCompletedAt));
        json.WriteString("issuedAt", At(permit.IssuedAt));
        json.WriteString("notBefore", At(permit.NotBefore));
        json.WriteString("validUntil", At(permit.ValidUntil));
        json.WriteString("limitingReason", permit.LimitingReason);
        if (permit.TillSessionId is { } till) json.WriteString("tillSessionId", Id(till));
        else json.WriteNull("tillSessionId");
    });

    /// <summary>Null unless the bytes are exactly the canonical encoding of a version 1 grant.</summary>
    public static GrantSnapshotV1? DecodeGrant(ReadOnlySpan<byte> payload) =>
        Decode<GrantSnapshotV1>(payload) is { V: Version } grant && payload.SequenceEqual(Encode(grant)) ? grant : null;

    /// <summary>Null unless the bytes are exactly the canonical encoding of a version 1 permit.</summary>
    public static OfflineLicensePermitV1? DecodePermit(ReadOnlySpan<byte> payload) =>
        Decode<OfflineLicensePermitV1>(payload) is { V: Version } permit && payload.SequenceEqual(Encode(permit))
            ? permit : null;

    private static T? Decode<T>(ReadOnlySpan<byte> payload) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload, Strict);
        }
        catch (Exception error) when (error is JsonException or FormatException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] Write(Action<Utf8JsonWriter> members)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            members(json);
            json.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static string Id(Guid value) => value.ToString("D");

    private static string At(DateTimeOffset value) =>
        Instant(value).UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture);
}
