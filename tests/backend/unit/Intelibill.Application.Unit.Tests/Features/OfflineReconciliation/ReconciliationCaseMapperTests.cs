using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.OfflineReconciliation;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.OfflineReconciliation;

public sealed class ReconciliationCaseMapperTests
{
    private const string DeviceId = "device-1";
    private const string InvoiceNumber = "INV-2026-27-000001";
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private readonly IReconciliationCaseEventRepository _eventRepository = Substitute.For<IReconciliationCaseEventRepository>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IInvoiceLeaseRepository _leaseRepository = Substitute.For<IInvoiceLeaseRepository>();
    private readonly IInventoryAdjustmentRepository _adjustmentRepository = Substitute.For<IInventoryAdjustmentRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly IOfflineSubmissionRepository _submissionRepository = Substitute.For<IOfflineSubmissionRepository>();

    private readonly Guid _shopId = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _cashier = Guid.NewGuid();
    private readonly Guid _itemId = Guid.NewGuid();
    private readonly Guid _batchId = Guid.NewGuid();

    public ReconciliationCaseMapperTests()
    {
        _eventRepository.GetByCaseAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _userRepository.GetDisplayNamesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
    }

    [Fact]
    public async Task Detail_ShowsClaimedTimeFirstReceiptAndLeaseWindow()
    {
        var lease = InvoiceLease.Create(_shopId, Guid.NewGuid(), DeviceId, 2026, "INV-2026-27-", 1, 50, 6, Now.AddDays(-9), Now.AddDays(-2));
        _leaseRepository.GetByIdAsync(lease.Id, Arg.Any<CancellationToken>()).Returns(lease);
        var envelope = Envelope(leaseId: lease.Id);
        var issue = HeldCase(Errors.OfflineReconciliation.IssueTimeInFuture.Code, envelope);

        var detail = await Mapper().ToDetailAsync(issue, envelope, ShopRole.Owner, _owner, CancellationToken.None);

        Assert.Equal(envelope.ClaimedSoldAt, detail.Submission!.ClaimedSoldAt);
        Assert.Equal(envelope.ReceivedAt, detail.Submission.ReceivedAt);
        Assert.Equal(lease.ReservedAt, detail.Submission.LeaseReservedAt);
        Assert.Equal(lease.ExpiresAt, detail.Submission.LeaseExpiresAt);
    }

    [Fact]
    public async Task Detail_WithoutAFoundLease_LeavesTheWindowEmpty()
    {
        var foreign = InvoiceLease.Create(Guid.NewGuid(), Guid.NewGuid(), DeviceId, 2026, "INV-2026-27-", 1, 50, 6, Now.AddDays(-9), Now.AddDays(-2));
        _leaseRepository.GetByIdAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        var envelope = Envelope(leaseId: foreign.Id);
        var noLease = Envelope(leaseId: null);

        var withForeign = await Mapper().ToDetailAsync(HeldCase(Errors.OfflineReconciliation.IssueTimeInFuture.Code, envelope), envelope, ShopRole.Owner, _owner, CancellationToken.None);
        var withNone = await Mapper().ToDetailAsync(HeldCase(Errors.Sale.InvoiceLeaseNotFound.Code, noLease), noLease, ShopRole.Owner, _owner, CancellationToken.None);

        Assert.Null(withForeign.Submission!.LeaseReservedAt);
        Assert.Null(withForeign.Submission.LeaseExpiresAt);
        Assert.Null(withNone.Submission!.LeaseReservedAt);
    }

    [Fact]
    public async Task StockCase_ListsOnlyAdjustmentsTheLinkWouldAccept()
    {
        var issue = StockCase();
        var covering = Adjustment(InventoryAdjustmentReason.FoundStock, 2m, "ADJ-0007");
        var otherGain = Adjustment(InventoryAdjustmentReason.OtherGain, 5m, "ADJ-0008");
        var tooSmall = Adjustment(InventoryAdjustmentReason.FoundStock, 1m, "ADJ-0009");
        var count = Adjustment(InventoryAdjustmentReason.StockCountCorrection, 2m, "ADJ-0010");
        _adjustmentRepository.GetUnlinkedIncreasesAsync(_shopId, _itemId, _batchId, Arg.Any<CancellationToken>())
            .Returns([covering, otherGain, tooSmall, count]);

        var detail = await Mapper().ToDetailAsync(issue, null, ShopRole.Manager, _owner, CancellationToken.None);

        Assert.Equal(["LinkStockCorrection", "Escalate"], detail.AvailableActions);
        Assert.Equal(
            [
                new ReconciliationLinkCandidateDto(covering.Id, "Adjustment", "ADJ-0007", 2m, covering.PerformedAt),
                new ReconciliationLinkCandidateDto(otherGain.Id, "Adjustment", "ADJ-0008", 5m, otherGain.PerformedAt),
            ],
            detail.LinkCandidates);
    }

