using ErrorOr;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Tills;
using Intelibill.Application.Features.Tills.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Tills;

public sealed class TillCommandHandlerTests
{
    private readonly IShopRepository _shops = Substitute.For<IShopRepository>();
    private readonly ITillRepository _tills = Substitute.For<ITillRepository>();
    private readonly IBankAccountRepository _banks = Substitute.For<IBankAccountRepository>();
    private readonly ITillSessionPolicy _policy = Substitute.For<ITillSessionPolicy>();
    private readonly IBusinessClock _clock = Substitute.For<IBusinessClock>();
    private readonly Shop _shop = Shop.Create("Shop", "Address", "City", "State", "560001", null, null, null);
    private readonly Guid _actor = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 11, 8, 6, 0, 0, TimeSpan.Zero);
    private readonly TillSession _session;
    private readonly TillPostingService _service;

    public TillCommandHandlerTests()
    {
        _session = TillSession.Open(_shop.Id, Guid.NewGuid(), _actor, "Front", "Cashier", _actor, _now,
            new DateOnly(2026, 11, 8), "Asia/Kolkata", 1000m, null).Value;
        _shops.GetByIdAsync(_shop.Id, Arg.Any<CancellationToken>()).Returns(_shop);
        _tills.GetSessionAsync(_shop.Id, _session.Id, Arg.Any<CancellationToken>()).Returns(_session);
        _tills.ReloadSessionAsync(_shop.Id, _session.Id, Arg.Any<CancellationToken>()).Returns(_session);
        _tills.LockSettingsReadAsync(_shop.Id, Arg.Any<CancellationToken>()).Returns(RowLockOutcome.Acquired);
        _tills.LockSessionAsync(_shop.Id, _session.Id, Arg.Any<CancellationToken>()).Returns(RowLockOutcome.Acquired);
        _policy.IsEnabledAsync(_shop.Id, Arg.Any<CancellationToken>()).Returns(true);
        _policy.AuthorizeAsync(_shop.Id, _actor, _actor, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result.Success);
        _policy.ValidateTradingDate(Arg.Any<TillSession>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(Result.Success);
        _clock.UtcNow.Returns(_now);
        _clock.Today(Arg.Any<string>()).Returns(new DateOnly(2026, 11, 8));
        _service = new(_tills, _shops, _banks, _policy, _clock);
    }

    [Fact]
    public async Task Append_NullTenderLine_RefusedBeforeCounters()
    {
        await _service.AcquireAsync(_shop.Id, _actor, new(_session.Id), _session.BusinessDate);
        var result = await _service.AppendAsync(_session, Source([null!]), _actor);
        Assert.True(result.IsError);
        Assert.Equal("Till.TenderRequired", result.FirstError.Code);
        Assert.Equal(0, _session.SourceCount);
        Assert.Equal(0, _session.LastMovementSequence);
    }

    [Fact]
    public async Task Append_WithoutAcquisition_CannotStageSource()
    {
        var result = await _service.AppendAsync(_session, Source([]), _actor);
        Assert.Equal("Till.Forbidden", result.FirstError.Code);
        Assert.Equal(0, _session.SourceCount);
    }

    [Fact]
    public async Task MissingContext_WhenEnabled_IsRefused()
    {
        var result = await _service.AcquireAsync(_shop.Id, _actor, null, _session.BusinessDate);
        Assert.Equal("Till.SessionRequired", result.FirstError.Code);
    }

    [Fact]
    public async Task InvalidTender_IsRejectedWithoutStaging()
    {
        await _service.AcquireAsync(_shop.Id, _actor, new(_session.Id), _session.BusinessDate);
        var result = await _service.AppendAsync(_session, Source([new(ReceiptTenderMethod.UnknownRecordedMethod, 10m, null, null, null, null)]), _actor);
        Assert.Equal("Till.TenderRequired", result.FirstError.Code);
        Assert.Equal(0, _session.SourceCount);
        Assert.Equal(0, _session.LastMovementSequence);
    }

    [Fact]
    public async Task MultipleTenders_AreRejectedWithoutStaging()
    {
        await _service.AcquireAsync(_shop.Id, _actor, new(_session.Id), _session.BusinessDate);
        var tender = new TenderLine(ReceiptTenderMethod.Cash, 10m, null, null, null, null);
        var result = await _service.AppendAsync(_session, Source([tender, tender]), _actor);
        Assert.Equal("Till.MixedTenderUnsupported", result.FirstError.Code);
        Assert.Equal(0, _session.SourceCount);
    }

    [Fact]
    public async Task ForeignBankReference_RefusedBeforeCounters()
    {
        await _service.AcquireAsync(_shop.Id, _actor, new(_session.Id), _session.BusinessDate);
        var bank = BankAccount.Create(Guid.NewGuid(), "Bank", "1234", null, null, null);
        _banks.GetByIdAsync(bank.Id, Arg.Any<CancellationToken>()).Returns(bank);
        var result = await _service.AppendAsync(_session, Source([new(ReceiptTenderMethod.BankTransfer, 10m, null, null, null, bank.Id)]), _actor);
        Assert.Equal("Till.TenderReferenceInvalid", result.FirstError.Code);
        Assert.Equal(0, _session.SourceCount);
    }

    private TillSourcePosting Source(IReadOnlyList<TenderLine> lines) =>
        new(TillMovementSource.OnlineSale, Guid.NewGuid(), "sale", lines, 1m, _now, "op", null);
}
