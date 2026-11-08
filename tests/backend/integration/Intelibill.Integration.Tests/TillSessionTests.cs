using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Application.Features.Tills;
using Intelibill.Application.Features.Tills.Commands.RecordTillCashMovement;
using Intelibill.Application.Features.Tills.Commands.CloseTill;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Intelibill.Integration.Tests.TillTestContext;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class TillSessionTests(PostgreSqlTestFixture fixture)
{
    private static readonly string[] SessionErrorCodes = ["Till.SessionNotFound", "Till.CashierMismatch", "Till.BusinessDateMismatch", "Till.SessionClosed"];
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CloseReplay_AuthorizesRequestedOverdueModeAgainstCurrentRole(bool overdue)
    {
        var (shop, owner, _, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var session = await app.OpenAsync(shop, owner, (await app.CreateAsync(shop, owner)).Id);
        if (overdue) app.Time.Now = app.Time.Now.AddDays(1);
        var command = new Intelibill.Application.Features.Tills.Commands.CloseTill.CloseTillCommand(shop, owner, session.Id, 1000m,
            overdue ? "Forgotten drawer counted" : null, session.Version, "role-replay", FinalizeOverdue: overdue);
        Success(await app.Lifecycle.CloseAsync(command, default));
        await app.Db.Database.ExecuteSqlAsync($"UPDATE shop_memberships SET role=3 WHERE shop_id={shop} AND user_id={owner}");
        app.Db.ChangeTracker.Clear();
        app.Time.Now = app.Time.Now.AddDays(1);
        var replay = await app.Lifecycle.CloseAsync(command, default);
        if (overdue) Assert.Equal("Till.Forbidden", replay.FirstError.Code);
        else Assert.Equal(session.Id, Success(replay).Id);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task RemovedCashier_ManagerMayFinalizeButCannotTrade(int status)
    {
        var (shop, owner, cashier, other) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var session = await app.OpenAsync(shop, cashier, (await app.CreateAsync(shop, owner)).Id);
        await app.Db.Database.ExecuteSqlAsync($"UPDATE shop_memberships SET status={status} WHERE shop_id={shop} AND user_id={cashier}");
        app.Db.ChangeTracker.Clear();
        Assert.Equal("Till.OverrideReasonRequired", (await app.Lifecycle.CloseAsync(new(shop, owner, session.Id, 1000m, null, session.Version, "missing-override"), default)).FirstError.Code);
        Assert.Equal("Till.Forbidden", (await app.Lifecycle.OpenAsync(new(shop, owner, session.TillId, cashier, 0m, "inactive-open", "Cashier left"), default)).FirstError.Code);
        var closed = await app.Lifecycle.CloseAsync(new(shop, owner, session.Id, 1000m, null, session.Version, "finalize-removed", "Cashier left; counted drawer"), default);
        var saved = Success(closed);
        Assert.Equal(cashier, saved.Close!.CashierId);
        Assert.Equal(session.CashierName, saved.Close.CashierName);
        Assert.Equal(session.BusinessDate, saved.Close.BusinessDate);
        Assert.Equal("Cashier left; counted drawer", saved.Close.OverrideReason);
        Assert.Equal("Till.Forbidden", (await app.Lifecycle.MoveAsync(new(shop, owner, session.Id, 1m, 1m, "No trading", saved.Version, "blocked-trading", "Cashier left"), default)).FirstError.Code);
        Assert.Equal("Till.Forbidden", (await app.Lifecycle.CloseAsync(new(shop, cashier, session.Id, 1000m, null, session.Version, "inactive-replay"), default)).FirstError.Code);
        Assert.Equal("Till.CashierMismatch", (await app.Lifecycle.CloseAsync(new(shop, other, session.Id, 1000m, null, session.Version, "staff-override", "Cashier left"), default)).FirstError.Code);
    }

    [Fact]
    public async Task DisabledLegacy_NoTillEffects()
    {
        using var factory = new ApiWebApplicationFactory(fixture);
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetService<ITillPostingService>();
        Assert.NotNull(service);
        var result = await service.AcquireAsync(Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 11, 8));
        Assert.False(result.IsError);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task WrongTillOrCashier_CannotMutateSession()
    {
        var (shop, owner, cashier, other) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var till = await app.CreateAsync(shop, owner);
        var session = await app.OpenAsync(shop, cashier, till.Id);
        var wrongCashier = await app.Lifecycle.MoveAsync(new(shop, other, session.Id, 1m, 10m, "float", session.Version, "other"), default);
        Assert.Equal("Till.CashierMismatch", wrongCashier.FirstError.Code);
        var wrongShop = await app.Lifecycle.CloseAsync(new(Guid.NewGuid(), owner, session.Id, 1000m, null, session.Version, "wrong-shop"), default);
        Assert.Equal("Till.Forbidden", wrongShop.FirstError.Code);
        var wrongTill = await app.Lifecycle.OpenAsync(new(shop, cashier, Guid.NewGuid(), cashier, 0m, "wrong-till"), default);
        Assert.Equal("Till.NotFound", wrongTill.FirstError.Code);
        var lookup = await app.Reads.OperationAsync(shop, other, "open", (await app.Db.OperationReceipts.SingleAsync(x => x.OperationType == FinancialOperationType.TillOpen && x.ShopId == shop)).OperationKey, default);
        Assert.Equal("Till.Forbidden", lookup.FirstError.Code);
        Assert.Equal("Till.Forbidden", (await app.Reads.SessionAsync(shop, other, session.Id, 1, 20, default)).FirstError.Code);
        Assert.Empty(Success(await app.Reads.SessionsAsync(shop, other, null, null, 1, 20, default)).Items);
    }

    [Fact]
    public async Task ManagerOverride_RequiresReason()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var till = await app.CreateAsync(shop, owner);
        var missing = await app.Lifecycle.OpenAsync(new(shop, owner, till.Id, cashier, 1000m, "override-open"), default);
        Assert.Equal("Till.OverrideReasonRequired", missing.FirstError.Code);
        var session = await app.OpenAsync(shop, cashier, till.Id);
        var move = await app.Lifecycle.MoveAsync(new(shop, owner, session.Id, 1m, 10m, "float", session.Version, "override-move"), default);
        Assert.Equal("Till.OverrideReasonRequired", move.FirstError.Code);
        var posted = Success(await app.Lifecycle.MoveAsync(new(shop, owner, session.Id, 1m, 10m, "float", session.Version, "override-move", "cashier assistance"), default));
        var detail = Success(await app.Reads.SessionAsync(shop, owner, session.Id, 1, 20, default));
        Assert.Equal(1010m, posted.ExpectedCash);
        Assert.Equal("cashier assistance", Assert.Single(detail.Sources.Items).OverrideReason);
        Assert.Equal("override-move", Assert.Single(detail.Sources.Items).OperationKey);
        Assert.Equal("float", Assert.Single(detail.Sources.Items).Reason);
    }

    [Fact]
    public async Task MovementChangesVersion_StaleCloseRejected()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var till = await app.CreateAsync(shop, owner);
        var session = await app.OpenAsync(shop, cashier, till.Id);
        var moved = Success(await app.Lifecycle.MoveAsync(new(shop, cashier, session.Id, -1m, 300m, "bank drop", session.Version, "move"), default));
        Assert.NotEqual(session.Version, moved.Version);
        var close = await app.Lifecycle.CloseAsync(new(shop, cashier, session.Id, 700m, null, session.Version, "close"), default);
        Assert.Equal("Till.VersionConflict", close.FirstError.Code);
        Assert.Empty(await app.Db.TillCloses.Where(x => x.ShopId == shop).ToArrayAsync());
        Assert.Equal(700m, moved.ExpectedCash);
    }

    [Fact]
    public async Task StaffVarianceClose_RequiresReview()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var session = await app.OpenAsync(shop, cashier, (await app.CreateAsync(shop, owner)).Id);
        var closed = Success(await app.Lifecycle.CloseAsync(new(shop, cashier, session.Id, 990m, "short ten", session.Version, "count"), default));
        Assert.True(closed.AwaitingReview);
        Assert.True(closed.Close!.AwaitingReview);
        Assert.Equal(-10m, closed.Close.Variance);
        Assert.Equal("Till.Forbidden", (await app.Lifecycle.ReviewAsync(new(shop, cashier, session.Id, "review", closed.Version, "staff-review"), default)).FirstError.Code);
        var reviewed = Success(await app.Lifecycle.ReviewAsync(new(shop, owner, session.Id, "count acknowledged", closed.Version, "review"), default));
        Assert.False(reviewed.AwaitingReview);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(closed.Close), JsonSerializer.SerializeToElement(reviewed.Close)));
        Assert.Equal(closed.Version, reviewed.Version);
        app.Options.Enabled = false;
        var replayed = Success(await app.Lifecycle.ReviewAsync(new(shop, owner, session.Id, "count acknowledged", closed.Version, "review"), default));
        Assert.Equal(reviewed.Review, replayed.Review);
        Assert.Equal(1, await app.Db.TillCloseReviews.CountAsync(x => x.ShopId == shop));
    }

    [Fact]
    public async Task OverdueTradeRejected_ManagerFinalizesOriginalOpenPeriod()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var session = await app.OpenAsync(shop, cashier, (await app.CreateAsync(shop, owner)).Id);
        app.Time.Now = app.Time.Now.AddDays(1);
        Assert.Equal("Till.Overdue", (await app.Lifecycle.MoveAsync(new(shop, cashier, session.Id, 1m, 10m, "float", session.Version, "late-move"), default)).FirstError.Code);
        Assert.Equal("Till.Forbidden", (await app.Lifecycle.CloseAsync(new(shop, cashier, session.Id, 1000m, "late count", session.Version, "late-staff", FinalizeOverdue: true), default)).FirstError.Code);
        var closed = Success(await app.Lifecycle.CloseAsync(new(shop, owner, session.Id, 1000m, "late count", session.Version, "late-manager", "cashier absent", true), default));
        Assert.True(closed.Close!.IsOverdue);
        Assert.Equal(session.BusinessDate, closed.Close.BusinessDate);
        Assert.True(closed.Close.ClosedAt > session.OpenedAt);
    }

    [Fact]
    public async Task DisabledExplicitTill_Refused()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var session = await app.OpenAsync(shop, cashier, (await app.CreateAsync(shop, owner)).Id);
        app.Options.Enabled = false;
        Assert.Equal("Till.Disabled", (await app.Posting.AcquireAsync(shop, cashier, new(session.Id), session.BusinessDate)).FirstError.Code);
        Assert.Equal("Till.Disabled", (await app.Lifecycle.MoveAsync(new(shop, cashier, session.Id, 1m, 10m, "float", session.Version, "disabled-move"), default)).FirstError.Code);
        var capabilities = Success(await app.Reads.CapabilitiesAsync(shop, cashier, default));
        Assert.False(capabilities.Enabled);
        Assert.False(capabilities.MixedTenderEnabled);
    }

    [Fact]
    public async Task ReplayAfterCloseAndDisable_ReturnsOriginal()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var till = await app.CreateAsync(shop, owner);
        var session = await app.OpenAsync(shop, cashier, till.Id);
        var request = new RecordTillCashMovementCommand(shop, cashier, session.Id, 1m, 100m, "float", session.Version, "move-replay");
        var moved = Success(await app.Lifecycle.MoveAsync(request, default));
        var closeRequest = new CloseTillCommand(shop, cashier, session.Id, 1100m, null, moved.Version, "close-replay");
        var closed = Success(await app.Lifecycle.CloseAsync(closeRequest, default));
        app.Options.Enabled = false;
        var replay = Success(await app.Lifecycle.MoveAsync(request, default));
        Assert.Equal(moved.Id, replay.Id);
        Assert.Equal(moved.Version, replay.Version);
        Assert.Equal(moved.State, replay.State);
        Assert.Equal(moved.ExpectedCash, replay.ExpectedCash);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(closed.Close), JsonSerializer.SerializeToElement(Success(await app.Lifecycle.CloseAsync(closeRequest, default)).Close)));
        var lookup = Success(await app.Reads.OperationAsync(shop, cashier, "cash-movement", "move-replay", default));
        Assert.Equal(moved.Version, Assert.IsType<TillSessionDto>(lookup.Response).Version);
        Assert.Equal("Operation.PayloadMismatch", (await app.Lifecycle.MoveAsync(request with { Amount = 101m }, default)).FirstError.Code);
        Assert.Equal(1, await app.Db.TillMovements.CountAsync(x => x.ShopId == shop));
    }

    [Fact]
    public async Task Posting_ValidatesWholeSourceBeforeStagingAndAttributesAllDueAudit()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var dto = await app.OpenAsync(shop, cashier, (await app.CreateAsync(shop, owner)).Id);
        await using var tx = await app.Db.Database.BeginTransactionAsync();
        Success(await app.Periods.AuthorizeForWriteAsync(shop, dto.BusinessDate));
        var session = Success(await app.Posting.AcquireAsync(shop, cashier, new(dto.Id), dto.BusinessDate))!;
        var invalid = await app.Posting.AppendAsync(session, new(TillMovementSource.OnlineSale, Guid.NewGuid(), "sale",
            [new(ReceiptTenderMethod.Cash, 10m, 10m, 1m, null, null)], 1m, app.Clock.UtcNow, "invalid", null), cashier);
        Assert.True(invalid.IsError);
        Assert.Equal(0, session.SourceCount);
        Assert.Equal(0, session.LastMovementSequence);
        Success(await app.Posting.AppendAsync(session, new(TillMovementSource.OnlineSale, Guid.NewGuid(), "sale", [], 1m, app.Clock.UtcNow, "all-due", "credit sale", ReceivableChange: 500m), cashier));
        Assert.Equal(0, await app.Db.TillSourceRecords.CountAsync(x => x.ShopId == shop));
        await app.Db.SaveChangesAsync();
        Assert.NotEqual(dto.Version, session.Version);
        await tx.CommitAsync();
        var detail = Success(await app.Reads.SessionAsync(shop, cashier, dto.Id, 1, 20, default));
        Assert.Empty(detail.Movements.Items);
        var source = Assert.Single(detail.Sources.Items);
        Assert.Equal("all-due", source.OperationKey);
        Assert.Equal("credit sale", source.Reason);
        Assert.Equal(500m, detail.Session.ReceivableChange);
        Assert.Equal(1000m, detail.Session.ExpectedCash);
    }

    [Fact]
    public async Task Close_FreezesCommittedCashAndRecordedTenderBoundary()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var dto = await app.OpenAsync(shop, cashier, (await app.CreateAsync(shop, owner)).Id);
        await using (var tx = await app.Db.Database.BeginTransactionAsync())
        {
            Success(await app.Periods.AuthorizeForWriteAsync(shop, dto.BusinessDate));
            var session = Success(await app.Posting.AcquireAsync(shop, cashier, new(dto.Id), dto.BusinessDate))!;
            foreach (var (type, amount, direction, method) in new[]
            {
                (TillMovementSource.OnlineSale, 500m, 1m, ReceiptTenderMethod.Cash),
                (TillMovementSource.CustomerPayment, 200m, 1m, ReceiptTenderMethod.Cash),
                (TillMovementSource.SaleReturn, 100m, -1m, ReceiptTenderMethod.Cash),
                (TillMovementSource.Expense, 50m, -1m, ReceiptTenderMethod.Cash),
                (TillMovementSource.ManualCash, 300m, -1m, ReceiptTenderMethod.Cash),
                (TillMovementSource.OnlineSale, 700m, 1m, ReceiptTenderMethod.Upi)
            })
                Success(await app.Posting.AppendAsync(session, new(type, Guid.NewGuid(), type.ToString(),
                    [new(method, amount, null, null, null, null)], direction, app.Clock.UtcNow, Guid.NewGuid().ToString(), "count fixture"), cashier));
            await app.Db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        var current = Success(await app.Reads.SessionAsync(shop, cashier, dto.Id, 1, 20, default)).Session;
        Assert.Equal(1250m, current.ExpectedCash);
        Assert.Equal(700m, current.RecordedTenderTotals[ReceiptTenderMethod.Upi]);
        var closed = Success(await app.Lifecycle.CloseAsync(new(shop, cashier, dto.Id, 1240m, "short ten", current.Version, "fixture-count"), default));
        Assert.Equal(-10m, closed.Close!.Variance);
        Assert.Equal(6, closed.Close.LastMovementSequence);
        Assert.Equal(6, closed.Close.Totals.MovementCount);
        await app.CreateAsync(shop, owner, "Second");
        var next = await app.OpenAsync(shop, cashier, dto.TillId);
        Success(await app.Lifecycle.MoveAsync(new(shop, cashier, next.Id, 1m, 50m, "next float", next.Version, "next-move"), default));
        var historical = Success(await app.Reads.SessionAsync(shop, owner, dto.Id, 1, 20, default));
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(closed.Close), JsonSerializer.SerializeToElement(historical.Session.Close)));
    }

    [Fact]
    public async Task OverdueClose_ClosedOriginalMonthRequiresReopening()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var dto = await app.OpenAsync(shop, cashier, (await app.CreateAsync(shop, owner)).Id);
        app.Time.Now = new DateTimeOffset(2026, 12, 1, 6, 0, 0, TimeSpan.Zero);
        var period = await app.Db.PostingPeriods.SingleAsync(x => x.ShopId == shop);
        Success(period.Close(owner, app.Clock.UtcNow, app.Clock.Today("Asia/Kolkata"), "month ended"));
        await app.Db.SaveChangesAsync();
        var refused = await app.Lifecycle.CloseAsync(new(shop, owner, dto.Id, 1000m, "late count", dto.Version, "overdue-closed", "cashier absent", true), default);
        Assert.Equal("PostingPeriod.Closed", refused.FirstError.Code);
        Assert.Empty(await app.Db.TillCloses.Where(x => x.ShopId == shop).ToArrayAsync());
        period = await app.Db.PostingPeriods.SingleAsync(x => x.ShopId == shop);
        Success(period.Reopen(owner, app.Clock.UtcNow, "finish drawer count"));
        await app.Db.SaveChangesAsync();
        var closed = Success(await app.Lifecycle.CloseAsync(new(shop, owner, dto.Id, 1000m, "late count", dto.Version, "overdue-closed", "cashier absent", true), default));
        Assert.Equal(dto.BusinessDate, closed.Close!.BusinessDate);
    }

    [Fact]
    public async Task SessionErrors_FollowPublicContract()
    {
        var (shop, owner, cashier, other) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var dto = await app.OpenAsync(shop, cashier, (await app.CreateAsync(shop, owner)).Id);
        var missing = await app.Posting.AcquireAsync(shop, cashier, new(Guid.NewGuid()), dto.BusinessDate);
        var differentCashier = await app.Posting.AcquireAsync(shop, other, new(dto.Id), dto.BusinessDate);
        string mismatch;
        await using (var tx = await app.Db.Database.BeginTransactionAsync())
        {
            Success(await app.Periods.AuthorizeForWriteAsync(shop, dto.BusinessDate));
            mismatch = (await app.Posting.AcquireAsync(shop, cashier, new(dto.Id), dto.BusinessDate.AddDays(1))).FirstError.Code;
            await tx.RollbackAsync();
        }
        Success(await app.Lifecycle.CloseAsync(new(shop, cashier, dto.Id, 1000m, null, dto.Version, "error-close"), default));
        await using var closedTx = await app.Db.Database.BeginTransactionAsync();
        Success(await app.Periods.AuthorizeForWriteAsync(shop, dto.BusinessDate));
        var closed = await app.Posting.AcquireAsync(shop, cashier, new(dto.Id), dto.BusinessDate);
        Assert.Equal(SessionErrorCodes,
            new[] { missing.FirstError.Code, differentCashier.FirstError.Code, mismatch, closed.FirstError.Code });
        await closedTx.RollbackAsync();
    }

    [Fact]
    public async Task Api_RuntimeRole_CreatesOpensMovesAndClosesWithTypedRecovery()
    {
        using var factory = new ApiWebApplicationFactory(fixture);
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var registered = await client.EnrollAsync(new { email = $"till-api-{Guid.NewGuid():N}@example.test", password = "Pass123!Aa", firstName = "Till", lastName = "Owner" });
        registered.EnsureSuccessStatusCode();
        var auth = await registered.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        var createdShop = await client.PostAsJsonAsync("/api/shops", new { name = "Till API", address = "Address", city = "City", state = "State", pincode = "560001" });
        createdShop.EnsureSuccessStatusCode();
        var scope = await createdShop.Content.ReadFromJsonAsync<JsonElement>();
        var shopId = scope.GetProperty("activeShopId").GetGuid();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.GetProperty("accessToken").GetString());
        var options = factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        options.Enabled = true; options.AllowedShopIds = [shopId];
        using var dbScope = factory.Services.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<Intelibill.Infrastructure.Data.ApplicationDbContext>();
        var ownerId = (await db.ShopMemberships.SingleAsync(x => x.ShopId == shopId)).UserId;
        var capability = await client.GetFromJsonAsync<JsonElement>("/api/tills/capabilities");
        Assert.True(capability.GetProperty("enabled").GetBoolean());
        var tillResponse = await client.PostAsJsonAsync("/api/tills", new { name = "Front", operationKey = "create" });
        Assert.Equal(HttpStatusCode.OK, tillResponse.StatusCode);
        var till = await tillResponse.Content.ReadFromJsonAsync<TillDto>();
        var opened = await client.PostAsJsonAsync($"/api/tills/{till!.Id}/sessions", new { cashierId = ownerId, openingFloat = 1000m, operationKey = "open" });
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var session = (await opened.Content.ReadFromJsonAsync<TillSessionDto>(jsonOptions))!;
        var closed = await client.PostAsJsonAsync($"/api/tills/sessions/{session.Id}/close", new { countedCash = 1000m, expectedVersion = session.Version, operationKey = "close" });
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var lookup = await client.GetFromJsonAsync<JsonElement>("/api/tills/operations/close/close");
        Assert.Equal(1000m, lookup.GetProperty("response").GetProperty("close").GetProperty("countedCash").GetDecimal());
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/tills/sessions/{session.Id}");
        Assert.Equal(1000m, detail.GetProperty("session").GetProperty("close").GetProperty("expectedCash").GetDecimal());
    }
}
