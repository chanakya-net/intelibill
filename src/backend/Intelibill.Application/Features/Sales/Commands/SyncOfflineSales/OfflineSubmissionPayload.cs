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
        TryReadCore(shopId, submission, requireV1GoodsLines: true);

    /// <summary>
    /// Validates a newly created in-process sync command receipt before persistence alters or retains
    /// it; HTTP v1 intake always binds goods lines, while direct handler callers may exercise services.
    /// </summary>
    internal static OfflineSaleSyncCommand? TryReadFreshCommand(Guid shopId, OfflineSubmission submission) =>
        TryReadCore(shopId, submission, requireV1GoodsLines: false);

    private static OfflineSaleSyncCommand? TryReadCore(Guid shopId, OfflineSubmission submission, bool requireV1GoodsLines)
    {
        if (submission.ShopId != shopId)
            return null;

        try
        {
            var sale = Deserialize(submission.PayloadJson);
            if (!HasValidHasherInputs(sale)) return null;
            if (requireV1GoodsLines && !HasCompatibleV1GoodsLines(sale!)) return null;
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
}
