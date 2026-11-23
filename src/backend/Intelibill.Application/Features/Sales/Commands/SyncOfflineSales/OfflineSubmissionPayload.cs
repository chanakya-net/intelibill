using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

/// <summary>
/// The frozen invoice stored on an <see cref="OfflineSubmission"/>: the sync command
/// serialized with web defaults, so a replay deserializes to a payload with the same idempotency hash.
/// </summary>
internal static class OfflineSubmissionPayload
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    internal static string Serialize(OfflineSaleSyncCommand sale) =>
        JsonSerializer.Serialize(sale, SerializerOptions);

    internal static OfflineSaleSyncCommand? Deserialize(string payloadJson) =>
        JsonSerializer.Deserialize<OfflineSaleSyncCommand>(payloadJson, SerializerOptions);

    /// <summary>
    /// Reads a retained receipt's frozen command using its original web JSON options, enforces the v1
    /// goods-line contract for the line properties omitted from the canonical digest, and verifies its
    /// canonical idempotency hash without modifying the first stored envelope.
    /// </summary>
    internal static OfflineSaleSyncCommand? TryReadValidated(Guid shopId, OfflineSubmission submission) =>
        TryReadCore(shopId, submission, expectedPayloadHash: null, submittedCommand: null, requireV1GoodsLines: true);

    /// <summary>
    /// Reads a receipt during sync delivery (first insert or idempotent redelivery), verifying its
    /// shop, canonical idempotency hash, and unhashed line semantics against the submitted command.
    /// Recovery-originated receipts additionally retain strict v1 goods-line validation.
    /// </summary>
    internal static OfflineSaleSyncCommand? TryReadValidatedSync(
        Guid shopId,
        OfflineSubmission submission,
        string expectedPayloadHash,
        OfflineSaleSyncCommand submittedCommand) =>
        TryReadCore(
            shopId,
            submission,
            expectedPayloadHash,
            submittedCommand,
            requireV1GoodsLines: submission.Source != OfflineSubmissionSource.Sync);

    private static OfflineSaleSyncCommand? TryReadCore(
        Guid shopId,
        OfflineSubmission submission,
        string? expectedPayloadHash,
        OfflineSaleSyncCommand? submittedCommand,
        bool requireV1GoodsLines)
    {
        if (submission.ShopId != shopId)
            return null;

        if (expectedPayloadHash is not null
            && !string.Equals(submission.PayloadHash, expectedPayloadHash, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var sale = Deserialize(submission.PayloadJson);
            if (!HasValidHasherInputs(sale)) return null;
            if (requireV1GoodsLines && !HasCompatibleV1GoodsLines(sale!)) return null;
            if (submittedCommand is not null && !MatchesUnhashedLineSemantics(sale!, submittedCommand)) return null;
            var recomputed = OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, submission.DeviceId, sale!);
            return string.Equals(recomputed, submission.PayloadHash, StringComparison.Ordinal) ? sale : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static bool HasValidHasherInputs(OfflineSaleSyncCommand? sale) =>
        sale is not null && sale.Items is not null && !sale.Items.Any(item => item is null);

    private static bool HasCompatibleV1GoodsLines(OfflineSaleSyncCommand sale) =>
        sale.Items.All(IsCompatibleV1GoodsLine);

    private static bool IsCompatibleV1GoodsLine(OfflineSaleSyncLineCommand line) =>
        string.Equals(line.LineType, nameof(SaleLineType.Goods), StringComparison.OrdinalIgnoreCase)
        && line.ServiceId is null;

    private static bool HasValidSyncLineSemantics(OfflineSaleSyncLineCommand line) =>
        IsCompatibleV1GoodsLine(line)
        || string.Equals(line.LineType, nameof(SaleLineType.Service), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesUnhashedLineSemantics(OfflineSaleSyncCommand stored, OfflineSaleSyncCommand submitted)
    {
        if (!HasValidHasherInputs(submitted) || stored.Items.Count != submitted.Items.Count)
            return false;

        for (var i = 0; i < stored.Items.Count; i++)
        {
            if (!HasValidSyncLineSemantics(stored.Items[i])
                || !string.Equals(stored.Items[i].LineType, submitted.Items[i].LineType, StringComparison.OrdinalIgnoreCase)
                || stored.Items[i].ServiceId != submitted.Items[i].ServiceId)
            {
                return false;
            }
        }

        return true;
    }
}
