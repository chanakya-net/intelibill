using ErrorOr;

namespace Intelibill.Licensing.Application.Payments;

public sealed record StoredPaymentAttempt(Guid AttemptId, Guid AccountId, Guid OrderId, Guid ActorId, string RequestHash,
    PaymentProviderPin Pin, long AmountPaise, string Currency, DateTimeOffset CreatedAt, string? ProviderOrderReference,
    string? LauncherFlow);

public sealed record NewPaymentAttempt(Guid AttemptId, Guid AccountId, Guid OrderId, Guid OperationKey, string RequestHash,
    Guid ReplayId, PaymentProviderPin Pin, long AmountPaise, string Currency, string SnapshotHash, DateTimeOffset CreatedAt);

public sealed record PaidTerm(Guid TermId, string Mode, int Capacity, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    string Period, DateTime AnchorLocal, int Ordinal);

public sealed record PaymentFulfilment(StoredPaymentAttempt Attempt, PaymentObservation Payment, PaidTerm Term,
    Guid PlanVersionId, int MemberAllowance, bool DevelopmentFixture, Guid[] ShopIds, DateTimeOffset At);

public sealed record PaymentShopDelivery(Guid ShopId, long ShopRevision);

public sealed record PaymentStatusRow(StoredPaymentAttempt? Attempt, bool VerificationRequested, DateTimeOffset? VerifiedAt,
    bool DefinitivelyFailed, bool NeedsReview, Guid? TermId, DateTimeOffset? TermStartsAt, DateTimeOffset? TermEndsAt,
    IReadOnlyList<PaymentShopDelivery> Deliveries);

public static class PaymentEventKinds
{
    public const string AttemptStarted = "AttemptStarted";
    public const string SessionRecorded = "SessionRecorded";
    public const string VerificationRequested = "VerificationRequested";
    public const string OrderFulfilled = "OrderFulfilled";
    public const string CheckoutFailed = "CheckoutFailed";
    public const string PaidNeedsReview = "PaidNeedsReview";
}

/// <summary>
/// SQL-owned payment evidence. Delegated calls run inside the caller's delegation transaction; attempt-scoped
/// calls run inside <see cref="InAttemptScopeAsync{T}"/>, a short transaction of their own that never spans a
/// gateway call.
/// </summary>
public interface IPaymentStore
{
    Task<ErrorOr<T>> InAttemptScopeAsync<T>(Guid account, Guid attempt, bool lockAccount,
        Func<CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct);
    Task UseAttemptAsync(Guid attempt, CancellationToken ct);
    Task<StoredPaymentAttempt?> FindAttemptAsync(Guid account, Guid id, bool byOperation, CancellationToken ct);
    Task<StoredPaymentAttempt?> FindOrderAttemptAsync(Guid account, Guid order, CancellationToken ct);
    Task<bool> HasOverlappingPaymentAsync(Guid account, Guid order, CancellationToken ct);
    Task<bool> AnyTrialAsync(Guid account, Guid[] shops, CancellationToken ct);
    Task InsertAttemptAsync(NewPaymentAttempt attempt, CancellationToken ct);
    Task<bool> RecordSessionAsync(StoredPaymentAttempt attempt, CheckoutSession session, DateTimeOffset at, CancellationToken ct);
    Task AppendEventAsync(StoredPaymentAttempt attempt, string kind, Guid? actor, DateTimeOffset at, CancellationToken ct);
    Task<bool> HasDefinitiveFailureAsync(Guid attempt, CancellationToken ct);
    Task<bool> HasVerifiedPaymentAsync(Guid attempt, CancellationToken ct);
    Task RecordVerifiedAsync(StoredPaymentAttempt attempt, PaymentObservation payment, DateTimeOffset at, CancellationToken ct);
    Task FulfilAsync(PaymentFulfilment fulfilment, CancellationToken ct);
    Task<PaymentStatusRow> ReadStatusAsync(Guid account, Guid order, CancellationToken ct);
}
