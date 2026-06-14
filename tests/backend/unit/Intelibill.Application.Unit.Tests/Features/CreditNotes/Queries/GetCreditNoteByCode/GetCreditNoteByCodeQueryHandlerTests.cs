using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Normalization;
using Intelibill.Application.Features.CreditNotes.Queries.GetCreditNoteByCode;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.CreditNotes.Queries.GetCreditNoteByCode;

public sealed class GetCreditNoteByCodeQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ICreditNoteRepository _creditNoteRepository = Substitute.For<ICreditNoteRepository>();
    private readonly ISaleReturnRepository _saleReturnRepository = Substitute.For<ISaleReturnRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();

    private GetCreditNoteByCodeQueryHandler CreateHandler() =>
        new(
            _userRepository,
            _shopRepository,
            _creditNoteRepository,
            _saleReturnRepository,
            _saleRepository);

    [Fact]
    public async Task HandleAsync_ActiveCreditNote_ReturnsUsableBalanceAndContext()
    {
        var fixture = BuildFixture();
        var creditNote = CreateCreditNote(fixture.shop.Id);
        ArrangeAuthorizedLookup(fixture.manager.Id, fixture.managerMembership, creditNote);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNoteByCodeQuery(fixture.manager.Id, fixture.shop.Id, creditNote.Code),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.Active, result.Value.Status);
        Assert.Equal(creditNote.AvailableBalance, result.Value.AvailableBalance);
        Assert.Equal(creditNote.ExpiresAt, result.Value.ExpiresAt);
        Assert.Equal(creditNote.SaleReturnId, result.Value.SaleReturnId);
        Assert.Equal("RET-001", result.Value.ReturnNumber);
        Assert.Equal("INV-001", result.Value.InvoiceNumber);
        Assert.Equal("Asha", result.Value.CustomerName);
        Assert.Null(result.Value.VoidReason);
    }

    [Fact]
    public async Task HandleAsync_NormalizesMixedSeparatorAndCaseCode()
    {
        var fixture = BuildFixture();
        var creditNote = CreateCreditNote(fixture.shop.Id);
        ArrangeAuthorizedLookup(fixture.manager.Id, fixture.managerMembership, creditNote);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNoteByCodeQuery(fixture.manager.Id, fixture.shop.Id, " cn - 001 "),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(creditNote.Code, result.Value.Code);
    }

    [Fact]
    public async Task HandleAsync_ExpiredCreditNote_ReturnsExpiredStatus()
    {
        var fixture = BuildFixture();
        var creditNote = CreateCreditNote(fixture.shop.Id, DateTimeOffset.UtcNow.AddDays(-1));
        ArrangeAuthorizedLookup(fixture.manager.Id, fixture.managerMembership, creditNote);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNoteByCodeQuery(fixture.manager.Id, fixture.shop.Id, creditNote.Code),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.Expired, result.Value.Status);
    }

    [Fact]
    public async Task HandleAsync_VoidedCreditNote_ReturnsVoidReasonForOwnerOrManager()
    {
        var fixture = BuildFixture();
        var creditNote = CreateCreditNote(fixture.shop.Id, voidReason: "Issued in error");
        ArrangeAuthorizedLookup(fixture.owner.Id, fixture.ownerMembership, creditNote);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNoteByCodeQuery(fixture.owner.Id, fixture.shop.Id, creditNote.Code),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.Voided, result.Value.Status);
        Assert.Equal("Issued in error", result.Value.VoidReason);
    }

    [Fact]
    public async Task HandleAsync_VoidedCreditNote_HidesVoidReasonFromStaff()
    {
        var fixture = BuildFixture();
        var creditNote = CreateCreditNote(fixture.shop.Id, voidReason: "Issued in error");
        ArrangeAuthorizedLookup(fixture.staff.Id, fixture.staffMembership, creditNote);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNoteByCodeQuery(fixture.staff.Id, fixture.shop.Id, creditNote.Code),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.Voided, result.Value.Status);
        Assert.Null(result.Value.VoidReason);
    }

    [Fact]
    public async Task HandleAsync_FullyRedeemedCreditNote_ReturnsFullyRedeemedStatus()
    {
        var fixture = BuildFixture();
        var creditNote = CreateCreditNote(fixture.shop.Id, redeemFullAmount: true);
        ArrangeAuthorizedLookup(fixture.manager.Id, fixture.managerMembership, creditNote);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNoteByCodeQuery(fixture.manager.Id, fixture.shop.Id, creditNote.Code),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.FullyRedeemed, result.Value.Status);
        Assert.Equal(0m, result.Value.AvailableBalance);
    }

    [Fact]
    public async Task HandleAsync_VoidedCreditNote_ReturnsVoidedStatusWithoutRevealForStaff()
    {
        var fixture = BuildFixture();
        var creditNote = CreateCreditNote(fixture.shop.Id, voidReason: "Issued in error");
        ArrangeAuthorizedLookup(fixture.staff.Id, fixture.staffMembership, creditNote);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNoteByCodeQuery(fixture.staff.Id, fixture.shop.Id, creditNote.Code),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.Voided, result.Value.Status);
        Assert.True(result.Value.IsVoided);
        Assert.Null(result.Value.VoidReason);
    }

    [Fact]
    public async Task HandleAsync_WhenMissing_ReturnsNotFound()
    {
        var fixture = BuildFixture();

        _userRepository.GetByIdAsync(fixture.manager.Id, Arg.Any<CancellationToken>()).Returns(fixture.manager);
        _shopRepository.GetByIdAsync(fixture.shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.shop);
        _shopRepository.GetMembershipAsync(fixture.manager.Id, fixture.shop.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.managerMembership);
        _creditNoteRepository.GetByCodeAsync(fixture.shop.Id, "CN-404", Arg.Any<CancellationToken>())
            .Returns((CreditNote?)null);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNoteByCodeQuery(fixture.manager.Id, fixture.shop.Id, "CN-404"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.CreditNote.CreditNoteNotFound("CN-404").Code, result.FirstError.Code);
    }

    private void ArrangeAuthorizedLookup(Guid userId, ShopMembership membership, CreditNote creditNote)
    {
        var (saleReturn, sale) = CreateSaleContext(membership.ShopId);
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(_ => membership.User);
        _shopRepository.GetByIdAsync(membership.ShopId, Arg.Any<CancellationToken>()).Returns(membership.Shop);
        _shopRepository.GetMembershipAsync(userId, membership.ShopId, Arg.Any<CancellationToken>())
            .Returns(membership);
        _creditNoteRepository.GetByCodeAsync(membership.ShopId, CreditNoteCodeNormalizer.Normalize(creditNote.Code), Arg.Any<CancellationToken>())
            .Returns(creditNote);
        _saleReturnRepository.GetByIdWithItemsAsync(
                membership.ShopId,
                creditNote.SaleReturnId,
                Arg.Any<CancellationToken>())
            .Returns(saleReturn);
        _saleRepository.GetByIdAsync(
                sale.Id,
                membership.ShopId,
                Arg.Any<CancellationToken>())
            .Returns(sale);
    }

    private static (SaleReturn SaleReturn, Sale Sale) CreateSaleContext(Guid shopId)
    {
        var sale = Sale.Create(
            shopId,
            "INV-001",
            null,
            "Asha",
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            100m,
            0m,
            100m,
            0m,
            []);

        var saleReturn = SaleReturn.Record(
            shopId,
            sale.Id,
            "RET-001",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            null,
            0m,
            0m,
            0m,
            null,
            0m,
            0m,
            null,
            null,
            []).Value;

        return (saleReturn, sale);
    }

    private static Fixture BuildFixture()
    {
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var manager = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var staff = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");

        var ownerMembership = ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true);
        var managerMembership = ShopMembership.Create(shop.Id, manager.Id, ShopRole.Manager, false);
        var staffMembership = ShopMembership.Create(shop.Id, staff.Id, ShopRole.Staff, false);

        owner.AddShopMembership(ownerMembership);
        manager.AddShopMembership(managerMembership);
        staff.AddShopMembership(staffMembership);

        shop.AddMembership(ownerMembership);
        shop.AddMembership(managerMembership);
        shop.AddMembership(staffMembership);

        return new Fixture(shop, owner, manager, staff, ownerMembership, managerMembership, staffMembership);
    }

    private static CreditNote CreateCreditNote(
        Guid shopId,
        DateTimeOffset? expiresAt = null,
        bool redeemFullAmount = false,
        string? voidReason = null)
    {
        var result = CreditNote.Issue(shopId, Guid.NewGuid(), 100m, "Return reason", "CN-001", expiresAt);
        var note = result.Value;

        if (redeemFullAmount)
        {
            note.Redeem(shopId, Guid.NewGuid(), 100m);
        }

        if (voidReason is not null)
        {
            note.Void(voidReason);
        }

        return note;
    }

    private sealed record Fixture(
        Shop shop,
        User owner,
        User manager,
        User staff,
        ShopMembership ownerMembership,
        ShopMembership managerMembership,
        ShopMembership staffMembership);
}
