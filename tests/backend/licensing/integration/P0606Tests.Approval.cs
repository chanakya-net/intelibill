using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0606Tests
{
    [Fact]
    public async Task ApprovalAndRecoveryAuthorizeBeforeReplayingOneIntent()
    {
        using var key = TestRsaKeys.Create(TestRsaKeyPurpose.General);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var (p, term) = await PrepareWithTradingNorthAsync(f);
        var permit = await IssueRealPermitAsync(f, p.Shops[2], EarlyStart, deliverObservation: true);
        var id = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, permit.ValidUntil, "CourtesyBridge", permit.ValidUntil);

        var catalogue = f.Checkout.Trial.Catalogue;
        // Enroll support admin + two Licensing Managers under a single owner session (avoids sign-in rate limit).
        const string supportEmail = "support.p0606@example.test";
        const string mgr1Email = "mgr1.p0606@example.test";
        const string mgr2Email = "mgr2.p0606@example.test";
        string supportSeed, seed1, seed2;
        using (var owner = await catalogue.SignInOwnerAsync())
        {
            supportSeed = await catalogue.EnrollOperatorAsync(owner, supportEmail);
            await P0206Fixture.GrantAsync(owner, supportEmail, "support");
            seed1 = await catalogue.EnrollOperatorAsync(owner, mgr1Email);
            await P0206Fixture.GrantAsync(owner, mgr1Email, "licensing-manager");
            seed2 = await catalogue.EnrollOperatorAsync(owner, mgr2Email);
            await P0206Fixture.GrantAsync(owner, mgr2Email, "licensing-manager");
        }
        catalogue.Clock.Advance(TimeSpan.FromSeconds(30));
        using var support = await catalogue.SignInOperatorAsync(supportSeed, supportEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.SendAsync(HttpMethod.Get, AdminListPath(p.Account))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.SendAsync(HttpMethod.Post, AdminPreviewPath(p.Account, id),
            new { }, await P0206Fixture.ProofAsync(support))).StatusCode);

        using var mgr1 = await catalogue.SignInOperatorAsync(seed1, mgr1Email);
        using var mgr2 = await catalogue.SignInOperatorAsync(seed2, mgr2Email);

        var preview = await AdminPreviewAsync(mgr1, p.Account, id);
        Assert.True(preview.GetProperty("canApprove").GetBoolean());

        // Stale revision/hash refuses with 409 before recording anything.
        using var staleRev = await AdminApproveAsync(mgr1, p.Account, id, Guid.NewGuid(), preview,
            "Verified shop relocation documents", proposalRevision: 999);
        Assert.Equal(HttpStatusCode.Conflict, staleRev.StatusCode);
        Assert.Equal(0L, await f.CountAsync("coverage_replacement_exception_approvals"));

        // Concurrent same-key approves converge on exactly one approval row.
        var opKey = Guid.NewGuid();
        const string reason = "Verified lease transfer and explicit owner consents";
        var results = await Task.WhenAll(
            AdminApproveAsync(mgr1, p.Account, id, opKey, preview, reason),
            AdminApproveAsync(mgr1, p.Account, id, opKey, preview, reason));
        Assert.All(results, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, r.StatusCode.ToString()));
        var bodies = await Task.WhenAll(results.Select(P0206Fixture.BodyAsync));
        Assert.Equal(bodies[0].GetProperty("approvalId").GetGuid(), bodies[1].GetProperty("approvalId").GetGuid());
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals"));
        var approvalId = bodies[0].GetProperty("approvalId").GetGuid();

        // Exact replay with same key succeeds even though exception is now Preparing; different payload returns 409.
        using var replay = await AdminApproveAsync(mgr1, p.Account, id, opKey, preview, reason);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(approvalId, (await P0206Fixture.BodyAsync(replay)).GetProperty("approvalId").GetGuid());

        using var mismatch = await AdminApproveAsync(mgr1, p.Account, id, opKey, preview, "Different reason");
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);

        // Recovery returns the same approval for mgr1, and 404 (zero disclosure) for mgr2.
        using var recovered = await mgr1.SendAsync(HttpMethod.Get, AdminRecoverPath(p.Account, id, opKey));
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(approvalId, (await P0206Fixture.BodyAsync(recovered)).GetProperty("approvalId").GetGuid());

        using var otherMgr = await mgr2.SendAsync(HttpMethod.Get, AdminRecoverPath(p.Account, id, opKey));
        Assert.Equal(HttpStatusCode.NotFound, otherMgr.StatusCode);

        // Advancing MFA age past 5 minutes refuses replay AND recovery BEFORE looking up the committed key!
        catalogue.Clock.Advance(TimeSpan.FromMinutes(5));
        using var staleMfaReplay = await AdminApproveAsync(mgr1, p.Account, id, opKey, preview, reason);
        using var staleMfaRecover = await mgr1.SendAsync(HttpMethod.Get, AdminRecoverPath(p.Account, id, opKey));
        Assert.Equal(HttpStatusCode.Forbidden, staleMfaReplay.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staleMfaRecover.StatusCode);
        Assert.Equal("Coverage.ExceptionRecentMfaRequired", (await P0206Fixture.BodyAsync(staleMfaReplay)).GetProperty("code").GetString());

        // Fresh MFA sign-in restores access to recover the original key.
        using var freshMgr1 = await catalogue.SignInOperatorAsync(seed1, mgr1Email);
        using var recoveredAgain = await freshMgr1.SendAsync(HttpMethod.Get, AdminRecoverPath(p.Account, id, opKey));
        Assert.Equal(HttpStatusCode.OK, recoveredAgain.StatusCode);
        Assert.Equal(approvalId, (await P0206Fixture.BodyAsync(recoveredAgain)).GetProperty("approvalId").GetGuid());
    }
}
