using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

/// <summary>
/// The durable envelope of one offline invoice as the server first received it: the frozen payload,
/// the device and invoice identity, who submitted it and who is claimed to have issued it.
/// </summary>
/// <remarks>
/// Unique on shop, device, client sale id and payload hash, so an identical resend returns the first
/// row (its <see cref="ReceivedAt"/> is the trusted receipt time) and a changed payload is a second
/// row. Never updated except the one-time <see cref="LinkSale"/>.
/// </remarks>
public sealed class OfflineSubmission : BaseEntity
{
    public Guid ShopId { get; private set; }
    public string DeviceId { get; private set; } = string.Empty;
    public string ClientSaleId { get; private set; } = string.Empty;
    public string InvoiceNumber { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTimeOffset ClaimedSoldAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public OfflineSubmissionSource Source { get; private set; }
    public Guid SubmittedByUserId { get; private set; }

    /// <summary>The issuer the payload claims; unverified.</summary>
    public Guid? AssertedIssuerUserId { get; private set; }

    /// <summary>The issuer once verified; null means "not recorded" or "not verified".</summary>
    public Guid? OriginalIssuerUserId { get; private set; }

    public Guid? OriginalLeaseId { get; private set; }
    public string? RecoveryOperationId { get; private set; }
    public Guid? SaleId { get; private set; }

    /// <summary>
    /// The user a replayed sale may be attributed to: the verified issuer, or for a legacy sync with
    /// no assertion the submitter. Null when nobody can be vouched for.
    /// </summary>
    public Guid? AttributableUserId =>
        OriginalIssuerUserId
        ?? (Source == OfflineSubmissionSource.Sync && AssertedIssuerUserId is null ? SubmittedByUserId : null);

    private OfflineSubmission() { }

    public static OfflineSubmission Record(
        Guid shopId,
        string deviceId,
        string clientSaleId,
        string invoiceNumber,
        string payloadHash,
        string payloadJson,
        DateTimeOffset claimedSoldAt,
        DateTimeOffset receivedAt,
        OfflineSubmissionSource source,
        Guid submittedByUserId,
        Guid? assertedIssuerUserId,
        Guid? originalIssuerUserId,
        Guid? originalLeaseId,
        string? recoveryOperationId)
    {
        // Npgsql writes timestamptz only for offset zero, and the client-supplied claim may carry any offset.
        var claimedUtc = new DateTimeOffset(claimedSoldAt.UtcDateTime, TimeSpan.Zero);
        var receivedUtc = new DateTimeOffset(receivedAt.UtcDateTime, TimeSpan.Zero);

        return new OfflineSubmission
        {
            ShopId = shopId,
            DeviceId = deviceId.Trim(),
            ClientSaleId = clientSaleId.Trim(),
            InvoiceNumber = invoiceNumber.Trim(),
            PayloadHash = payloadHash,
            PayloadJson = payloadJson,
            ClaimedSoldAt = claimedUtc,
            ReceivedAt = receivedUtc,
            Source = source,
            SubmittedByUserId = submittedByUserId,
            AssertedIssuerUserId = assertedIssuerUserId,
            OriginalIssuerUserId = originalIssuerUserId,
            OriginalLeaseId = originalLeaseId,
            RecoveryOperationId = recoveryOperationId,
            CreatedAt = receivedUtc,
        };
    }

    /// <summary>Links the posted sale once. The same sale again is a no-op; another sale is refused.</summary>
    public ErrorOr<Success> LinkSale(Guid saleId)
    {
        if (SaleId is null)
        {
            SaleId = saleId;
            return Result.Success;
        }

        return SaleId == saleId
            ? Result.Success
            : Error.Conflict("OfflineSubmission.SaleAlreadyLinked", "The submission is already linked to a different sale.");
    }
}
