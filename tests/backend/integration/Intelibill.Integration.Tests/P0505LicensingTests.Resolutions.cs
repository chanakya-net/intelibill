using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0505LicensingTests
{
    [Fact]
    public async Task Unproven_or_suspended_history_cannot_resolve()
    {
        var start = At("2027-01-20T12:00:00Z");
        var end = start.AddHours(1);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(owner, start, end);
        using var otherShopOwner = CreateClient(host);
        await OpenHistoricalShopAsync(otherShopOwner, start, end);
        using var staff = await CreateStaffClientAsync(host, actor.Shop, start.AddDays(400));
        using var ownerSession2 = await CreateSecondSessionClientAsync(host, actor, start.AddDays(400));

        // 1. Unsupported evidence hold: issue both invoices while operating, then advance past expiry.
        var sale = await BuildIssuedInvoiceAsync(owner, actor, start, end, "unsupported");
        var staffSale = await BuildIssuedInvoiceAsync(owner, actor, start, end, "unsupported", device: "p0505-staff-dev");
        _clock.Now = end.AddMinutes(5);
        var held = await SyncInvoiceAsync(owner, sale);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseDetailAsync(owner, caseId);
        Assert.Equal(["Escalate"], detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());

        var version = detail.GetProperty("version").GetUInt32();
        foreach (var badAction in new[]
        {
            "PostAnyway", "Redate", "Renumber", "Delete", "RetryHistoricalLease",
            "LinkExistingSale", "AcknowledgeVariance", "LinkStockCorrection", "ReconcileTillSale",
        })
        {
            using var res = await owner.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
            {
                operationId = Guid.NewGuid().ToString("N"),
                expectedVersion = version,
                action = badAction,
                reason = "Unsupported action attempt",
                linkedOperationId = Guid.NewGuid(),
            }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        // 2. Staff cannot see owner's case (404), and on their own case cannot take non-Escalate actions (403).
        using (var staffGetOwner = await staff.GetAsync($"/api/offline-reconciliation/{caseId:D}", Ct))
            Assert.Equal(HttpStatusCode.NotFound, staffGetOwner.StatusCode);
        using (var staffPostOwner = await staff.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
        {
            operationId = Guid.NewGuid().ToString("N"), expectedVersion = version, action = "Escalate", reason = "Staff probing",
        }, Ct))
            Assert.Equal(HttpStatusCode.NotFound, staffPostOwner.StatusCode);

        var staffUserId = Guid.Parse(new JwtSecurityToken(staff.DefaultRequestHeaders.Authorization!.Parameter!).Subject);
        staffSale["issuedByUserId"] = staffUserId;
        var staffHeld = await SyncInvoiceAsync(staff, staffSale, device: "p0505-staff-dev");
        var staffCaseId = staffHeld.GetProperty("reviewCaseId").GetGuid();
        var staffDetail = await GetCaseDetailAsync(staff, staffCaseId);
        Assert.Equal(["Escalate"], staffDetail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        using (var staffForbidden = await staff.PostAsJsonAsync($"/api/offline-reconciliation/{staffCaseId:D}/resolve", new
        {
            operationId = Guid.NewGuid().ToString("N"),
            expectedVersion = staffDetail.GetProperty("version").GetUInt32(),
            action = "RetryHistoricalLease",
            reason = "Staff attempting retry",
        }, Ct))
            Assert.Equal(HttpStatusCode.Forbidden, staffForbidden.StatusCode);

        // 3. Another shop cannot read or resolve this case (404).
        using (var crossGet = await otherShopOwner.GetAsync($"/api/offline-reconciliation/{caseId:D}", Ct))
            Assert.Equal(HttpStatusCode.NotFound, crossGet.StatusCode);
        using (var crossPost = await otherShopOwner.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
        {
            operationId = Guid.NewGuid().ToString("N"), expectedVersion = version, action = "Escalate", reason = "Cross tenant",
        }, Ct))
            Assert.Equal(HttpStatusCode.NotFound, crossPost.StatusCode);

        // 4. Observe-only / legacy hold without historical coverage cannot resolve even after a later renewal.
        _clock.Now = start;
        using var obsClient = CreateClient(host);
        var obsActor = await OpenHistoricalShopAsync(obsClient, start, end, enforce: false);
        _clock.Now = end.AddMinutes(5);
        Guid legacyHoldId;
        await using (var db = Open(Migrator))
        {
            var seq = InvoiceSequence.Create(obsActor.Shop, FiscalYear.ForDate(start));
            db.InvoiceSequences.Add(seq);
            var legacyLease = InvoiceLease.Create(
                obsActor.Shop, seq.Id, "legacy-dev", 2026, "A/2627/", 1, 25, 6,
                start.AddDays(-5), start.AddDays(2), reservedByUserId: null, reservedBySessionId: null);
            db.InvoiceLeases.Add(legacyLease);
            var batch = await db.InventoryBatches.SingleAsync(x => x.ShopId == obsActor.Shop, Ct);
            var item = await db.Items.SingleAsync(x => x.ShopId == obsActor.Shop, Ct);
            var legacyCmd = new OfflineSaleSyncCommand(
                Guid.NewGuid().ToString("N"), "A/2627/000001", start.AddDays(-3), null, null, null,
                PaymentMethod.Cash, 2m, 0m, 2m, 2m, 0m, 0m, 2m, InstantDiscountType.None, 0m, null, null, null, null,
                [new OfflineSaleSyncLineCommand(item.Barcode, "LOT", item.Name, 1m, 1m, 2m, 2m, 0m, false, batch.Id,
                    2m, 0m, 0m, 2m, 0m, 2m, null, null, InstantDiscountType.None, 0m, null)],
                IssuedByUserId: obsActor.User);
            var legacyHash = OfflineSaleSyncIdempotencyHasher.ComputeHash(obsActor.Shop, "legacy-dev", legacyCmd);
            var env = OfflineSubmission.Record(
                obsActor.Shop, "legacy-dev", legacyCmd.ClientSaleId, legacyCmd.InvoiceNumber, legacyHash,
                OfflineSubmissionPayload.Serialize(legacyCmd), legacyCmd.SoldAt, start.AddDays(-3),
                OfflineSubmissionSource.Sync, obsActor.User, obsActor.User, obsActor.User, legacyLease.Id, null);
            db.OfflineSubmissions.Add(env);
            var issue = ReconciliationIssue.Create(
                obsActor.Shop, null, legacyCmd.ClientSaleId, "legacy-dev", ReconciliationIssueType.LicenseHistoryReview,
                "OfflineLicenceHistory.EvidenceMissing", "The original offline licence evidence requires review.",
                obsActor.User, invoiceNumber: legacyCmd.InvoiceNumber, offlineSubmissionId: env.Id);
            db.ReconciliationIssues.Add(issue);
            await db.SaveChangesAsync(Ct);
            legacyHoldId = issue.Id;
        }
        var obsDetail = await GetCaseDetailAsync(obsClient, legacyHoldId);
        Assert.Equal(["Escalate"], obsDetail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        // Renew the shop later (new term covering [end+1d, end+30d]) — still cannot prove the earlier gap!
        _clock.Now = end.AddDays(2);
        await ApplyAsync(obsActor.Binding, end.AddDays(30), revision: 2);
        await EnforceAsync(obsActor.Shop);
        var afterRenewal = await GetCaseDetailAsync(obsClient, legacyHoldId);
        Assert.Equal(["Escalate"], afterRenewal.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        using (var postRenewal = await obsClient.PostAsJsonAsync($"/api/offline-reconciliation/{legacyHoldId:D}/resolve", new
        {
            operationId = Guid.NewGuid().ToString("N"),
            expectedVersion = afterRenewal.GetProperty("version").GetUInt32(),
            action = "RetryHistoricalLease",
            reason = "Renewal cannot prove old gap",
        }, Ct))
            Assert.Equal(HttpStatusCode.BadRequest, postRenewal.StatusCode);

        // 5. Commit an Escalate operation with opKey, then verify replay is BLOCKED when authority is lost:
        //    security suspension, revoked session, suspended membership, disabled login.
        _clock.Now = end.AddMinutes(6);
        var committedOpId = "committed-escalate-op";
        var escalatePayload = new
        {
            operationId = committedOpId,
            expectedVersion = version,
            action = "Escalate",
            reason = "Escalate before authority loss checks",
        };
        using (var okEsc = await owner.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", escalatePayload, Ct))
            await ReadSuccessAsync(okEsc);

        // 5a. Security suspension blocks GET, POST, and replay of committed operation.
        await ApplyAsync(actor.Binding, end.AddDays(1), revision: 2, restriction: "Security");
        await AssertProblemAsync(await owner.GetAsync($"/api/offline-reconciliation/{caseId:D}", Ct),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        await AssertProblemAsync(await owner.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", escalatePayload, Ct),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");

        // Release security suspension with reviewed release revision (3 releasing 2) so owner can continue testing identity loss.
        await ApplyWithReleaseAsync(actor.Binding, end.AddDays(5), revision: 3, releasedRevision: 2);
        using (var replayedOk = await owner.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", escalatePayload, Ct))
            await ReadSuccessAsync(replayedOk);

        // 5b. Revoked session blocks replay on old session while same-issuer second session remains authorized.
        await using (var db = Open(Migrator))
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE auth_sessions SET revoked_at = {_clock.Now} WHERE id = {actor.Session}", Ct);
        }
        using (var revokedReplay = await owner.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", escalatePayload, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, revokedReplay.StatusCode);
        using (var session2Replay = await ownerSession2.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", escalatePayload, Ct))
            await ReadSuccessAsync(session2Replay);

        // 5c. Suspended membership blocks replay.
        await using (var db = Open(Migrator))
        {
            var m = await db.ShopMemberships.SingleAsync(x => x.ShopId == actor.Shop && x.UserId == actor.User, Ct);
            m.ChangeAuthority(m.Role, MembershipStatus.Suspended);
            await db.SaveChangesAsync(Ct);
        }
        using (var suspendedReplay = await ownerSession2.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", escalatePayload, Ct))
            Assert.Contains(suspendedReplay.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound });

        // 5d. Disabled login (user deactivated) blocks replay even if membership were active.
        await using (var db = Open(Migrator))
        {
            var m = await db.ShopMemberships.SingleAsync(x => x.ShopId == actor.Shop && x.UserId == actor.User, Ct);
            m.ChangeAuthority(m.Role, MembershipStatus.Active);
            var u = await db.Users.SingleAsync(x => x.Id == actor.User, Ct);
            u.SetLoginEnabled(false);
            await db.SaveChangesAsync(Ct);
        }
        using (var disabledReplay = await ownerSession2.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", escalatePayload, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, disabledReplay.StatusCode);

        // Original case remains unresolved and zero sales were created.
        await using (var db = Open(Migrator))
        {
            var originalCase = await db.ReconciliationIssues.SingleAsync(x => x.Id == caseId, Ct);
            Assert.Equal(ReconciliationCaseStatus.Escalated, originalCase.Status);
            Assert.False(await db.Sales.AnyAsync(x => x.ShopId == actor.Shop, Ct));
            Assert.False(await db.Sales.AnyAsync(x => x.ShopId == obsActor.Shop, Ct));
        }
    }

    [Fact]
    public async Task Restored_original_proof_retries_once_in_original_open_period()
    {
        var start = At("2027-01-31T12:00:00Z");
        var end = start.AddHours(1);
        var keyUnavailable = false;
        using var host = CreateHost(configure: services =>
        {
            services.RemoveAll<IOfflineLicenceEvidenceStore>();
            services.AddScoped<IOfflineLicenceEvidenceStore>(provider => new ToggleKeyEvidenceStore(
                new OfflineLicenceEvidenceStore(provider.GetRequiredService<ApplicationDbContext>()),
                () => Volatile.Read(ref keyUnavailable)));
        });
        await host.InitializeAsync();
        using var owner1 = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(owner1, start, end);
        using var owner2 = await CreateSecondSessionClientAsync(host, actor, start.AddDays(400));

        var sale = await BuildIssuedInvoiceAsync(owner1, actor, start, end, "valid");

        // Hide archived verification key temporarily before first sync (permit/grant rows stay intact & match at receipt!).
        Volatile.Write(ref keyUnavailable, true);
        _clock.Now = At("2027-02-01T12:00:00Z"); // Past ordinary expiry and into next month.
        var held = await SyncInvoiceAsync(owner1, sale);
        Assert.Equal("NeedsReview", held.GetProperty("status").GetString());
        Assert.Equal("OfflineLicenceHistory.EvidenceInvalid", held.GetProperty("errors")[0].GetProperty("code").GetString());
        var caseId = held.GetProperty("reviewCaseId").GetGuid();

        // Read the real receipt and BOTH captured IDs BEFORE restoring key access.
        Guid capturedPermitId;
        Guid capturedGrantId;
        string expectedHash;
        await using (var db = Open(Migrator))
        {
            var receipt = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == actor.Shop, Ct);
            Assert.NotNull(receipt.LicencePermitId);
            Assert.NotNull(receipt.LicenceGrantId);
            capturedPermitId = receipt.LicencePermitId!.Value;
            capturedGrantId = receipt.LicenceGrantId!.Value;
            expectedHash = receipt.PayloadHash;
            Assert.False(await db.Sales.AnyAsync(x => x.ShopId == actor.Shop, Ct));
        }

        var detailUnavailable = await GetCaseDetailAsync(owner1, caseId);
        Assert.Equal("LicenseHistoryReview", detailUnavailable.GetProperty("issueType").GetString());
        Assert.Equal(["Escalate"], detailUnavailable.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        var historyBefore = detailUnavailable.GetProperty("licenceHistory");
        Assert.Equal("VerificationKeyUnavailable", historyBefore.GetProperty("currentSubreason").GetString());
        Assert.Equal(capturedPermitId, historyBefore.GetProperty("retainedPermitId").GetGuid());
        Assert.Equal(capturedGrantId, historyBefore.GetProperty("retainedGrantId").GetGuid());
        Assert.False(historyBefore.GetProperty("retryEligible").GetBoolean());

        // Restore access to the archived verification key -> RetryHistoricalLease now appears!
        Volatile.Write(ref keyUnavailable, false);
        var detailReady = await GetCaseDetailAsync(owner1, caseId);
        Assert.Equal(["RetryHistoricalLease", "Escalate"],
            detailReady.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.True(detailReady.GetProperty("licenceHistory").GetProperty("retryEligible").GetBoolean());
        var readyVersion = detailReady.GetProperty("version").GetUInt32();

        // 1. Close the original month (2027-01) between detail and resolve -> refusal, case stays open.
        using (var close = await owner1.PostAsJsonAsync("/api/posting-periods/2027-01/close", new { operationKey = "close-jan", reason = "Month end" }, Ct))
            await ReadSuccessAsync(close);
        using (var closedRetry = await owner1.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
        {
            operationId = "retry-while-closed", expectedVersion = readyVersion, action = "RetryHistoricalLease", reason = "Closed period check",
        }, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, closedRetry.StatusCode);
            Assert.Contains("PostingPeriod.Closed", await closedRetry.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        // Explicitly reopen January as owner.
        using (var reopen = await owner1.PostAsJsonAsync("/api/posting-periods/2027-01/reopen", new { operationKey = "reopen-jan", reason = "Late verified invoice" }, Ct))
            await ReadSuccessAsync(reopen);

        // 2. Withdraw proof after GET -> direct POST refused, case stays open.
        Volatile.Write(ref keyUnavailable, true);
        using (var withdrawnRetry = await owner1.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
        {
            operationId = "retry-after-withdraw", expectedVersion = readyVersion, action = "RetryHistoricalLease", reason = "Key withdrawn after GET",
        }, Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, withdrawnRetry.StatusCode);
            Assert.Contains("Reconciliation.ActionNotAllowed", await withdrawnRetry.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
        Volatile.Write(ref keyUnavailable, false);

        // 3. Stale version refused with 409 CaseVersionConflict.
        using (var stale = await owner1.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
        {
            operationId = "retry-stale-ver", expectedVersion = readyVersion + 99, action = "RetryHistoricalLease", reason = "Stale version",
        }, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Contains("Reconciliation.CaseVersionConflict", await stale.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        // 4. Race two reviewers concurrently with distinct operation keys -> exactly one wins!
        var opA = "race-retry-a";
        var opB = "race-retry-b";
        var raceResponses = await RunBarrierRaceAsync(
            async () =>
            {
                using var r = await owner1.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
                {
                    operationId = opA, expectedVersion = readyVersion, action = "RetryHistoricalLease", reason = "Verified original permit A",
                }, Ct);
                return (Op: opA, Reason: "Verified original permit A", r.StatusCode, Body: await r.Content.ReadAsStringAsync(Ct));
            },
            async () =>
            {
                using var r = await owner2.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
                {
                    operationId = opB, expectedVersion = readyVersion, action = "RetryHistoricalLease", reason = "Verified original permit B",
                }, Ct);
                return (Op: opB, Reason: "Verified original permit B", r.StatusCode, Body: await r.Content.ReadAsStringAsync(Ct));
            });

        var winner = Assert.Single(raceResponses, x => x.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(raceResponses, x => x.StatusCode != HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);

        // 5. Replay winning operation key (same intent -> 200 OK; changed intent -> 409 Operation.PayloadMismatch).
        using (var replaySame = await owner1.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
        {
            operationId = winner.Op, expectedVersion = readyVersion, action = "RetryHistoricalLease", reason = winner.Reason,
        }, Ct))
        {
            var replayed = await ReadSuccessAsync(replaySame);
            Assert.Equal("Resolved", replayed.GetProperty("status").GetString());
        }
        using (var replayChanged = await owner1.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
        {
            operationId = winner.Op, expectedVersion = readyVersion, action = "RetryHistoricalLease", reason = "Changed intent on same key",
        }, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayChanged.StatusCode);
            Assert.Contains("Operation.PayloadMismatch", await replayChanged.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        // 6. Verify single sale, single movement set, original issuer/date/number/hash/document and single closing event.
        await using (var db = Open(Migrator))
        {
            var posted = await db.Sales.SingleAsync(x => x.ShopId == actor.Shop, Ct);
            Assert.Equal(actor.User, posted.ActorUserId);
            Assert.Equal(sale["soldAt"]!.GetValue<DateTimeOffset>(), posted.SoldAt);
            Assert.Equal(new DateOnly(2027, 1, 31), posted.BusinessDate);
            Assert.Equal(sale["invoiceNumber"]!.GetValue<string>(), posted.InvoiceNumber);
            Assert.Equal(expectedHash, posted.RequestHash);
            Assert.NotNull(posted.InvoiceDocument);
            Assert.Equal(posted.InvoiceNumber, posted.InvoiceDocument!.Number);

            var facts = await ReadEconomicFactsAsync(actor.Shop);
            Assert.Equal(new ShopEconomicFacts(1, 1, 0, 0, 9m), facts);

            var closingEvents = await db.ReconciliationCaseEvents
                .Where(e => e.ShopId == actor.Shop && e.CaseId == caseId && e.ToStatus == ReconciliationCaseStatus.Resolved)
                .ToListAsync(Ct);
            Assert.Single(closingEvents);
            _output.WriteLine(
                "P0505 retry demo: caseId={0} invoice={1} businessDate={2} soldAt={3:O} permitId={4} grantId={5} saleId={6} closingEvent={7}->{8}",
                caseId, posted.InvoiceNumber, posted.BusinessDate, posted.SoldAt, capturedPermitId, capturedGrantId,
                posted.Id, closingEvents[0].Action, closingEvents[0].ToStatus);
        }
    }

    [Fact]
    public async Task History_link_requires_exact_posted_sale_and_has_no_economic_effect()
    {
        var start = At("2027-01-31T12:00:00Z");
        var end = start.AddHours(1);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(owner, start, end);

        // Post an offline sale through real signed sync, and also post an unrelated online sale of equal total.
        var saleJson = await BuildIssuedInvoiceAsync(owner, actor, start, end, "valid");
        _clock.Now = end.AddMinutes(2);
        var synced = await SyncInvoiceAsync(owner, saleJson);
        Assert.Equal("created", synced.GetProperty("status").GetString());
        var postedOfflineSaleId = synced.GetProperty("saleId").GetGuid();

        // Also seed an online sale with the same amount (2m) while shop is operating.
        _clock.Now = start.AddMinutes(10);
        Guid onlineSaleId;
        await using (var db = Open(Migrator))
        {
            var batch = await db.InventoryBatches.SingleAsync(x => x.ShopId == actor.Shop, Ct);
            var item = await db.Items.SingleAsync(x => x.ShopId == actor.Shop, Ct);
            using var onlineResp = await owner.PostAsJsonAsync("/api/sales", new
            {
                idempotencyKey = $"online-{Guid.NewGuid():N}",
                paymentMethod = "Cash",
                paidAmount = 2m,
                dueAmount = 0m,
                items = new[] { new { barcode = item.Barcode, batchNumber = "LOT", itemName = item.Name, quantity = 1m,
                    costPrice = 1m, salesPrice = 2m, mrp = 2m, taxRatePercent = 0m, isPriceIncludingTax = false,
                    inventoryBatchId = batch.Id, lineType = "Goods" } },
            }, Ct);
            onlineSaleId = (await ReadSuccessAsync(onlineResp)).GetProperty("saleId").GetGuid();
        }

        // Advance into next month and close January (2027-01).
        _clock.Now = At("2027-02-01T12:00:00Z");
        using (var close = await owner.PostAsJsonAsync("/api/posting-periods/2027-01/close", new { operationKey = "close-link", reason = "Closed period link test" }, Ct))
            await ReadSuccessAsync(close);

        // Fixture 1: Seed only the outstanding historical review case on the posted sale's retained envelope.
        Guid linkedCaseId;
        Guid noEnvelopeCaseId;
        await using (var db = Open(Migrator))
        {
            var envelope = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == actor.Shop, Ct);
            var issue = ReconciliationIssue.Create(
                actor.Shop, null, envelope.ClientSaleId, envelope.DeviceId, ReconciliationIssueType.LicenseHistoryReview,
                "OfflineLicenceHistory.EvidenceInvalid", "Retained historical review case", actor.User,
                invoiceNumber: envelope.InvoiceNumber, offlineSubmissionId: envelope.Id);
            var noEnv = ReconciliationIssue.Create(
                actor.Shop, null, envelope.ClientSaleId, envelope.DeviceId, ReconciliationIssueType.LicenseHistoryReview,
                "OfflineLicenceHistory.EvidenceMissing", "No envelope case", actor.User,
                invoiceNumber: envelope.InvoiceNumber, offlineSubmissionId: null);
            db.ReconciliationIssues.AddRange(issue, noEnv);
            await db.SaveChangesAsync(Ct);
            linkedCaseId = issue.Id;
            noEnvelopeCaseId = noEnv.Id;
        }

        // Absent envelope offers NO LinkExistingSale and refuses direct POST.
        var noEnvDetail = await GetCaseDetailAsync(owner, noEnvelopeCaseId);
        Assert.Equal(["Escalate"], noEnvDetail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        using (var noEnvLink = await owner.PostAsJsonAsync($"/api/offline-reconciliation/{noEnvelopeCaseId:D}/resolve", new
        {
            operationId = "no-env-link",
            expectedVersion = noEnvDetail.GetProperty("version").GetUInt32(),
            action = "LinkExistingSale",
            reason = "Absent envelope cannot match",
            linkedOperationId = postedOfflineSaleId,
        }, Ct))
            Assert.Equal(HttpStatusCode.BadRequest, noEnvLink.StatusCode);

        // Linked envelope case prefers LinkExistingSale alone (no RetryHistoricalLease!) and lists only exact posted sale.
        var detail1 = await GetCaseDetailAsync(owner, linkedCaseId);
        Assert.Equal(["LinkExistingSale", "Escalate"],
            detail1.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        var candidates1 = detail1.GetProperty("linkCandidates").EnumerateArray().ToArray();
        Assert.Single(candidates1);
        Assert.Equal(postedOfflineSaleId, candidates1[0].GetProperty("operationId").GetGuid());
        Assert.True(detail1.GetProperty("licenceHistory").GetProperty("linkEligible").GetBoolean());
        Assert.False(detail1.GetProperty("licenceHistory").GetProperty("retryEligible").GetBoolean());

        // Negative match: attempting to link the equal-total online sale fails with SaleIdentityMismatch.
        using (var linkOnline = await owner.PostAsJsonAsync($"/api/offline-reconciliation/{linkedCaseId:D}/resolve", new
        {
            operationId = "link-online-bad",
            expectedVersion = detail1.GetProperty("version").GetUInt32(),
            action = "LinkExistingSale",
            reason = "Online sale must not match",
            linkedOperationId = onlineSaleId,
        }, Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, linkOnline.StatusCode);
            Assert.Contains("Reconciliation.SaleIdentityMismatch", await linkOnline.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        // Positive match in closed January: succeeds with ZERO economic or sequence changes!
        var factsBefore = await ReadEconomicFactsAsync(actor.Shop);
        int sequenceNextBefore;
        await using (var db = Open(Migrator))
            sequenceNextBefore = await db.InvoiceSequences.Where(s => s.ShopId == actor.Shop).SumAsync(s => s.NextNumber, Ct);

        using (var linkOk = await owner.PostAsJsonAsync($"/api/offline-reconciliation/{linkedCaseId:D}/resolve", new
        {
            operationId = "link-exact-ok",
            expectedVersion = detail1.GetProperty("version").GetUInt32(),
            action = "LinkExistingSale",
            reason = "Exact posted offline transaction matched",
            linkedOperationId = postedOfflineSaleId,
        }, Ct))
        {
            var resolved = await ReadSuccessAsync(linkOk);
            Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
            Assert.Equal("LinkExistingSale", resolved.GetProperty("resolution").GetString());
            Assert.Equal(postedOfflineSaleId, resolved.GetProperty("saleId").GetGuid());
            _output.WriteLine(
                "P0505 link demo: caseId={0} invoice={1} linkedSaleId={2} resolution={3} status={4}",
                linkedCaseId, detail1.GetProperty("invoiceNumber").GetString(), postedOfflineSaleId,
                resolved.GetProperty("resolution").GetString(), resolved.GetProperty("status").GetString());
        }

        Assert.Equal(factsBefore, await ReadEconomicFactsAsync(actor.Shop));
        await using (var db = Open(Migrator))
            Assert.Equal(sequenceNextBefore, await db.InvoiceSequences.Where(s => s.ShopId == actor.Shop).SumAsync(s => s.NextNumber, Ct));

        // Fixture 2: Unlinked historical fixture (pre-envelope posted offline sale + matching unlinked receipt/case).
        Guid unlinkedSaleId;
        Guid unlinkedCaseId;
        await using (var db = Open(Migrator))
        {
            var batch = await db.InventoryBatches.SingleAsync(x => x.ShopId == actor.Shop, Ct);
            var item = await db.Items.SingleAsync(x => x.ShopId == actor.Shop, Ct);
            var soldAt = At("2027-01-25T10:15:30.123456Z");
            var clientSaleId = Guid.NewGuid().ToString("N");
            var invNumber = "A/2627/000900";
            var cmd = new OfflineSaleSyncCommand(
                clientSaleId, invNumber, soldAt, null, null, null, PaymentMethod.Cash, 2m, 0m, 2m, 2m, 0m, 0m, 2m,
                InstantDiscountType.None, 0m, null, null, null, null,
                [new OfflineSaleSyncLineCommand(item.Barcode, "LOT", item.Name, 1m, 1m, 2m, 2m, 0m, false, batch.Id,
                    2m, 0m, 0m, 2m, 0m, 2m, null, null, InstantDiscountType.None, 0m, null)],
                IssuedByUserId: actor.User);
            var realHash = OfflineSaleSyncIdempotencyHasher.ComputeHash(actor.Shop, "pre-env-dev", cmd);
            var saleItem = SaleItem.CreateGoods(
                actor.Shop, item.Id, batch.Id, item.Name, item.Barcode, 1m, 1m, 2m, 2m, 0m, false, false,
                originalSalesPrice: 2m, finalSalesPrice: 2m, preTaxAmountBeforeDiscount: 2m,
                taxableAmount: 2m, taxAmount: 0m, totalAmount: 2m);
            var preEnvSale = Sale.Create(
                actor.Shop, actor.User, OfflineSaleSyncIdempotencyHasher.ComputeKey("pre-env-dev", clientSaleId),
                realHash, invNumber, null, null, null, PaymentMethod.Cash, soldAt, BusinessDay.InDefaultZone(soldAt),
                2m, 0m, 2m, 0m, [saleItem], 2m, 2m, 0m, source: SaleSource.Offline,
                clientSaleId: clientSaleId, deviceId: "pre-env-dev", syncedAt: soldAt.AddMinutes(1));
            db.Sales.Add(preEnvSale);
            var env = OfflineSubmission.Record(
                actor.Shop, "pre-env-dev", clientSaleId, invNumber, realHash, OfflineSubmissionPayload.Serialize(cmd),
                soldAt, soldAt.AddMinutes(2), OfflineSubmissionSource.Sync, actor.User, actor.User, actor.User, null, null);
            db.OfflineSubmissions.Add(env);
            var unlinkedCase = ReconciliationIssue.Create(
                actor.Shop, null, clientSaleId, "pre-env-dev", ReconciliationIssueType.LicenseHistoryReview,
                "OfflineLicenceHistory.EvidenceMissing", "Pre-envelope historical match", actor.User,
                invoiceNumber: invNumber, offlineSubmissionId: env.Id);
            db.ReconciliationIssues.Add(unlinkedCase);
            await db.SaveChangesAsync(Ct);
            unlinkedSaleId = preEnvSale.Id;
            unlinkedCaseId = unlinkedCase.Id;
        }

        var detail2 = await GetCaseDetailAsync(owner, unlinkedCaseId);
        Assert.Equal(["LinkExistingSale", "Escalate"],
            detail2.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal(unlinkedSaleId, detail2.GetProperty("linkCandidates")[0].GetProperty("operationId").GetGuid());
        var factsBefore2 = await ReadEconomicFactsAsync(actor.Shop);
        using (var link2 = await owner.PostAsJsonAsync($"/api/offline-reconciliation/{unlinkedCaseId:D}/resolve", new
        {
            operationId = "link-pre-env-ok",
            expectedVersion = detail2.GetProperty("version").GetUInt32(),
            action = "LinkExistingSale",
            reason = "Matched unlinked pre-envelope offline sale",
            linkedOperationId = unlinkedSaleId,
        }, Ct))
        {
            var resolved2 = await ReadSuccessAsync(link2);
            Assert.Equal("Resolved", resolved2.GetProperty("status").GetString());
            Assert.Equal(unlinkedSaleId, resolved2.GetProperty("saleId").GetGuid());
        }
        Assert.Equal(factsBefore2, await ReadEconomicFactsAsync(actor.Shop));
    }

    [Fact]
    public async Task License_history_review_enforces_uniqueness_rls_and_append_only_observations()
    {
        var dbName = $"p0505_mig_{Guid.NewGuid():N}";
        var adminBuilder = new NpgsqlConnectionStringBuilder(Migrator);
        await using (var adminConn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await adminConn.OpenAsync(Ct);
            await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", adminConn);
            await createCmd.ExecuteNonQueryAsync(Ct);
        }

        var disposableBuilder = new NpgsqlConnectionStringBuilder(Migrator) { Database = dbName };
        var disposableConn = disposableBuilder.ConnectionString;
        try
        {
            await using var migratorDb = Open(disposableConn);
            await migratorDb.Database.MigrateAsync(Ct);

            // 1. Seed Shop A, Shop B and an envelope.
            var shopA = Shop.Create("Mig Shop A", "1 Main", "Bengaluru", "Karnataka", "560001", null, null, null);
            var shopB = Shop.Create("Mig Shop B", "2 Main", "Bengaluru", "Karnataka", "560001", null, null, null);
            migratorDb.Shops.AddRange(shopA, shopB);
            var now = At("2027-01-20T12:00:00Z");
            var envNew = OfflineSubmission.Record(
                shopA.Id, "dev-mig", "client-new", "A/2627/000002", new string('b', 64), "{}",
                now, now, OfflineSubmissionSource.Sync, Guid.NewGuid(), null, null, null, null);
            migratorDb.OfflineSubmissions.Add(envNew);
            await migratorDb.SaveChangesAsync(Ct);

            // 2. Type 15 + observation work; multiple non-history variances coexist; duplicate type 15 fails.
            var newCase = ReconciliationIssue.Create(
                shopA.Id, null, "client-new", "dev-mig", ReconciliationIssueType.LicenseHistoryReview,
                "OfflineLicenceHistory.EvidenceUnsupported", "Typed #904 case", Guid.NewGuid(),
                invoiceNumber: "A/2627/000002", offlineSubmissionId: envNew.Id);
            var var1 = ReconciliationIssue.Create(
                shopA.Id, null, "client-new", "dev-mig", ReconciliationIssueType.PricingVariance,
                "offline_sync.pricing_variance", "Price diff", Guid.NewGuid(), invoiceNumber: "A/2627/000002", offlineSubmissionId: envNew.Id);
            var var2 = ReconciliationIssue.Create(
                shopA.Id, null, "client-new", "dev-mig", ReconciliationIssueType.DiscountVariance,
                "offline_sync.discount_variance", "Discount diff", Guid.NewGuid(), invoiceNumber: "A/2627/000002", offlineSubmissionId: envNew.Id);
            migratorDb.ReconciliationIssues.AddRange(newCase, var1, var2);
            await migratorDb.SaveChangesAsync(Ct);

            var obs = LicenseHistoryReviewObservation.Create(
                shopA.Id, newCase.Id, envNew.Id, now, "OfflineLicenceHistory.EvidenceUnsupported", "EvidenceVersionUnsupported", "version");
            migratorDb.LicenseHistoryReviewObservations.Add(obs);
            await migratorDb.SaveChangesAsync(Ct);

            // Duplicate type 15 on same (shop_id, offline_submission_id) must fail unique constraint (23505).
            var dupTyped = ReconciliationIssue.Create(
                shopA.Id, null, "client-new", "dev-mig", ReconciliationIssueType.LicenseHistoryReview,
                "OfflineLicenceHistory.EvidenceInvalid", "Duplicate typed case", Guid.NewGuid(),
                invoiceNumber: "A/2627/000002", offlineSubmissionId: envNew.Id);
            migratorDb.ReconciliationIssues.Add(dupTyped);
            var dupEx = await Assert.ThrowsAsync<DbUpdateException>(() => migratorDb.SaveChangesAsync(Ct));
            Assert.Equal("23505", (dupEx.InnerException as PostgresException)?.SqlState);
            migratorDb.ChangeTracker.Clear();

            // 3. Runtime-role RLS isolation & append-only triggers (UPDATE/DELETE refused).
            await RestrictedRuntimeRole.EnsureAsync(disposableConn);
            await RestrictedRuntimeRole.GrantAsync(disposableConn);
            await using var runtimeConn = new NpgsqlConnection(RestrictedRuntimeRole.ConnectionStringFor(disposableConn));
            await runtimeConn.OpenAsync(Ct);
            await using (var txB = await runtimeConn.BeginTransactionAsync(Ct))
            {
                await using (var setB = new NpgsqlCommand($"SELECT set_config('app.active_shop_id', '{shopB.Id:D}', true)", runtimeConn, txB))
                    await setB.ExecuteNonQueryAsync(Ct);
                await using (var countCmd = new NpgsqlCommand("SELECT count(*) FROM public.license_history_review_observations", runtimeConn, txB))
                    Assert.Equal(0L, (long)(await countCmd.ExecuteScalarAsync(Ct))!);
                await using var crossInsert = new NpgsqlCommand(
                    $"INSERT INTO public.license_history_review_observations (id, shop_id, case_id, offline_submission_id, observed_at, reason_code, subreason, fingerprint) VALUES ('{Guid.NewGuid():D}', '{shopA.Id:D}', '{newCase.Id:D}', '{envNew.Id:D}', now(), 'OfflineLicenceHistory.EvidenceInvalid', 'CrossShop', '{new string('c', 64)}')",
                    runtimeConn, txB);
                var rlsEx = await Assert.ThrowsAsync<PostgresException>(() => crossInsert.ExecuteNonQueryAsync(Ct));
                Assert.Equal("42501", rlsEx.SqlState);
                await txB.RollbackAsync(Ct);
            }

            await using (var txA = await runtimeConn.BeginTransactionAsync(Ct))
            {
                await using (var setA = new NpgsqlCommand($"SELECT set_config('app.active_shop_id', '{shopA.Id:D}', true)", runtimeConn, txA))
                    await setA.ExecuteNonQueryAsync(Ct);
                await using (var countCmd = new NpgsqlCommand("SELECT count(*) FROM public.license_history_review_observations", runtimeConn, txA))
                    Assert.Equal(1L, (long)(await countCmd.ExecuteScalarAsync(Ct))!);
                await using var updateCmd = new NpgsqlCommand(
                    $"UPDATE public.license_history_review_observations SET subreason = 'Edited' WHERE id = '{obs.Id:D}'", runtimeConn, txA);
                await Assert.ThrowsAsync<PostgresException>(() => updateCmd.ExecuteNonQueryAsync(Ct));
                await txA.RollbackAsync(Ct);
            }

            await using (var txDel = await runtimeConn.BeginTransactionAsync(Ct))
            {
                await using (var setA = new NpgsqlCommand($"SELECT set_config('app.active_shop_id', '{shopA.Id:D}', true)", runtimeConn, txDel))
                    await setA.ExecuteNonQueryAsync(Ct);
                await using var deleteCmd = new NpgsqlCommand(
                    $"DELETE FROM public.license_history_review_observations WHERE id = '{obs.Id:D}'", runtimeConn, txDel);
                await Assert.ThrowsAsync<PostgresException>(() => deleteCmd.ExecuteNonQueryAsync(Ct));
                await txDel.RollbackAsync(Ct);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var dropConn = new NpgsqlConnection(adminBuilder.ConnectionString);
            await dropConn.OpenAsync(Ct);
            await using var killCmd = new NpgsqlCommand(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{dbName}' AND pid <> pg_backend_pid()", dropConn);
            await killCmd.ExecuteNonQueryAsync(Ct);
            await using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\"", dropConn);
            await dropCmd.ExecuteNonQueryAsync(Ct);
        }
    }

    private async Task ApplyWithReleaseAsync(Binding binding, DateTimeOffset end, long revision, long releasedRevision)
    {
        await using var db = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        var now = _clock.Now;
        var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.Account, binding.Shop, binding.Request,
            new(revision, now, "Unrestricted", releasedRevision),
            new(revision, now, "Active", binding.Term, "Paid", now.AddDays(-1), end, end, 2, 3));
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(Guid.NewGuid(), "test-key", "test-public-key", now, now.AddMinutes(1),
            SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        var fence = new ShopLicenceFence(db, new LicenceActionPolicy(), _clock);
        var result = await new EntitlementProjectionStore(db, _clock, fence).ApplyAsync(snapshot, body, proof, Ct);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
    }

    private sealed class ToggleKeyEvidenceStore(IOfflineLicenceEvidenceStore inner, Func<bool> hideKey) : IOfflineLicenceEvidenceStore
    {
        public bool IsBeingReserved(InvoiceLease lease) => inner.IsBeingReserved(lease);
        public Task<OfflineAuthFacts?> ReadAuthAsync(Guid shopId, Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken ct) =>
            inner.ReadAuthAsync(shopId, userId, sessionId, now, ct);
        public Task<ErrorOr<T>> ExecuteInSavepointAsync<T>(string name, Func<Task<ErrorOr<T>>> action, CancellationToken ct) =>
            inner.ExecuteInSavepointAsync(name, action, ct);
        public Task<bool> HasConflictingReceiptAsync(Guid shopId, string deviceId, string clientSaleId, string invoiceNumber, string payloadHash, CancellationToken ct) =>
            inner.HasConflictingReceiptAsync(shopId, deviceId, clientSaleId, invoiceNumber, payloadHash, ct);
        public Task<Guid?> FindConflictingReceiptIdAsync(Guid shopId, string deviceId, string clientSaleId, string invoiceNumber, string payloadHash, CancellationToken ct) =>
            inner.FindConflictingReceiptIdAsync(shopId, deviceId, clientSaleId, invoiceNumber, payloadHash, ct);
        public Task<bool> HasGrantForLeaseAsync(Guid shopId, Guid leaseId, CancellationToken ct) =>
            inner.HasGrantForLeaseAsync(shopId, leaseId, ct);
        public void StageGrant(OfflineLicenseGrant grant) => inner.StageGrant(grant);
        public Task<OfflineLicenseGrant?> FindGrantAsync(Guid shopId, Guid grantId, CancellationToken ct) =>
            inner.FindGrantAsync(shopId, grantId, ct);
        public Task<InvoiceLease?> FindLeaseAsync(Guid shopId, Guid leaseId, CancellationToken ct) =>
            inner.FindLeaseAsync(shopId, leaseId, ct);
        public Task<OfflineSnapshotFacts> ReadSnapshotAsync(Guid shopId, Guid snapshotId, CancellationToken ct) =>
            inner.ReadSnapshotAsync(shopId, snapshotId, ct);
        public Task<OfflineLicensePermit?> FindPermitAsync(Guid shopId, Guid snapshotId, CancellationToken ct) =>
            inner.FindPermitAsync(shopId, snapshotId, ct);
        public Task<OfflineLicensePermit?> FindPermitByIdAsync(Guid shopId, Guid permitId, CancellationToken ct) =>
            inner.FindPermitByIdAsync(shopId, permitId, ct);
        public Task<IReadOnlyList<LicenseGrantEvidence>> ReadGrantEvidenceAsync(Guid shopId, Guid accountId, Guid affiliationRequestId, CancellationToken ct) =>
            inner.ReadGrantEvidenceAsync(shopId, accountId, affiliationRequestId, ct);
        public Task<ErrorOr<Success>> RetainKeyAsync(Guid shopId, string keyId, byte[] publicKey, DateTimeOffset now, CancellationToken ct) =>
            inner.RetainKeyAsync(shopId, keyId, publicKey, now, ct);
        public Task<byte[]?> FindKeyAsync(Guid shopId, string keyId, CancellationToken ct) =>
            hideKey() ? Task.FromResult<byte[]?>(null) : inner.FindKeyAsync(shopId, keyId, ct);
        public Task<bool> TryInsertPermitAsync(OfflineLicensePermit permit, CancellationToken ct) =>
            inner.TryInsertPermitAsync(permit, ct);
    }
}
