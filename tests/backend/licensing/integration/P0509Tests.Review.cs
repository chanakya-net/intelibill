using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>Missed-suspension phases: monotonic cutoff, first-receipt immutability, and restricted review.</summary>
public sealed partial class P0509Tests
{
    private const int ReplayDelaySeconds = 30;

    private const string NotAllowed = "Reconciliation.ActionNotAllowed: This action is not available for the case.";

    /// <summary>
    /// The request validator rejects a name outside <c>ReconciliationAction</c> before any case policy runs. On the
    /// wire its failure is keyed by the request property, with the <c>Reconciliation.UnknownAction</c> description.
    /// </summary>
    private const string UnknownAction = "Action: The action is not recognised.";

    private static readonly (string Action, string Refusal)[] RefusedReviewActions =
    [
        ("RetryHistoricalLease", NotAllowed),
        ("LinkExistingSale", NotAllowed),
        ("PostAnyway", UnknownAction),
        ("Redate", UnknownAction),
        ("Renumber", UnknownAction),
        ("Delete", UnknownAction),
    ];

    private static void SetMerchantAndBusinessClocks(P0213Fixture fixture, P0201Clock businessClock, DateTimeOffset at)
    {
        fixture.Hosts.Fixture.Clock.Now = at;
        businessClock.Now = at;
    }

