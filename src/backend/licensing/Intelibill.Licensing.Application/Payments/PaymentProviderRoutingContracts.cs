using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;

namespace Intelibill.Licensing.Application.Payments;

public sealed record PaymentProviderProfileStatus(string Id, PaymentProviderPin Pin, bool Available, string? Blocker);
public sealed record PaymentProviderSelection(long Revision, string? ProfileId, PaymentProviderPin? Pin);
public sealed record PaymentProviderStatus(int SchemaVersion, PaymentProviderSelection Selection, bool Available,
    IReadOnlyList<PaymentProviderProfileStatus> Profiles);
public sealed record SelectPaymentProvider(Guid OperationKey, long ExpectedRevision, string ProfileId, string Reason);
public sealed record PaymentProviderSelectionReceipt(int SchemaVersion, Guid OperationKey, Guid AdminId,
    DateTimeOffset MfaVerifiedAt, DateTimeOffset SelectedAt, long Revision, string ProfileId, PaymentProviderPin Pin);
public sealed record StoredProviderSelection(string RequestHash, PaymentProviderSelectionReceipt Receipt);
public sealed record PaymentRoutingDetail(int SchemaVersion, Guid AccountId, Guid OrderId, Guid? AttemptId,
    PaymentProviderPin? Original, PaymentProviderSelection CurrentSelection, bool RecoveryAvailable, string? Blocker);

public interface IPaymentProviderRegistry
{
    IReadOnlyList<PaymentProviderProfileStatus> Profiles();
}

public interface IPaymentProviderSelection
{
    /// <summary>With intention=true, holds the routing lock until the caller commits its payment intention.</summary>
    Task<PaymentProviderSelection> SelectedAsync(bool intention, CancellationToken ct);
}

public interface IPaymentProviderRoutingStore : IPaymentProviderSelection
{
    Task<ErrorOr<T>> InAuthorityAsync<T>(AdminSessionCaller caller, bool selection,
        Func<DateTimeOffset, CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct);
    Task<PaymentProviderSelection> LockSelectionAsync(CancellationToken ct);
    Task<StoredProviderSelection?> OperationAsync(Guid key, CancellationToken ct);
    Task<bool> SaveAsync(PaymentProviderSelection previous, string hash, string reason,
        PaymentProviderSelectionReceipt receipt, CancellationToken ct);
    Task ScopeOrderAsync(Guid account, Guid order, CancellationToken ct);
}

public static class PaymentProviderErrors
{
    public static readonly Error Denied = Error.Forbidden("PaymentProvider.Denied", "Provider routing capability is required.");
    public static readonly Error RecentMfa = Error.Forbidden("PaymentProvider.RecentMfaRequired", "Verify MFA again before selecting a provider.");
    public static readonly Error Invalid = Error.Validation("PaymentProvider.Invalid", "A configured profile, operation key, revision and reason are required.");
    public static readonly Error Changed = Error.Conflict("PaymentProvider.Changed", "Provider selection changed. Reload its revision.");
    public static readonly Error Unavailable = Error.Conflict("PaymentProvider.ConfigurationUnavailable", "The exact provider configuration is unavailable.");
    public static readonly Error Missing = Error.NotFound("PaymentProvider.OperationMissing", "No selection was recorded for this operation.");
}
