using Intelibill.Application.Features.CreditNotes.DTOs;
using Intelibill.Application.Features.CreditNotes.Queries.GetCreditNotes;
using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.CreditNotes.Queries.GetCreditNotes;

public sealed class GetCreditNotesQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ICreditNoteRepository _creditNoteRepository = Substitute.For<ICreditNoteRepository>();

    private GetCreditNotesQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _creditNoteRepository);

    private static Fixture BuildFixture()
    {
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var staff = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        var ownerMembership = ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true);
        var staffMembership = ShopMembership.Create(shop.Id, staff.Id, ShopRole.Staff, false);
        owner.AddShopMembership(ownerMembership);
        staff.AddShopMembership(staffMembership);
        shop.AddMembership(ownerMembership);
        shop.AddMembership(staffMembership);
        return new Fixture(shop, owner, staff, ownerMembership, staffMembership);
    }

    private static CreditNoteListRow MakeRow(
        Guid shopId,
        string code = "CN-001",
        bool isVoided = false,
        decimal balance = 100m,
        DateTimeOffset? expiresAt = null) =>
        new(
            Guid.NewGuid(),
            code,
            100m,
            balance,
            expiresAt,
            isVoided,
            DateTimeOffset.UtcNow.AddDays(-1),
            Guid.NewGuid(),
            "RET-001",
            Guid.NewGuid(),
            "INV-001",
            "Alice");

    [Fact]
    public async Task Handle_ReturnsPaginatedList_WhenAuthorized()
    {
        var fixture = BuildFixture();
        var row = MakeRow(fixture.Shop.Id);

        _userRepository.GetByIdAsync(fixture.Owner.Id, Arg.Any<CancellationToken>()).Returns(fixture.Owner);
        _shopRepository.GetByIdAsync(fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.Shop);
        _shopRepository.GetMembershipAsync(fixture.Owner.Id, fixture.Shop.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.OwnerMembership);
        _creditNoteRepository.GetPagedAsync(
            fixture.Shop.Id, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(([row], 1));

        var result = await CreateHandler().Handle(
            new GetCreditNotesQuery(fixture.Owner.Id, fixture.Shop.Id, null, null, 1, 20),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Items);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal(row.Code, result.Value.Items[0].Code);
    }

    [Fact]
    public async Task Handle_PassesSearchAndStatusToRepository()
    {
        var fixture = BuildFixture();

        _userRepository.GetByIdAsync(fixture.Owner.Id, Arg.Any<CancellationToken>()).Returns(fixture.Owner);
        _shopRepository.GetByIdAsync(fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.Shop);
        _shopRepository.GetMembershipAsync(fixture.Owner.Id, fixture.Shop.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.OwnerMembership);
        _creditNoteRepository.GetPagedAsync(
            fixture.Shop.Id, "CN-", CreditNoteStatus.Active, 2, 10, Arg.Any<CancellationToken>())
            .Returns(([], 0));

        var result = await CreateHandler().Handle(
            new GetCreditNotesQuery(fixture.Owner.Id, fixture.Shop.Id, "CN-", CreditNoteStatus.Active, 2, 10),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Items);

        await _creditNoteRepository.Received(1).GetPagedAsync(
            fixture.Shop.Id, "CN-", CreditNoteStatus.Active, 2, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MapsStatusCorrectlyForActiveNote()
    {
        var fixture = BuildFixture();
        var row = MakeRow(fixture.Shop.Id, isVoided: false, balance: 100m);

        _userRepository.GetByIdAsync(fixture.Owner.Id, Arg.Any<CancellationToken>()).Returns(fixture.Owner);
        _shopRepository.GetByIdAsync(fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.Shop);
        _shopRepository.GetMembershipAsync(fixture.Owner.Id, fixture.Shop.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.OwnerMembership);
        _creditNoteRepository.GetPagedAsync(
            fixture.Shop.Id, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(([row], 1));

        var result = await CreateHandler().Handle(
            new GetCreditNotesQuery(fixture.Owner.Id, fixture.Shop.Id, null, null),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.Active, result.Value.Items[0].Status);
    }

    [Fact]
    public async Task Handle_MapsStatusCorrectlyForVoidedNote()
    {
        var fixture = BuildFixture();
        var row = MakeRow(fixture.Shop.Id, isVoided: true);

        _userRepository.GetByIdAsync(fixture.Owner.Id, Arg.Any<CancellationToken>()).Returns(fixture.Owner);
        _shopRepository.GetByIdAsync(fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.Shop);
        _shopRepository.GetMembershipAsync(fixture.Owner.Id, fixture.Shop.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.OwnerMembership);
        _creditNoteRepository.GetPagedAsync(
            fixture.Shop.Id, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(([row], 1));

        var result = await CreateHandler().Handle(
            new GetCreditNotesQuery(fixture.Owner.Id, fixture.Shop.Id, null, null),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.Voided, result.Value.Items[0].Status);
    }

    [Fact]
    public async Task Handle_MapsStatusCorrectlyForFullyRedeemedNote()
    {
        var fixture = BuildFixture();
        var row = MakeRow(fixture.Shop.Id, isVoided: false, balance: 0m);

        _userRepository.GetByIdAsync(fixture.Owner.Id, Arg.Any<CancellationToken>()).Returns(fixture.Owner);
        _shopRepository.GetByIdAsync(fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.Shop);
        _shopRepository.GetMembershipAsync(fixture.Owner.Id, fixture.Shop.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.OwnerMembership);
        _creditNoteRepository.GetPagedAsync(
            fixture.Shop.Id, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(([row], 1));

        var result = await CreateHandler().Handle(
            new GetCreditNotesQuery(fixture.Owner.Id, fixture.Shop.Id, null, null),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.FullyRedeemed, result.Value.Items[0].Status);
    }

    [Fact]
    public async Task Handle_MapsStatusCorrectlyForExpiredNote()
    {
        var fixture = BuildFixture();
        var row = MakeRow(fixture.Shop.Id, isVoided: false, balance: 50m,
            expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        _userRepository.GetByIdAsync(fixture.Owner.Id, Arg.Any<CancellationToken>()).Returns(fixture.Owner);
        _shopRepository.GetByIdAsync(fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.Shop);
        _shopRepository.GetMembershipAsync(fixture.Owner.Id, fixture.Shop.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.OwnerMembership);
        _creditNoteRepository.GetPagedAsync(
            fixture.Shop.Id, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(([row], 1));

        var result = await CreateHandler().Handle(
            new GetCreditNotesQuery(fixture.Owner.Id, fixture.Shop.Id, null, null),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.Expired, result.Value.Items[0].Status);
    }

    [Fact]
    public async Task Handle_ReturnsUserNotFound_WhenUserMissing()
    {
        var fixture = BuildFixture();
        _userRepository.GetByIdAsync(fixture.Owner.Id, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await CreateHandler().Handle(
            new GetCreditNotesQuery(fixture.Owner.Id, fixture.Shop.Id, null, null),
            CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Handle_ReturnsMembershipNotFound_WhenNotMember()
    {
        var fixture = BuildFixture();

        _userRepository.GetByIdAsync(fixture.Owner.Id, Arg.Any<CancellationToken>()).Returns(fixture.Owner);
        _shopRepository.GetByIdAsync(fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.Shop);
        _shopRepository.GetMembershipAsync(fixture.Owner.Id, fixture.Shop.Id, Arg.Any<CancellationToken>())
            .Returns((ShopMembership?)null);

        var result = await CreateHandler().Handle(
            new GetCreditNotesQuery(fixture.Owner.Id, fixture.Shop.Id, null, null),
            CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Handle_AllowsStaffToList()
    {
        var fixture = BuildFixture();
        _userRepository.GetByIdAsync(fixture.Staff.Id, Arg.Any<CancellationToken>()).Returns(fixture.Staff);
        _shopRepository.GetByIdAsync(fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.Shop);
        _shopRepository.GetMembershipAsync(fixture.Staff.Id, fixture.Shop.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.StaffMembership);
        _creditNoteRepository.GetPagedAsync(
            fixture.Shop.Id, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(([], 0));

        var result = await CreateHandler().Handle(
            new GetCreditNotesQuery(fixture.Staff.Id, fixture.Shop.Id, null, null),
            CancellationToken.None);

        Assert.False(result.IsError);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(-99, 20)]
    public async Task Handle_NormalizesNonPositivePageNumberToOne(int rawPage, int expectedPageSize)
    {
        var fixture = BuildFixture();

        _userRepository.GetByIdAsync(fixture.Owner.Id, Arg.Any<CancellationToken>()).Returns(fixture.Owner);
        _shopRepository.GetByIdAsync(fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.Shop);
        _shopRepository.GetMembershipAsync(fixture.Owner.Id, fixture.Shop.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.OwnerMembership);
        _creditNoteRepository.GetPagedAsync(
            fixture.Shop.Id, null, null, 1, expectedPageSize, Arg.Any<CancellationToken>())
            .Returns(([], 0));

        var result = await CreateHandler().Handle(
            new GetCreditNotesQuery(fixture.Owner.Id, fixture.Shop.Id, null, null, rawPage, expectedPageSize),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.PageNumber);
        await _creditNoteRepository.Received(1).GetPagedAsync(
            fixture.Shop.Id, null, null, 1, expectedPageSize, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Handle_NormalizesNonPositivePageSizeToDefault(int rawPageSize)
    {
        var fixture = BuildFixture();

        _userRepository.GetByIdAsync(fixture.Owner.Id, Arg.Any<CancellationToken>()).Returns(fixture.Owner);
        _shopRepository.GetByIdAsync(fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.Shop);
        _shopRepository.GetMembershipAsync(fixture.Owner.Id, fixture.Shop.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.OwnerMembership);
        _creditNoteRepository.GetPagedAsync(
            fixture.Shop.Id, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(([], 0));

        var result = await CreateHandler().Handle(
            new GetCreditNotesQuery(fixture.Owner.Id, fixture.Shop.Id, null, null, 1, rawPageSize),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(20, result.Value.PageSize);
        await _creditNoteRepository.Received(1).GetPagedAsync(
            fixture.Shop.Id, null, null, 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClampsOversizedPageSizeToMax()
    {
        var fixture = BuildFixture();

        _userRepository.GetByIdAsync(fixture.Owner.Id, Arg.Any<CancellationToken>()).Returns(fixture.Owner);
        _shopRepository.GetByIdAsync(fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.Shop);
        _shopRepository.GetMembershipAsync(fixture.Owner.Id, fixture.Shop.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.OwnerMembership);
        _creditNoteRepository.GetPagedAsync(
            fixture.Shop.Id, null, null, 1, 100, Arg.Any<CancellationToken>())
            .Returns(([], 0));

        var result = await CreateHandler().Handle(
            new GetCreditNotesQuery(fixture.Owner.Id, fixture.Shop.Id, null, null, 1, 9999),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(100, result.Value.PageSize);
        await _creditNoteRepository.Received(1).GetPagedAsync(
            fixture.Shop.Id, null, null, 1, 100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MapsStatusActiveForNoteExpiringExactlyNow()
    {
        var fixture = BuildFixture();
        // Use a note expiring slightly in the future; ComputeStatus uses strict < so any future
        // expiry is Active. This pins the boundary: ExpiresAt >= UtcNow means Active, not Expired.
        var nearFuture = DateTimeOffset.UtcNow.AddMilliseconds(500);
        var row = MakeRow(fixture.Shop.Id, isVoided: false, balance: 50m, expiresAt: nearFuture);

        _userRepository.GetByIdAsync(fixture.Owner.Id, Arg.Any<CancellationToken>()).Returns(fixture.Owner);
        _shopRepository.GetByIdAsync(fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(fixture.Shop);
        _shopRepository.GetMembershipAsync(fixture.Owner.Id, fixture.Shop.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.OwnerMembership);
        _creditNoteRepository.GetPagedAsync(
            fixture.Shop.Id, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(([row], 1));

        var result = await CreateHandler().Handle(
            new GetCreditNotesQuery(fixture.Owner.Id, fixture.Shop.Id, null, null),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(CreditNoteStatus.Active, result.Value.Items[0].Status);
    }

    private sealed record Fixture(
        Shop Shop,
        User Owner,
        User Staff,
        ShopMembership OwnerMembership,
        ShopMembership StaffMembership);
}
