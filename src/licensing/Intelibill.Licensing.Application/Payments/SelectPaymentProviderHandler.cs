using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.BillingAccounts;

namespace Intelibill.Licensing.Application.Payments;

public sealed class SelectPaymentProviderHandler(IPaymentProviderRoutingStore store, IPaymentProviderRegistry registry,
    IPaymentGatewayResolver gateways, IPaymentStore payments, TimeProvider clock)
{
    public Task<ErrorOr<PaymentProviderStatus>> StatusAsync(AdminSessionCaller caller, CancellationToken ct) =>
        store.InAuthorityAsync<PaymentProviderStatus>(caller, false, async (_, token) =>
        {
            var current = await store.SelectedAsync(false, token);
            return new PaymentProviderStatus(1, current, current.Pin is { } pin && gateways.ForPin(pin) is not null, registry.Profiles());
        }, ct);

    public Task<ErrorOr<PaymentProviderSelectionReceipt>> SelectAsync(AdminSessionCaller caller, SelectPaymentProvider? request,
        CancellationToken ct) => store.InAuthorityAsync<PaymentProviderSelectionReceipt>(caller, true,
        (mfa, token) => SelectLockedAsync(caller, request, mfa, token), ct);

    public Task<ErrorOr<PaymentProviderSelectionReceipt>> RecoverAsync(AdminSessionCaller caller, Guid key, CancellationToken ct) =>
        store.InAuthorityAsync<PaymentProviderSelectionReceipt>(caller, true, async (_, token) =>
            await store.OperationAsync(key, token) is { Receipt: var prior } && prior.AdminId == caller.AdminId
                ? prior : PaymentProviderErrors.Missing, ct);

    public Task<ErrorOr<PaymentRoutingDetail>> DetailAsync(AdminSessionCaller caller, Guid account, Guid order, CancellationToken ct) =>
        store.InAuthorityAsync<PaymentRoutingDetail>(caller, false, async (_, token) =>
        {
            await store.ScopeOrderAsync(account, order, token);
            var attempt = await payments.FindOrderAttemptAsync(account, order, token);
            var current = await store.SelectedAsync(false, token);
            var available = attempt is not null && gateways.ForPin(attempt.Pin) is not null;
            var blocker = attempt is null || available ? null : registry.Profiles().FirstOrDefault(p => p.Pin == attempt.Pin)?.Blocker ?? "MissingProfile";
            return new PaymentRoutingDetail(1, account, order, attempt?.AttemptId, attempt?.Pin, current, available, blocker);
        }, ct);

    private async Task<ErrorOr<PaymentProviderSelectionReceipt>> SelectLockedAsync(AdminSessionCaller caller,
        SelectPaymentProvider? request, DateTimeOffset mfa, CancellationToken ct)
    {
        if (request is not { OperationKey: var key, ExpectedRevision: >= 0, ProfileId.Length: > 0 and <= 80, Reason.Length: >= 3 and <= 255 }
            || key == Guid.Empty || request.Reason.Trim().Length < 3)
            return PaymentProviderErrors.Invalid;
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        var previous = await store.LockSelectionAsync(ct);
        if (await store.OperationAsync(key, ct) is { } prior)
            return prior.RequestHash == hash && prior.Receipt.AdminId == caller.AdminId ? prior.Receipt : BillingAccountErrors.PayloadMismatch;
        if (previous.Revision != request.ExpectedRevision) return PaymentProviderErrors.Changed;
        var matches = registry.Profiles().Where(p => p.Id == request.ProfileId).ToArray();
        if (matches is not [{ Available: true } profile] || gateways.ForPin(profile.Pin) is null) return PaymentProviderErrors.Unavailable;
        var receipt = new PaymentProviderSelectionReceipt(1, key, caller.AdminId, mfa,
            BillingAccountRules.StorageInstant(clock.GetUtcNow()), previous.Revision + 1, profile.Id, profile.Pin);
        return await store.SaveAsync(previous, hash, request.Reason, receipt, ct) ? receipt : PaymentProviderErrors.Unavailable;
    }
}
