using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Sales.Commands.ReserveInvoiceLease;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using Errors = Intelibill.Application.Common.Errors.Errors;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.ReserveInvoiceLease;

public class ReserveInvoiceLeaseCommandHandlerTests
{
    private const string Gstin = "29AAPFU0939F1ZR";
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IInvoiceLeaseRepository _invoiceLeaseRepository = Substitute.For<IInvoiceLeaseRepository>();
    private readonly IInvoiceNumberAllocator _allocator = Substitute.For<IInvoiceNumberAllocator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly Guid _sequenceId = Guid.NewGuid();
    private InvoiceSeriesKey? _reservedSeries;
    private int? _reservedCount;

    public ReserveInvoiceLeaseCommandHandlerTests()
    {
        _allocator.ReserveRangeAsync(Arg.Any<InvoiceSeriesKey>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _reservedSeries = call.ArgAt<InvoiceSeriesKey>(0);
                _reservedCount = call.ArgAt<int>(1);
                var prefix = InvoiceNumberFormat.Prefix(_reservedSeries.SeriesCode, _reservedSeries.FiscalYear);
                return Task.FromResult<ErrorOr<ReservedInvoiceRange>>(new ReservedInvoiceRange(_sequenceId, prefix, 201, 200 + _reservedCount.Value));
            });
    }

    private ReserveInvoiceLeaseCommandHandler CreateHandler(TimeProvider? clock = null) =>
        new(_userRepository, _shopRepository, _invoiceLeaseRepository, _allocator, _unitOfWork, clock ?? TimeProvider.System);

    private (User Actor, Shop Shop) Arrange(ShopRole role = ShopRole.Owner, bool declared = true, EInvoiceStatus eInvoice = EInvoiceStatus.BelowThreshold)
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "Karnataka", "560001", null, null, Gstin);
        if (declared)
            Assert.False(shop.DeclareTaxProfile(null, "29", TaxRegime.Regular, Gstin, null, eInvoice, actor.Id, DateTimeOffset.UtcNow).IsError);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, role, true));
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        return (actor, shop);
    }

    [Fact]
    public async Task HandleAsync_WhenUserMissing_ReturnsNotFound()
    {
        var result = await CreateHandler().HandleAsync(new ReserveInvoiceLeaseCommand(Guid.NewGuid(), Guid.NewGuid(), "device-1", 200), CancellationToken.None);

        Assert.Equal(Errors.Auth.UserNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenMembershipMissing_ReturnsForbidden()
    {
        var (actor, _) = Arrange();

        var result = await CreateHandler().HandleAsync(new ReserveInvoiceLeaseCommand(actor.Id, Guid.NewGuid(), "device-1", 200), CancellationToken.None);

        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenRoleNotOwnerOrManager_ReturnsForbidden()
    {
        var (actor, shop) = Arrange(ShopRole.Staff);

        var result = await CreateHandler().HandleAsync(new ReserveInvoiceLeaseCommand(actor.Id, shop.Id, "device-1", 200), CancellationToken.None);

        Assert.Equal(Errors.InvoiceLease.UserNotAuthorized.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenTaxProfileNotDeclared_ReservesNothing()
    {
        var (actor, shop) = Arrange(declared: false);

        var result = await CreateHandler().HandleAsync(new ReserveInvoiceLeaseCommand(actor.Id, shop.Id, "device-1", null), CancellationToken.None);

        Assert.Equal(Errors.Invoice.TaxProfileRequired.Code, result.FirstError.Code);
        await _allocator.DidNotReceive().ReserveRangeAsync(Arg.Any<InvoiceSeriesKey>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenEInvoiceRequired_ReservesNothing()
    {
        var (actor, shop) = Arrange(eInvoice: EInvoiceStatus.Required);

        var result = await CreateHandler().HandleAsync(new ReserveInvoiceLeaseCommand(actor.Id, shop.Id, "device-1", null), CancellationToken.None);

        Assert.Equal(Errors.Invoice.EInvoiceUnsupported.Code, result.FirstError.Code);
        await _allocator.DidNotReceive().ReserveRangeAsync(Arg.Any<InvoiceSeriesKey>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenValid_ReservesFromTheSharedSeriesAndFreezesTheProfile()
    {
        var (actor, shop) = Arrange(ShopRole.Manager);
        var sessionId = Guid.NewGuid();
        InvoiceLease? added = null;
        await _invoiceLeaseRepository.AddAsync(Arg.Do<InvoiceLease>(lease => added = lease), Arg.Any<CancellationToken>());
        var clock = new FixedClock(new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero));

        var result = await CreateHandler(clock).HandleAsync(new ReserveInvoiceLeaseCommand(actor.Id, shop.Id, "device-1", null, sessionId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(new InvoiceSeriesKey(shop.Id, Gstin, "A", new Domain.ValueObjects.FiscalYear(2026)), _reservedSeries);
        Assert.Equal(InvoiceLeaseDefaults.DefaultBlockSize, _reservedCount);
        Assert.NotNull(added);
        Assert.Equal(_sequenceId, added!.InvoiceSequenceId);
        Assert.Equal("A/2627/", added.Prefix);
        Assert.Equal((201, 400), (added.RangeStart, added.RangeEnd));
        Assert.Equal(actor.Id, added.ReservedByUserId);
        Assert.Equal(sessionId, added.ReservedBySessionId);
        Assert.Equal(Gstin, added.SellerProfile!.Seller.Gstin);
        Assert.Equal("A", added.SellerProfile.SeriesCode);
        Assert.Equal("A/2627/000201", added.FormatInvoiceNumber(added.RangeStart));
        Assert.Equal(("2026-27", "A/2627/", 6, 201, 400), (result.Value.FiscalYear, result.Value.Prefix, result.Value.NumberPadding, result.Value.RangeStart, result.Value.RangeEnd));
        Received.InOrder(() =>
        {
            _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>());
            _allocator.ReserveRangeAsync(Arg.Any<InvoiceSeriesKey>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _unitOfWork.CommitTransactionAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task HandleAsync_WhenValid_ReturnsTheFrozenSellerProfile()
    {
        var (actor, shop) = Arrange(ShopRole.Owner);
        InvoiceLease? added = null;
        await _invoiceLeaseRepository.AddAsync(Arg.Do<InvoiceLease>(lease => added = lease), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(new ReserveInvoiceLeaseCommand(actor.Id, shop.Id, "device-1", null, null), CancellationToken.None);

        // The device prints offline invoices under the identity the server will issue them under.
        Assert.False(result.IsError);
        Assert.NotNull(result.Value.SellerProfile);
        Assert.Equal(added!.SellerProfile, result.Value.SellerProfile);
        Assert.Equal(Gstin, result.Value.SellerProfile!.Seller.Gstin);
    }

    [Fact]
    public async Task HandleAsync_WhenSeriesBelongsToAnotherShop_RollsBack()
    {
        var (actor, shop) = Arrange();
        _allocator.ReserveRangeAsync(Arg.Any<InvoiceSeriesKey>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<ReservedInvoiceRange>>(Errors.Invoice.SeriesRegisteredToAnotherShop));

        var result = await CreateHandler().HandleAsync(new ReserveInvoiceLeaseCommand(actor.Id, shop.Id, "device-1", null), CancellationToken.None);

        Assert.Equal(Errors.Invoice.SeriesRegisteredToAnotherShop.Code, result.FirstError.Code);
        await _invoiceLeaseRepository.DidNotReceive().AddAsync(Arg.Any<InvoiceLease>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    // The fiscal year turns at 1 April 00:00 IST (31 March 18:30 UTC); a lease never straddles it.
    [InlineData("2027-03-28T10:00:00+00:00", "2027-03-31T18:30:00+00:00", 2026)]
    [InlineData("2027-03-31T18:29:00+00:00", "2027-03-31T18:30:00+00:00", 2026)]
    // Well inside the year the usual seven days apply.
    [InlineData("2027-03-10T10:00:00+00:00", "2027-03-17T10:00:00+00:00", 2026)]
    [InlineData("2027-03-31T18:30:00+00:00", "2027-04-07T18:30:00+00:00", 2027)]
    public async Task Reserve_ExpiryCappedAtFiscalYearEnd(string reservedAt, string expectedExpiry, int fiscalYearStart)
    {
        var (actor, shop) = Arrange();
        var now = DateTimeOffset.Parse(reservedAt, System.Globalization.CultureInfo.InvariantCulture);
        InvoiceLease? added = null;
        await _invoiceLeaseRepository.AddAsync(Arg.Do<InvoiceLease>(lease => added = lease), Arg.Any<CancellationToken>());

        var result = await CreateHandler(new FixedClock(now)).HandleAsync(new ReserveInvoiceLeaseCommand(actor.Id, shop.Id, "device-1", null), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(now, added!.ReservedAt);
        Assert.Equal(DateTimeOffset.Parse(expectedExpiry, System.Globalization.CultureInfo.InvariantCulture), added.ExpiresAt);
        Assert.Equal(TimeSpan.Zero, added.ExpiresAt.Offset);
        Assert.Equal(fiscalYearStart, added.FiscalYearStart);
        Assert.Equal(fiscalYearStart, _reservedSeries!.FiscalYear.StartYear);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
