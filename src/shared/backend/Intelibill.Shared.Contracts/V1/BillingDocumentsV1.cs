using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

/// <summary>
/// Customer-facing private billing-document metadata and download contract. Exposes only accounting document identity,
/// issuer metadata and integrity digest; internal operator, MFA, operation-key and original upload filename facts stay private.
/// </summary>
public static class BillingDocumentsV1
{
    public const string Policy = "billing-document/1";
    public const string List = "billing.document.list";
    public const string Download = "billing.document.download";
    public const string AccountingInvoice = "AccountingInvoice";
    public const string MediaType = "application/pdf";
    public const int MaxDocumentBytes = 10 * 1024 * 1024;
    public const int PageSize = 25;
    // 25 maximum-length UTF-8 issuer rows fit well under 64 KiB.
    public const int MaxListBytes = 65536;
    public const int MaxIssuerNameLength = 200;
    public const int MaxIssuerReferenceLength = 64;
    public const string NotFound = "Billing.DocumentNotFound";
    public const string DigestHeader = "X-Document-Sha256";
    public static readonly DateOnly EarliestIssueDate = new(2000, 1, 1);

    public static bool IsAction(string action) => action is List or Download;

    public static string ListPath(Guid account, Guid order) =>
        $"{CheckoutOrdersV1.DetailPath(account, order)}/billing-documents";

    public static string ContentPath(Guid account, Guid order, Guid version) =>
        $"{ListPath(account, order)}/{version:D}/content";

    public static string FileName(Guid versionId) => $"accounting-invoice-{versionId:D}.pdf";

    public static bool IsValid(BillingDocumentV1? value, Guid account, Guid order) =>
        value is not null &&
        value.AccountId == account &&
        value.OrderId == order &&
        value.DocumentId != Guid.Empty &&
        value.VersionId != Guid.Empty &&
        value.Version >= 1 &&
        (value.Version == 1) == (value.SupersedesVersionId is null) &&
        (value.SupersedesVersionId is null || (value.SupersedesVersionId != Guid.Empty && value.SupersedesVersionId != value.VersionId)) &&
        value.Kind == AccountingInvoice &&
        value.MediaType == MediaType &&
        value.ByteLength is > 0 and <= MaxDocumentBytes &&
        CheckoutOrdersV1.IsHash(value.Sha256) &&
        IsIssuerField(value.IssuerName, MaxIssuerNameLength) &&
        IsIssuerField(value.IssuerReference, MaxIssuerReferenceLength) &&
        value.IssuedOn >= EarliestIssueDate &&
        EntitlementSnapshotsV1.IsInstant(value.UploadedAt);

    public static bool IsValidList(BillingDocumentListV1? value, Guid account, Guid order) =>
        value is not null &&
        value.SchemaVersion == 1 &&
        value.AccountId == account &&
        value.OrderId == order &&
        value.Documents is { Length: <= PageSize } rows &&
        rows.All(row => IsValid(row, account, order)) &&
        rows.Select(row => row.VersionId).Distinct().Count() == rows.Length;

    private static bool IsIssuerField(string? value, int max) =>
        value is { Length: > 0 } && value.Length <= max &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
        !value.Any(char.IsControl);
}

public sealed record BillingDocumentV1(
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("orderId")] Guid OrderId,
    [property: JsonPropertyName("documentId")] Guid DocumentId,
    [property: JsonPropertyName("versionId")] Guid VersionId,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("supersedesVersionId")] Guid? SupersedesVersionId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("byteLength")] int ByteLength,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("issuerName")] string IssuerName,
    [property: JsonPropertyName("issuerReference")] string IssuerReference,
    [property: JsonPropertyName("issuedOn")] DateOnly IssuedOn,
    [property: JsonPropertyName("uploadedAt")] DateTimeOffset UploadedAt)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record BillingDocumentListV1(
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("orderId")] Guid OrderId,
    [property: JsonPropertyName("documents")] BillingDocumentV1[] Documents)
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    public override string ToString() => "[REDACTED]";
}
