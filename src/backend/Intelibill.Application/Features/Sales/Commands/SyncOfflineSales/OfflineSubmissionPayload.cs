using System.Text.Json;
using Intelibill.Domain.Entities;

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
    /// Reads a retained receipt's frozen command using its original web JSON options and verifies its
    /// canonical idempotency hash without modifying the first stored envelope.
    /// </summary>
    internal static OfflineSaleSyncCommand? TryReadValidated(Guid shopId, OfflineSubmission submission)
    {
        if (submission.ShopId != shopId)
            return null;

        try
        {
            var sale = Deserialize(submission.PayloadJson);
            if (!HasValidHasherInputs(sale)) return null;
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
}
