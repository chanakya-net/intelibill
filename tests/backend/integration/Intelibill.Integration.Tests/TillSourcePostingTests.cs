using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Application.Features.Tills;
using static Intelibill.Integration.Tests.TillTestContext;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class TillSourcePostingTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task EnabledSale_RequiresExplicitSession_BeforeAnyPosting()
    {
        using var factory = new ApiWebApplicationFactory(fixture);
        await factory.InitializeAsync();
        using var client = await OpenShopAsync(factory);
        var (shop, service, _) = await SeedSaleAsync(factory, client);
        factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value.Enabled = true;
        factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value.AllowedShopIds = [shop];
        using var response = await client.PostAsJsonAsync("/api/sales", Request(service));
        Assert.Contains("Till.SessionRequired", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Sales.AnyAsync(x => x.ShopId == shop));
    }

    [Fact]
    public async Task CashTenderedFiveHundredForThreeHundred_ChangeTwoHundred()
    {
        await using var setup = await ArrangeAsync();
        var request = setup.Request();
        using var response = await setup.Client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("saleId").GetGuid();
        Assert.Equal(setup.Session.Id, json.GetProperty("tillSessionId").GetGuid());
        Assert.Equal(500m, json.GetProperty("tenderLines")[0].GetProperty("cashTendered").GetDecimal());
        var movement = await setup.App.Db.TillMovements.SingleAsync(x => x.ShopId == setup.Shop);
        Assert.Equal(id, movement.SourceId);
        Assert.Equal(300m, movement.SignedAmount);
        Assert.Equal(200m, movement.Change);
        Assert.Equal(ReceiptTenderMethod.Cash, movement.Method);
        var detail = Success(await setup.App.Reads.SessionAsync(setup.Shop, setup.Actor, setup.Session.Id, 1, 20, default));
        Assert.Equal(1300m, detail.Session.ExpectedCash);
        Assert.Equal(id, Assert.Single(detail.Sources.Items).SourceId);
        var sale = await setup.App.Db.Sales.SingleAsync(x => x.Id == id);
        Assert.Equal(setup.Session.Id, sale.TillSessionId);
        Assert.Equal(300m, Assert.Single(sale.TenderLines!).AppliedAmount);
        Assert.Equal(300m, sale.InvoiceDocument!.Totals.InvoiceTotal);
        using var fetched = await setup.Client.GetAsync($"/api/sales/{id}");
        fetched.EnsureSuccessStatusCode();
        var fetchedSale = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(setup.Session.Id, fetchedSale.GetProperty("tillSessionId").GetGuid());
        Assert.Equal(json.GetProperty("tenderLines").GetRawText(), fetchedSale.GetProperty("tenderLines").GetRawText());
        // A source movement changes xmin, so the opening version cannot close over this sale.
        var stale = await setup.App.Lifecycle.CloseAsync(new(setup.Shop, setup.Actor, setup.Session.Id, 1300m, null, setup.Session.Version, "stale-close"), default);
        Assert.Equal("Till.VersionConflict", stale.FirstError.Code);
    }

    [Fact]
    public async Task AllDueSale_AttributedWithoutCash()
    {
        await using var setup = await ArrangeAsync();
        var customer = Customer.Create(setup.Shop, "Credit customer", "+919222222222", null, true);
        setup.App.Db.Customers.Add(customer);
        await setup.App.Db.SaveChangesAsync();
        var request = setup.Request();
        request["customerId"] = customer.Id;
        request["paymentMethod"] = "Credit";
        request["paidAmount"] = 0m;
        request["dueAmount"] = 300m;
        request["tenderLines"] = new JsonArray();
        using var response = await setup.Client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var source = await setup.App.Db.TillSourceRecords.SingleAsync(x => x.ShopId == setup.Shop);
        Assert.Equal(300m, source.Settlement.ReceivableChange);
        Assert.Equal(0m, source.Settlement.StoreCreditRedeemed);
        Assert.False(await setup.App.Db.TillMovements.AnyAsync(x => x.ShopId == setup.Shop));
        Assert.Equal(1000m, Success(await setup.App.Reads.SessionAsync(setup.Shop, setup.Actor, setup.Session.Id, 1, 20, default)).Session.ExpectedCash);
    }

    [Fact]
    public async Task MixedTenderDisabled_RejectsMultipleExternalLines()
    {
        await using var setup = await ArrangeAsync();
        var request = setup.Request();
        request["tenderLines"] = JsonNode.Parse("[{\"method\":\"Cash\",\"appliedAmount\":200},{\"method\":\"Upi\",\"appliedAmount\":100}]");
        using var response = await setup.Client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Mixed tender is unavailable.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await setup.App.Db.Sales.AnyAsync(x => x.ShopId == setup.Shop));
        Assert.False(await setup.App.Db.TillSourceRecords.AnyAsync(x => x.ShopId == setup.Shop));
    }

    [Fact]
    public async Task TillEvidenceChanged_SameKeyConflicts_AndReplaySurvivesCloseAndDisable()
    {
        await using var setup = await ArrangeAsync();
        var request = setup.Request();
        using var posted = await setup.Client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        var saved = await posted.Content.ReadFromJsonAsync<JsonElement>();
        setup.App.Db.ChangeTracker.Clear();
        var current = Success(await setup.App.Reads.SessionAsync(setup.Shop, setup.Actor, setup.Session.Id, 1, 20, default)).Session;
        setup.App.Time.Now = DateTimeOffset.UtcNow;
        Success(await setup.App.Lifecycle.CloseAsync(new(setup.Shop, setup.Actor, current.Id, 1300m, null, current.Version, "closed"), default));
        setup.Factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value.Enabled = false;
        using var replay = await setup.Client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var recovered = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(saved.GetProperty("saleId").GetGuid(), recovered.GetProperty("saleId").GetGuid());
        Assert.Equal(saved.GetProperty("tenderLines").GetRawText(), recovered.GetProperty("tenderLines").GetRawText());
        request["till"]!["overrideReason"] = "different evidence";
        using var conflict = await setup.Client.PostAsJsonAsync("/api/sales", request);
        Assert.Contains("Sale.IdempotencyConflict", await conflict.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single(await setup.App.Db.TillMovements.Where(x => x.ShopId == setup.Shop).ToArrayAsync());
        request["idempotencyKey"] = Guid.NewGuid().ToString();
        using var disabled = await setup.Client.PostAsJsonAsync("/api/sales", request);
        Assert.Contains("Till.Disabled", await disabled.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitTenderWithoutTill_DisabledRefusesRatherThanDroppingEvidence()
    {
        await using var setup = await ArrangeAsync();
        setup.Factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value.Enabled = false;
        var request = setup.Request();
        request.Remove("till");
        using var refused = await setup.Client.PostAsJsonAsync("/api/sales", request);
        Assert.Contains("Till.Disabled", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        request.Remove("tenderLines");
        using var legacy = await setup.Client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.Created, legacy.StatusCode);
        Assert.False(await setup.App.Db.TillSourceRecords.AnyAsync(x => x.ShopId == setup.Shop));
    }

    [Fact]
    public async Task SaleRollback_RollsBackTill()
    {
        await using var setup = await ArrangeAsync();
        var request = setup.Request();
        request["idempotencyKey"] = "till-source-rollback-test";
        // Fail after the immutable movement insert, so real source/till writes share the rollback.
        await setup.App.Db.Database.ExecuteSqlRawAsync("CREATE FUNCTION fail_sale_source_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.operation_key = 'till-source-rollback-test' THEN RAISE EXCEPTION 'test source rollback'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_sale_source_test AFTER INSERT ON till_movements FOR EACH ROW EXECUTE FUNCTION fail_sale_source_test();");
        try
        {
            using var failed = await setup.Client.PostAsJsonAsync("/api/sales", request);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.False(await setup.App.Db.Sales.AnyAsync(x => x.ShopId == setup.Shop));
            Assert.False(await setup.App.Db.TillMovements.AnyAsync(x => x.ShopId == setup.Shop));
            Assert.False(await setup.App.Db.TillSourceRecords.AnyAsync(x => x.ShopId == setup.Shop));
            setup.App.Db.ChangeTracker.Clear();
            var session = await setup.App.Db.TillSessions.SingleAsync(x => x.Id == setup.Session.Id);
            Assert.Equal(0, session.SourceCount);
            Assert.Equal(0, session.LastMovementSequence);
            Assert.Equal(setup.Session.Version, session.Version);
        }
        finally
        {
            await setup.App.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_sale_source_test ON till_movements; DROP FUNCTION fail_sale_source_test();");
        }
        using var retried = await setup.Client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.Single(await setup.App.Db.TillSourceRecords.Where(x => x.ShopId == setup.Shop).ToArrayAsync());
        Assert.Single(await setup.App.Db.TillMovements.Where(x => x.ShopId == setup.Shop).ToArrayAsync());
    }

    [Fact]
    public async Task CloseWins_SaleWaitsThenRefusesWithoutLateMovement()
    {
        await using var setup = await ArrangeAsync();
        await using var blocker = new TillTestContext(setup.Factory.MigratorConnectionString, setup.Shop, new TillTestClock { Now = DateTimeOffset.UtcNow });
        await using var tx = await blocker.Db.Database.BeginTransactionAsync();
        Success(await blocker.Periods.AuthorizeForWriteAsync(setup.Shop, setup.Session.BusinessDate));
        var session = Success(await blocker.Posting.AcquireAsync(setup.Shop, setup.Actor, new(setup.Session.Id), setup.Session.BusinessDate))!;
        var totals = Intelibill.Domain.ValueObjects.TillTotals.From([], []);
        var close = Success(session.Close(totals, 1000m, setup.Actor, DateTimeOffset.UtcNow, setup.Session.BusinessDate, null, false, false));
        blocker.Db.TillCloses.Add(close);
        await blocker.Db.SaveChangesAsync();
        var posting = setup.Client.PostAsJsonAsync("/api/sales", setup.Request());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await setup.App.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE wait_event_type='Lock' AND query LIKE '%FROM till_sessions%' AND pid <> pg_backend_pid()) AS \"Value\"").SingleAsync(timeout.Token))
            await Task.Delay(10, timeout.Token);
        Assert.False(posting.IsCompleted);
        await tx.CommitAsync();
        using var response = await posting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("Till.SessionClosed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await setup.App.Db.Sales.AnyAsync(x => x.ShopId == setup.Shop));
        Assert.False(await setup.App.Db.TillMovements.AnyAsync(x => x.ShopId == setup.Shop));
        Assert.Equal(1000m, (await setup.App.Db.TillCloses.SingleAsync(x => x.SessionId == setup.Session.Id)).ExpectedCash);
    }

    [Fact]
    public async Task SaleEvidence_AdditiveMigration_PreservesRlsAndTenantForeignKey()
    {
        await using var setup = await ArrangeAsync();
        using var posted = await setup.Client.PostAsJsonAsync("/api/sales", setup.Request());
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        var sale = await setup.App.Db.Sales.SingleAsync(x => x.ShopId == setup.Shop);
        var (foreignShop, foreignOwner, _, _) = await SeedAsync(setup.Factory.MigratorConnectionString);
        await using var foreign = new TillTestContext(setup.Factory.MigratorConnectionString, foreignShop, new TillTestClock { Now = DateTimeOffset.UtcNow });
        var foreignSession = await foreign.OpenAsync(foreignShop, foreignOwner, (await foreign.CreateAsync(foreignShop, foreignOwner)).Id);
        var fk = await Assert.ThrowsAsync<PostgresException>(() => setup.App.Db.Database.ExecuteSqlAsync($"UPDATE sales SET till_session_id={foreignSession.Id} WHERE id={sale.Id}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, fk.SqlState);
        var shape = await Assert.ThrowsAsync<PostgresException>(() => setup.App.Db.Database.ExecuteSqlAsync($"UPDATE sales SET tender_lines=NULL WHERE id={sale.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, shape.SqlState);
        await using var runtime = new NpgsqlConnection(setup.Factory.RuntimeConnectionString);
        await runtime.OpenAsync();
        await using (var scope = new NpgsqlCommand("SELECT set_config('app.active_shop_id', @shop, false)", runtime))
        {
            scope.Parameters.AddWithValue("shop", foreignShop.ToString());
            await scope.ExecuteNonQueryAsync();
        }
        await using (var hidden = new NpgsqlCommand("SELECT till_session_id, tender_lines FROM sales WHERE id=@id", runtime))
        {
            hidden.Parameters.AddWithValue("id", sale.Id);
            await using var reader = await hidden.ExecuteReaderAsync();
            Assert.False(await reader.ReadAsync());
        }
        await using (var ownScope = new NpgsqlCommand("SELECT set_config('app.active_shop_id', @shop, false)", runtime))
        {
            ownScope.Parameters.AddWithValue("shop", setup.Shop.ToString());
            await ownScope.ExecuteNonQueryAsync();
        }
        await using var visible = new NpgsqlCommand("SELECT till_session_id FROM sales WHERE id=@id", runtime);
        visible.Parameters.AddWithValue("id", sale.Id);
        Assert.Equal(setup.Session.Id, (Guid)(await visible.ExecuteScalarAsync())!);
    }

    private async Task<SaleSetup> ArrangeAsync()
    {
        var factory = new ApiWebApplicationFactory(fixture);
        await factory.InitializeAsync();
        var client = await OpenShopAsync(factory);
        var (shop, service, actor) = await SeedSaleAsync(factory, client);
        var options = factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        options.Enabled = true; options.AllowedShopIds = [shop];
        var app = new TillTestContext(factory.MigratorConnectionString, shop, new TillTestClock { Now = DateTimeOffset.UtcNow });
        var session = await app.OpenAsync(shop, actor, (await app.CreateAsync(shop, actor)).Id);
        return new(factory, client, app, shop, service, actor, session);
    }

    private sealed record SaleSetup(ApiWebApplicationFactory Factory, HttpClient Client, TillTestContext App, Guid Shop, Guid Service, Guid Actor, TillSessionDto Session) : IAsyncDisposable
    {
        public JsonObject Request()
        {
            var request = JsonSerializer.SerializeToNode(TillSourcePostingTests.Request(Service))!.AsObject();
            request["till"] = new JsonObject { ["sessionId"] = Session.Id };
            request["tenderLines"] = JsonNode.Parse("[{\"method\":\"Cash\",\"appliedAmount\":300,\"cashTendered\":500,\"change\":200}]");
            return request;
        }
        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }

    private static object Request(Guid service) => new
    {
        idempotencyKey = Guid.NewGuid().ToString(), paymentMethod = "Cash", paidAmount = 300m, dueAmount = 0m,
        items = new[] { new { barcode = "SERVICE", batchNumber = "", itemName = "Repair", quantity = 1m, costPrice = 0m,
            salesPrice = 300m, mrp = 0m, taxRatePercent = 0m, isPriceIncludingTax = false,
            inventoryBatchId = Guid.Empty, lineType = "Service", serviceId = service } }
    };

    private static async Task<HttpClient> OpenShopAsync(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var registered = await client.EnrollAsync(new { email = $"till-sale-{Guid.NewGuid():N}@example.test", password = "Pass123!Aa", firstName = "Till", lastName = "Owner" });
        registered.EnsureSuccessStatusCode();
        var auth = await registered.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        using var created = await client.PostAsJsonAsync("/api/shops", new { name = "Till Sales", address = "Address", city = "City", state = "State", pincode = "560001" });
        created.EnsureSuccessStatusCode();
        var scope = await created.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<(Guid Shop, Guid Service, Guid Actor)> SeedSaleAsync(ApiWebApplicationFactory factory, HttpClient client)
    {
        using var me = await client.GetAsync("/api/shops/me");
        me.EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // The fixture connection is a schema-owner scope for setup; HTTP still uses the restricted runtime role.
        var shop = await db.Shops.OrderByDescending(x => x.CreatedAt).FirstAsync(x => x.Name == "Till Sales");
        var actor = (await db.ShopMemberships.SingleAsync(x => x.ShopId == shop.Id)).UserId;
        Assert.False(shop.DeclareTaxProfile(null, "29", TaxRegime.Unregistered, null, null, EInvoiceStatus.BelowThreshold, actor, DateTimeOffset.UtcNow).IsError);
        var service = Service.Create(shop.Id, "SERVICE", "Repair", null, 300m, null, 0m, false, true, actor);
        db.Services.Add(service);
        await db.SaveChangesAsync();
        return (shop.Id, service.Id, actor);
    }
}
