using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;
using System.Security.Cryptography;

namespace Intelibill.Licensing.Application.Restrictions;

public sealed partial class CloseShopHandler(IBillingDelegationAccess access, IClosureStore store,
    IMerchantClosureValidation merchant, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = EntitlementSnapshotsV1.Json;

    public Task<ErrorOr<ClosureSavedV1>> CloseAsync(VerifiedBillingDelegation proof, Guid shop,
        ShopClosureRequestV1 input, CancellationToken ct) => access.ExecuteAsync<ClosureSavedV1>(proof, async (scope, token) =>
    {
        if (input.AccountId == Guid.Empty || input.Target?.ShopId != shop || input.OperationKey == Guid.Empty ||
            !ShopClosuresV1.IsDigest(input.WorkDigest))
            return Stale;

        await scope.UseAccountAsync(input.AccountId, token);
        await store.LockAccountAsync(input.AccountId, token);
        if (clock.GetUtcNow() >= proof.ExpiresAt) return BillingDelegationErrors.Invalid;

        var targets = await store.TargetsAsync(input.AccountId, shop, token);
        if (targets is not [var actual] || actual.AffiliationRequestId != input.Target.AffiliationRequestId)
            return Stale;
        var digest = Digest(input);
        var saved = await store.ReadOperationAsync(input.AccountId, input.OperationKey, token);
        if (saved is not null && (saved.ActorId != scope.ActorId || saved.ShopId != shop || saved.RequestDigest != digest))
            return Stale;
        if (saved is null && (actual.ClosureVersion != 0 || actual.ClosureVersion != input.Target.ClosureVersion))
            return Stale;

        var validation = await RequireConsentAsync(input.AccountId, input.OperationKey, [input.Target], scope.ActorId,
            saved is not null, proof.ExpiresAt, token);
        if (validation.IsError) return validation.Errors;
        if (validation.Value.Targets is not [var consent] || consent.WorkDigest != input.WorkDigest)
            return Stale;
        return saved is not null ? await store.ReadSavedAsync(saved, token) :
            await store.SaveAsync(new(Guid.NewGuid(), input.AccountId, scope.ActorId, shop, input.OperationKey,
                digest, EntitlementSnapshotsV1.Instant(clock)), [input.Target], validation.Value, token);
    }, ct);

    private async Task<ErrorOr<ClosureValidatedV1>> RequireConsentAsync(Guid account, Guid proposal, ClosureTargetV1[] targets,
        Guid? actor, bool recovering, DateTimeOffset proofExpiresAt, CancellationToken ct)
    {
        var deadline = clock.GetUtcNow().AddSeconds(8);
        if (proofExpiresAt < deadline) deadline = proofExpiresAt;
        var request = new ClosureValidationV1(account, proposal, Guid.NewGuid(), deadline, Digest(targets), targets, actor, recovering);
        var result = await merchant.CheckAsync(request, ct);
        if (clock.GetUtcNow() >= deadline || (recovering && result.IsError && result.FirstError.Code == ShopClosuresV1.ReviewChangedCode))
            return Error.Failure("Closure.MerchantUnavailable", "A current merchant confirmation could not be verified.");
        return result;
    }

    private static string Digest<T>(T input) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input, Json)));
    private static Error Stale => Error.Conflict(ShopClosuresV1.ReviewChangedCode, "The closure scope or review changed.");
}
