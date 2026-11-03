namespace Intelibill.Application.Features.OfflineReconciliation.Commands.SubmitRecoveryBundle;

/// <param name="ActorUserId">The operator submitting the bundle: an active owner or manager of the shop.</param>
/// <param name="ShopId">The operator's active shop; the bundle must belong to it.</param>
/// <param name="OperationId">The idempotency key: a resend with the same key replays the committed answer.</param>
/// <param name="Bundle">The bundle as exported from the retired browser.</param>
public sealed record SubmitRecoveryBundleCommand(
    Guid ActorUserId,
    Guid ShopId,
    string OperationId,
    RecoveryBundle Bundle);

/// <summary>A support recovery bundle: the unsynced invoices of one device, checksummed as exported.</summary>
/// <param name="Format">Always <c>intelibill.offline-recovery</c>.</param>
/// <param name="Version">Always 1.</param>
/// <param name="ShopId">The shop the invoices were issued in.</param>
/// <param name="DeviceId">The device the invoices were issued on; every invoice number must be leased to it.</param>
/// <param name="ExportedAt">When the browser exported the bundle, as the browser's clock read it.</param>
/// <param name="Checksum">Hex SHA-256 of the UTF-8 bytes of <paramref name="InvoicesJson"/>.</param>
/// <param name="InvoicesJson">
/// The bundle's <c>invoices</c> array exactly as received: each element is the device's sync request
/// for one invoice, including <c>issuedByUserId</c> when the device recorded it.
/// </param>
public sealed record RecoveryBundle(
    string Format,
    int Version,
    Guid ShopId,
    string DeviceId,
    DateTimeOffset ExportedAt,
    string Checksum,
    string InvoicesJson);
