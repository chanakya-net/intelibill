using ErrorOr;

namespace Intelibill.Licensing.Application.Documents;

public record BillingDocumentMetadata
{
    public string Kind { get; init; } = "";
    public string FileName { get; init; } = "";
    public string MediaType { get; init; } = "";
    public int ByteLength { get; init; }
    public string Sha256 { get; init; } = "";
    public string IssuerName { get; init; } = "";
    public string IssuerReference { get; init; } = "";
    public DateOnly IssuedOn { get; init; }

    public override string ToString() => "[REDACTED]";
}

public sealed record UploadBillingDocument : BillingDocumentMetadata
{
    public Guid OperationKey { get; init; }
    public string PreviewDigest { get; init; } = "";
    public bool Confirmed { get; init; }

    public override string ToString() => "[REDACTED]";
}

public sealed record BillingDocumentOrder(string PlanName, string Period, long AmountPaise, string Currency,
    DateTimeOffset AcceptedAt, int Revision, string SnapshotHash);

public sealed record BillingDocumentList(Guid AccountId, Guid OrderId, BillingDocumentOrder Order,
    int PaymentConfirmations, BillingDocumentVersionView[] Documents)
{
    public int SchemaVersion { get; } = 1;
    public override string ToString() => "[REDACTED]";
}

public sealed record BillingDocumentPreview(Guid AccountId, Guid OrderId, BillingDocumentOrder Order,
    int PaymentConfirmations, BillingDocumentMetadata Metadata, string PreviewDigest)
{
    public int SchemaVersion { get; } = 1;
    public int Version { get; } = 1;
    public override string ToString() => "[REDACTED]";
}

public sealed record BillingDocumentVersionView(Guid AccountId, Guid OrderId, Guid DocumentId, Guid VersionId,
    int Version, Guid? SupersedesVersionId, string Kind, string FileName, string MediaType, int ByteLength,
    string Sha256, string IssuerName, string IssuerReference, DateOnly IssuedOn, Guid OperationKey,
    Guid UploadedBy, DateTimeOffset MfaVerifiedAt, DateTimeOffset UploadedAt)
{
    public int SchemaVersion { get; } = 1;
    public override string ToString() => "[REDACTED]";
}

public sealed record StoredBillingDocument(BillingDocumentVersionView View, string PayloadHash);
public sealed record BillingDocumentUpload(BillingDocumentVersionView Document, bool Created);

public interface IBillingDocumentStore
{
    Task<BillingDocumentVersionView[]> ListAsync(Guid account, Guid order, CancellationToken ct);
    Task<StoredBillingDocument?> FindOperationAsync(Guid account, Guid key, CancellationToken ct);
    Task<int> CountConfirmationsAsync(Guid account, Guid order, CancellationToken ct);
    Task<bool> InsertAsync(StoredBillingDocument document, Guid session, byte[] content, CancellationToken ct);
}

public static class BillingDocumentErrors
{
    public static Error Invalid => Error.Validation("BillingDocument.Invalid", "Check the invoice metadata and confirmation.");
    public static Error ContentInvalid => Error.Validation("BillingDocument.ContentInvalid", "Select a valid PDF.");
    public static Error ContentMismatch => Error.Validation("BillingDocument.ContentMismatch", "The PDF differs from the reviewed file.");
    public static Error TooLarge => Error.Validation("BillingDocument.TooLarge", "PDFs must be no larger than 10 MiB.");
    public static Error MediaType => Error.Validation("BillingDocument.MediaTypeUnsupported", "Only PDF content is accepted.");
    public static Error OrderNotFound => Error.NotFound("BillingDocument.OrderNotFound", "The accepted order was not found for this account.");
    public static Error OperationNotFound => Error.NotFound("BillingDocument.OperationNotFound", "The upload was not found.");
    public static Error AlreadyAttached => Error.Conflict("BillingDocument.AlreadyAttached", "An accounting invoice already exists for this order.");
    public static Error PayloadMismatch => Error.Conflict("BillingDocument.PayloadMismatch", "This operation key belongs to different upload evidence.");
    public static Error PreviewStale => Error.Conflict("BillingDocument.PreviewStale", "Review the order and invoice again before attaching.");
}
