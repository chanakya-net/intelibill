using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.OfflineReconciliation;
using Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Unit.Tests.Features.OfflineReconciliation;

public sealed class ResolveReconciliationCaseCommandHandlerTests
{
    private const string DeviceId = "device-1";
    private const string InvoiceNumber = "INV-2026-27-000001";
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IReconciliationIssueRepository _issueRepository = Substitute.For<IReconciliationIssueRepository>();
    private readonly IReconciliationCaseEventRepository _eventRepository = Substitute.For<IReconciliationCaseEventRepository>();
    private readonly IOfflineSubmissionRepository _submissionRepository = Substitute.For<IOfflineSubmissionRepository>();
    private readonly IInventoryAdjustmentRepository _adjustmentRepository = Substitute.For<IInventoryAdjustmentRepository>();
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly IInvoiceLeaseRepository _leaseRepository = Substitute.For<IInvoiceLeaseRepository>();
    private readonly IDiscountRuleRepository _discountRuleRepository = Substitute.For<IDiscountRuleRepository>();
    private readonly IOfflineSalePoster _poster = Substitute.For<IOfflineSalePoster>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IFinancialOperationExecutor _executor = Substitute.For<IFinancialOperationExecutor>();

    private readonly Shop _shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
    private readonly User _owner = User.CreateWithEmail("owner@test.com", "hash", "Olive", "Owner");
    private readonly User _staff = User.CreateWithEmail("staff@test.com", "hash", "Sam", "Staff");
    private readonly Guid _itemId = Guid.NewGuid();
    private readonly InventoryBatch _batch;
    private readonly DomainInventory _inventory;
    private readonly List<ReconciliationCaseEvent> _events = [];

