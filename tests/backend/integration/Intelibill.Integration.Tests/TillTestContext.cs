using ErrorOr;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Tills;
using Intelibill.Application.Features.Tills.Commands.CreateTill;
using Intelibill.Application.Features.Tills.Commands.OpenTill;
using Intelibill.Application.Features.Tills.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Options;
using Intelibill.Infrastructure.Repositories;
using Intelibill.Infrastructure.Services.Operations;
using Intelibill.Infrastructure.Services.Posting;
using Intelibill.Infrastructure.Services.Tills;
using Intelibill.Infrastructure.Services.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Intelibill.Integration.Tests;

internal sealed class TillTestContext : IAsyncDisposable
{
    public ApplicationDbContext Db { get; }
    public TillRepository Tills { get; }
    public TillPostingService Posting { get; }
    public TillLifecycleService Lifecycle { get; }
    public TillReadService Reads { get; }
    public PostingPeriodPolicy Periods { get; }
    public TillSessionOptions Options { get; }
    public TillTestClock Time { get; }
    public IBusinessClock Clock { get; }

    public TillTestContext(string connection, Guid shopId, TillTestClock? time = null, TillSessionOptions? options = null)
    {
        Time = time ?? new();
        Clock = new BusinessClock(Time);
        Options = options ?? new() { Enabled = true, AllowedShopIds = [shopId] };
        Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);
        Tills = new(Db);
        var shops = new ShopRepository(Db);
        var policy = new TillSessionPolicy(Microsoft.Extensions.Options.Options.Create(Options), shops);
        var receipts = new OperationReceiptRepository(Db);
        var builder = new TillDtoBuilder(Tills);
        Posting = new(Tills, shops, new BankAccountRepository(Db), policy, Clock);
        Periods = new(Db, new PostingPeriodRepository(Db));
        Lifecycle = new(Tills, shops, new UserRepository(Db), receipts, policy, Posting, new FinancialOperationExecutor(Db), Periods,
            new UnitOfWork(Db), Clock, builder);
        Reads = new(Tills, shops, receipts, policy, Clock, builder);
    }

    public static async Task<(Guid ShopId, Guid Owner, Guid Cashier, Guid Other)> SeedAsync(string connection)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);
        var shop = Shop.Create("Till lifecycle", "address", "city", "state", "560001", null, null, null);
        var owner = User.CreateWithEmail($"till-owner-{Guid.NewGuid():N}@example.test", "hash", "Owner", "Name");
        var cashier = User.CreateWithEmail($"till-staff-{Guid.NewGuid():N}@example.test", "hash", "Cashier", "Name");
        var other = User.CreateWithEmail($"till-other-{Guid.NewGuid():N}@example.test", "hash", "Other", "Name");
        db.AddRange(shop, owner, cashier, other, ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, false),
            ShopMembership.Create(shop.Id, cashier.Id, ShopRole.Staff, false), ShopMembership.Create(shop.Id, other.Id, ShopRole.Staff, false));
        await db.SaveChangesAsync();
        return (shop.Id, owner.Id, cashier.Id, other.Id);
    }

    public async Task<TillDto> CreateAsync(Guid shop, Guid owner, string name = "Front") =>
        Success(await Lifecycle.CreateAsync(new(shop, owner, name, Guid.NewGuid().ToString()), default));
    public async Task<TillSessionDto> OpenAsync(Guid shop, Guid cashier, Guid till) =>
        Success(await Lifecycle.OpenAsync(new(shop, cashier, till, cashier, 1000m, Guid.NewGuid().ToString()), default));
    public static T Success<T>(ErrorOr<T> result)
    {
        Assert.False(result.IsError, result.IsError ? string.Join(",", result.Errors.Select(x => x.Code)) : string.Empty);
        return result.Value;
    }
    public ValueTask DisposeAsync() => Db.DisposeAsync();
}

internal sealed class TillTestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 8, 6, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
