using ErrorOr;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.PostingPeriods.Commands.ClosePostingPeriod;
using Intelibill.Application.Features.PostingPeriods.Commands.ReopenPostingPeriod;
using Intelibill.Application.Features.PostingPeriods.DTOs;
using Intelibill.Application.Features.PostingPeriods.Queries.GetPostingPeriods;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.PostingPeriods;

public sealed class PostingPeriodHandlerTests
{
    private static readonly DateOnly June = new(2026, 6, 1);
    private readonly Guid _actor = Guid.NewGuid();
    private readonly Shop _shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
    private readonly IShopRepository _shops = Substitute.For<IShopRepository>();
    private readonly IPostingPeriodRepository _periods = Substitute.For<IPostingPeriodRepository>();
    private readonly IFinancialOperationExecutor _executor = Substitute.For<IFinancialOperationExecutor>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IBusinessClock _clock = Substitute.For<IBusinessClock>();
    private readonly PostingPeriod _period;

    public PostingPeriodHandlerTests()
    {
        _period = PostingPeriod.Open(_shop.Id, June);
        _shops.GetByIdAsync(_shop.Id, Arg.Any<CancellationToken>()).Returns(_shop);
        _shops.GetActiveRoleAsync(_actor, _shop.Id, Arg.Any<CancellationToken>()).Returns(ShopRole.Owner);
        _periods.LockForTransitionAsync(_shop.Id, June, Arg.Any<CancellationToken>()).Returns(_period);
        _periods.GetEventsAsync(_shop.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PostingPeriodEvent>());
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 7, 5, 6, 0, 0, TimeSpan.Zero));
        _clock.Today(Arg.Any<string>()).Returns(new DateOnly(2026, 7, 5));
        _executor.ExecuteAsync(
                Arg.Any<FinancialOperationScope>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<ErrorOr<PostingPeriodDto>>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<ErrorOr<PostingPeriodDto>>>>()(CancellationToken.None));
    }

    private ClosePostingPeriodCommandHandler CloseHandler() => new(_shops, _periods, _executor, _unitOfWork, _clock);

    private ReopenPostingPeriodCommandHandler ReopenHandler() => new(_shops, _periods, _executor, _unitOfWork, _clock);

    [Fact]
    public async Task Close_ByNonOwner_IsForbiddenAndNeverReachesTheExecutor()
    {
        _shops.GetActiveRoleAsync(_actor, _shop.Id, Arg.Any<CancellationToken>()).Returns(ShopRole.Manager);

        var result = await CloseHandler().HandleAsync(new ClosePostingPeriodCommand(_shop.Id, _actor, June, "done", "k"), default);

        Assert.Equal("PostingPeriod.OwnerRequired", result.FirstError.Code);
        await _executor.DidNotReceiveWithAnyArgs().ExecuteAsync<PostingPeriodDto>(default!, default, default, default!, default!, default);
    }

    [Fact]
    public async Task Close_RunsUnderTheCloseScopeAndRecordsTheEvent()
    {
        var result = await CloseHandler().HandleAsync(new ClosePostingPeriodCommand(_shop.Id, _actor, June, " done ", " k "), default);

        Assert.False(result.IsError);
        Assert.Equal("Closed", result.Value.State);
        await _executor.Received(1).ExecuteAsync(
            Arg.Is<FinancialOperationScope>(s => s.ShopId == _shop.Id && s.OperationType == FinancialOperationType.PostingPeriodClose && s.Key == "k"),
            _actor, _shop.Id, Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<ErrorOr<PostingPeriodDto>>>>(), Arg.Any<CancellationToken>());
        await _periods.Received(1).EnsureExistsAsync(_shop.Id, June, new DateOnly(2026, 6, 30), Arg.Any<CancellationToken>());
        await _periods.Received(1).AddEventAsync(Arg.Is<PostingPeriodEvent>(e => e.Reason == "done" && e.Action == PostingPeriodAction.Closed), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Close_WhenAuthorityIsLostWhileTheLockIsHeld_IsForbiddenAndWritesNothing()
    {
        _shops.GetActiveRoleAsync(_actor, _shop.Id, Arg.Any<CancellationToken>()).Returns(ShopRole.Owner, ShopRole.Staff);

        var result = await CloseHandler().HandleAsync(new ClosePostingPeriodCommand(_shop.Id, _actor, June, "done", "k"), default);

        Assert.Equal("PostingPeriod.OwnerRequired", result.FirstError.Code);
        Assert.Equal(PostingPeriodState.Open, _period.State);
        await _periods.DidNotReceiveWithAnyArgs().AddEventAsync(default!, default);
    }

    [Theory]
    [InlineData(2026, 6, 2, "done", "k")]
    [InlineData(2026, 6, 1, "  ", "k")]
    [InlineData(2026, 6, 1, "done", " ")]
    public async Task Close_RejectsInvalidInputBeforeAnyWork(int year, int month, int day, string reason, string key)
    {
        var result = await CloseHandler().HandleAsync(
            new ClosePostingPeriodCommand(_shop.Id, _actor, new DateOnly(year, month, day), reason, key), default);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        await _shops.DidNotReceiveWithAnyArgs().GetActiveRoleAsync(default, default, default);
    }

    [Fact]
    public async Task Reopen_UsesTheReopenScopeAndReopensTheClosedMonth()
    {
        Assert.False(_period.Close(_actor, _clock.UtcNow, new DateOnly(2026, 7, 5), "done").IsError);

        var result = await ReopenHandler().HandleAsync(new ReopenPostingPeriodCommand(_shop.Id, _actor, June, "missed", "k2"), default);

        Assert.False(result.IsError);
        Assert.Equal("Open", result.Value.State);
        await _executor.Received(1).ExecuteAsync(
            Arg.Is<FinancialOperationScope>(s => s.OperationType == FinancialOperationType.PostingPeriodReopen),
            _actor, _shop.Id, Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<ErrorOr<PostingPeriodDto>>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestHash_CoversActionPeriodAndTrimmedReason()
    {
        var hashes = new List<string>();
        _executor.ExecuteAsync(
                Arg.Any<FinancialOperationScope>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Do<string>(hashes.Add),
                Arg.Any<Func<CancellationToken, Task<ErrorOr<PostingPeriodDto>>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<PostingPeriodDto>>(PostingPeriodDtoStub()));

        await CloseHandler().HandleAsync(new ClosePostingPeriodCommand(_shop.Id, _actor, June, "done", "k"), default);
        await CloseHandler().HandleAsync(new ClosePostingPeriodCommand(_shop.Id, _actor, June, "  done  ", "other-key"), default);
        await CloseHandler().HandleAsync(new ClosePostingPeriodCommand(_shop.Id, _actor, June, "different", "k"), default);
        await CloseHandler().HandleAsync(new ClosePostingPeriodCommand(_shop.Id, _actor, June.AddMonths(1), "done", "k"), default);
        await ReopenHandler().HandleAsync(new ReopenPostingPeriodCommand(_shop.Id, _actor, June, "done", "k"), default);

        Assert.Equal(hashes[0], hashes[1]);
        Assert.Equal(4, hashes.Distinct().Count());
    }

    [Fact]
    public async Task List_ReportsTwelveMonthsAprilToMarch_UntouchedOnesOpen()
    {
        _shops.GetActiveRoleAsync(_actor, _shop.Id, Arg.Any<CancellationToken>()).Returns(ShopRole.Manager);
        Assert.False(_period.Close(_actor, _clock.UtcNow, new DateOnly(2026, 7, 5), "done").IsError);
        _periods.GetRangeAsync(_shop.Id, new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), Arg.Any<CancellationToken>())
            .Returns([_period]);

        var handler = new GetPostingPeriodsQueryHandler(_shops, _periods, _clock);
        var result = await handler.HandleAsync(new GetPostingPeriodsQuery(_shop.Id, _actor, 2026), default);

        Assert.False(result.IsError);
        Assert.Equal(12, result.Value.Periods.Count);
        Assert.Equal(new DateOnly(2026, 4, 1), result.Value.Periods[0].StartDate);
        Assert.Equal(new DateOnly(2027, 3, 31), result.Value.Periods[11].EndDate);
        Assert.Equal("Closed", result.Value.Periods[2].State);
        Assert.All(result.Value.Periods.Where((_, i) => i != 2), p => Assert.Equal("Open", p.State));
    }

    [Fact]
    public async Task List_DefaultsToTheFiscalYearOfToday_AndRefusesStaff()
    {
        var handler = new GetPostingPeriodsQueryHandler(_shops, _periods, _clock);
        _clock.Today(Arg.Any<string>()).Returns(new DateOnly(2027, 2, 10));
        _shops.GetActiveRoleAsync(_actor, _shop.Id, Arg.Any<CancellationToken>()).Returns(ShopRole.Owner);

        var current = await handler.HandleAsync(new GetPostingPeriodsQuery(_shop.Id, _actor, 0), default);
        Assert.Equal(2026, current.Value.FiscalYearStart);

        _shops.GetActiveRoleAsync(_actor, _shop.Id, Arg.Any<CancellationToken>()).Returns(ShopRole.Staff);
        var refused = await handler.HandleAsync(new GetPostingPeriodsQuery(_shop.Id, _actor, 2026), default);
        Assert.Equal("PostingPeriod.OwnerOrManagerRequired", refused.FirstError.Code);
    }

    private static PostingPeriodDto PostingPeriodDtoStub() => new(June, new DateOnly(2026, 6, 30), "Closed", null, null, []);
}
