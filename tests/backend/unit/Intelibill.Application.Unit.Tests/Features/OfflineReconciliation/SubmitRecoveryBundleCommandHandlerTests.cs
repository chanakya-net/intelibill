using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.OfflineReconciliation.Commands.SubmitRecoveryBundle;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.OfflineReconciliation;

public sealed class SubmitRecoveryBundleCommandHandlerTests
{
    private const string DeviceId = "device-1";
    private const string InvoiceNumber = "INV-2026-27-000002";
    private const string OperationId = "recovery-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IInvoiceLeaseRepository _leaseRepository = Substitute.For<IInvoiceLeaseRepository>();
    private readonly IOfflineSubmissionRepository _submissionRepository = Substitute.For<IOfflineSubmissionRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly IReconciliationIssueRepository _issueRepository = Substitute.For<IReconciliationIssueRepository>();
    private readonly IReconciliationCaseEventRepository _eventRepository = Substitute.For<IReconciliationCaseEventRepository>();
    private readonly IOfflineSalePoster _poster = Substitute.For<IOfflineSalePoster>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IFinancialOperationExecutor _executor = Substitute.For<IFinancialOperationExecutor>();

    private readonly Shop _shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
    private readonly User _owner = User.CreateWithEmail("owner@test.com", "hash", "Olive", "Owner");
    private readonly User _staff = User.CreateWithEmail("staff@test.com", "hash", "Sam", "Staff");
    private readonly Guid _formerMemberId = Guid.NewGuid();
    private readonly Guid _batchId = Guid.NewGuid();
    private readonly InvoiceLease _lease;
    private readonly List<OfflineSubmission> _envelopes = [];
    private readonly List<ReconciliationIssue> _issues = [];
    private readonly List<ReconciliationCaseEvent> _events = [];

