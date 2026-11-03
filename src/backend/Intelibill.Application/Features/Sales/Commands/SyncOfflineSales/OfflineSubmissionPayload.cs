using System.Text.Json;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

/// <summary>
/// The frozen invoice stored on an <see cref="Domain.Entities.OfflineSubmission"/>: the sync command
/// serialized with web defaults, so a replay deserializes to a payload with the same idempotency hash.
/// </summary>
internal static class OfflineSubmissionPayload
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    internal static string Serialize(OfflineSaleSyncCommand sale) =>
        JsonSerializer.Serialize(sale, SerializerOptions);

    internal static OfflineSaleSyncCommand? Deserialize(string payloadJson) =>
        JsonSerializer.Deserialize<OfflineSaleSyncCommand>(payloadJson, SerializerOptions);
}
