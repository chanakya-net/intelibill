using System.Net;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient
{
    private static Error CancellationInvalid => Error.Validation("Billing.CancellationInvalid", "Confirm the current term end and affected shops.");

    public Task<ErrorOr<SubscriptionCancellationDetailV1>> ReadCancellationAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid? after, CancellationToken ct) =>
        RunAsync<SubscriptionCancellationDetailV1>(caller, new(SubscriptionCancellationsV1.Read, HttpMethod.Get,
            SubscriptionCancellationsV1.DetailPath(account, term, after), account, []),
            SubscriptionCancellationsV1.MaximumResponseBytes, async (response, token) =>
            {
                var value = await ReadJsonAsync<SubscriptionCancellationDetailV1>(response, token);
                return value is not null && IsCancellationDetailValid(value, account, term, after)
                    ? value : Unavailable("InvalidCancellationContract");
            }, ct);

    public Task<ErrorOr<SubscriptionCancellationOutcomeV1>> ScheduleCancellationAsync(MerchantBillingCaller caller, Guid account,
        Guid term, ScheduleSubscriptionCancellationV1 request, CancellationToken ct) =>
        request.IsValid() ? CancellationOutcomeAsync(caller, account, term, request.OperationKey,
            new(SubscriptionCancellationsV1.Schedule, HttpMethod.Post, SubscriptionCancellationsV1.Path(account, term),
                account, JsonSerializer.SerializeToUtf8Bytes(request, Json)), request, ct)
            : Task.FromResult<ErrorOr<SubscriptionCancellationOutcomeV1>>(CancellationInvalid);

    public Task<ErrorOr<SubscriptionCancellationOutcomeV1>> RecoverCancellationAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid operation, CancellationToken ct) =>
        CancellationOutcomeAsync(caller, account, term, operation, new(SubscriptionCancellationsV1.Recover,
            HttpMethod.Get, SubscriptionCancellationsV1.OperationPath(account, term, operation), account, []), null, ct);

    private Task<ErrorOr<SubscriptionCancellationOutcomeV1>> CancellationOutcomeAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid key, BillingDelegationIntent intent, ScheduleSubscriptionCancellationV1? request, CancellationToken ct) =>
        RunAsync<SubscriptionCancellationOutcomeV1>(caller, intent, MaximumResponseBytes, async (response, token) =>
        {
            var value = await ReadJsonAsync<SubscriptionCancellationOutcomeV1>(response, token);
            return value is { SchemaVersion: 1, AffectedShopCount: > 0, Cancellation: { } change }
                && value.AccountId == account && value.TermId == term && value.OperationKey == key
                && IsCancellationValid(change, change.EffectiveAt)
                && (request is null || request.AcceptedEffectiveAt == change.EffectiveAt && request.AffectedShopCount == value.AffectedShopCount)
                ? value : Unavailable("InvalidCancellationContract");
        }, ct);

    public Task<ErrorOr<SubscriptionCancellationWithdrawalOutcomeV1>> WithdrawCancellationAsync(MerchantBillingCaller caller,
        Guid account, Guid term, WithdrawSubscriptionCancellationV1 request, CancellationToken ct) =>
        request.IsValid() ? WithdrawalOutcomeAsync(caller, account, term, request.OperationKey,
            new(SubscriptionCancellationsV1.Withdraw, HttpMethod.Post, SubscriptionCancellationsV1.WithdrawalPath(account, term),
                account, JsonSerializer.SerializeToUtf8Bytes(request, Json)), request, ct)
            : Task.FromResult<ErrorOr<SubscriptionCancellationWithdrawalOutcomeV1>>(CancellationInvalid);

    public Task<ErrorOr<SubscriptionCancellationWithdrawalOutcomeV1>> RecoverCancellationWithdrawalAsync(
        MerchantBillingCaller caller, Guid account, Guid term, Guid operation, CancellationToken ct) =>
        WithdrawalOutcomeAsync(caller, account, term, operation, new(SubscriptionCancellationsV1.WithdrawRecover,
            HttpMethod.Get, SubscriptionCancellationsV1.WithdrawalOperationPath(account, term, operation), account, []), null, ct);

    private Task<ErrorOr<SubscriptionCancellationWithdrawalOutcomeV1>> WithdrawalOutcomeAsync(MerchantBillingCaller caller,
        Guid account, Guid term, Guid key, BillingDelegationIntent intent, WithdrawSubscriptionCancellationV1? request,
        CancellationToken ct) =>
        RunAsync<SubscriptionCancellationWithdrawalOutcomeV1>(caller, intent, MaximumResponseBytes, async (response, token) =>
        {
            var value = await ReadJsonAsync<SubscriptionCancellationWithdrawalOutcomeV1>(response, token);
            return value is not null && IsWithdrawalValid(value, account, term, key)
                && (request is null || request.ChangeId == value.WithdrawnChangeId && request.CurrentVersion + 1 == value.Version
                    && request.AcceptedEffectiveAt == value.EffectiveAt)
                ? value : Unavailable("InvalidCancellationContract");
        }, ct);

    // A withdrawal is the even revision after its schedule, recorded strictly before the unchanged boundary.
    private static bool IsWithdrawalValid(SubscriptionCancellationWithdrawalOutcomeV1 value, Guid account, Guid term, Guid key) =>
        value is { SchemaVersion: 1, AffectedShopCount: > 0, Version: > 0 } && SubscriptionCancellationsV1.IsOpenVersion(value.Version)
        && value.AccountId == account && value.TermId == term && value.OperationKey == key && value.WithdrawnChangeId != Guid.Empty
        && EntitlementSnapshotsV1.IsInstant(value.EffectiveAt) && EntitlementSnapshotsV1.IsInstant(value.WithdrawnAt)
        && value.WithdrawnAt < value.EffectiveAt;

    private static bool IsCancellationDetailValid(SubscriptionCancellationDetailV1 value, Guid account, Guid term, Guid? after)
    {
        if (value is not { SchemaVersion: 1, NoRefund: true, NoGrace: true, TimeZone: "Asia/Kolkata", Basis: "Paid", AffectedShopCount: > 0,
                AffectedShops: { Length: <= SubscriptionCancellationsV1.PageSize } shops,
                Commercial: { PricePaise: >= 0, Currency: "INR" }, PolicyVersion: SubscriptionCancellationsV1.Policy }
            || value.AccountId != account || value.TermId != term || shops.Length > value.AffectedShopCount
            || !EntitlementSnapshotsV1.IsInstant(value.EffectiveAt) || !SubscriptionCancellationsV1.IsDigest(value.AffectedShopSetHash))
            return false;
        if (value.ChangeId is null ? !SubscriptionCancellationsV1.IsOpenVersion(value.Version) || !SubscriptionCancellationsV1.IsDigest(value.SourceHash)
            : value.ChangeId == Guid.Empty || !SubscriptionCancellationsV1.IsScheduledVersion(value.Version) || value.SourceHash is not null)
            return false;

        var prior = after;
        foreach (var shop in shops)
        {
            if (shop is not { ShopName.Length: > 0 and <= SubscriptionCancellationsV1.MaximumShopNameLength } || shop.ShopId == Guid.Empty || prior >= shop.ShopId)
                return false;
            prior = shop.ShopId;
        }

        return value.NextCursor is null || (shops.Length == SubscriptionCancellationsV1.PageSize && value.NextCursor == prior);
    }

    internal static bool IsCancellationValid(SubscriptionCancellationSummaryV1 change, DateTimeOffset paidThrough) =>
        change is { PolicyVersion: SubscriptionCancellationsV1.Policy } && SubscriptionCancellationsV1.IsScheduledVersion(change.Version)
        && change.ChangeId != Guid.Empty && change.EffectiveAt == paidThrough && EntitlementSnapshotsV1.IsInstant(change.EffectiveAt);

    private Error MapCancellationFailure(HttpStatusCode status, string? code) => (status, code) switch
    {
        (HttpStatusCode.BadRequest, "Billing.CancellationInvalid") => CancellationInvalid,
        (HttpStatusCode.Conflict, "Billing.CancellationConflict") => Error.Conflict(code, "The term or affected shops changed."),
        (HttpStatusCode.Conflict, "Billing.CancellationEffective") => Error.Conflict(code, "The cancellation has already taken effect."),
        (HttpStatusCode.Conflict, "Billing.OperationPayloadMismatch") => MerchantBillingErrors.PayloadMismatch,
        (HttpStatusCode.NotFound, "Billing.CancellationNotFound") => Error.NotFound(code, "Cancellation details are unavailable."),
        _ => Unavailable("UpstreamStatus"),
    };
}
