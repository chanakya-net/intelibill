using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Refunds;

/// <summary>Account-scoped read queries for customer refund request status and read-only admin billing intake.</summary>
public sealed class ReadRefundRequestsHandler(
    IBillingDelegationAccess access,
    IManualPaymentStore admins,
    IRefundRequestStore store)
{
    public Task<ErrorOr<RefundRequestPageV1>> ListAsync(
        VerifiedBillingDelegation proof, Guid account, Guid? after, CancellationToken ct) =>
        access.ExecuteAsync<RefundRequestPageV1>(proof, async (_, token) =>
            await store.PageAsync(account, after, token), ct);

    public Task<ErrorOr<RefundIntakePage>> IntakeAsync(
        AdminSessionCaller caller, Guid account, Guid? after, CancellationToken ct, Guid? request = null) =>
        admins.InManualScopeAsync<RefundIntakePage>(caller, account, Guid.Empty, recentMfa: false, lockAccount: false,
            async (_, token) => await store.IntakeAsync(account, after, token, request), ct);
}
