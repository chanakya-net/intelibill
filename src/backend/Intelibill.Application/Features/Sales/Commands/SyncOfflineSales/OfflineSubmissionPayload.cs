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
        TryReadCore(shopId, submission, expectedPayloadHash: null, expectedFreshCommand: null);

    /// <summary>
    /// Reads a newly inserted sync receipt and verifies both its canonical idempotency hash and that
    /// the line properties omitted from the v1 digest still match the original in-process command.
    /// </summary>
    internal static OfflineSaleSyncCommand? TryReadValidatedFresh(
        Guid shopId,
        OfflineSubmission submission,
        string expectedPayloadHash,
        OfflineSaleSyncCommand expectedCommand) =>
        TryReadCore(shopId, submission, expectedPayloadHash, expectedCommand);

    private static OfflineSaleSyncCommand? TryReadCore(
        Guid shopId,
        OfflineSubmission submission,
        string? expectedPayloadHash,
        OfflineSaleSyncCommand? expectedFreshCommand)
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
            var lineSemanticsValid = expectedFreshCommand is not null
                ? MatchesUnhashedLineSemantics(sale!, expectedFreshCommand)
                : HasCompatibleV1GoodsLines(sale!);
            if (!lineSemanticsValid) return null;
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
        sale.Items.All(line =>
            string.Equals(line.LineType, nameof(SaleLineType.Goods), StringComparison.OrdinalIgnoreCase)
            && line.ServiceId is null);

    private static bool MatchesUnhashedLineSemantics(OfflineSaleSyncCommand stored, OfflineSaleSyncCommand original)
    {
        if (!HasValidHasherInputs(original) || stored.Items.Count != original.Items.Count)
            return false;

        for (var i = 0; i < stored.Items.Count; i++)
        {
            if (!string.Equals(stored.Items[i].LineType, original.Items[i].LineType, StringComparison.Ordinal)
                || stored.Items[i].ServiceId != original.Items[i].ServiceId)
            {
                return false;
            }
        }

        return true;
    }
}
