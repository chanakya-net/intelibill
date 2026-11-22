using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Options;
using Intelibill.Infrastructure.Services.Time;
using Microsoft.Extensions.Options;
using Intelibill.Infrastructure.Billing;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class P0413bLicensingTests(PostgreSqlTestFixture fixture, ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    private readonly TestClock _clock = new(Deadline.AddMinutes(-1));
    private static readonly DateTimeOffset Deadline = EntitlementSnapshotsV1.Instant(TimeProvider.System).AddMinutes(1);
    private string Migrator => fixture.MigratorConnectionString;
    private static CancellationToken Ct => CancellationToken.None;

    private ApiWebApplicationFactory CreateHost() => new(fixture, services =>
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(_clock);
        services.RemoveAll<IBusinessClock>();
        services.AddSingleton<IBusinessClock>(new BusinessClock(_clock));
        foreach (var worker in services.Where(x => x.ServiceType == typeof(IHostedService) &&
            (x.ImplementationType == typeof(LeasedHttpDeliveryWorker) ||
             x.ImplementationType == typeof(ShopAffiliationDeliveryWorker) ||
             x.ImplementationType == typeof(TrialActivationDeliveryWorker))).ToList())
            services.Remove(worker);
    }, runtimeIdentityOutsideRequests: true);

    private static HttpClient CreateClient(ApiWebApplicationFactory host) =>
        host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<Guid> CreateShopAsync(HttpClient client)
    {
        await RegisterAsync(client);
        using var created = await client.PostAsJsonAsync("/api/shops", new
        {
            name = "P04-13b merchant", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001",
            taxProfile = new { stateCode = "29", taxRegime = "Unregistered", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" },
        }, Ct);
        var token = (await ReadSuccessAsync(created)).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return Guid.Parse(new JwtSecurityToken(token).Claims.Single(x => x.Type == "active_shop_id").Value);
    }

    private static async Task RegisterAsync(HttpClient client, string? email = null)
    {
        using var enroll = await client.EnrollAsync(new { email = email ?? $"p0413b-{Guid.NewGuid():N}@fixture.test",
            password = "Pass123!Aa", firstName = "Test", lastName = "Owner" });
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(enroll)).GetProperty("accessToken").GetString());
    }

    private static async Task AddMemberAsync(ApiWebApplicationFactory host, HttpClient owner, HttpClient member,
        Guid shop, string role)
    {
        var email = $"p0413b-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@fixture.test";
        await RegisterAsync(member, email);
        using var created = await owner.PostAsJsonAsync($"/api/shops/{shop}/invitations", new { email, role }, Ct);
        await ReadSuccessAsync(created);
        var invitation = await IdentityTestMailbox.ReadInvitationAsync(host, email);
        using var accepted = await member.PostAsJsonAsync($"/api/invitations/{invitation.Id}/accept",
            new { token = invitation.Token }, Ct);
        await ReadSuccessAsync(accepted);
        using var scoped = await member.PostAsJsonAsync("/api/shops/switch", new { shopId = shop }, Ct);
        member.DefaultRequestHeaders.Authorization = new("Bearer",
            (await ReadSuccessAsync(scoped)).GetProperty("accessToken").GetString());
    }

    private async Task<Binding> BindAsync(Guid shop, Guid? account = null)
    {
        await using var db = Open(Migrator);
        var membership = await db.ShopMemberships.SingleAsync(x => x.ShopId == shop && x.Role == ShopRole.Owner, Ct);
        var binding = new Binding(shop, account ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var now = _clock.Now;
        foreach (var kind in new[] { "Granted", "Accepted" })
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.shop_affiliation_consent_events (id, shop_id, account_id, request_id,
                    actor_id, membership_id, authority_version, operation_key, kind, recorded_at, term_id,
                    shop_name, owner_name, request_expires_at)
                VALUES ({Guid.NewGuid()}, {shop}, {binding.Account}, {binding.Request},
                    {membership.UserId}, {membership.Id}, {membership.AuthorityVersion}, {Guid.NewGuid()}, {kind}, {now},
                    {binding.Term}, {"P04-13b merchant"}, {"Fixture Owner"}, {now.AddDays(15)})
                """, Ct);
        return binding;
    }

    private async Task EnforceAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"""
            UPDATE public.shop_license_states SET enforcement_activated_at = {_clock.Now}
            WHERE shop_id = {shop}
            """, Ct));
    }

    private async Task ApplyAsync(Binding binding, DateTimeOffset end, long revision = 1,
        string restriction = "Unrestricted",
        ApplicationDbContext? connection = null, DateTimeOffset? effective = null, long? released = null)
    {
        await using var owned = connection is null ? Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator)) : null;
        var db = connection ?? owned!;
        var now = _clock.Now;
        var suspension = restriction == "Unrestricted" ? null
            : new CustomerSuspensionV1(restriction, effective ?? now, "Fixture account restriction");
        var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.Account, binding.Shop, binding.Request, new(revision, now, restriction, released, suspension),
            new(revision, now, "Active", binding.Term, "Paid", now.AddDays(-1), end, end, 2, 3));
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(Guid.NewGuid(), "test-key", "test-public-key", now, now.AddMinutes(1),
            SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        var fence = new ShopLicenceFence(db, new LicenceActionPolicy(), _clock);
        var result = await new EntitlementProjectionStore(db, _clock, fence).ApplyAsync(snapshot, body, proof, Ct);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        var acknowledgement = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(result.Value, EntitlementSnapshotsV1.Json);
        Assert.True(acknowledgement?.Outcome == "Applied", acknowledgement?.Disposition);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.True(status == response.StatusCode,
                $"Expected {(int)status} {code}, received {(int)response.StatusCode}.");
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(code, (body.TryGetProperty("code", out var stableCode) ? stableCode : body.GetProperty("title")).GetString());
        }
    }

    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static ApplicationDbContext Open(string connection) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);

    private static SaleCancellationOptions Rollout(ApiWebApplicationFactory host) =>
        host.Services.GetRequiredService<IOptionsMonitor<SaleCancellationOptions>>().CurrentValue;

    private async Task<Receipt> SeedReceiptAsync(ApiWebApplicationFactory host, HttpClient client,
        PaymentMethod method = PaymentMethod.Cash, InvoiceDocumentType documentType = InvoiceDocumentType.Receipt,
        DateTimeOffset? soldAt = null, bool service = false, bool offline = false, decimal tax = 0m,
        decimal paid = 80m)
    {
        var shop = await CreateShopAsync(client);
        var actor = Guid.Parse(new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter).Subject);
        const decimal cost = 60m;
        await using var db = Open(Migrator);
        var customer = Customer.Create(shop, "Buyer", Guid.NewGuid().ToString("N")[..10], null);
        var item = Item.Create(shop, "Sugar", null, "kg", Guid.NewGuid().ToString("N"), true, actor);
        var batch = InventoryBatch.Create(shop, item.Id, "LOT", 8m, cost, 100m, 100m, 0m, false, null, null, null, actor).Value;
        var stock = Inventory.Create(shop, item.Id, 8m, 0m, 100m, actor).Value;
        var now = soldAt ?? _clock.Now;
        var total = 200m + 2 * tax;
        var soldService = service ? Service.Create(shop, "SUGAR", "Frozen sugar", null, 100m, null,
            tax, false, true, actor) : null;
        if (soldService is not null) db.Add(soldService);
        var line = new SaleLineInput(shop, service ? SaleLineType.Service : SaleLineType.Goods,
            service ? null : item.Id, service ? null : batch.Id, soldService?.Id, "Frozen sugar", "SUGAR",
            1m, service ? 0m : cost, 100m, service ? 0m : 100m, tax, false, false);
        var sale = Sale.Record(shop, actor, Guid.NewGuid().ToString("N"), "HASH", "A/2627/000001", [line, line], customer.Id, "Buyer", null, method, paid, total - paid, now, BusinessDay.InDefaultZone(now)).Value;
        sale.AttachInvoiceDocument(new(2, documentType, now, sale.BusinessDate, 2026, "A", sale.InvoiceNumber,
            new("Frozen seller", "Shop", "Address", "29", "Karnataka", null, TaxRegime.Unregistered), new("Frozen buyer", null, null, null, null, null), "29", "Karnataka", false, false,
            sale.Items.Select((x, i) => new InvoiceDocumentLine(i + 1, "Frozen sugar", null, "KGS", x.Quantity, 100m, 0m, 100m, tax, tax == 0m, [], x.TotalAmount)).ToArray(),
            new(0m, 200m, [], 2 * tax, 0m, total), [InvoiceDeclaration.NotRegisteredUnderGst], InvoiceSignatureMode.AuthorisedSignatory, "component-split/1"));
        db.AddRange(customer, item, batch, stock, sale);
        if (offline) db.Entry(sale).Property(x => x.Source).CurrentValue = SaleSource.Offline;
        if (paid < total) db.Add(CustomerLedgerEntry.Create(shop, customer.Id, sale.Id, CustomerLedgerEntryType.SaleDue, total - paid, sale.BusinessDate, null, actor).Value);
        await db.SaveChangesAsync();
        var rollout = Rollout(host);
        rollout.Enabled = true;
        rollout.AllowedShopIds = [.. rollout.AllowedShopIds, shop];
        return new(shop, actor, sale, batch.Id);
    }
    private sealed record Receipt(Guid Shop, Guid Actor, Sale Sale, Guid Batch)
    {
        public string Path => $"/api/sales/{Sale.Id}";
        public object Command(string key = "cancel-892", bool goods = true, bool cash = true, uint? version = null,
            string reason = "Recovered all goods") =>
            new { operationKey = key, expectedVersion = version ?? Sale.Version, reason,
                goodsRecoveredConfirmed = goods, cashRefundConfirmed = cash };
    }

    private sealed record Binding(Guid Shop, Guid Account, Guid Request, Guid Term);
    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private long _ticks = now.UtcTicks;
        public DateTimeOffset Now
        {
            get => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _ticks, value.UtcTicks);
        }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
