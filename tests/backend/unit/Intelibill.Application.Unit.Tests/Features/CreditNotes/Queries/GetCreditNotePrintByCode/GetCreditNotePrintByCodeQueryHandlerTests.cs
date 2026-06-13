using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.CreditNotes.Queries.GetCreditNotePrintByCode;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.CreditNotes.Queries.GetCreditNotePrintByCode;

public sealed class GetCreditNotePrintByCodeQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ICreditNoteRepository _creditNoteRepository = Substitute.For<ICreditNoteRepository>();
    private readonly ISaleReturnRepository _saleReturnRepository = Substitute.For<ISaleReturnRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();

    [Fact]
    public async Task HandleAsync_ActiveCreditNote_ReturnsPrintContext()
    {
        var fixture = BuildFixture();
        var sale = CreateSale(fixture.shop.Id, "INV-001", "Jane Doe");
        var saleReturn = CreateSaleReturn(fixture.shop.Id, sale.Id, "RET-001");
        var note = CreateCreditNote(fixture.shop.Id, saleReturn.Id);
        ArrangeAuthorizedLookup(fixture.manager.Id, fixture.managerMembership, note, saleReturn, sale);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNotePrintByCodeQuery(fixture.manager.Id, fixture.shop.Id, note.Code),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.Active, result.Value.Status);
        Assert.True(result.Value.IsUsable);
        Assert.Equal(note.OriginalAmount, result.Value.OriginalAmount);
        Assert.Equal(note.AvailableBalance, result.Value.AvailableBalance);
        Assert.Equal(note.CreatedAt, result.Value.IssuedAt);
        Assert.Equal(note.ExpiresAt, result.Value.ExpiresAt);
        Assert.Equal(sale.Id, result.Value.SaleId);
        Assert.Equal("INV-001", result.Value.InvoiceNumber);
        Assert.Equal(saleReturn.Id, result.Value.SaleReturnId);
        Assert.Equal("RET-001", result.Value.ReturnNumber);
        Assert.Equal("Jane Doe", result.Value.CustomerDisplayName);
        Assert.Equal(note.Reason, result.Value.Reason);
        Assert.Null(result.Value.VoidReason);
    }

    [Fact]
    public async Task HandleAsync_ExpiredCreditNote_ReturnsExpiredPrintContext()
    {
        var fixture = BuildFixture();
        var sale = CreateSale(fixture.shop.Id, "INV-002", null);
        var saleReturn = CreateSaleReturn(fixture.shop.Id, sale.Id, "RET-002");
        var note = CreateCreditNote(fixture.shop.Id, saleReturn.Id, DateTimeOffset.UtcNow.AddDays(-1));
        ArrangeAuthorizedLookup(fixture.manager.Id, fixture.managerMembership, note, saleReturn, sale);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNotePrintByCodeQuery(fixture.manager.Id, fixture.shop.Id, note.Code),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.Expired, result.Value.Status);
        Assert.False(result.Value.IsUsable);
        Assert.Equal("Walk-in Customer", result.Value.CustomerDisplayName);
        Assert.Equal(note.ExpiresAt, result.Value.ExpiresAt);
    }

    [Fact]
    public async Task HandleAsync_FullyRedeemedCreditNote_ReturnsRedeemedPrintContext()
    {
        var fixture = BuildFixture();
        var sale = CreateSale(fixture.shop.Id, "INV-003", "Alex Customer");
        var saleReturn = CreateSaleReturn(fixture.shop.Id, sale.Id, "RET-003");
        var note = CreateCreditNote(fixture.shop.Id, saleReturn.Id, redeemFullAmount: true);
        ArrangeAuthorizedLookup(fixture.manager.Id, fixture.managerMembership, note, saleReturn, sale);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNotePrintByCodeQuery(fixture.manager.Id, fixture.shop.Id, note.Code),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.FullyRedeemed, result.Value.Status);
        Assert.False(result.Value.IsUsable);
        Assert.Equal(0m, result.Value.AvailableBalance);
    }

    [Fact]
    public async Task HandleAsync_VoidedCreditNote_ReturnsVoidReasonForOwnerOrManager()
    {
        var fixture = BuildFixture();
        var sale = CreateSale(fixture.shop.Id, "INV-004", "Taylor Customer");
        var saleReturn = CreateSaleReturn(fixture.shop.Id, sale.Id, "RET-004");
        var note = CreateCreditNote(fixture.shop.Id, saleReturn.Id, voidReason: "Issued in error");
        ArrangeAuthorizedLookup(fixture.owner.Id, fixture.ownerMembership, note, saleReturn, sale);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNotePrintByCodeQuery(fixture.owner.Id, fixture.shop.Id, note.Code),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.Voided, result.Value.Status);
        Assert.False(result.Value.IsUsable);
        Assert.Equal("Issued in error", result.Value.VoidReason);
    }

    [Fact]
    public async Task HandleAsync_VoidedCreditNote_HidesVoidReasonFromStaff()
    {
        var fixture = BuildFixture();
        var sale = CreateSale(fixture.shop.Id, "INV-005", "Taylor Customer");
        var saleReturn = CreateSaleReturn(fixture.shop.Id, sale.Id, "RET-005");
        var note = CreateCreditNote(fixture.shop.Id, saleReturn.Id, voidReason: "Issued in error");
        ArrangeAuthorizedLookup(fixture.staff.Id, fixture.staffMembership, note, saleReturn, sale);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNotePrintByCodeQuery(fixture.staff.Id, fixture.shop.Id, note.Code),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.Voided, result.Value.Status);
        Assert.False(result.Value.IsUsable);
        Assert.Null(result.Value.VoidReason);
    }

    [Fact]
    public async Task HandleAsync_WhenMissing_ReturnsNotFound()
    {
        var fixture = BuildFixture();
        var sale = CreateSale(fixture.shop.Id, "INV-404", "Jane Doe");
        var saleReturn = CreateSaleReturn(fixture.shop.Id, sale.Id, "RET-404");

        _userRepository.GetByIdAsync(fixture.manager.Id, Arg.Any<CancellationToken>()).Returns(fixture.manager);
        _shopRepository.GetByIdAsync(fixture.shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.shop);
        _shopRepository.GetMembershipAsync(fixture.manager.Id, fixture.shop.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.managerMembership);
        _creditNoteRepository.GetByCodeAsync(fixture.shop.Id, "CN-404", Arg.Any<CancellationToken>())
            .Returns((CreditNote?)null);
        _saleReturnRepository.GetByIdWithItemsAsync(fixture.shop.Id, saleReturn.Id, Arg.Any<CancellationToken>())
            .Returns(saleReturn);
        _saleRepository.GetByIdAsync(sale.Id, fixture.shop.Id, Arg.Any<CancellationToken>())
            .Returns(sale);

        var result = await CreateHandler().HandleAsync(
            new GetCreditNotePrintByCodeQuery(fixture.manager.Id, fixture.shop.Id, "CN-404"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.CreditNote.CreditNoteNotFound("CN-404").Code, result.FirstError.Code);
    }

    private GetCreditNotePrintByCodeQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _creditNoteRepository, _saleReturnRepository, _saleRepository);

    private void ArrangeAuthorizedLookup(
        Guid userId,
        ShopMembership membership,
        CreditNote creditNote,
        SaleReturn saleReturn,
        Sale sale)
    {
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(membership.User);
        _shopRepository.GetByIdAsync(membership.ShopId, Arg.Any<CancellationToken>()).Returns(membership.Shop);
        _shopRepository.GetMembershipAsync(userId, membership.ShopId, Arg.Any<CancellationToken>())
            .Returns(membership);
        _creditNoteRepository.GetByCodeAsync(membership.ShopId, creditNote.Code, Arg.Any<CancellationToken>())
            .Returns(creditNote);
        _saleReturnRepository.GetByIdWithItemsAsync(membership.ShopId, saleReturn.Id, Arg.Any<CancellationToken>())
            .Returns(saleReturn);
        _saleRepository.GetByIdAsync(sale.Id, membership.ShopId, Arg.Any<CancellationToken>())
            .Returns(sale);
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
        Guid saleReturnId,
        DateTimeOffset? expiresAt = null,
        bool redeemFullAmount = false,
        string? voidReason = null)
    {
        var result = CreditNote.Issue(shopId, saleReturnId, 100m, "Return reason", "CN-001", expiresAt);
        var note = result.Value;

        if (redeemFullAmount)
        {
            var redemptionResult = note.Redeem(shopId, Guid.NewGuid(), 100m);
            Assert.False(redemptionResult.IsError);
        }

        if (voidReason is not null)
        {
            var voidResult = note.Void(voidReason);
            Assert.False(voidResult.IsError);
        }

        return note;
    }

    private static Sale CreateSale(Guid shopId, string invoiceNumber, string? customerName)
    {
        var saleItem = SaleItem.CreateService(
            shopId,
            Guid.NewGuid(),
            "Item",
            "ITEM-001",
            1m,
            0m,
            100m,
            100m,
            0m,
            false,
            false);

        return Sale.Create(
            shopId,
            invoiceNumber,
            null,
            customerName,
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            100m,
            0m,
            100m,
            0m,
            [saleItem]);
    }

    private static SaleReturn CreateSaleReturn(Guid shopId, Guid saleId, string returnNumber) =>
        SaleReturn.Record(
            shopId,
            saleId,
            returnNumber,
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

    private sealed record Fixture(
        Shop shop,
        User owner,
        User manager,
        User staff,
        ShopMembership ownerMembership,
        ShopMembership managerMembership,
        ShopMembership staffMembership);
}