    public SubmitRecoveryBundleCommandHandlerTests()
    {
        _owner.AddShopMembership(ShopMembership.Create(_shop.Id, _owner.Id, ShopRole.Owner, true));
        _staff.AddShopMembership(ShopMembership.Create(_shop.Id, _staff.Id, ShopRole.Staff, true));
        _userRepository.GetByIdWithDetailsAsync(_owner.Id, Arg.Any<CancellationToken>()).Returns(_owner);
        _userRepository.GetByIdWithDetailsAsync(_staff.Id, Arg.Any<CancellationToken>()).Returns(_staff);

        // The staff member issued invoices on the device; the former member held a membership but never used it.
        _shopRepository.GetMembershipsForUsersInShopsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var userId = call.ArgAt<IReadOnlyList<Guid>>(0).Single();
                return userId == _staff.Id || userId == _formerMemberId
                    ? [ShopMembership.Create(_shop.Id, userId, ShopRole.Staff, false)]
                    : new List<ShopMembership>();
            });
        _submissionRepository.HasDeviceHistoryAsync(_shop.Id, DeviceId, _staff.Id, Arg.Any<CancellationToken>()).Returns(true);

        _lease = InvoiceLease.Create(_shop.Id, Guid.NewGuid(), DeviceId, 2026, "INV-2026-27-", 1, 50, 6, Now.AddDays(-9), Now.AddDays(-2));
        _leaseRepository.FindReservedRangeAsync(_shop.Id, DeviceId, InvoiceNumber, Arg.Any<CancellationToken>()).Returns(_lease);

        _submissionRepository.RecordAsync(Arg.Any<OfflineSubmission>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var candidate = call.Arg<OfflineSubmission>();
                var stored = _envelopes.FirstOrDefault(e => e.ClientSaleId == candidate.ClientSaleId && e.PayloadHash == candidate.PayloadHash);
                if (stored is not null)
                    return stored;
                _envelopes.Add(candidate);
                return candidate;
            });

        _issueRepository.When(r => r.AddAsync(Arg.Any<ReconciliationIssue>(), Arg.Any<CancellationToken>()))
            .Do(call => _issues.Add(call.Arg<ReconciliationIssue>()));
        _issueRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => _issues.FirstOrDefault(i => i.Id == call.Arg<Guid>()));
        _issueRepository.FindUnresolvedByEnvelopeAsync(_shop.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => _issues.FirstOrDefault(i => i.OfflineSubmissionId == call.ArgAt<Guid>(1) && i.Status != ReconciliationCaseStatus.Resolved));
        _eventRepository.When(r => r.AddAsync(Arg.Any<ReconciliationCaseEvent>(), Arg.Any<CancellationToken>()))
            .Do(call => _events.Add(call.Arg<ReconciliationCaseEvent>()));

        _poster.CheckStructure(Arg.Any<OfflineSaleSyncCommand>()).Returns(Result.Success);

        _executor
            .ExecuteAsync(
                Arg.Any<FinancialOperationScope>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<ErrorOr<RecoverySubmissionResultDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<CancellationToken, Task<ErrorOr<RecoverySubmissionResultDto>>>>(4)(CancellationToken.None));
    }

    [Fact]
    public async Task ChecksumMismatch_RejectsBundle()
    {
        var command = Command([Invoice(issuedBy: _staff.Id)]);
        var edited = command with { Bundle = command.Bundle with { InvoicesJson = command.Bundle.InvoicesJson.Replace("118", "11.8", StringComparison.Ordinal) } };

        var result = await Handle(edited);

        Assert.Equal(Errors.Recovery.BundleChecksumMismatch.Code, result.FirstError.Code);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Checksum_ComparedIgnoringCase()
    {
        var command = Command([Invoice(issuedBy: _staff.Id)]);

        var result = await Handle(command with { Bundle = command.Bundle with { Checksum = command.Bundle.Checksum.ToUpperInvariant() } });

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.Equal(RecoveryInvoiceStatus.ReviewOpened, Assert.Single(result.Value.Results).Status);
    }

    [Fact]
    public async Task ShopMismatch_RejectsBundle()
    {
        var command = Command([Invoice(issuedBy: _staff.Id)]);

        var result = await Handle(command with { Bundle = command.Bundle with { ShopId = Guid.NewGuid() } });

        Assert.Equal(Errors.Recovery.BundleShopMismatch.Code, result.FirstError.Code);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task UnknownFormat_Rejected()
    {
        var command = Command([Invoice(issuedBy: _staff.Id)]);

        var otherFormat = await Handle(command with { Bundle = command.Bundle with { Format = "intelibill.support-export" } });
        var otherVersion = await Handle(command with { Bundle = command.Bundle with { Version = 2 } });

        Assert.Equal(Errors.Recovery.BundleFormatUnsupported.Code, otherFormat.FirstError.Code);
        Assert.Equal(Errors.Recovery.BundleFormatUnsupported.Code, otherVersion.FirstError.Code);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task TooManyInvoices_Rejected()
    {
        var invoices = Enumerable.Range(0, 101).Select(i => Invoice(clientSaleId: $"client-{i}", issuedBy: _staff.Id)).ToList();

        var result = await Handle(Command(invoices));

        Assert.Equal(Errors.Recovery.BundleTooLarge.Code, result.FirstError.Code);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task EmptyOrUnreadableInvoices_Rejected()
    {
        var empty = await Handle(Command([]));
        var unreadable = await Handle(CommandFromText("[{\"clientSaleId\":\"client-1\",\"soldAt\":\"yesterday\"}]"));
        var nullInvoice = await Handle(CommandFromText("[null]"));

        Assert.Equal(Errors.Recovery.BundleEmpty.Code, empty.FirstError.Code);
        Assert.Equal(Errors.Recovery.BundleInvalid.Code, unreadable.FirstError.Code);
        Assert.Equal(Errors.Recovery.BundleInvalid.Code, nullInvoice.FirstError.Code);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task UnstorableDeviceId_RejectsBundle()
    {
        var command = Command([Invoice(issuedBy: _staff.Id)]);

        var withNul = await Handle(command with { Bundle = command.Bundle with { DeviceId = "device\u00001" } });
        var withLoneSurrogate = await Handle(command with { Bundle = command.Bundle with { DeviceId = "device-\uD800" } });

        Assert.Equal(Errors.Recovery.BundleInvalid.Code, withNul.FirstError.Code);
        Assert.Equal(Errors.Recovery.BundleInvalid.Code, withLoneSurrogate.FirstError.Code);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task StaffOperator_NotAuthorized()
    {
        var command = Command([Invoice(issuedBy: _staff.Id)]) with { ActorUserId = _staff.Id };

        var result = await Handle(command);

        Assert.Equal(Errors.Recovery.NotAuthorized.Code, result.FirstError.Code);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task IssuerNeverMember_RowRejected()
    {
        var stranger = Guid.NewGuid();

        var result = await Handle(Command([Invoice(issuedBy: stranger)]));

        var row = Assert.Single(result.Value.Results);
        Assert.Equal(RecoveryInvoiceStatus.Rejected, row.Status);
        Assert.Equal(Errors.Recovery.IssuerMismatch.Code, Assert.Single(row.Errors).Code);
        Assert.Null(row.CaseId);
        Assert.Empty(_envelopes);
        Assert.Empty(_issues);
        await AssertNoSaleStagedAsync();
    }

    [Fact]
    public async Task InvoiceNumberNotLeasedToBundleDevice_RowRejected()
    {
        var result = await Handle(Command([Invoice(invoiceNumber: "INV-2026-27-000099", issuedBy: _staff.Id)]));

        var row = Assert.Single(result.Value.Results);
        Assert.Equal(RecoveryInvoiceStatus.Rejected, row.Status);
        Assert.Equal(Errors.Recovery.LeaseNotOwned.Code, Assert.Single(row.Errors).Code);
        Assert.Empty(_envelopes);
        Assert.Empty(_issues);
    }

    [Fact]
    public async Task MissingClientSaleIdOrBadStructure_RowRejected()
    {
        _poster.CheckStructure(Arg.Is<OfflineSaleSyncCommand>(s => s.ClientSaleId == "client-2")).Returns(Errors.Sale.ItemsRequired);

        var result = await Handle(Command([Invoice(clientSaleId: " ", issuedBy: _staff.Id), Invoice(clientSaleId: "client-2", issuedBy: _staff.Id)]));

        Assert.Equal(Errors.Sale.ClientSaleIdRequired.Code, Assert.Single(result.Value.Results[0].Errors).Code);
        Assert.Equal(Errors.Sale.ItemsRequired.Code, Assert.Single(result.Value.Results[1].Errors).Code);
        Assert.All(result.Value.Results, row => Assert.Equal(RecoveryInvoiceStatus.Rejected, row.Status));
        Assert.Empty(_envelopes);
        Assert.Empty(_issues);
    }

    [Fact]
    public async Task MemberWithoutDeviceHistory_EscalatedWithoutPosting()
    {
        var result = await Handle(Command([Invoice(issuedBy: _formerMemberId)]));

        var row = Assert.Single(result.Value.Results);
        Assert.Equal(RecoveryInvoiceStatus.Escalated, row.Status);
        Assert.Equal(Errors.OfflineReconciliation.IssuerUnverified.Code, Assert.Single(row.Errors).Code);
        var envelope = Assert.Single(_envelopes);
        Assert.Equal(_formerMemberId, envelope.AssertedIssuerUserId);
        Assert.Null(envelope.OriginalIssuerUserId);
        Assert.Null(envelope.AttributableUserId);
        AssertEscalatedCase(row, envelope, Errors.OfflineReconciliation.IssuerUnverified);
        await AssertNoSaleStagedAsync();
    }

    [Fact]
    public async Task MissingIssuer_EscalatedWithoutPosting()
    {
        var result = await Handle(Command([Invoice(issuedBy: null)]));

        var row = Assert.Single(result.Value.Results);
        Assert.Equal(RecoveryInvoiceStatus.Escalated, row.Status);
        Assert.Equal(Errors.OfflineReconciliation.ProvenanceUnknown.Code, Assert.Single(row.Errors).Code);
        var envelope = Assert.Single(_envelopes);
        Assert.Null(envelope.AssertedIssuerUserId);
        Assert.Null(envelope.AttributableUserId);
        AssertEscalatedCase(row, envelope, Errors.OfflineReconciliation.ProvenanceUnknown);
        await AssertNoSaleStagedAsync();
    }

    [Fact]
    public async Task ResubmittedUnverifiedInvoice_ReusesEscalatedCase()
    {
        await Handle(Command([Invoice(issuedBy: null)]));
        var escalated = Assert.Single(_issues);
        _issueRepository.FindOpenIssueIdAsync(
                _shop.Id, "client-1", DeviceId, ReconciliationIssueType.ProvenanceReview, Arg.Any<string>(), Arg.Any<string>(), escalated.OfflineSubmissionId, Arg.Any<CancellationToken>())
            .Returns(escalated.Id);

        var again = await Handle(Command([Invoice(issuedBy: null)], operationId: "recovery-2"));

        var row = Assert.Single(again.Value.Results);
        Assert.Equal(RecoveryInvoiceStatus.Escalated, row.Status);
        Assert.Equal(escalated.Id, row.CaseId);
        Assert.Single(_issues);
        Assert.Single(_events);
    }

    [Fact]
    public async Task VerifiedIssuer_OpensRecoveryReview()
    {
        var result = await Handle(Command([Invoice(issuedBy: _staff.Id)]));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.Equal(OperationId, result.Value.OperationId);
        var row = Assert.Single(result.Value.Results);
        Assert.Equal(RecoveryInvoiceStatus.ReviewOpened, row.Status);
        Assert.Equal("client-1", row.ClientSaleId);
        Assert.Equal(InvoiceNumber, row.InvoiceNumber);
        Assert.Equal(Errors.OfflineReconciliation.RecoveryReviewRequired.Code, Assert.Single(row.Errors).Code);
        Assert.Null(row.SaleId);

        var envelope = Assert.Single(_envelopes);
        Assert.Equal(OfflineSubmissionSource.Recovery, envelope.Source);
        Assert.Equal(_owner.Id, envelope.SubmittedByUserId);
        Assert.Equal(OperationId, envelope.RecoveryOperationId);
        Assert.Equal(_staff.Id, envelope.AssertedIssuerUserId);
        Assert.Equal(_staff.Id, envelope.OriginalIssuerUserId);
        Assert.Equal(_staff.Id, envelope.AttributableUserId);
        Assert.Equal(_lease.Id, envelope.OriginalLeaseId);
        Assert.Equal(Now, envelope.ReceivedAt);
        Assert.Equal(DeviceId, envelope.DeviceId);
        Assert.Equal(InvoiceNumber, envelope.InvoiceNumber);
        Assert.Null(envelope.SaleId);
        var invoice = Invoice(issuedBy: _staff.Id);
        Assert.Equal(OfflineSaleSyncIdempotencyHasher.ComputeHash(_shop.Id, DeviceId, invoice), envelope.PayloadHash);
        Assert.Equal(envelope.PayloadHash, OfflineSaleSyncIdempotencyHasher.ComputeHash(_shop.Id, DeviceId, OfflineSubmissionPayload.Deserialize(envelope.PayloadJson)!));

        var issue = Assert.Single(_issues);
        Assert.Equal(row.CaseId, issue.Id);
        Assert.Equal(ReconciliationIssueType.RecoveryReview, issue.IssueType);
        Assert.Equal(Errors.OfflineReconciliation.RecoveryReviewRequired.Code, issue.Code);
        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
        Assert.Equal(envelope.Id, issue.OfflineSubmissionId);
        Assert.Equal(InvoiceNumber, issue.InvoiceNumber);
        Assert.Equal(_owner.Id, issue.CreatedBy);
        Assert.Empty(_events);

        await _executor.Received(1).ExecuteAsync(
            new FinancialOperationScope(_shop.Id, FinancialOperationType.OfflineRecoverySubmission, OperationId),
            _owner.Id,
            _shop.Id,
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<ErrorOr<RecoverySubmissionResultDto>>>>(),
            Arg.Any<CancellationToken>());
        await AssertNoSaleStagedAsync();
    }

    [Fact]
    public async Task EnvelopeHeldBySync_ReusesItsOpenCase()
    {
        // The cashier's sync held this exact invoice for its issue time; the export carried it unchanged.
        var invoice = Invoice(issuedBy: _staff.Id);
        var synced = StoreSyncEnvelope(invoice, receivedAt: Now.AddDays(-6));
        var held = HeldCase(synced, ReconciliationIssueType.IssueTimingReview, Errors.OfflineReconciliation.IssueTimeInFuture);

        var result = await Handle(Command([invoice]));

        var row = Assert.Single(result.Value.Results);
        Assert.Equal(RecoveryInvoiceStatus.ReviewOpened, row.Status);
        Assert.Equal(held.Id, row.CaseId);
        Assert.Equal(Errors.OfflineReconciliation.IssueTimeInFuture.Code, Assert.Single(row.Errors).Code);
        Assert.Same(held, Assert.Single(_issues));
        Assert.Same(synced, Assert.Single(_envelopes));
        Assert.Empty(_events);
        await AssertNoSaleStagedAsync();
    }

    [Fact]
    public async Task EnvelopeWithEscalatedCase_ReportsItEscalated()
    {
        var invoice = Invoice(issuedBy: _staff.Id);
        var synced = StoreSyncEnvelope(invoice, receivedAt: Now.AddDays(-6));
        var held = HeldCase(synced, ReconciliationIssueType.ArithmeticConflict, Errors.Sale.OfflineArithmeticMismatch);
        held.Escalate(_owner.Id, Now.AddDays(-1));

        var result = await Handle(Command([invoice]));

        var row = Assert.Single(result.Value.Results);
        Assert.Equal(RecoveryInvoiceStatus.Escalated, row.Status);
        Assert.Equal(held.Id, row.CaseId);
        Assert.Equal(Errors.Sale.OfflineArithmeticMismatch.Code, Assert.Single(row.Errors).Code);
        Assert.Single(_issues);
        Assert.Empty(_events);
    }

    [Theory]
    [InlineData(-10.0, "OfflineReconciliation.IssueTimeBeforeLease")]
    [InlineData(1.0 / 24, "OfflineReconciliation.IssueTimeInFuture")]
    [InlineData(-1.0, "OfflineReconciliation.IssuedAfterLeaseExpiry")]
    public async Task ImplausibleIssueTime_OpensTimingReview(double soldAtDaysFromNow, string code)
    {
        // The default lease: reserved nine days ago, expired two days ago.
        var invoice = Invoice(issuedBy: _staff.Id) with { SoldAt = Now.AddDays(soldAtDaysFromNow) };

        var result = await Handle(Command([invoice]));

        var row = Assert.Single(result.Value.Results);
        Assert.Equal(RecoveryInvoiceStatus.ReviewOpened, row.Status);
        Assert.Equal(code, Assert.Single(row.Errors).Code);
        var issue = Assert.Single(_issues);
        Assert.Equal(row.CaseId, issue.Id);
        Assert.Equal(ReconciliationIssueType.IssueTimingReview, issue.IssueType);
        Assert.Equal(code, issue.Code);
        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
        Assert.Equal(Assert.Single(_envelopes).Id, issue.OfflineSubmissionId);
        await AssertNoSaleStagedAsync();
    }

    [Fact]
    public async Task ClaimInAnotherFiscalYear_OpensTimingReview()
    {
        var lastYearLease = InvoiceLease.Create(_shop.Id, Guid.NewGuid(), DeviceId, 2025, "INV-2026-27-", 1, 50, 6, Now.AddDays(-9), Now.AddDays(-2));
        _leaseRepository.FindReservedRangeAsync(_shop.Id, DeviceId, InvoiceNumber, Arg.Any<CancellationToken>()).Returns(lastYearLease);

        var result = await Handle(Command([Invoice(issuedBy: _staff.Id)]));

        var issue = Assert.Single(_issues);
        Assert.Equal(ReconciliationIssueType.IssueTimingReview, issue.IssueType);
        Assert.Equal(Errors.OfflineReconciliation.IssueFiscalYearMismatch.Code, issue.Code);
        Assert.Equal(issue.Id, Assert.Single(result.Value.Results).CaseId);
    }

    [Fact]
    public async Task IssueTime_JudgedAgainstTheEnvelopesFirstReceipt()
    {
        // First received (with no case filed) three days ago, before the lease expired: a claim half a day
        // later than that receipt is in the future, although it is in the past by today's clock.
        var invoice = Invoice(issuedBy: _staff.Id) with { SoldAt = Now.AddDays(-2.5) };
        StoreSyncEnvelope(invoice, receivedAt: Now.AddDays(-3));

        var result = await Handle(Command([invoice]));

        Assert.Equal(Errors.OfflineReconciliation.IssueTimeInFuture.Code, Assert.Single(Assert.Single(result.Value.Results).Errors).Code);
        Assert.Equal(Errors.OfflineReconciliation.IssueTimeInFuture.Code, Assert.Single(_issues).Code);
    }

    [Fact]
    public async Task ServiceLineFields_ReadAsTheSyncEndpointMapsThem()
    {
        // The sync endpoint maps neither lineType nor serviceId, so recovery must not honour them either.
        var json = JsonSerializer.Serialize(new[] { Invoice(issuedBy: _staff.Id) }, WebOptions)
            .Replace("\"lineType\":\"Goods\"", "\"lineType\":\"Service\"", StringComparison.Ordinal)
            .Replace("\"serviceId\":null", $"\"serviceId\":\"{Guid.NewGuid()}\"", StringComparison.Ordinal);
        Assert.Contains("\"lineType\":\"Service\"", json, StringComparison.Ordinal);

        var result = await Handle(CommandFromText(json));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        var stored = OfflineSubmissionPayload.Deserialize(Assert.Single(_envelopes).PayloadJson)!;
        var line = Assert.Single(stored.Items);
        Assert.Equal("Goods", line.LineType);
        Assert.Null(line.ServiceId);
        _poster.Received().CheckStructure(Arg.Is<OfflineSaleSyncCommand>(s => s.Items.All(l => l.LineType == "Goods" && l.ServiceId == null)));
    }

    [Fact]
    public async Task SameBundle_HashesAlike_WhateverChecksumCase()
    {
        var command = Command([Invoice(issuedBy: _staff.Id)]);
        var hashes = new List<string>();
        _executor
            .When(e => e.ExecuteAsync(
                Arg.Any<FinancialOperationScope>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<ErrorOr<RecoverySubmissionResultDto>>>>(),
                Arg.Any<CancellationToken>()))
            .Do(call => hashes.Add(call.ArgAt<string>(3)));

        await Handle(command);
        await Handle(command with { Bundle = command.Bundle with { Checksum = command.Bundle.Checksum.ToUpperInvariant(), DeviceId = $" {DeviceId} " } });
        var other = Command([Invoice(clientSaleId: "client-2", issuedBy: _staff.Id)]);
        await Handle(other);

        Assert.Equal(3, hashes.Count);
        Assert.Equal(hashes[0], hashes[1]);
        Assert.NotEqual(hashes[0], hashes[2]);
    }

    [Fact]
    public async Task IdenticalSaleExists_LinksAlreadyPosted()
    {
        var invoice = Invoice(issuedBy: _staff.Id);
        var sale = SaleFor(OfflineSaleSyncIdempotencyHasher.ComputeHash(_shop.Id, DeviceId, invoice));
        _saleRepository.GetByClientSaleIdAsync(_shop.Id, DeviceId, "client-1", Arg.Any<CancellationToken>()).Returns(sale);

        var result = await Handle(Command([invoice]));

        var row = Assert.Single(result.Value.Results);
        Assert.Equal(RecoveryInvoiceStatus.AlreadyPosted, row.Status);
        Assert.Equal(sale.Id, row.SaleId);
        Assert.Null(row.CaseId);
        Assert.Empty(row.Errors);
        Assert.Equal(sale.Id, Assert.Single(_envelopes).SaleId);
        Assert.Empty(_issues);
        await AssertNoSaleStagedAsync();
    }

    [Fact]
    public async Task IdenticalSaleExists_UnattributableInvoice_StillLinksAlreadyPosted()
    {
        var invoice = Invoice(issuedBy: null);
        var sale = SaleFor(OfflineSaleSyncIdempotencyHasher.ComputeHash(_shop.Id, DeviceId, invoice));
        _saleRepository.GetByClientSaleIdAsync(_shop.Id, DeviceId, "client-1", Arg.Any<CancellationToken>()).Returns(sale);

        var result = await Handle(Command([invoice]));

        Assert.Equal(RecoveryInvoiceStatus.AlreadyPosted, Assert.Single(result.Value.Results).Status);
        Assert.Empty(_issues);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task ChangedPayloadForPostedSale_OpensIdempotencyConflict()
    {
        var sale = SaleFor("a-different-hash");
        _saleRepository.GetByClientSaleIdAsync(_shop.Id, DeviceId, "client-1", Arg.Any<CancellationToken>()).Returns(sale);

        var result = await Handle(Command([Invoice(issuedBy: _staff.Id)]));

        var row = Assert.Single(result.Value.Results);
        Assert.Equal(RecoveryInvoiceStatus.ReviewOpened, row.Status);
        Assert.Equal(Errors.Sale.IdempotencyConflict.Code, Assert.Single(row.Errors).Code);
        var issue = Assert.Single(_issues);
        Assert.Equal(ReconciliationIssueType.ValidationConflict, issue.IssueType);
        Assert.Equal(Errors.Sale.IdempotencyConflict.Code, issue.Code);
        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
        Assert.Equal(sale.Id, issue.SaleId);
        var envelope = Assert.Single(_envelopes);
        Assert.Equal(envelope.Id, issue.OfflineSubmissionId);
        Assert.Null(envelope.SaleId);
        await AssertNoSaleStagedAsync();
    }

    [Fact]
    public async Task ArithmeticMismatch_OpensArithmeticCase()
    {
        // Paid and due still add up to the total, so only the recomputation catches the inflated tax.
        var result = await Handle(Command([Invoice(issuedBy: _staff.Id, totalTax: 20m)]));

        var row = Assert.Single(result.Value.Results);
        Assert.Equal(RecoveryInvoiceStatus.ReviewOpened, row.Status);
        Assert.Equal(Errors.Sale.OfflineArithmeticMismatch.Code, Assert.Single(row.Errors).Code);
        var issue = Assert.Single(_issues);
        Assert.Equal(row.CaseId, issue.Id);
        Assert.Equal(ReconciliationIssueType.ArithmeticConflict, issue.IssueType);
        Assert.Equal(Errors.Sale.OfflineArithmeticMismatch.Code, issue.Code);
        Assert.StartsWith("Offline invoice arithmetic (legacy) does not reconcile:", issue.Message, StringComparison.Ordinal);
        Assert.Contains("totalTaxAmount supplied 20.00 expected 18.00", issue.Message, StringComparison.Ordinal);
        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
        Assert.Equal(Assert.Single(_envelopes).Id, issue.OfflineSubmissionId);
        await AssertNoSaleStagedAsync();
    }

    [Fact]
    public async Task WebClientEnumNames_ReadAsTheSyncEndpointReadsThem()
    {
        // The web client's sync mapper sends the configured sale rule type by name and amounts as numbers.
        var json = JsonSerializer.Serialize(new[] { Invoice(issuedBy: _staff.Id) }, WebOptions)
            .Replace("\"paymentMethod\":1", "\"paymentMethod\":\"Cash\"", StringComparison.Ordinal)
            .Replace("\"configuredSaleRuleType\":null", "\"configuredSaleRuleType\":\"SalePercentage\"", StringComparison.Ordinal);
        Assert.Contains("\"SalePercentage\"", json, StringComparison.Ordinal);

        var result = await Handle(CommandFromText(json));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.NotEqual(RecoveryInvoiceStatus.Rejected, Assert.Single(result.Value.Results).Status);
        var stored = OfflineSubmissionPayload.Deserialize(Assert.Single(_envelopes).PayloadJson)!;
        Assert.Equal(PaymentMethod.Cash, stored.PaymentMethod);
        Assert.Equal(DiscountRuleType.SalePercentage, stored.ConfiguredSaleRuleType);
    }

    private void AssertEscalatedCase(RecoveryInvoiceResultDto row, OfflineSubmission envelope, Error reason)
    {
        Assert.Equal(OfflineSubmissionSource.Recovery, envelope.Source);
        Assert.Equal(_owner.Id, envelope.SubmittedByUserId);
        Assert.Equal(OperationId, envelope.RecoveryOperationId);
        var issue = Assert.Single(_issues);
        Assert.Equal(row.CaseId, issue.Id);
        Assert.Equal(ReconciliationIssueType.ProvenanceReview, issue.IssueType);
        Assert.Equal(reason.Code, issue.Code);
        Assert.Equal(ReconciliationCaseStatus.Escalated, issue.Status);
        Assert.Equal(envelope.Id, issue.OfflineSubmissionId);
        var escalation = Assert.Single(_events);
        Assert.Equal(issue.Id, escalation.CaseId);
        Assert.Equal(ReconciliationAction.Escalate, escalation.Action);
        Assert.Equal(ReconciliationCaseStatus.Open, escalation.FromStatus);
        Assert.Equal(ReconciliationCaseStatus.Escalated, escalation.ToStatus);
        Assert.Equal(_owner.Id, escalation.ActorUserId);
        Assert.Equal(reason.Description, escalation.Reason);
        Assert.Equal(OperationId, escalation.OperationKey);
    }

    private async Task AssertNoSaleStagedAsync()
    {
        await _poster.DidNotReceive().StageAsync(Arg.Any<OfflineSalePostingRequest>(), Arg.Any<CancellationToken>());
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }

    private async Task AssertNothingWrittenAsync()
    {
        await _executor.DidNotReceive().ExecuteAsync(
            Arg.Any<FinancialOperationScope>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<ErrorOr<RecoverySubmissionResultDto>>>>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(_envelopes);
        Assert.Empty(_issues);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private SubmitRecoveryBundleCommand Command(IReadOnlyList<OfflineSaleSyncCommand> invoices, string operationId = OperationId) =>
        CommandFromText(JsonSerializer.Serialize(invoices, WebOptions), operationId);

    private SubmitRecoveryBundleCommand CommandFromText(string invoicesJson, string operationId = OperationId) =>
        new(
            _owner.Id,
            _shop.Id,
            operationId,
            new RecoveryBundle(
                SubmitRecoveryBundleCommandHandler.BundleFormat,
                SubmitRecoveryBundleCommandHandler.BundleVersion,
                _shop.Id,
                DeviceId,
                Now.AddHours(-1),
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(invoicesJson))),
                invoicesJson));

    private OfflineSaleSyncCommand Invoice(
        string clientSaleId = "client-1",
        string invoiceNumber = InvoiceNumber,
        Guid? issuedBy = null,
        decimal totalTax = 18m)
    {
        var total = 100m + totalTax;
        return new OfflineSaleSyncCommand(
            clientSaleId,
            invoiceNumber,
            Now.AddDays(-5),
            null,
            null,
            null,
            PaymentMethod.Cash,
            total,
            0m,
            100m,
            total,
            0m,
            totalTax,
            total,
            InstantDiscountType.None,
            0m,
            null,
            null,
            null,
            null,
            [new OfflineSaleSyncLineCommand("BC-1", "B-1", "Rice", 1m, 80m, 100m, 120m, 18m, false, _batchId, 100m, 0m, 0m, 100m, 18m, 118m, null, null, InstantDiscountType.None, 0m, null)],
            IssuedByUserId: issuedBy);
    }

    private OfflineSubmission StoreSyncEnvelope(OfflineSaleSyncCommand invoice, DateTimeOffset receivedAt)
    {
        var envelope = OfflineSubmission.Record(
            _shop.Id,
            DeviceId,
            invoice.ClientSaleId,
            invoice.InvoiceNumber,
            OfflineSaleSyncIdempotencyHasher.ComputeHash(_shop.Id, DeviceId, invoice),
            OfflineSubmissionPayload.Serialize(invoice),
            invoice.SoldAt,
            receivedAt,
            OfflineSubmissionSource.Sync,
            _staff.Id,
            invoice.IssuedByUserId,
            invoice.IssuedByUserId,
            _lease.Id,
            recoveryOperationId: null);
        _envelopes.Add(envelope);
        return envelope;
    }

    private ReconciliationIssue HeldCase(OfflineSubmission envelope, ReconciliationIssueType type, Error reason)
    {
        var issue = ReconciliationIssue.Create(
            _shop.Id,
            null,
            envelope.ClientSaleId,
            DeviceId,
            type,
            reason.Code,
            reason.Description,
            _staff.Id,
            invoiceNumber: envelope.InvoiceNumber,
            offlineSubmissionId: envelope.Id);
        _issues.Add(issue);
        return issue;
    }

    private Sale SaleFor(string requestHash) =>
        Sale.Create(
            _shop.Id,
            _staff.Id,
            OfflineSaleSyncIdempotencyHasher.ComputeKey(DeviceId, "client-1"),
            requestHash,
            InvoiceNumber,
            null,
            null,
            null,
            PaymentMethod.Cash,
            Now.AddDays(-5),
            global::Intelibill.Domain.ValueObjects.BusinessDay.InDefaultZone(Now.AddDays(-5)),
            118m,
            0m,
            118m,
            18m,
            [SaleItem.CreateGoods(_shop.Id, Guid.NewGuid(), _batchId, lineName: "Rice", lineCode: "BC-1", 1m, 80m, 100m, 120m, 18m, false, false)],
            source: SaleSource.Offline,
            clientSaleId: "client-1",
            deviceId: DeviceId,
            syncedAt: Now.AddDays(-1));

    private Task<ErrorOr<RecoverySubmissionResultDto>> Handle(SubmitRecoveryBundleCommand command) =>
        new SubmitRecoveryBundleCommandHandler(
            _userRepository,
            _shopRepository,
            _leaseRepository,
            _submissionRepository,
            _saleRepository,
            _issueRepository,
            _eventRepository,
            new OfflineSaleVarianceAnalyzer(_issueRepository),
            _poster,
            _unitOfWork,
            _executor,
            new FixedClock(Now)).HandleAsync(command, CancellationToken.None);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
