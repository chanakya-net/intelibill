using System.Data;
using ErrorOr;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Infrastructure.Billing;

public interface ICoverageTransferAuthorityReceiver
{
    Task<ErrorOr<CoverageTransferShopAuthorityV1>> CheckAsync(CoverageTransferShopAuthorityCheckV1 check,
        VerifiedServiceAssertion proof, CancellationToken ct);
}

/// <summary>
/// Answers licensing's signed review-time question about the named shop. Under the shop projection fence it locks
/// user → shop → membership and reads the live verified owner, the exact accepted consent, any later revocation,
/// the shop's actual affiliation and its actual unexpired permits. The answer proves one instant only.
/// </summary>
internal sealed class MerchantCoverageTransferAuthority(
    ApplicationDbContext db, ShopLicenceFence fence, EntitlementProjectionStore projection,
    IMembershipAuthorityRepository memberships, IAuthSessionRepository sessions, TimeProvider transportClock,
    [FromKeyedServices(ShopTrialsV1.DeliverySection)] TimeProvider businessClock) : ICoverageTransferAuthorityReceiver
{
    private static Error Unavailable => Error.Failure("Coverage.TransferAuthorityUnavailable");

    public async Task<ErrorOr<CoverageTransferShopAuthorityV1>> CheckAsync(CoverageTransferShopAuthorityCheckV1 c,
        VerifiedServiceAssertion proof, CancellationToken ct)
    {
        if (!c.IsValid() || c.Deadline <= transportClock.GetUtcNow() || c.Deadline > proof.ExpiresAt)
            return Unavailable;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {ShopLicenceFence.LockTimeout}, true)", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT set_config('app.active_shop_id', {c.ShopId.ToString("D")}, true),
                    set_config('app.entitlement_account_id', {c.AccountId.ToString("D")}, true)
                """, ct);
            if ((await fence.EnterProjectionAsync(c.AccountId, c.ShopId, ct)).IsError || !await projection.ConsumeAsync(proof, ct))
                return Unavailable;

            var user = await sessions.LockUserAuthorityAsync(c.ExpectedActorId, ct);
            await memberships.LockShopAsync(c.ShopId, ct);
            await memberships.LockMembershipAsync(c.ShopId, c.ExpectedActorId, ct);
            var now = EntitlementSnapshotsV1.Instant(businessClock);
            var seen = await InspectAsync(c, now, ct);
            if (seen is null || transportClock.GetUtcNow() >= c.Deadline) return Unavailable;
            var reason = user is { IsLoginEnabled: true } ? seen.Reason : "ShopOwnerChanged";
            await tx.CommitAsync(ct);
            return new CoverageTransferShopAuthorityV1(CoverageTransfersV1.SchemaVersion, c.Challenge, c.TransferId, c.ShopId,
                c.ExpectedConsentEventId, reason is null, reason, seen.PermitCount, seen.MaxPermitValidUntil, now);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return Unavailable;
        }
    }

    private Task<Inspection?> InspectAsync(CoverageTransferShopAuthorityCheckV1 c, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<Inspection>($"""
            SELECT reason, permit_count, max_permit_valid_until FROM public.coverage_transfer_shop_inspect(
                {c.AccountId}, {c.ShopId}, {c.AffiliationRequestId}, {c.TransferId}, {c.ExpectedConsentEventId},
                {c.ExpectedActorId}, {c.ExpectedMembershipId}, {c.ExpectedAuthorityVersion}, {c.ProposalRevision},
                {c.TermsDigest}, {now})
            """).SingleOrDefaultAsync(ct);

    private sealed record Inspection(string? Reason, int PermitCount, DateTimeOffset? MaxPermitValidUntil);
}