    private static string SignedPermitSha256(JsonElement wireSale) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(wireSale.GetProperty("licenceEvidence").GetProperty("signedPermit").GetString()!)));

    /// <summary>
    /// Suspends at the next whole second strictly after both the merchant clock and real time, so no clock moves
    /// backward and the cutoff is later than the notBefore of the permit the browser already holds.
    /// </summary>
    private static async Task<DateTimeOffset> ApplyAcknowledgedTradingSuspensionAsync(
        P0213Fixture fixture, P0201Clock businessClock, P0110CookieBrowser securityAdmin, Guid account, Guid shopId)
    {
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var merchantNow = fixture.Hosts.Fixture.Clock.Now;
        var realNow = DateTimeOffset.UtcNow;
        var latest = merchantNow > realNow ? merchantNow : realNow;
        var suspendAt = DateTimeOffset.FromUnixTimeSeconds(latest.ToUnixTimeSeconds() + 1);
        Assert.True(suspendAt > merchantNow && suspendAt >= businessClock.Now, "Suspension must not move a clock backward.");
        SetMerchantAndBusinessClocks(fixture, businessClock, suspendAt);

        var restrictionId = await P0404Tests.SuspendAsync(securityAdmin, path, "Shop", shopId, "Trading");
        await P0403Tests.AssertPropagationAsync(securityAdmin, path, restrictionId, "Pending");
        await DrainBothAsync(fixture);
        await P0403Tests.AssertPropagationAsync(securityAdmin, path, restrictionId, "Acknowledged");
        var cutoff = (DateTime)(await fixture.MerchantScalarAsync(
            $"SELECT suspension_effective_at FROM public.shop_license_states WHERE shop_id = '{shopId:D}'"))!;
        return new DateTimeOffset(DateTime.SpecifyKind(cutoff, DateTimeKind.Utc));
    }

    /// <summary>The verdict the app stored in IndexedDB from the real upload answer: held for review, one case.</summary>
    private static Guid AssertBrowserHeldForReview(JsonElement synced)
    {
        Assert.Equal(("NeedsReview", "OfflineLicenceHistory.RestrictionConflict"),
            (synced.GetProperty("queueStatusAfter").GetString(), synced.GetProperty("errorCode").GetString()));
        Assert.Equal(200, synced.GetProperty("httpStatus").GetInt32());
        return synced.GetProperty("reviewCaseId").GetGuid();
    }

    /// <summary>
    /// Snapshots the first receipt and case, moves both clocks past the first receipt, races two byte-identical
    /// replays, then requires the same row: a rewrite or delete-and-reinsert would change id or received_at.
    /// </summary>
    private static async Task<(SubmissionEvidence Submission, DateTimeOffset ReplayAt)> AssertReceiptSurvivesReplayRaceAsync(
        P0213Fixture fixture, P0201Clock businessClock, ReviewActors actors, HeldCase held, JsonElement wireSale, CancellationToken ct)
    {
        var before = await AssertHeldReviewDatabaseStateAsync(fixture, held);
        var caseBefore = await ReadCaseAsync(actors.Owner, held.CaseId, ct);

        var replayAt = held.SyncAt.AddSeconds(ReplayDelaySeconds);
        SetMerchantAndBusinessClocks(fixture, businessClock, replayAt);
        await RaceDuplicateSuspensionSyncAsync(actors.Owner, actors.Owner2, held.Permit.DeviceId, wireSale, held.CaseId, ct);

        var after = await AssertHeldReviewDatabaseStateAsync(fixture, held);
        Assert.Equal((before.Id, held.SyncAt, before.PayloadHash, before.PayloadJson),
            (after.Id, after.ReceivedAt, after.PayloadHash, after.PayloadJson));
        Assert.Equal(caseBefore, await ReadCaseAsync(actors.Owner, held.CaseId, ct));
        return (after, replayAt);
    }

    private static async Task RaceDuplicateSuspensionSyncAsync(
        HttpClient ownerClient, HttpClient ownerClient2, string deviceId, JsonElement wireSale, Guid caseId, CancellationToken ct)
    {
        var raced = await RunBarrierRaceAsync(
            () => SyncSingleAsync(ownerClient, deviceId, wireSale, ct),
            () => SyncSingleAsync(ownerClient2, deviceId, wireSale, ct));
        Assert.All(raced, row => Assert.Equal(("NeedsReview", "OfflineLicenceHistory.RestrictionConflict", caseId), (
            row.GetProperty("status").GetString(),
            row.GetProperty("errors")[0].GetProperty("code").GetString(),
            row.GetProperty("reviewCaseId").GetGuid())));
    }

    /// <summary>One receipt received at the first sync, one case with the browser's case id, nothing posted.</summary>
    private static async Task<SubmissionEvidence> AssertHeldReviewDatabaseStateAsync(P0213Fixture fixture, HeldCase held)
    {
        var shop = $"shop_id = '{held.ShopId:D}'";
        Assert.Equal(0L, await CountMerchantAsync(fixture, "sales", shop));
        Assert.Equal(1L, await CountMerchantAsync(fixture, "offline_submissions", shop));
        Assert.Equal(1L, await CountMerchantAsync(fixture, "reconciliation_issues", shop));
        Assert.Equal(held.CaseId, (Guid)(await fixture.MerchantScalarAsync($"SELECT id FROM public.reconciliation_issues WHERE {shop}"))!);
        Assert.Equal(held.StockBefore, await ReadShopStockQuantityAsync(fixture, held.ShopId));
        Assert.Equal(held.SequenceBefore, await ReadInvoiceSequenceSumAsync(fixture, held.ShopId));
        var submission = await ReadSubmissionAsync(fixture, held.ShopId, held.Sale.ClientSaleId);
        Assert.NotNull(submission);
        Assert.Null(submission!.SaleId);
        Assert.Equal((held.Sale.InvoiceNumber, held.Sale.SoldAt, held.SyncAt, held.Permit.PermitId, held.Permit.GrantId),
            (submission.InvoiceNumber, submission.ClaimedSoldAt, submission.ReceivedAt, submission.LicencePermitId, submission.LicenceGrantId));
        return submission;
    }

    private static async Task<string> AssertMissedSuspensionProviderViewAsync(
        P0213Fixture fixture, Guid account, Guid shopId, string invoiceNumber, DateTimeOffset permitValidUntil, DateTimeOffset replayAt)
    {
        await DrainBothAsync(fixture);
        using var support = await P0417Tests.SignInAsync(fixture, "support");
        fixture.Hosts.StatusClock.Now = replayAt.AddMinutes(1);
        var provider = await ReadProviderDevicePermitsAsync(support, account, shopId);
        var deviceRow = Assert.Single(provider.GetProperty("devices").EnumerateArray());
        // The provider sees contact times only: the later replay is the device's last contact, not a new receipt.
        Assert.Equal((permitValidUntil, replayAt, true, "Unknown"), (
            deviceRow.GetProperty("knownPermitEnd").GetDateTimeOffset(),
            deviceRow.GetProperty("lastSynchronizedAt").GetDateTimeOffset(),
            deviceRow.GetProperty("outstanding").GetBoolean(),
            provider.GetProperty("disconnectedQueues").GetString()));
        Assert.DoesNotContain(invoiceNumber, provider.GetRawText(), StringComparison.Ordinal);
        return deviceRow.GetProperty("deviceRef").GetString()!;
    }

    private static async Task VerifyReviewAccessAndActionsAsync(
        P0213Fixture fixture, ReviewActors actors, HeldCase held, JsonElement wireSale, DateTimeOffset effectiveAt, CancellationToken ct)
    {
        var original = await AssertManagersSeeEscalateOnlyCaseAsync(actors, held.CaseId, effectiveAt, ct);
        await AssertReviewActionsRefusedByCodeAsync(actors.Owner, held.CaseId, original.Version, ct);
        await AssertOtherTenantAndForeignStaffDeniedAsync(actors, held.CaseId, original.Version, ct);
        await AssertStaffEscalatesOnlyOwnCaseAsync(actors, held.Permit.DeviceId, wireSale, ct);

        // Nothing above may have posted the invoice or moved the original case.
        Assert.Equal(0L, await CountMerchantAsync(fixture, "sales", $"shop_id = '{held.ShopId:D}'"));
        Assert.Equal(original, await ReadCaseAsync(actors.Owner, held.CaseId, ct));
        var submission = await ReadSubmissionAsync(fixture, held.ShopId, held.Sale.ClientSaleId);
        Assert.Equal((null, held.SyncAt), (submission!.SaleId, submission.ReceivedAt));
    }

    private static async Task<CaseView> AssertManagersSeeEscalateOnlyCaseAsync(
        ReviewActors actors, Guid caseId, DateTimeOffset effectiveAt, CancellationToken ct)
    {
        var views = new List<CaseView>();
        foreach (var viewer in new[] { actors.Owner, actors.Manager })
        {
            using var get = await viewer.GetAsync($"/api/offline-reconciliation/{caseId:D}", ct);
            var detail = await P0418Tests.ReadAsync(get, HttpStatusCode.OK);
            Assert.Equal("LicenseHistoryReview", detail.GetProperty("issueType").GetString());
            Assert.Contains("OfflineLicenceHistory.RestrictionConflict",
                detail.GetProperty("reasonCodes").EnumerateArray().Select(x => x.GetString()));
            var history = detail.GetProperty("licenceHistory");
            Assert.Equal((false, false), (history.GetProperty("retryEligible").GetBoolean(), history.GetProperty("linkEligible").GetBoolean()));
            Assert.Equal(effectiveAt, history.GetProperty("observations")[0].GetProperty("sourceCompletionCutoff").GetDateTimeOffset());
            Assert.DoesNotContain("signedPermit", detail.GetRawText(), StringComparison.OrdinalIgnoreCase);
            views.Add(CaseView.From(detail));
        }
        Assert.Equal(new CaseView(caseId, "Open", "Escalate", views[0].Version), views[0]);
        Assert.Equal(views[0], views[1]);
        return views[0];
    }

    private static async Task AssertReviewActionsRefusedByCodeAsync(HttpClient owner, Guid caseId, uint version, CancellationToken ct)
    {
        foreach (var (action, refusal) in RefusedReviewActions)
        {
            using var refused = await owner.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
            {
                operationId = Guid.NewGuid().ToString("N"), expectedVersion = version, action,
                reason = "Disallowed review action", linkedOperationId = Guid.NewGuid(),
            }, ct);
            var body = await refused.Content.ReadAsStringAsync(ct);
            Assert.True(refused.StatusCode == HttpStatusCode.BadRequest, $"{action}: {(int)refused.StatusCode} {body}");
            Assert.Equal((action, refusal), (action, Assert.Single(Refusals(body))));
        }
    }

    /// <summary>"code: description" of each error in <c>{ title, status, detail, errors: [{ code, description }] }</c>.</summary>
    private static string[] Refusals(string body)
    {
        using var problem = JsonDocument.Parse(body);
        return [.. problem.RootElement.GetProperty("errors").EnumerateArray().Select(error =>
            $"{error.GetProperty("code").GetString()}: {error.GetProperty("description").GetString()}")];
    }

    private static async Task AssertOtherTenantAndForeignStaffDeniedAsync(
        ReviewActors actors, Guid caseId, uint version, CancellationToken ct)
    {
        using (var crossGet = await actors.OtherTenant.GetAsync($"/api/offline-reconciliation/{caseId:D}", ct))
            Assert.Equal(HttpStatusCode.NotFound, crossGet.StatusCode);
        using (var crossPost = await actors.OtherTenant.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
        {
            operationId = Guid.NewGuid().ToString("N"), expectedVersion = version, action = "Escalate", reason = "Cross tenant",
        }, ct))
            Assert.Equal(HttpStatusCode.NotFound, crossPost.StatusCode);
        using var staffForeign = await actors.Staff.GetAsync($"/api/offline-reconciliation/{caseId:D}", ct);
        Assert.Equal(HttpStatusCode.NotFound, staffForeign.StatusCode);
    }

    private static async Task AssertStaffEscalatesOnlyOwnCaseAsync(
        ReviewActors actors, string deviceId, JsonElement wireSale, CancellationToken ct)
    {
        var staffNode = JsonNode.Parse(wireSale.GetRawText())!.AsObject();
        staffNode["clientSaleId"] = Guid.NewGuid().ToString("N");
        staffNode["invoiceNumber"] = "A/2627/000002";
        staffNode["issuedByUserId"] = actors.StaffUserId.ToString("D");
        var staffSync = await SyncSingleAsync(actors.Staff, deviceId, JsonSerializer.SerializeToElement(staffNode), ct);
        Assert.Equal("NeedsReview", staffSync.GetProperty("status").GetString());
        var staffCase = await ReadCaseAsync(actors.Staff, staffSync.GetProperty("reviewCaseId").GetGuid(), ct);
        Assert.Equal("Escalate", staffCase.AvailableActions);

        var resolve = $"/api/offline-reconciliation/{staffCase.Id:D}/resolve";
        using (var staffRetry = await actors.Staff.PostAsJsonAsync(resolve, new
        {
            operationId = Guid.NewGuid().ToString("N"), expectedVersion = staffCase.Version, action = "RetryHistoricalLease", reason = "Staff retry",
        }, ct))
            Assert.Equal(HttpStatusCode.Forbidden, staffRetry.StatusCode);
        using var staffEscalate = await actors.Staff.PostAsJsonAsync(resolve, new
        {
            operationId = Guid.NewGuid().ToString("N"), expectedVersion = staffCase.Version, action = "Escalate", reason = "Staff escalates own held case",
        }, ct);
        Assert.Equal("Escalated", (await P0418Tests.ReadAsync(staffEscalate, HttpStatusCode.OK)).GetProperty("status").GetString());
    }

    private static async Task<CaseView> ReadCaseAsync(HttpClient viewer, Guid caseId, CancellationToken ct)
    {
        using var get = await viewer.GetAsync($"/api/offline-reconciliation/{caseId:D}", ct);
        return CaseView.From(await P0418Tests.ReadAsync(get, HttpStatusCode.OK));
    }

    private sealed record CaseView(Guid Id, string Status, string AvailableActions, uint Version)
    {
        internal static CaseView From(JsonElement detail) => new(
            detail.GetProperty("caseId").GetGuid(),
            detail.GetProperty("status").GetString()!,
            string.Join(',', detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString())),
            detail.GetProperty("version").GetUInt32());
    }

    private sealed record HeldPermit(string DeviceId, Guid PermitId, Guid GrantId, DateTimeOffset NotBefore, DateTimeOffset ValidUntil)
    {
        internal static HeldPermit From(JsonElement observation) => new(
            observation.GetProperty("deviceId").GetString()!,
            observation.GetProperty("permitId").GetGuid(),
            observation.GetProperty("grantId").GetGuid(),
            ParseInstant(observation.GetProperty("notBefore").GetString()!),
            ParseInstant(observation.GetProperty("validUntil").GetString()!));
    }

    private sealed record QueuedSale(
        string ClientSaleId, string InvoiceNumber, DateTimeOffset SoldAt, string RawPayloadSha256, string SignedPermitSha256)
    {
        internal static QueuedSale From(JsonElement observation) => new(
            observation.GetProperty("clientSaleId").GetString()!,
            observation.GetProperty("invoiceNumber").GetString()!,
            ParseInstant(observation.GetProperty("soldAt").GetString()!),
            observation.GetProperty("rawPayloadSha256").GetString()!,
            observation.GetProperty("signedPermitSha256").GetString()!);
    }

    private sealed record HeldCase(
        Guid ShopId, Guid CaseId, QueuedSale Sale, HeldPermit Permit, DateTimeOffset SyncAt, decimal StockBefore, long SequenceBefore);

    /// <summary>Signed-in parties of the review: two owner sessions, manager, staff, another tenant, and a provider admin.</summary>
    private sealed record ReviewActors(
        HttpClient Owner, HttpClient Owner2, HttpClient OtherTenant, HttpClient Manager, HttpClient Staff,
        Guid StaffUserId, P0110CookieBrowser SecurityAdmin) : IDisposable
    {
        internal static async Task<ReviewActors> CreateAsync(P0213Fixture fixture, Merchant shop, DateTimeOffset at)
        {
            var (otherShop, _, _) = await PreparePaidShopAsync(fixture, at);
            var manager = await SignInAsync(fixture.Hosts, ShopRole.Manager, existingShop: shop.ShopId);
            var staff = await SignInAsync(fixture.Hosts, ShopRole.Staff, existingShop: shop.ShopId);
            return new(
                shop.Client, await CreateSecondSessionClientAsync(fixture, shop.Email), otherShop.Client,
                manager.Client, staff.Client, staff.UserId, await P0403Tests.SignInSecurityAsync(fixture));
        }

        public void Dispose()
        {
            foreach (var client in new[] { Owner, Owner2, OtherTenant, Manager, Staff }) client.Dispose();
            SecurityAdmin.Dispose();
        }
    }
}