    [Fact]
    public async Task StaffOrResolvedCase_HasNoCandidates()
    {
        var issue = StockCase(createdBy: _cashier);
        _adjustmentRepository.GetUnlinkedIncreasesAsync(_shopId, _itemId, _batchId, Arg.Any<CancellationToken>())
            .Returns([Adjustment(InventoryAdjustmentReason.FoundStock, 2m, "ADJ-0007")]);

        var staffView = await Mapper().ToDetailAsync(issue, null, ShopRole.Staff, _cashier, CancellationToken.None);
        issue.Close(ReconciliationAction.LinkStockCorrection, _owner, Now, "closed", null, null);
        var resolvedView = await Mapper().ToDetailAsync(issue, null, ShopRole.Owner, _owner, CancellationToken.None);

        Assert.Empty(staffView.LinkCandidates);
        Assert.Empty(resolvedView.LinkCandidates);
        await _adjustmentRepository.DidNotReceive().GetUnlinkedIncreasesAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HeldCase_ListsTheSaleCarryingItsEnvelope()
    {
        var envelope = Envelope(leaseId: null);
        var issue = HeldCase(Errors.OfflineReconciliation.ProvenanceUnknown.Code, envelope);
        var sale = SaleFor(envelope.ClientSaleId, envelope.PayloadHash, InvoiceNumber);
        _saleRepository.GetByClientSaleIdAsync(_shopId, DeviceId, envelope.ClientSaleId, Arg.Any<CancellationToken>()).Returns(sale);

        var detail = await Mapper().ToDetailAsync(issue, envelope, ShopRole.Owner, _owner, CancellationToken.None);

        Assert.Equal([new ReconciliationLinkCandidateDto(sale.Id, "Sale", InvoiceNumber, null, sale.SoldAt)], detail.LinkCandidates);
    }

    [Fact]
    public async Task HeldCase_SaleWithAnotherPayload_NotListed()
    {
        var envelope = Envelope(leaseId: null);
        var issue = HeldCase(Errors.OfflineReconciliation.IssueTimeInFuture.Code, envelope);
        _saleRepository.GetByClientSaleIdAsync(_shopId, DeviceId, envelope.ClientSaleId, Arg.Any<CancellationToken>())
            .Returns(SaleFor(envelope.ClientSaleId, "ANOTHER-HASH", InvoiceNumber));

        var detail = await Mapper().ToDetailAsync(issue, envelope, ShopRole.Owner, _owner, CancellationToken.None);

        Assert.Contains("LinkExistingSale", detail.AvailableActions);
        Assert.Empty(detail.LinkCandidates);
    }

    [Fact]
    public async Task InvoiceNumberUsedCase_ListsTheHolderOnlyWhenTheLinkWouldAcceptIt()
    {
        // The number is held by an invoice with another client sale id: linking it would be refused, so it is not offered.
        var envelope = Envelope(leaseId: null);
        var issue = HeldCase(Errors.Sale.InvoiceNumberAlreadyUsed.Code, envelope, ReconciliationIssueType.InvoiceConflict);
        _saleRepository.GetByInvoiceNumberAsync(_shopId, InvoiceNumber, Arg.Any<CancellationToken>())
            .Returns(SaleFor("client-other", "OTHER-HASH", InvoiceNumber));

        var foreignHolder = await Mapper().ToDetailAsync(issue, envelope, ShopRole.Owner, _owner, CancellationToken.None);

        // A concurrent identical sync posted this very invoice between the two checks: the holder is the sale itself.
        var itself = SaleFor(envelope.ClientSaleId, envelope.PayloadHash, InvoiceNumber);
        _saleRepository.GetByInvoiceNumberAsync(_shopId, InvoiceNumber, Arg.Any<CancellationToken>()).Returns(itself);
        var ownHolder = await Mapper().ToDetailAsync(issue, envelope, ShopRole.Owner, _owner, CancellationToken.None);

        Assert.Empty(foreignHolder.LinkCandidates);
        Assert.Equal([new ReconciliationLinkCandidateDto(itself.Id, "Sale", InvoiceNumber, null, itself.SoldAt)], ownHolder.LinkCandidates);
    }

    [Fact]
    public async Task LegacyHeldCase_ListsTheSaleWithItsIdentity()
    {
        var issue = ReconciliationIssue.Create(
            _shopId, null, "client-1", DeviceId, ReconciliationIssueType.InvoiceConflict,
            Errors.Sale.InvoiceLeaseNotFound.Code, Errors.Sale.InvoiceLeaseNotFound.Description, _cashier);
        var sale = SaleFor("client-1", "ANY-HASH", InvoiceNumber);
        _saleRepository.GetByClientSaleIdAsync(_shopId, DeviceId, "client-1", Arg.Any<CancellationToken>()).Returns(sale);

        var detail = await Mapper().ToDetailAsync(issue, null, ShopRole.Owner, _owner, CancellationToken.None);

        Assert.False(detail.HasOriginalEnvelope);
        Assert.Equal([new ReconciliationLinkCandidateDto(sale.Id, "Sale", InvoiceNumber, null, sale.SoldAt)], detail.LinkCandidates);
    }

    [Fact]
    public async Task IdempotencyConflict_ListsTheCasesSaleWhenAnEarlierEnvelopeMatchesIt()
    {
        var original = Envelope(leaseId: null, hash: "HASH-A");
        var conflicting = Envelope(leaseId: null, hash: "HASH-B");
        var sale = SaleFor(original.ClientSaleId, "HASH-A", InvoiceNumber);
        var issue = ReconciliationIssue.Create(
            _shopId, sale.Id, original.ClientSaleId, DeviceId, ReconciliationIssueType.ValidationConflict,
            Errors.Sale.IdempotencyConflict.Code, "conflict", _cashier, invoiceNumber: InvoiceNumber, offlineSubmissionId: conflicting.Id);
        _saleRepository.GetByClientSaleIdAsync(_shopId, DeviceId, original.ClientSaleId, Arg.Any<CancellationToken>()).Returns(sale);
        _submissionRepository.FindEarliestByHashAsync(_shopId, DeviceId, original.ClientSaleId, "HASH-A", Arg.Any<CancellationToken>()).Returns(original);

        var detail = await Mapper().ToDetailAsync(issue, conflicting, ShopRole.Owner, _owner, CancellationToken.None);

        Assert.Equal([new ReconciliationLinkCandidateDto(sale.Id, "Sale", InvoiceNumber, null, sale.SoldAt)], detail.LinkCandidates);
    }

    private ReconciliationCaseMapper Mapper() =>
        new(_eventRepository, _userRepository, _leaseRepository, _adjustmentRepository, _saleRepository, _submissionRepository);

    private ReconciliationIssue StockCase(Guid? createdBy = null) =>
        ReconciliationIssue.Create(
            _shopId,
            Guid.NewGuid(),
            "client-1",
            DeviceId,
            ReconciliationIssueType.StockVariance,
            "offline_sync.stock_shortage",
            "printed 5, consumed 3",
            createdBy ?? _cashier,
            itemId: _itemId,
            inventoryBatchId: _batchId,
            printedQuantity: 5m,
            availableQuantity: 3m,
            consumedQuantity: 3m,
            invoiceNumber: InvoiceNumber);

    private ReconciliationIssue HeldCase(string code, OfflineSubmission envelope, ReconciliationIssueType type = ReconciliationIssueType.IssueTimingReview) =>
        ReconciliationIssue.Create(
            _shopId, null, envelope.ClientSaleId, DeviceId, type, code, "held", _cashier,
            invoiceNumber: envelope.InvoiceNumber, offlineSubmissionId: envelope.Id);

    private InventoryAdjustment Adjustment(InventoryAdjustmentReason reason, decimal quantity, string number) =>
        InventoryAdjustment.Create(
            _shopId,
            _itemId,
            _batchId,
            number,
            InventoryAdjustmentDirection.Increase,
            reason,
            quantity,
            80m,
            80m * quantity,
            0m,
            quantity,
            0m,
            quantity,
            Now.AddHours(-1),
            _owner,
            "Found behind the shelf",
            _owner).Value;

    private OfflineSubmission Envelope(Guid? leaseId, string hash = "HASH")
    {
        var payload = new OfflineSaleSyncCommand(
            "client-1", InvoiceNumber, Now.AddHours(1), null, null, null, PaymentMethod.Cash, 118m, 0m, 100m, 118m, 0m, 18m, 118m,
            InstantDiscountType.None, 0m, null, null, null, null,
            [new OfflineSaleSyncLineCommand("BC-1", "B-1", "Rice", 1m, 80m, 100m, 120m, 18m, false, _batchId, 100m, 0m, 0m, 100m, 18m, 118m, null, null, InstantDiscountType.None, 0m, null)]);
        return OfflineSubmission.Record(
            _shopId, DeviceId, "client-1", InvoiceNumber, hash, OfflineSubmissionPayload.Serialize(payload),
            payload.SoldAt, Now, OfflineSubmissionSource.Sync, _cashier, null, null, leaseId, null);
    }

    private Sale SaleFor(string clientSaleId, string requestHash, string invoiceNumber) =>
        Sale.Create(
            _shopId,
            _cashier,
            OfflineSaleSyncIdempotencyHasher.ComputeKey(DeviceId, clientSaleId),
            requestHash,
            invoiceNumber,
            null,
            null,
            null,
            PaymentMethod.Cash,
            Now.AddHours(1),
            global::Intelibill.Domain.ValueObjects.BusinessDay.InDefaultZone(Now.AddHours(1)),
            118m,
            0m,
            118m,
            18m,
            [SaleItem.CreateGoods(_shopId, _itemId, _batchId, lineName: "Rice", lineCode: "BC-1", 1m, 80m, 100m, 120m, 18m, false, false)],
            source: SaleSource.Offline,
            clientSaleId: clientSaleId,
            deviceId: DeviceId,
            syncedAt: Now);
}
