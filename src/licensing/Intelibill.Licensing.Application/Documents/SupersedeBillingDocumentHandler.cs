using System.Security.Cryptography;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Documents;

public sealed class SupersedeBillingDocumentHandler(IManualPaymentStore authority, ICheckoutOrderStore orders,
    IBillingDocumentStore documents, SupersedeBillingDocumentMetadataValidator metadataValidator,
    SupersedeBillingDocumentValidator supersedeValidator, TimeProvider clock)
{
    public Task<ErrorOr<BillingDocumentSupersessionPreview>> PreviewAsync(AdminSessionCaller caller, Guid account,
        Guid order, Guid predecessorId, SupersedeBillingDocumentMetadata? metadata, CancellationToken ct) =>
        authority.InManualScopeAsync<BillingDocumentSupersessionPreview>(caller, account, order, false, false,
            async (_, token) =>
            {
                if (metadata is null) return BillingDocumentErrors.Invalid;
                if (UploadBillingDocumentHandler.MetadataRefusal(metadata, clock.GetUtcNow()) is { } refusal)
                    return refusal;
                if (!metadataValidator.Validate(metadata).IsValid) return BillingDocumentErrors.Invalid;
                var accepted = await UploadBillingDocumentHandler.AcceptedOrderAsync(orders, account, order, token);
                if (accepted is null) return BillingDocumentErrors.OrderNotFound;
                var predecessor = await documents.FindVersionAsync(account, order, predecessorId, token);
                if (predecessor is null) return BillingDocumentErrors.PredecessorNotFound;
                var latest = await documents.FindLatestAsync(account, order, token);
                if (latest is null || latest.VersionId != predecessor.VersionId)
                    return BillingDocumentErrors.PredecessorStale;
                var confirmations = await documents.CountConfirmationsAsync(account, order, token);
                var digest = PreviewDigest(account, order, accepted, predecessor, metadata);
                return new BillingDocumentSupersessionPreview(account, order, accepted, confirmations,
                    predecessor.DocumentId, predecessor.VersionId, predecessor.Version, predecessor.Version + 1,
                    CopyMetadata(metadata), metadata.Reason, digest);
            }, ct);

    public Task<ErrorOr<BillingDocumentUpload>> SupersedeAsync(AdminSessionCaller caller, Guid account, Guid order,
        Guid predecessorId, SupersedeBillingDocument command, string? partFileName, byte[] bytes, CancellationToken ct) =>
        authority.InManualScopeAsync<BillingDocumentUpload>(caller, account, order, true, true,
            (mfa, token) => SaveAsync(caller, account, order, predecessorId, command, partFileName, bytes, mfa, token), ct);

    private async Task<ErrorOr<BillingDocumentUpload>> SaveAsync(AdminSessionCaller caller, Guid account, Guid order,
        Guid predecessorId, SupersedeBillingDocument command, string? partFileName, byte[] bytes, DateTimeOffset mfa,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (UploadBillingDocumentHandler.MetadataRefusal(command, now) is { } refusal) return refusal;
        if (!supersedeValidator.Validate(command).IsValid) return BillingDocumentErrors.Invalid;
        if (!PdfStructure.HasEnvelope(bytes)) return BillingDocumentErrors.ContentInvalid;
        if (bytes.Length != command.ByteLength || command.FileName != partFileName
            || Convert.ToHexStringLower(SHA256.HashData(bytes)) != command.Sha256)
            return BillingDocumentErrors.ContentMismatch;

        var hash = PayloadHash(account, order, predecessorId, command);
        var existing = await documents.FindOperationAsync(account, command.OperationKey, ct);
        if (existing is not null) return Replay(existing, caller, order, predecessorId, hash);

        var accepted = await UploadBillingDocumentHandler.AcceptedOrderAsync(orders, account, order, ct);
        if (accepted is null) return BillingDocumentErrors.OrderNotFound;
        var predecessor = await documents.FindVersionAsync(account, order, predecessorId, ct);
        if (predecessor is null) return BillingDocumentErrors.PredecessorNotFound;
        var latest = await documents.FindLatestAsync(account, order, ct);
        if (latest is null || latest.VersionId != predecessor.VersionId)
            return BillingDocumentErrors.PredecessorStale;
        if (command.PreviewDigest != PreviewDigest(account, order, accepted, predecessor, command))
            return BillingDocumentErrors.PreviewStale;

        var view = new BillingDocumentVersionView(account, order, predecessor.DocumentId, Guid.NewGuid(),
            predecessor.Version + 1, predecessor.VersionId, command.Kind, command.FileName, command.MediaType,
            command.ByteLength, command.Sha256, command.IssuerName, command.IssuerReference, command.IssuedOn,
            command.OperationKey, caller.AdminId, mfa, now, command.Reason);
        if (await documents.InsertAsync(new(view, hash), caller.SessionId, bytes, ct))
        {
            var stored = await documents.FindOperationAsync(account, command.OperationKey, ct);
            return new BillingDocumentUpload(stored!.View, true);
        }
        existing = await documents.FindOperationAsync(account, command.OperationKey, ct);
        return existing is null
            ? BillingDocumentErrors.PredecessorStale
            : Replay(existing, caller, order, predecessorId, hash);
    }

    private static ErrorOr<BillingDocumentUpload> Replay(StoredBillingDocument saved, AdminSessionCaller caller,
        Guid order, Guid predecessorId, string hash) =>
        saved.View.OrderId == order && saved.View.SupersedesVersionId == predecessorId
            && saved.View.UploadedBy == caller.AdminId && saved.PayloadHash == hash
            ? new BillingDocumentUpload(saved.View, false)
            : BillingDocumentErrors.PayloadMismatch;

    private static string PreviewDigest(Guid account, Guid order, BillingDocumentOrder accepted,
        BillingDocumentVersionView predecessor, SupersedeBillingDocumentMetadata metadata) =>
        CheckoutOrdersV1.Digest(BillingDocumentsV1.Policy, "supersede-preview", account, order, accepted.Revision,
            accepted.SnapshotHash, predecessor.DocumentId, predecessor.VersionId, predecessor.Version,
            metadata.Reason, UploadBillingDocumentHandler.MetadataDigest(metadata));

    private static string PayloadHash(Guid account, Guid order, Guid predecessorId, SupersedeBillingDocument command) =>
        CheckoutOrdersV1.Digest(BillingDocumentsV1.Policy, "supersede", account, order, predecessorId,
            command.OperationKey, command.PreviewDigest, command.Confirmed, command.Reason,
            UploadBillingDocumentHandler.MetadataDigest(command));

    private static BillingDocumentMetadata CopyMetadata(BillingDocumentMetadata value) => new()
    {
        Kind = value.Kind,
        FileName = value.FileName,
        MediaType = value.MediaType,
        ByteLength = value.ByteLength,
        Sha256 = value.Sha256,
        IssuerName = value.IssuerName,
        IssuerReference = value.IssuerReference,
        IssuedOn = value.IssuedOn,
    };
}
