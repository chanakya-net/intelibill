using System.Security.Cryptography;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Documents;

public sealed class UploadBillingDocumentHandler(IManualPaymentStore authority, ICheckoutOrderStore orders,
    IBillingDocumentStore documents, BillingDocumentMetadataValidator metadataValidator,
    UploadBillingDocumentValidator uploadValidator, TimeProvider clock)
{
    private const string Policy = "billing-document/1";

    public Task<ErrorOr<BillingDocumentList>> ReadAsync(AdminSessionCaller caller, Guid account, Guid order, CancellationToken ct) =>
        authority.InManualScopeAsync<BillingDocumentList>(caller, account, order, false, false, async (_, token) =>
        {
            var accepted = await AcceptedOrderAsync(account, order, token);
            if (accepted is null) return BillingDocumentErrors.OrderNotFound;
            return new BillingDocumentList(account, order, accepted, await documents.CountConfirmationsAsync(account, order, token),
                await documents.ListAsync(account, order, token));
        }, ct);

    public Task<ErrorOr<BillingDocumentPreview>> PreviewAsync(AdminSessionCaller caller, Guid account, Guid order,
        BillingDocumentMetadata? metadata, CancellationToken ct) =>
        authority.InManualScopeAsync<BillingDocumentPreview>(caller, account, order, false, false, async (_, token) =>
        {
            if (metadata is null) return BillingDocumentErrors.Invalid;
            if (MetadataRefusal(metadata, clock.GetUtcNow()) is { } refusal) return refusal;
            if (!metadataValidator.Validate(metadata).IsValid) return BillingDocumentErrors.Invalid;
            var accepted = await AcceptedOrderAsync(account, order, token);
            if (accepted is null) return BillingDocumentErrors.OrderNotFound;
            return new BillingDocumentPreview(account, order, accepted, await documents.CountConfirmationsAsync(account, order, token),
                metadata, PreviewDigest(account, order, accepted, metadata));
        }, ct);

    public Task<ErrorOr<BillingDocumentUpload>> UploadAsync(AdminSessionCaller caller, Guid account, Guid order,
        UploadBillingDocument command, string? partFileName, byte[] bytes, CancellationToken ct) =>
        authority.InManualScopeAsync<BillingDocumentUpload>(caller, account, order, true, true, (mfa, token) =>
            SaveAsync(caller, account, order, command, partFileName, bytes, mfa, token), ct);

    public Task<ErrorOr<BillingDocumentVersionView>> RecoverAsync(AdminSessionCaller caller, Guid account, Guid order,
        Guid operationKey, CancellationToken ct) =>
        authority.InManualScopeAsync<BillingDocumentVersionView>(caller, account, order, true, false, async (_, token) =>
        {
            var saved = await documents.FindOperationAsync(account, operationKey, token);
            return saved?.View is { } view && view.UploadedBy == caller.AdminId && view.OrderId == order
                ? view : BillingDocumentErrors.OperationNotFound;
        }, ct);

    private async Task<ErrorOr<BillingDocumentUpload>> SaveAsync(AdminSessionCaller caller, Guid account, Guid order,
        UploadBillingDocument command, string? partFileName, byte[] bytes, DateTimeOffset mfa, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (MetadataRefusal(command, now) is { } refusal) return refusal;
        if (!uploadValidator.Validate(command).IsValid) return BillingDocumentErrors.Invalid;
        if (!PdfStructure.HasEnvelope(bytes)) return BillingDocumentErrors.ContentInvalid;
        if (bytes.Length != command.ByteLength || command.FileName != partFileName
            || Convert.ToHexStringLower(SHA256.HashData(bytes)) != command.Sha256)
            return BillingDocumentErrors.ContentMismatch;

        var hash = CheckoutOrdersV1.Digest(Policy, "upload", account, order, command.OperationKey, command.PreviewDigest,
            command.Confirmed, MetadataDigest(command));
        var existing = await documents.FindOperationAsync(account, command.OperationKey, ct);
        if (existing is not null) return Replay(existing, caller, order, hash);

        var accepted = await AcceptedOrderAsync(account, order, ct);
        if (accepted is null) return BillingDocumentErrors.OrderNotFound;
        if ((await documents.ListAsync(account, order, ct)).Length > 0) return BillingDocumentErrors.AlreadyAttached;
        if (command.PreviewDigest != PreviewDigest(account, order, accepted, command)) return BillingDocumentErrors.PreviewStale;

        var view = new BillingDocumentVersionView(account, order, Guid.NewGuid(), Guid.NewGuid(), 1, null, command.Kind,
            command.FileName, command.MediaType, command.ByteLength, command.Sha256, command.IssuerName, command.IssuerReference,
            command.IssuedOn, command.OperationKey, caller.AdminId, mfa, now);
        if (await documents.InsertAsync(new(view, hash), caller.SessionId, bytes, ct))
        {
            // Read the committed representation, including PostgreSQL's timestamp precision, for exact replay.
            var stored = await documents.FindOperationAsync(account, command.OperationKey, ct);
            return new BillingDocumentUpload(stored!.View, true);
        }
        existing = await documents.FindOperationAsync(account, command.OperationKey, ct);
        return existing is null ? BillingDocumentErrors.AlreadyAttached : Replay(existing, caller, order, hash);
    }

    private static Error? MetadataRefusal(BillingDocumentMetadata value, DateTimeOffset now)
    {
        if (value.ByteLength > BillingDocumentMetadataValidator.MaxBytes) return BillingDocumentErrors.TooLarge;
        if (value.MediaType != BillingDocumentMetadataValidator.PdfMediaType) return BillingDocumentErrors.MediaType;
        return value.IssuedOn > DateOnly.FromDateTime(BillingCalendar.LocalStart(now))
            ? BillingDocumentErrors.Invalid : null;
    }

    private async Task<BillingDocumentOrder?> AcceptedOrderAsync(Guid account, Guid order, CancellationToken ct)
    {
        var quote = (await orders.FindAsync(account, order, false, ct))?.Quote;
        return quote?.Acceptance is { } accepted
            ? new(quote.PlanName, quote.Period, quote.AmountPaise, quote.Currency, accepted.AcceptedAt, quote.Revision, quote.SnapshotHash)
            : null;
    }

    private static ErrorOr<BillingDocumentUpload> Replay(StoredBillingDocument saved, AdminSessionCaller caller, Guid order, string hash) =>
        saved.View.OrderId == order && saved.View.UploadedBy == caller.AdminId && saved.PayloadHash == hash
            ? new BillingDocumentUpload(saved.View, false) : BillingDocumentErrors.PayloadMismatch;

    private static string PreviewDigest(Guid account, Guid order, BillingDocumentOrder accepted, BillingDocumentMetadata metadata) =>
        CheckoutOrdersV1.Digest(Policy, "preview", account, order, accepted.Revision, accepted.SnapshotHash, MetadataDigest(metadata));

    private static string MetadataDigest(BillingDocumentMetadata value) => CheckoutOrdersV1.Digest(value.Kind, value.FileName,
        value.MediaType, value.ByteLength, value.Sha256, value.IssuerName, value.IssuerReference, value.IssuedOn);
}
