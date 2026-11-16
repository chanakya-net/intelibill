using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed record ConfirmManualPayment(Guid OperationKey, int ExpectedRevision, string SnapshotHash,
    string ReceivingAccountId, string ConfigurationVersion, string Reference, string Evidence,
    DateTimeOffset ReceivedAt, long AmountPaise, string Currency, string Reason, bool Verified);

public sealed record ManualReceivingAccount(string Id, string Label, string Environment,
    string ConfigurationVersion, string EvidencePolicyVersion);

public sealed record ManualPaymentReceipt(Guid ReceiptId, Guid OperationKey, Guid AdminId, DateTimeOffset MfaVerifiedAt,
    DateTimeOffset ReceivedAt, DateTimeOffset VerifiedAt, long AmountPaise, string Currency, string ReceivingAccountId,
    string ConfigurationVersion, string EvidencePolicyVersion, string Environment, string? ReviewReason, Guid? TermId)
{
    public string Source { get; } = "Manual";
    public string Payment { get; } = "Verified";
    public string Fulfilment => TermId is null ? "NeedsReview" : "Fulfilled";
}

public sealed record ManualPaymentOrder(Guid AccountId, Guid OrderId, int Revision, string SnapshotHash,
    string PlanName, string Period, long AmountPaise, string Currency, DateTimeOffset AcceptedAt,
    int ShopCount, CheckoutShopV1[] Shops, Guid? NextShop);
public sealed record ManualPaymentView(ManualPaymentOrder Order, ManualReceivingAccount? ReceivingAccount,
    CheckoutPaymentV1 Status, bool OnlineFundsNeedReview, IReadOnlyList<ManualPaymentReceipt> Receipts);
public sealed record ManualPaymentPreview(ManualPaymentOrder Order, ManualReceivingAccount ReceivingAccount,
    DateTimeOffset ReceivedAt, string? ReviewReason, DateTimeOffset? EstimatedStart, DateTimeOffset? EstimatedEnd);
public sealed record ManualPaymentEvidenceDetails(string Reference, string Evidence, string Reason);
public sealed record ManualPaymentOutcome(ManualPaymentReceipt Receipt, bool Created);
public sealed record StoredManualReceipt(Guid OrderId, ManualPaymentReceipt Receipt, string ProtectedPayload);

public interface IManualPaymentEvidence
{
    ManualReceivingAccount? Account { get; }
    string? ReferenceDigest(ConfirmManualPayment command);
    string Protect(Guid account, Guid order, Guid receipt, ConfirmManualPayment command);
    ManualPaymentEvidenceDetails Read(Guid account, StoredManualReceipt receipt);
    bool Matches(Guid account, StoredManualReceipt receipt, ConfirmManualPayment command);
}

public interface IManualPaymentStore
{
    Task<ErrorOr<T>> InManualScopeAsync<T>(AdminSessionCaller caller, Guid account, Guid order, bool recentMfa,
        bool lockAccount, Func<DateTimeOffset, CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct);
    Task<bool> OnlineFundsNeedReviewAsync(Guid account, Guid order, CancellationToken ct);
    Task<StoredManualReceipt?> FindManualAsync(Guid account, Guid key, bool byOperation, CancellationToken ct);
    Task<IReadOnlyList<ManualPaymentReceipt>> ListManualAsync(Guid account, Guid order, CancellationToken ct);
    Task SaveManualAsync(Guid account, Guid order, AdminSessionCaller caller, ConfirmManualPayment command,
        ManualPaymentReceipt receipt, string digest, string protectedPayload, CancellationToken ct);
}

public static class ManualPaymentErrors
{
    public static Error Invalid => Error.Validation("ManualPayment.Invalid", "Enter one complete verified receipt for this order.");
    public static Error Unavailable => Error.Failure("ManualPayment.Unavailable", "Manual payment confirmation is unavailable.");
    public static Error Denied => Error.Forbidden("ManualPayment.AccessDenied", "Billing capability is required.");
    public static Error RecentMfa => Error.Forbidden("ManualPayment.RecentMfaRequired", "Verify your MFA again to continue.");
    public static Error Missing => Error.NotFound("ManualPayment.OrderNotFound", "This accepted order is unavailable.");
    public static Error OperationMissing => Error.NotFound("ManualPayment.OperationNotFound", "This confirmation is unavailable.");
    public static Error PayloadMismatch => Error.Conflict("ManualPayment.PayloadMismatch", "This confirmation key was already used.");
    public static Error ReferenceConflict => Error.Conflict("ManualPayment.ReferenceConflict", "This payment reference was already recorded.");
}
