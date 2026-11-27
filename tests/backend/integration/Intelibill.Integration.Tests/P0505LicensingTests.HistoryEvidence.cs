using System.Text.Json;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Infrastructure.Repositories;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0505LicensingTests
{
    [Fact]
    public async Task Quarantined_snapshot_cannot_establish_commercial_coverage()
    {
        var start = At("2027-01-20T12:00:00Z");
        var soldAt = start.AddDays(-2);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(owner, start, start.AddDays(3));
        var issue = await SeedUnanchoredHoldAsync(actor, soldAt, actor.User, actor.User);

        // A second event for revision one claims a longer term than the applied projection.
        var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            actor.Binding.Account, actor.Shop, actor.Binding.Request, new(1, start, "Unrestricted", null),
            new(1, start, "Active", actor.Binding.Term, "Paid", start.AddDays(-3), start.AddDays(3), start.AddDays(3), 2, 3));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(Guid.NewGuid(), "fixture", "fixture-key", start, start.AddMinutes(1),
            SignedServiceAssertion.Digest(bytes), "fixture-verified-assertion");
        await using var db = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        var store = new EntitlementProjectionStore(db, _clock, new ShopLicenceFence(db, new LicenceActionPolicy(), _clock));
        var receipt = await store.ApplyAsync(snapshot, bytes, proof, Ct);
        Assert.False(receipt.IsError);
        var ack = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(receipt.Value, EntitlementSnapshotsV1.Json);
        Assert.Equal("NeedsReview", ack?.Outcome);
        Assert.Equal("ConflictingShopScope", ack?.Disposition);

        var detail = await GetCaseDetailAsync(owner, issue.Id);
        Assert.Equal("HistoricalCoverageUnproven", detail.GetProperty("licenceHistory").GetProperty("currentSubreason").GetString());
        Assert.False(detail.GetProperty("licenceHistory").GetProperty("retryEligible").GetBoolean());
    }

    [Theory]
    [InlineData(false, "IssuerMissing")]
    [InlineData(true, "IssuerUnverified")]
    public async Task Unattributable_recovery_has_no_supported_history(bool asserted, string expectedSubreason)
    {
        var start = At("2027-01-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(owner, start, start.AddDays(1));
        var issue = await SeedUnanchoredHoldAsync(actor, start, asserted ? actor.User : null, verifiedIssuer: null);

        var detail = await GetCaseDetailAsync(owner, issue.Id);
        var history = detail.GetProperty("licenceHistory");
        Assert.Equal("OfflineLicenceHistory.EvidenceMissing", history.GetProperty("currentReasonCode").GetString());
        Assert.Equal(expectedSubreason, history.GetProperty("currentSubreason").GetString());
        Assert.False(history.GetProperty("retryEligible").GetBoolean());
    }

    [Fact]
    public async Task Open_or_attach_reuses_resolved_typed_case_without_changing_version()
    {
        var start = At("2027-01-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(owner, start, start.AddDays(1));
        var seeded = await SeedUnanchoredHoldAsync(actor, start, actor.User, actor.User);
        await using var db = Open(Migrator);
        var issue = await db.ReconciliationIssues.SingleAsync(i => i.Id == seeded.Id, Ct);
        issue.Resolve(actor.User, start);
        await db.SaveChangesAsync(Ct);
        var version = issue.Version;
        db.ChangeTracker.Clear();

        var analyzer = new OfflineSaleVarianceAnalyzer(new ReconciliationIssueRepository(db),
            new LicenseHistoryReviewObservationRepository(db), new UnitOfWork(db));
        var reason = Errors.OfflineLicenceHistory.EvidenceInvalid;
        var result = await analyzer.OpenOrAttachLicenceHistoryAsync(actor.Shop, null, issue.ClientSaleId, Device,
            issue.InvoiceNumber!, issue.OfflineSubmissionId!.Value, actor.User, reason,
            new(reason.Code, "PayloadCorrupted", "payloadJson"), start.AddMinutes(1), Ct);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(issue.Id, result.Id);
        var retained = await db.ReconciliationIssues.AsNoTracking().SingleAsync(i => i.OfflineSubmissionId == issue.OfflineSubmissionId, Ct);
        Assert.Equal(ReconciliationCaseStatus.Resolved, retained.Status);
        Assert.Equal(version, retained.Version);
        Assert.False(await db.LicenseHistoryReviewObservations.AnyAsync(o => o.CaseId == issue.Id, Ct));
    }

    private async Task<ReconciliationIssue> SeedUnanchoredHoldAsync(Actor actor, DateTimeOffset soldAt,
        Guid? assertedIssuer, Guid? verifiedIssuer)
    {
        await using var db = Open(Migrator);
        var year = FiscalYear.ForDate(soldAt);
        var sequence = await db.InvoiceSequences.FirstOrDefaultAsync(
            s => s.ShopId == actor.Shop && s.FiscalYearStart == year.StartYear && s.SeriesCode == "A", Ct);
        if (sequence is null)
        {
            var shop = await db.Shops.SingleAsync(s => s.Id == actor.Shop, Ct);
            sequence = InvoiceSequence.Open(actor.Shop, shop.SellerRegistrationKey, "A", year);
            db.InvoiceSequences.Add(sequence);
        }
        var lease = InvoiceLease.Create(actor.Shop, sequence.Id, Device, year.StartYear, "A/2627/", 1, 25, 6,
            soldAt.AddHours(-1), soldAt.AddDays(1), actor.User, actor.Session);
        db.InvoiceLeases.Add(lease);
        var item = await db.Items.SingleAsync(i => i.ShopId == actor.Shop, Ct);
        var batch = await db.InventoryBatches.SingleAsync(b => b.ShopId == actor.Shop, Ct);
        var invoice = new OfflineSaleSyncCommand(Guid.NewGuid().ToString("N"), "A/2627/000001", soldAt, null, null, null,
            PaymentMethod.Cash, 2m, 0m, 2m, 2m, 0m, 0m, 2m, InstantDiscountType.None, 0m, null, null, null, null,
            [new(item.Barcode, "LOT", item.Name, 1m, 1m, 2m, 2m, 0m, false, batch.Id,
                2m, 0m, 0m, 2m, 0m, 2m, null, null, InstantDiscountType.None, 0m, null)], CalculationVersion: 1, IssuedByUserId: assertedIssuer);
        var envelope = OfflineSubmission.Record(actor.Shop, Device, invoice.ClientSaleId, invoice.InvoiceNumber,
            OfflineSaleSyncIdempotencyHasher.ComputeHash(actor.Shop, Device, invoice), OfflineSubmissionPayload.Serialize(invoice),
            soldAt, soldAt, OfflineSubmissionSource.Recovery, actor.User, assertedIssuer, verifiedIssuer, lease.Id, "fixture-recovery");
        db.OfflineSubmissions.Add(envelope);
        var reason = Errors.OfflineLicenceHistory.EvidenceMissing;
        var issue = ReconciliationIssue.Create(actor.Shop, null, invoice.ClientSaleId, Device,
            ReconciliationIssueType.LicenseHistoryReview, reason.Code, reason.Description, actor.User,
            invoiceNumber: invoice.InvoiceNumber, offlineSubmissionId: envelope.Id);
        db.ReconciliationIssues.Add(issue);
        await db.SaveChangesAsync(Ct);
        return issue;
    }
}
