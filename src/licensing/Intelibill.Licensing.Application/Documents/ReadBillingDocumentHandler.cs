using System.Security.Cryptography;
using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Documents;

public sealed record BillingDocumentContent(Guid VersionId, string Sha256, byte[] Bytes)
{
    public override string ToString() => "[REDACTED]";
}

public sealed class ReadBillingDocumentHandler(IBillingDelegationAccess access, ICheckoutOrderStore orders,
    IBillingDocumentStore documents)
{
    public Task<ErrorOr<BillingDocumentListV1>> ListAsync(VerifiedBillingDelegation proof, Guid account, Guid order,
        CancellationToken ct, int? beforeVersion = null) =>
        access.ExecuteAsync(proof, (_, token) => ReadListAsync(account, order, beforeVersion, token), ct);

    public Task<ErrorOr<BillingDocumentContent>> ContentAsync(VerifiedBillingDelegation proof, Guid account, Guid order,
        Guid version, CancellationToken ct) =>
        access.ExecuteAsync(proof, (_, token) => ReadContentAsync(account, order, version, token), ct);

    private async Task<ErrorOr<BillingDocumentListV1>> ReadListAsync(Guid account, Guid order, int? beforeVersion,
        CancellationToken ct)
    {
        if (beforeVersion is <= 1)
            return Error.Validation("BillingDocument.Invalid", "The version cursor must be greater than one.");
        if (await orders.FindAsync(account, order, false, ct) is null)
            return Missing;
        var rows = await documents.ListAsync(account, order, beforeVersion, BillingDocumentsV1.PageSize, ct);
        var mapped = rows.Select(ToContract).ToArray();
        return new BillingDocumentListV1(account, order, mapped);
    }

    private async Task<ErrorOr<BillingDocumentContent>> ReadContentAsync(Guid account, Guid order, Guid version,
        CancellationToken ct)
    {
        var stored = await documents.FindContentAsync(account, order, version, ct);
        if (stored is null)
            return Missing;
        if (!MatchesIntegrity(stored, account, order, version))
            return IntegrityFailure;
        return new BillingDocumentContent(stored.View.VersionId, stored.View.Sha256, stored.Content);
    }

    private static bool MatchesIntegrity(StoredBillingDocumentContent stored, Guid account, Guid order, Guid version)
    {
        var view = stored.View;
        var bytes = stored.Content;
        return view.AccountId == account && view.OrderId == order && view.VersionId == version &&
            view.Kind == BillingDocumentsV1.AccountingInvoice && view.MediaType == BillingDocumentsV1.MediaType &&
            bytes.Length is > 0 and <= BillingDocumentsV1.MaxDocumentBytes &&
            bytes.Length == view.ByteLength &&
            bytes.AsSpan().StartsWith("%PDF-"u8) &&
            Convert.ToHexStringLower(SHA256.HashData(bytes)) == view.Sha256;
    }

    private static BillingDocumentV1 ToContract(BillingDocumentVersionView view) => new(
        view.AccountId, view.OrderId, view.DocumentId, view.VersionId, view.Version, view.SupersedesVersionId,
        view.Kind, view.MediaType, view.ByteLength, view.Sha256, view.IssuerName, view.IssuerReference,
        view.IssuedOn, view.UploadedAt);

    private static Error Missing => Error.NotFound(BillingDocumentsV1.NotFound, "The billing document was not found.");
    private static Error IntegrityFailure => Error.Failure("Billing.DocumentIntegrityFailed", "The billing document could not be verified.");
}
