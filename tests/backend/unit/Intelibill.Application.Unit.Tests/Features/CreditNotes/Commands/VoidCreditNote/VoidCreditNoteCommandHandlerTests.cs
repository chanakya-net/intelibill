using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.CreditNotes.Commands.VoidCreditNote;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.CreditNotes.Commands.VoidCreditNote;

public sealed class VoidCreditNoteCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ICreditNoteRepository _creditNoteRepository = Substitute.For<ICreditNoteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Theory]
    [InlineData(ShopRole.Owner)]
    [InlineData(ShopRole.Manager)]
    public async Task HandleAsync_ForOwnerOrManager_VoidsUnredeemedNote(ShopRole role)
    {
        var fixture = Arrange(role);
        var creditNote = fixture.CreditNote ?? throw new InvalidOperationException("Expected a credit note.");

        var result = await CreateHandler().HandleAsync(
            new VoidCreditNoteCommand(fixture.User.Id, fixture.Shop.Id, creditNote.Code, "Issued in error"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(creditNote.IsVoided);
        Assert.Equal("Issued in error", creditNote.VoidReason);
        _creditNoteRepository.Received(1).Update(creditNote);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenReasonMissing_ReturnsValidationError()
    {
        var fixture = Arrange(ShopRole.Owner);
        var creditNote = fixture.CreditNote ?? throw new InvalidOperationException("Expected a credit note.");

        var result = await CreateHandler().HandleAsync(
            new VoidCreditNoteCommand(fixture.User.Id, fixture.Shop.Id, creditNote.Code, " "),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.CreditNote.VoidReasonRequired.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCreditNoteMissing_ReturnsNotFound()
    {
        var fixture = Arrange(ShopRole.Owner, noteExists: false);

        var result = await CreateHandler().HandleAsync(
            new VoidCreditNoteCommand(fixture.User.Id, fixture.Shop.Id, "CN-404", "Issued in error"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.CreditNote.CreditNoteNotFound("CN-404").Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenActorIsStaff_ReturnsForbidden()
    {
        var fixture = Arrange(ShopRole.Staff);
        var creditNote = fixture.CreditNote ?? throw new InvalidOperationException("Expected a credit note.");

        var result = await CreateHandler().HandleAsync(
            new VoidCreditNoteCommand(fixture.User.Id, fixture.Shop.Id, creditNote.Code, "Issued in error"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.CreditNote.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyVoided_ReturnsValidationError()
    {
        var fixture = Arrange(ShopRole.Owner, voided: true);
        var creditNote = fixture.CreditNote ?? throw new InvalidOperationException("Expected a credit note.");

        var result = await CreateHandler().HandleAsync(
            new VoidCreditNoteCommand(fixture.User.Id, fixture.Shop.Id, creditNote.Code, "Issued in error"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.CreditNote.AlreadyVoided.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenRedemptionsExist_ReturnsConflict()
    {
        var fixture = Arrange(ShopRole.Owner, redeemed: true);
        var creditNote = fixture.CreditNote ?? throw new InvalidOperationException("Expected a credit note.");

        var result = await CreateHandler().HandleAsync(
            new VoidCreditNoteCommand(fixture.User.Id, fixture.Shop.Id, creditNote.Code, "Issued in error"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.CreditNote.CannotVoidAfterRedemption.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private VoidCreditNoteCommandHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _creditNoteRepository, _unitOfWork);

    private Fixture Arrange(ShopRole role, bool noteExists = true, bool redeemed = false, bool voided = false)
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, user.Id, role, true);
        user.AddShopMembership(membership);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);

        CreditNote? creditNote = null;
        if (noteExists)
        {
            creditNote = CreditNote.Issue(shop.Id, Guid.NewGuid(), 100m, "Return reason", "CN-001", null).Value;
            if (redeemed)
            {
                creditNote.Redeem(shop.Id, Guid.NewGuid(), 25m);
            }

            if (voided)
            {
                creditNote.Void("Earlier void");
            }

            _creditNoteRepository.GetByCodeAsync(shop.Id, creditNote.Code, Arg.Any<CancellationToken>())
                .Returns(creditNote);
        }
        else
        {
            _creditNoteRepository.GetByCodeAsync(shop.Id, "CN-404", Arg.Any<CancellationToken>())
                .Returns((CreditNote?)null);
        }

        return new Fixture(user, shop, membership, creditNote);
    }

    private sealed record Fixture(
        User User,
        Shop Shop,
        ShopMembership Membership,
        CreditNote? CreditNote);
}