    public ResolveReconciliationCaseCommandHandlerTests()
    {
        _owner.AddShopMembership(ShopMembership.Create(_shop.Id, _owner.Id, ShopRole.Owner, true));
        _staff.AddShopMembership(ShopMembership.Create(_shop.Id, _staff.Id, ShopRole.Staff, true));
        _userRepository.GetByIdWithDetailsAsync(_owner.Id, Arg.Any<CancellationToken>()).Returns(_owner);
        _userRepository.GetByIdWithDetailsAsync(_staff.Id, Arg.Any<CancellationToken>()).Returns(_staff);
        _userRepository.GetDisplayNamesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [_owner.Id] = "Olive Owner", [_staff.Id] = "Sam Staff" });

        // The batch printed 5 with 3 on hand; a found-stock adjustment has since added 2.
        _batch = InventoryBatch.Create(_shop.Id, _itemId, "B-1", 2m, 80m, 120m, 110m, 5m, false, null, null, null, _owner.Id).Value;
        _inventory = DomainInventory.Create(_shop.Id, _itemId, 2m, 0m, 50m, _owner.Id).Value;
        _batchRepository.GetByIdAsync(_batch.Id, Arg.Any<CancellationToken>()).Returns(_batch);
        _inventoryRepository.GetByItemAsync(_shop.Id, _itemId, Arg.Any<CancellationToken>()).Returns(_inventory);

        _eventRepository.When(r => r.AddAsync(Arg.Any<ReconciliationCaseEvent>(), Arg.Any<CancellationToken>()))
            .Do(call => _events.Add(call.Arg<ReconciliationCaseEvent>()));
        _eventRepository.GetByCaseAsync(_shop.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => _events.ToList());
        _discountRuleRepository.GetActiveByShopAsync(_shop.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<DiscountRule>());

        _executor
            .ExecuteAsync(
                Arg.Any<FinancialOperationScope>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<ErrorOr<ReconciliationCaseDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<CancellationToken, Task<ErrorOr<ReconciliationCaseDto>>>>(4)(CancellationToken.None));
    }

    [Fact]
    public async Task Acknowledge_OnStockVariance_Refused()
    {
        var issue = StockCase();

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.AcknowledgeVariance));

        Assert.Equal(Errors.Reconciliation.ActionNotAllowed.Code, result.FirstError.Code);
        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkStockCorrection_WithStockCountAdjustment_Refused()
    {
        var issue = StockCase();
        var adjustment = Adjustment(InventoryAdjustmentReason.StockCountCorrection, 2m);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.LinkStockCorrection, adjustment.Id));

        Assert.Equal(Errors.Reconciliation.LinkedAdjustmentInvalid.Code, result.FirstError.Code);
        Assert.Equal(2m, _batch.Quantity);
        await _stockTransactionRepository.DidNotReceive().AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkStockCorrection_InsufficientQuantity_Refused()
    {
        var issue = StockCase();
        var adjustment = Adjustment(InventoryAdjustmentReason.FoundStock, 1m);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.LinkStockCorrection, adjustment.Id));

        Assert.Equal(Errors.Reconciliation.LinkedAdjustmentInvalid.Code, result.FirstError.Code);
        Assert.Equal(2m, _batch.Quantity);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkStockCorrection_OtherBatchOrVoidedAdjustment_Refused()
    {
        var issue = StockCase();
        var otherBatch = InventoryAdjustment.Create(
            _shop.Id, _itemId, Guid.NewGuid(), "ADJ-2", InventoryAdjustmentDirection.Increase, InventoryAdjustmentReason.FoundStock,
            2m, 80m, 160m, 0m, 2m, 0m, 2m, Now, _owner.Id, null, _owner.Id).Value;
        _adjustmentRepository.GetByIdAsync(otherBatch.Id, Arg.Any<CancellationToken>()).Returns(otherBatch);
        var voided = Adjustment(InventoryAdjustmentReason.FoundStock, 2m);
        voided.Void(Now, _owner.Id, "Entered twice", Guid.NewGuid());

        var wrongBatch = await Handle(Command(_owner.Id, issue, ReconciliationAction.LinkStockCorrection, otherBatch.Id));
        var wasVoided = await Handle(Command(_owner.Id, issue, ReconciliationAction.LinkStockCorrection, voided.Id));

        Assert.Equal(Errors.Reconciliation.LinkedAdjustmentInvalid.Code, wrongBatch.FirstError.Code);
        Assert.Equal(Errors.Reconciliation.LinkedAdjustmentInvalid.Code, wasVoided.FirstError.Code);
    }

    [Fact]
    public async Task LinkStockCorrection_FoundStock_PostsOneCatchUpMovement()
    {
        var issue = StockCase();
        var adjustment = Adjustment(InventoryAdjustmentReason.FoundStock, 2m);
        StockTransaction? movement = null;
        _stockTransactionRepository.When(r => r.AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>()))
            .Do(call => movement = call.Arg<StockTransaction>());

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.LinkStockCorrection, adjustment.Id) with { OperationId = "resolve-1" });

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.Equal(0m, _batch.Quantity);
        Assert.Equal(0m, _inventory.Quantity);
        Assert.NotNull(movement);
        Assert.Equal(StockTransactionType.Out, movement!.TransactionType);
        Assert.Equal(-2m, movement.Quantity);
        Assert.Equal(InvoiceNumber, movement.ReferenceNumber);
        Assert.Equal(_owner.Id, movement.PerformedBy);
        Assert.Equal(ReconciliationCaseStatus.Resolved, issue.Status);
        Assert.Equal(ReconciliationAction.LinkStockCorrection, issue.Resolution);
        Assert.Equal(adjustment.Id, issue.LinkedOperationId);
        Assert.Equal(movement.Id, issue.ResolutionStockTransactionId);
        var recorded = Assert.Single(_events);
        Assert.Equal(ReconciliationAction.LinkStockCorrection, recorded.Action);
        Assert.Equal(ReconciliationCaseStatus.Open, recorded.FromStatus);
        Assert.Equal(ReconciliationCaseStatus.Resolved, recorded.ToStatus);
        Assert.Equal("resolve-1", recorded.OperationKey);
        Assert.Equal(adjustment.Id, recorded.LinkedOperationId);
        Assert.Equal("Resolved", result.Value.Status);
        Assert.Equal("LinkStockCorrection", result.Value.Resolution);
        Assert.Empty(result.Value.AvailableActions);
        Assert.Equal("Olive Owner", result.Value.ResolvedBy!.DisplayName);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkStockCorrection_AdjustmentLinkedToAnotherCase_Conflicts()
    {
        var issue = StockCase();
        var adjustment = Adjustment(InventoryAdjustmentReason.FoundStock, 2m);
        _issueRepository.IsOperationLinkedAsync(_shop.Id, adjustment.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.LinkStockCorrection, adjustment.Id));

        Assert.Equal(Errors.Reconciliation.OperationAlreadyLinked.Code, result.FirstError.Code);
        Assert.Equal(2m, _batch.Quantity);
    }

    [Fact]
    public async Task StaleExpectedVersion_Conflicts()
    {
        var issue = Case(ReconciliationIssueType.PricingVariance, "offline_sync.pricing_variance");

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.AcknowledgeVariance) with { ExpectedVersion = 99u });

        Assert.Equal(Errors.Reconciliation.CaseVersionConflict.Code, result.FirstError.Code);
        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveLosesRowVersionRace_Conflicts()
    {
        var issue = Case(ReconciliationIssueType.PricingVariance, "offline_sync.pricing_variance");
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateConcurrencyException("conflict", new List<IUpdateEntry>()));

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.AcknowledgeVariance));

        Assert.Equal(Errors.Reconciliation.CaseVersionConflict.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task CaseLockNotGranted_ConcurrentChange()
    {
        var issue = Case(ReconciliationIssueType.PricingVariance, "offline_sync.pricing_variance");
        _issueRepository.LockForResolutionAsync(_shop.Id, issue.Id, Arg.Any<CancellationToken>()).Returns(RowLockOutcome.TimedOut);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.AcknowledgeVariance));

        Assert.Equal(Errors.Reconciliation.ConcurrentChange.Code, result.FirstError.Code);
        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
        await _issueRepository.DidNotReceive().GetTrackedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolvedCase_Refused()
    {
        var issue = Case(ReconciliationIssueType.PricingVariance, "offline_sync.pricing_variance");
        issue.Close(ReconciliationAction.AcknowledgeVariance, _owner.Id, Now, "Checked", null, null);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.AcknowledgeVariance));

        Assert.Equal(Errors.Reconciliation.CaseAlreadyResolved.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task UnknownCase_NotFound()
    {
        var result = await Handle(new ResolveReconciliationCaseCommand(
            _owner.Id, _shop.Id, Guid.NewGuid(), "op-1", 0u, nameof(ReconciliationAction.Escalate), "Look again", null));

        Assert.Equal(Errors.Reconciliation.CaseNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Staff_ResolveOtherThanEscalate_Forbidden()
    {
        var issue = Case(ReconciliationIssueType.PricingVariance, "offline_sync.pricing_variance", createdBy: _staff.Id);

        var result = await Handle(Command(_staff.Id, issue, ReconciliationAction.AcknowledgeVariance));

        Assert.Equal(Errors.Reconciliation.ActionForbidden.Code, result.FirstError.Code);
        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
    }

    [Fact]
    public async Task Staff_EscalateOwnCase_Escalates()
    {
        var issue = Case(ReconciliationIssueType.PricingVariance, "offline_sync.pricing_variance", createdBy: _staff.Id);

        var result = await Handle(Command(_staff.Id, issue, ReconciliationAction.Escalate));

        Assert.False(result.IsError);
        Assert.Equal(ReconciliationCaseStatus.Escalated, issue.Status);
        Assert.False(issue.IsResolved);
        Assert.Equal("Escalated", result.Value.Status);
        Assert.Empty(result.Value.AvailableActions);
        var recorded = Assert.Single(_events);
        Assert.Equal(ReconciliationAction.Escalate, recorded.Action);
        Assert.Equal(ReconciliationCaseStatus.Escalated, recorded.ToStatus);
        Assert.Equal(_staff.Id, recorded.ActorUserId);
    }

    [Fact]
    public async Task Staff_OtherUsersCase_NotFound()
    {
        var issue = Case(ReconciliationIssueType.PricingVariance, "offline_sync.pricing_variance", createdBy: _owner.Id);

        var result = await Handle(Command(_staff.Id, issue, ReconciliationAction.Escalate));

        Assert.Equal(Errors.Reconciliation.CaseNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task RetryHistoricalLease_WithoutAttributableUser_NotAllowed()
    {
        // A recovered envelope whose issuer was never verified: nobody can be attributed the sale.
        var envelope = Envelope(OfflineSubmissionSource.Recovery, asserted: _staff.Id, original: null);
        var issue = Case(ReconciliationIssueType.RecoveryReview, Errors.OfflineReconciliation.RecoveryReviewRequired.Code, envelope.Id);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.RetryHistoricalLease));

        Assert.Equal(Errors.Reconciliation.ActionNotAllowed.Code, result.FirstError.Code);
        await _poster.DidNotReceive().StageAsync(Arg.Any<OfflineSalePostingRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryHistoricalLease_ExistingIdenticalSale_LinksWithoutStaging()
    {
        var envelope = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null);
        var issue = Case(ReconciliationIssueType.IssueTimingReview, Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code, envelope.Id);
        StubLease();
        var existing = SaleFor(envelope, envelope.PayloadHash);
        _saleRepository.GetByClientSaleIdAsync(_shop.Id, DeviceId, envelope.ClientSaleId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.RetryHistoricalLease));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.Equal(existing.Id, envelope.SaleId);
        Assert.Equal(existing.Id, issue.SaleId);
        Assert.Equal(ReconciliationCaseStatus.Resolved, issue.Status);
        Assert.Equal(ReconciliationAction.RetryHistoricalLease, issue.Resolution);
        await _poster.DidNotReceive().StageAsync(Arg.Any<OfflineSalePostingRequest>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(existing.Id, result.Value.SaleId);
    }

    [Fact]
    public async Task RetryHistoricalLease_StagesStoredPayloadWithStoredHash()
    {
        var envelope = Envelope(OfflineSubmissionSource.Recovery, asserted: _staff.Id, original: _staff.Id);
        var issue = Case(ReconciliationIssueType.RecoveryReview, Errors.OfflineReconciliation.RecoveryReviewRequired.Code, envelope.Id);
        StubLease();
        var staged = SaleFor(envelope, envelope.PayloadHash);
        OfflineSalePostingRequest? request = null;
        _poster.StageAsync(Arg.Do<OfflineSalePostingRequest>(r => request = r), Arg.Any<CancellationToken>())
            .Returns(new OfflineSalePostingOutcome.Staged(staged, []));

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.RetryHistoricalLease));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.NotNull(request);
        Assert.Equal(envelope.PayloadHash, request!.PayloadHash);
        Assert.Equal(_staff.Id, request.AttributedUserId);
        Assert.Equal(_owner.Id, request.RecordedByUserId);
        Assert.Equal(envelope.Id, request.OfflineSubmissionId);
        Assert.Equal(DeviceId, request.DeviceId);
        Assert.Equal(envelope.ClientSaleId, request.ClientSaleId);
        Assert.Equal(InvoiceNumber, request.Sale.InvoiceNumber);
        Assert.Equal(Now, request.Now);
        Assert.Equal(staged.Id, envelope.SaleId);
        Assert.Equal(staged.Id, issue.SaleId);
        Assert.Equal(ReconciliationCaseStatus.Resolved, issue.Status);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryHistoricalLease_ReplayHeld_RejectedWithoutSaving()
    {
        var envelope = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null);
        var issue = Case(ReconciliationIssueType.IssueTimingReview, Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code, envelope.Id);
        StubLease();
        _poster.StageAsync(Arg.Any<OfflineSalePostingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OfflineSalePostingOutcome.Held(ReconciliationIssueType.ArithmeticConflict, Errors.Sale.OfflineArithmeticMismatch, "mismatch", []));

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.RetryHistoricalLease));

        Assert.Equal(
            [Errors.Reconciliation.ReplayRejected.Code, Errors.Sale.OfflineArithmeticMismatch.Code],
            result.Errors.Select(e => e.Code));
        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
        Assert.Null(envelope.SaleId);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryHistoricalLease_InvoiceNumberUsedByAnotherSale_Refused()
    {
        var envelope = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null);
        var issue = Case(ReconciliationIssueType.IssueTimingReview, Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code, envelope.Id);
        StubLease();
        _saleRepository.GetByInvoiceNumberAsync(_shop.Id, InvoiceNumber, Arg.Any<CancellationToken>())
            .Returns(Sale.Create(_shop.Id, InvoiceNumber, null, null, null, PaymentMethod.Cash, Now, 0m, 0m, 0m, 0m, []));

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.RetryHistoricalLease));

        Assert.Equal(Errors.Sale.InvoiceNumberAlreadyUsed.Code, result.FirstError.Code);
        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
        await _poster.DidNotReceive().StageAsync(Arg.Any<OfflineSalePostingRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryHistoricalLease_LeaseNoLongerFound_Refused()
    {
        var envelope = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null);
        var issue = Case(ReconciliationIssueType.IssueTimingReview, Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code, envelope.Id);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.RetryHistoricalLease));

        Assert.Equal(Errors.Sale.InvoiceLeaseNotFound.Code, result.FirstError.Code);
        await _poster.DidNotReceive().StageAsync(Arg.Any<OfflineSalePostingRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkExistingSale_OnIdempotencyConflict_KeepsConflictingEnvelopeUnlinked()
    {
        var original = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null, hash: "HASH-A");
        var conflicting = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null, hash: "HASH-B");
        var sale = SaleFor(original, "HASH-A");
        var issue = Case(ReconciliationIssueType.ValidationConflict, Errors.Sale.IdempotencyConflict.Code, conflicting.Id, saleId: sale.Id);
        _saleRepository.GetByIdAsync(sale.Id, _shop.Id, Arg.Any<CancellationToken>()).Returns(sale);
        _submissionRepository.FindEarliestByHashAsync(_shop.Id, DeviceId, original.ClientSaleId, "HASH-A", Arg.Any<CancellationToken>()).Returns(original);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.LinkExistingSale, sale.Id));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.Null(conflicting.SaleId);
        Assert.Equal(sale.Id, issue.SaleId);
        Assert.Equal(sale.Id, issue.LinkedOperationId);
        Assert.Equal(ReconciliationAction.LinkExistingSale, issue.Resolution);
    }

    [Fact]
    public async Task LinkExistingSale_OnIdempotencyConflict_OtherSale_Refused()
    {
        var original = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null, hash: "HASH-A");
        var conflicting = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null, hash: "HASH-B");
        var sale = SaleFor(original, "HASH-A");
        var other = SaleFor(original, "HASH-A");
        var issue = Case(ReconciliationIssueType.ValidationConflict, Errors.Sale.IdempotencyConflict.Code, conflicting.Id, saleId: sale.Id);
        _saleRepository.GetByIdAsync(other.Id, _shop.Id, Arg.Any<CancellationToken>()).Returns(other);
        _submissionRepository.FindEarliestByHashAsync(_shop.Id, DeviceId, original.ClientSaleId, "HASH-A", Arg.Any<CancellationToken>()).Returns(original);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.LinkExistingSale, other.Id));

        Assert.Equal(Errors.Reconciliation.SaleIdentityMismatch.Code, result.FirstError.Code);
        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
    }

    [Fact]
    public async Task LinkExistingSale_HashMismatch_Refused()
    {
        var envelope = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null);
        var issue = Case(ReconciliationIssueType.IssueTimingReview, Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code, envelope.Id);
        var sale = SaleFor(envelope, "SOME-OTHER-HASH");
        _saleRepository.GetByIdAsync(sale.Id, _shop.Id, Arg.Any<CancellationToken>()).Returns(sale);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.LinkExistingSale, sale.Id));

        Assert.Equal(Errors.Reconciliation.SaleIdentityMismatch.Code, result.FirstError.Code);
        Assert.Null(envelope.SaleId);
    }

    [Fact]
    public async Task LinkExistingSale_IdenticalSale_LinksEnvelopeAndCase()
    {
        var envelope = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null);
        var issue = Case(ReconciliationIssueType.ProvenanceReview, Errors.OfflineReconciliation.IssuerNotSubmitter.Code, envelope.Id);
        var sale = SaleFor(envelope, envelope.PayloadHash);
        _saleRepository.GetByIdAsync(sale.Id, _shop.Id, Arg.Any<CancellationToken>()).Returns(sale);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.LinkExistingSale, sale.Id));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.Equal(sale.Id, envelope.SaleId);
        Assert.Equal(sale.Id, issue.SaleId);
        Assert.Equal(ReconciliationCaseStatus.Resolved, issue.Status);
    }

    [Fact]
    public async Task LinkExistingSale_EscalatedCaseWhoseEnvelopePostedLater_Links()
    {
        // A recovered legacy invoice escalated for provenance, then synced unchanged by its cashier.
        var envelope = Envelope(OfflineSubmissionSource.Recovery, asserted: null, original: null);
        var issue = Case(ReconciliationIssueType.ProvenanceReview, Errors.OfflineReconciliation.ProvenanceUnknown.Code, envelope.Id);
        issue.Escalate(_owner.Id, Now);
        var sale = SaleFor(envelope, envelope.PayloadHash);
        envelope.LinkSale(sale.Id);
        _saleRepository.GetByIdAsync(sale.Id, _shop.Id, Arg.Any<CancellationToken>()).Returns(sale);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.LinkExistingSale, sale.Id));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.Equal(ReconciliationCaseStatus.Resolved, issue.Status);
        Assert.Equal(sale.Id, issue.SaleId);
        Assert.Equal(sale.Id, envelope.SaleId);
        Assert.Equal(ReconciliationCaseStatus.Escalated, Assert.Single(_events).FromStatus);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkExistingSale_LegacyCaseWithoutEnvelope_LinksByIdentity()
    {
        var issue = Case(ReconciliationIssueType.InvoiceConflict, Errors.Sale.InvoiceLeaseNotFound.Code);
        var envelope = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null);
        // Any payload: an envelope-less case has no hash to compare, only the identity.
        var sale = SaleFor(envelope, "WHATEVER-THE-DEVICE-SENT");
        _saleRepository.GetByIdAsync(sale.Id, _shop.Id, Arg.Any<CancellationToken>()).Returns(sale);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.LinkExistingSale, sale.Id));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.Equal(ReconciliationCaseStatus.Resolved, issue.Status);
        Assert.Equal(sale.Id, issue.SaleId);
        Assert.Equal(sale.Id, issue.LinkedOperationId);
        Assert.Null(envelope.SaleId);
    }

    [Fact]
    public async Task LinkExistingSale_LegacyCase_OtherIdentity_Refused()
    {
        var issue = Case(ReconciliationIssueType.InvoiceConflict, Errors.Sale.InvoiceLeaseNotFound.Code);
        var other = Sale.Create(
            _shop.Id, _staff.Id, "key", "HASH", "INV-2026-27-000099", null, null, null, PaymentMethod.Cash, Now, 0m, 0m, 0m, 0m, [],
            source: SaleSource.Offline, clientSaleId: "client-2", deviceId: DeviceId, syncedAt: Now);
        _saleRepository.GetByIdAsync(other.Id, _shop.Id, Arg.Any<CancellationToken>()).Returns(other);

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.LinkExistingSale, other.Id));

        Assert.Equal(Errors.Reconciliation.SaleIdentityMismatch.Code, result.FirstError.Code);
        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("OfflineReconciliation.IssueTimeInFuture")]
    [InlineData("OfflineReconciliation.IssueTimeBeforeLease")]
    [InlineData("OfflineReconciliation.IssueFiscalYearMismatch")]
    [InlineData("OfflineReconciliation.IssuedAfterLeaseExpiry")]
    public async Task RetryHistoricalLease_OnTimingHold_PostsTheClaimedIssueTime(string code)
    {
        var envelope = Envelope(OfflineSubmissionSource.Sync, asserted: _staff.Id, original: _staff.Id);
        var issue = Case(ReconciliationIssueType.IssueTimingReview, code, envelope.Id);
        StubLease();
        var staged = SaleFor(envelope, envelope.PayloadHash);
        OfflineSalePostingRequest? request = null;
        _poster.StageAsync(Arg.Do<OfflineSalePostingRequest>(r => request = r), Arg.Any<CancellationToken>())
            .Returns(new OfflineSalePostingOutcome.Staged(staged, []));

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.RetryHistoricalLease));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        // Never rewritten: the bill carries the time the device printed.
        Assert.Equal(envelope.ClaimedSoldAt, request!.Sale.SoldAt);
        Assert.Equal(envelope.PayloadHash, request.PayloadHash);
        Assert.Equal(ReconciliationCaseStatus.Resolved, issue.Status);
        Assert.Equal("Reviewed with the cashier", issue.ResolutionReason);
    }

    [Fact]
    public async Task Acknowledge_ValidationVariance_Resolves()
    {
        var issue = Case(ReconciliationIssueType.ValidationConflict, "offline_sync.validation_variance", saleId: Guid.NewGuid());

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.AcknowledgeVariance));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.Equal(ReconciliationCaseStatus.Resolved, issue.Status);
        Assert.Equal(ReconciliationAction.AcknowledgeVariance, issue.Resolution);
    }

    [Fact]
    public async Task Acknowledge_IdempotencyConflict_Refused()
    {
        var issue = Case(ReconciliationIssueType.ValidationConflict, Errors.Sale.IdempotencyConflict.Code, saleId: Guid.NewGuid());

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.AcknowledgeVariance));

        Assert.Equal(Errors.Reconciliation.ActionNotAllowed.Code, result.FirstError.Code);
        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
    }

    [Fact]
    public async Task Acknowledge_InformationalVariance_Resolves()
    {
        var issue = Case(ReconciliationIssueType.PricingVariance, "offline_sync.pricing_variance");

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.AcknowledgeVariance) with { Reason = "  Price list was stale  " });

        Assert.False(result.IsError);
        Assert.Equal(ReconciliationCaseStatus.Resolved, issue.Status);
        Assert.Equal("Price list was stale", issue.ResolutionReason);
        Assert.Equal("Price list was stale", Assert.Single(_events).Reason);
    }

    [Fact]
    public async Task RunsThroughExecutorScopedToReconciliationResolution()
    {
        var issue = Case(ReconciliationIssueType.PricingVariance, "offline_sync.pricing_variance");
        var command = Command(_owner.Id, issue, ReconciliationAction.AcknowledgeVariance) with { OperationId = " resolve-7 " };

        await Handle(command);
        await Handle(command with { Reason = command.Reason + "  " });

        await _executor.Received(2).ExecuteAsync(
            Arg.Is<FinancialOperationScope>(s => s.OperationType == FinancialOperationType.ReconciliationResolution && s.Key == "resolve-7" && s.ShopId == _shop.Id),
            _owner.Id,
            issue.Id,
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<ErrorOr<ReconciliationCaseDto>>>>(),
            Arg.Any<CancellationToken>());
        var hashes = _executor.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IFinancialOperationExecutor.ExecuteAsync))
            .Select(c => (string)c.GetArguments()[3]!)
            .ToList();
        Assert.Equal(2, hashes.Count);
        Assert.Equal(hashes[0], hashes[1]);
    }

    [Theory]
    [InlineData("Delete", "Unwanted", "Reconciliation.UnknownAction")]
    [InlineData("1", "Unwanted", "Reconciliation.UnknownAction")]
    [InlineData("Escalate", "   ", "Reconciliation.ReasonRequired")]
    public async Task InvalidCommand_RefusedBeforeAnyLookup(string action, string reason, string code)
    {
        var result = await Handle(new ResolveReconciliationCaseCommand(_owner.Id, _shop.Id, Guid.NewGuid(), "op-1", 0u, action, reason, null));

        Assert.Contains(result.Errors, e => e.Code == code);
        await _userRepository.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReasonWithUnstorableText_ReasonInvalid()
    {
        // PostgreSQL text cannot hold U+0000, and an unpaired surrogate is not UTF-8 at all.
        foreach (var reason in new[] { "Checked\0 twice", "Checked \uD800 twice", "Checked \uDC00", "Checked \uD83D" })
        {
            var result = await Handle(new ResolveReconciliationCaseCommand(_owner.Id, _shop.Id, Guid.NewGuid(), "op-1", 0u, "Escalate", reason, null));

            Assert.Equal(Errors.Reconciliation.ReasonInvalid.Code, Assert.Single(result.Errors).Code);
            Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        }

        await _userRepository.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReasonWithPairedSurrogates_Accepted()
    {
        var issue = Case(ReconciliationIssueType.PricingVariance, "offline_sync.pricing_variance");

        var result = await Handle(Command(_owner.Id, issue, ReconciliationAction.AcknowledgeVariance) with { Reason = "Checked \U0001F44D" });

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
    }

    [Fact]
    public async Task InvalidCommand_LinkWithoutOperationOrBadKey_Refused()
    {
        var noLink = await Handle(new ResolveReconciliationCaseCommand(
            _owner.Id, _shop.Id, Guid.NewGuid(), "op-1", 0u, nameof(ReconciliationAction.LinkStockCorrection), "Found", null));
        var badKey = await Handle(new ResolveReconciliationCaseCommand(
            _owner.Id, _shop.Id, Guid.NewGuid(), new string('k', 129), 0u, nameof(ReconciliationAction.Escalate), "Look", null));
        var longReason = await Handle(new ResolveReconciliationCaseCommand(
            _owner.Id, _shop.Id, Guid.NewGuid(), "op-1", 0u, nameof(ReconciliationAction.Escalate), new string('r', 501), null));

        Assert.Contains(noLink.Errors, e => e.Code == Errors.Reconciliation.LinkedOperationRequired.Code);
        Assert.Contains(badKey.Errors, e => e.Code == Errors.Operation.InvalidKey.Code);
        Assert.Contains(longReason.Errors, e => e.Code == Errors.Reconciliation.ReasonRequired.Code);
    }

    [Fact]
    public async Task InactiveOrMissingMembership_Refused()
    {
        var outsider = User.CreateWithEmail("outsider@test.com", "hash", "Out", "Sider");
        _userRepository.GetByIdWithDetailsAsync(outsider.Id, Arg.Any<CancellationToken>()).Returns(outsider);
        var issue = Case(ReconciliationIssueType.PricingVariance, "offline_sync.pricing_variance");

        var result = await Handle(Command(outsider.Id, issue, ReconciliationAction.Escalate));

        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
        await _executor.DidNotReceive().ExecuteAsync(
            Arg.Any<FinancialOperationScope>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<ErrorOr<ReconciliationCaseDto>>>>(),
            Arg.Any<CancellationToken>());
    }

    private Task<ErrorOr<ReconciliationCaseDto>> Handle(ResolveReconciliationCaseCommand command) =>
        CreateHandler().HandleAsync(command, CancellationToken.None);

    private ResolveReconciliationCaseCommand Command(
        Guid actor,
        ReconciliationIssue issue,
        ReconciliationAction action,
        Guid? linkedOperationId = null) =>
        new(actor, _shop.Id, issue.Id, $"op-{Guid.NewGuid():N}", issue.Version, action.ToString(), "Reviewed with the cashier", linkedOperationId);

    private ReconciliationIssue Case(
        ReconciliationIssueType type,
        string code,
        Guid? submissionId = null,
        Guid? createdBy = null,
        Guid? saleId = null)
    {
        var issue = ReconciliationIssue.Create(
            _shop.Id,
            saleId,
            "client-1",
            DeviceId,
            type,
            code,
            "message",
            createdBy ?? _staff.Id,
            invoiceNumber: InvoiceNumber,
            offlineSubmissionId: submissionId);
        StubCase(issue);
        return issue;
    }

    private ReconciliationIssue StockCase()
    {
        var issue = ReconciliationIssue.Create(
            _shop.Id,
            Guid.NewGuid(),
            "client-1",
            DeviceId,
            ReconciliationIssueType.StockVariance,
            "offline_sync.stock_shortage",
            "printed 5, consumed 3",
            _staff.Id,
            itemId: _itemId,
            inventoryBatchId: _batch.Id,
            printedQuantity: 5m,
            availableQuantity: 3m,
            consumedQuantity: 3m,
            invoiceNumber: InvoiceNumber);
        StubCase(issue);
        return issue;
    }

    private void StubCase(ReconciliationIssue issue)
    {
        _issueRepository.GetAsync(_shop.Id, issue.Id, Arg.Any<CancellationToken>()).Returns(issue);
        _issueRepository.GetTrackedAsync(_shop.Id, issue.Id, Arg.Any<CancellationToken>()).Returns(issue);
        _issueRepository.LockForResolutionAsync(_shop.Id, issue.Id, Arg.Any<CancellationToken>()).Returns(RowLockOutcome.Acquired);
    }

    private InventoryAdjustment Adjustment(InventoryAdjustmentReason reason, decimal quantity)
    {
        var adjustment = InventoryAdjustment.Create(
            _shop.Id,
            _itemId,
            _batch.Id,
            $"ADJ-{Guid.NewGuid():N}"[..20],
            InventoryAdjustmentDirection.Increase,
            reason,
            quantity,
            80m,
            80m * quantity,
            0m,
            quantity,
            0m,
            quantity,
            Now,
            _owner.Id,
            "Found behind the shelf",
            _owner.Id).Value;
        _adjustmentRepository.GetByIdAsync(adjustment.Id, Arg.Any<CancellationToken>()).Returns(adjustment);
        return adjustment;
    }

    private OfflineSubmission Envelope(OfflineSubmissionSource source, Guid? asserted, Guid? original, string? hash = null)
    {
        var payload = new OfflineSaleSyncCommand(
            "client-1",
            InvoiceNumber,
            Now.AddDays(-5),
            null,
            null,
            null,
            PaymentMethod.Cash,
            118m,
            0m,
            100m,
            118m,
            0m,
            18m,
            118m,
            InstantDiscountType.None,
            0m,
            null,
            null,
            null,
            null,
            [new OfflineSaleSyncLineCommand("BC-1", "B-1", "Rice", 1m, 80m, 100m, 120m, 18m, false, _batch.Id, 100m, 0m, 0m, 100m, 18m, 118m, null, null, InstantDiscountType.None, 0m, null)],
            IssuedByUserId: asserted);
        var envelope = OfflineSubmission.Record(
            _shop.Id,
            DeviceId,
            "client-1",
            InvoiceNumber,
            hash ?? OfflineSaleSyncIdempotencyHasher.ComputeHash(_shop.Id, DeviceId, payload),
            OfflineSubmissionPayload.Serialize(payload),
            payload.SoldAt,
            Now.AddDays(-1),
            source,
            source == OfflineSubmissionSource.Recovery ? _owner.Id : _staff.Id,
            asserted,
            original,
            Guid.NewGuid(),
            source == OfflineSubmissionSource.Recovery ? "recovery-1" : null);
        _submissionRepository.GetByIdAsync(envelope.Id, Arg.Any<CancellationToken>()).Returns(envelope);
        return envelope;
    }

    private void StubLease() =>
        _leaseRepository.FindReservedRangeAsync(_shop.Id, DeviceId, InvoiceNumber, Arg.Any<CancellationToken>())
            .Returns(InvoiceLease.Create(_shop.Id, Guid.NewGuid(), DeviceId, 2026, "INV-2026-27-", 1, 50, 6, Now.AddDays(-9), Now.AddDays(-2)));

    private Sale SaleFor(OfflineSubmission envelope, string requestHash) =>
        Sale.Create(
            _shop.Id,
            _staff.Id,
            OfflineSaleSyncIdempotencyHasher.ComputeKey(envelope.DeviceId, envelope.ClientSaleId),
            requestHash,
            envelope.InvoiceNumber,
            null,
            null,
            null,
            PaymentMethod.Cash,
            envelope.ClaimedSoldAt,
            118m,
            0m,
            118m,
            18m,
            [SaleItem.CreateGoods(_shop.Id, _itemId, _batch.Id, lineName: "Rice", lineCode: "BC-1", 1m, 80m, 100m, 120m, 18m, false, false)],
            source: SaleSource.Offline,
            clientSaleId: envelope.ClientSaleId,
            deviceId: envelope.DeviceId,
            syncedAt: Now);

    private ResolveReconciliationCaseCommandHandler CreateHandler() =>
        new(
            _userRepository,
            _issueRepository,
            _eventRepository,
            _submissionRepository,
            _adjustmentRepository,
            _batchRepository,
            _inventoryRepository,
            _stockTransactionRepository,
            _saleRepository,
            _leaseRepository,
            _discountRuleRepository,
            _poster,
            new ReconciliationCaseMapper(_eventRepository, _userRepository, _leaseRepository, _adjustmentRepository, _saleRepository, _submissionRepository),
            _unitOfWork,
            _executor,
            new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
