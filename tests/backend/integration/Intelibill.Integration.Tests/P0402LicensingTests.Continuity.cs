using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using static Intelibill.Application.Common.Licensing.LicenceOutcome;

namespace Intelibill.Integration.Tests;

public sealed partial class P0402LicensingTests
{
    [Theory]
    [InlineData(48, null, "Licence.Expired")]
    [InlineData(24, null, "Licence.Expired")]
    [InlineData(240, 24, "Licence.VerificationUnavailable")]
    [InlineData(240, 48, "Licence.VerificationUnavailable")]
    public async Task Known_end_and_nonterminal_boundary_are_distinct(int endHours, int? boundaryHours, string denial)
    {
        var binding = await SeedAsync();
        await using var db = CreateDb();
        await ApplyAsync(db, binding, VerifiedAt, VerifiedAt, VerifiedAt.AddHours(endHours),
            boundary: boundaryHours is { } hours ? VerifiedAt.AddHours(hours) : null);

        var boundary = VerifiedAt.AddHours(boundaryHours ?? endHours);
        await AssertTradingAsync(binding, boundary.AddMicroseconds(-1), Allowed);
        await AssertTradingAsync(binding, boundary, Blocked, denial);
        await AssertTradingAsync(binding, VerifiedAt.AddHours(endHours), Blocked, denial);
    }

    [Fact]
    public async Task Newer_shop_head_cannot_refresh_account_proof_or_clear_its_restriction()
    {
        var binding = await SeedAsync();
        await using (var db = CreateDb())
            await ApplyAsync(db, binding, VerifiedAt, VerifiedAt, VerifiedAt.AddDays(10));

        _clock.Now = VerifiedAt.AddHours(48);
        await using (var db = CreateDb())
            await ApplyAsync(db, binding, VerifiedAt, _clock.Now, VerifiedAt.AddDays(10), shopRevision: 2);
        await AssertTradingAsync(binding, _clock.Now, Blocked, "Licence.VerificationUnavailable");

        await using (var db = CreateDb())
            await ApplyAsync(db, binding, _clock.Now, _clock.Now, VerifiedAt.AddDays(10), 3, "Security", 2);
        await using (var db = CreateDb())
            await ApplyAsync(db, binding, VerifiedAt, _clock.Now, VerifiedAt.AddDays(10), shopRevision: 4);
        await AssertTradingAsync(binding, _clock.Now, Blocked, "Licence.SecuritySuspended");
    }

    [Fact]
    public async Task Warm_sale_stock_snapshot_restart_and_replay_preserve_authority()
    {
        _clock.Now = EntitlementSnapshotsV1.Instant(TimeProvider.System);
        using var signer = RSA.Create(2048);
        var transport = new FixtureTransport();
        using var merchantKey = RSA.Create(2048);
        using var factory = MerchantHost(signer, transport, merchantKey);
        await factory.InitializeAsync();
        using var client = CreateClient(factory);
        var (shop, token) = await EnrollShopAsync(client);
        var binding = await BindShopAsync(shop);
        var verified = _clock.Now.AddHours(-47).AddMinutes(-59);
        var body = SnapshotBody(binding, verified, _clock.Now.AddDays(10));
        var receipt = await PostSnapshotAsync(client, signer, body);
        var initialProof = await ReadProofAsync(binding);
        var batch = await SeedStockAsync(client);
        using var commercial = await client.GetAsync($"/api/licensing/shops/{shop:D}/status", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, commercial.StatusCode);
        var callsBefore = transport.Calls;
        Assert.Equal(1, callsBefore);

        await AssertTradingAsync(binding, _clock.Now, Allowed);
        await PostSaleAsync(client, batch);
        await AssertTradingAsync(binding, _clock.Now, Allowed);
        using var snapshot = await client.GetAsync("/api/sales/offline-snapshot/stream", Ct);
        var lines = (await snapshot.Content.ReadAsStringAsync(Ct)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(HttpStatusCode.OK, snapshot.StatusCode);
        using var complete = JsonDocument.Parse(lines[^1]);
        Assert.Equal("complete", complete.RootElement.GetProperty("type").GetString());
        await AssertSaleStoredAsync(binding.ShopId, batch.BatchId);
        Assert.Equal(callsBefore, transport.Calls);

        await factory.DisposeAsync();
        using var restarted = MerchantHost(signer, transport, merchantKey);
        using var afterRestart = CreateClient(restarted);
        afterRestart.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _clock.Now = verified.AddHours(48);
        Assert.Equal(receipt, await PostSnapshotAsync(afterRestart, signer, body));
        Assert.Equal(initialProof, await ReadProofAsync(binding));
        await AssertTradingAsync(binding, verified.AddHours(48), Blocked, "Licence.VerificationUnavailable");
        Assert.Equal(callsBefore, transport.Calls);
    }

    private ApiWebApplicationFactory MerchantHost(RSA signer, FixtureTransport transport, RSA? merchantSigner = null)
    {
        using var billingSigner = RSA.Create(2048);
        var billingPrivatePem = billingSigner.ExportPkcs8PrivateKeyPem();
        var factory = new ApiWebApplicationFactory(fixture, services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_clock);
            services.AddSingleton<IOptionsMonitor<EntitlementTransportOptions>>(
                new FixtureOptionsMonitor<EntitlementTransportOptions>(new()
                {
                    Enabled = true, PeerOrigin = "https://licensing.fixture.test",
                    LocalKeyId = "p0402-merchant", LocalPrivateKeyPem = merchantSigner?.ExportPkcs8PrivateKeyPem(),
                    PublicKeys = new() { ["p0402-source"] = signer.ExportSubjectPublicKeyInfoPem() },
                }));
            if (merchantSigner is not null)
                services.AddSingleton<IOptionsMonitor<MerchantBillingDelegationOptions>>(
                    new FixtureOptionsMonitor<MerchantBillingDelegationOptions>(new()
                    {
                        LicensingOrigin = "https://licensing.fixture.test", LocalKeyId = "p0402-billing",
                        LocalPrivateKeyPem = billingPrivatePem,
                    }));
            foreach (var registration in services.Where(x => x.ServiceType == typeof(IHostedService) &&
                x.ImplementationType == typeof(LeasedHttpDeliveryWorker)).ToList())
                services.Remove(registration);
            foreach (var name in new[] { "MerchantBillingDelegation", LeasedHttpDeliveryPump.ClientName })
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(transport.CreateHandler);
        }, runtimeIdentityOutsideRequests: true);
        return factory;
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });

    private async Task<Binding> BindShopAsync(Guid shop)
    {
        await using var owner = CreateDb(owner: true);
        var membership = await owner.ShopMemberships.SingleAsync(x => x.ShopId == shop, Ct);
        return await BindAsync(owner, membership, "P04-02 merchant");
    }

    private static async Task<(Guid Shop, string Token)> EnrollShopAsync(HttpClient client)
    {
        using var enroll = await client.EnrollAsync(new { email = $"p0402-{Guid.NewGuid():N}@example.test",
            password = "Pass123!Aa", firstName = "Test", lastName = "Owner" });
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(enroll)).GetProperty("accessToken").GetString());
        using var created = await client.PostAsJsonAsync("/api/shops", new
        {
            name = "P04-02 merchant", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001",
            taxProfile = new { stateCode = "29", taxRegime = "Unregistered", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" },
        }, Ct);
        var token = (await ReadSuccessAsync(created)).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(token);
        return (Guid.Parse(jwt.Claims.Single(x => x.Type == "active_shop_id").Value), token);
    }

    private static async Task<(string Barcode, Guid BatchId)> SeedStockAsync(HttpClient client)
    {
        var barcode = Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = "Fixture stock", barcode, uom = "pcs", batchNumber = "LOT", quantity = 10m,
            totalPurchaseCost = 10m, mrp = 2m, salesPrice = 2m, taxRatePercent = 0m, taxIncluded = false, purchaseTaxIncluded = false,
        }, Ct);
        return (barcode, (await ReadSuccessAsync(response)).GetProperty("inventoryBatchId").GetGuid());
    }

    private static async Task PostSaleAsync(HttpClient client, (string Barcode, Guid BatchId) stock)
    {
        using var response = await client.PostAsJsonAsync("/api/sales", new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}", paymentMethod = (int)PaymentMethod.Cash, paidAmount = 2m, dueAmount = 0m,
            items = new[] { new { barcode = stock.Barcode, batchNumber = "LOT", itemName = "Fixture stock", quantity = 1m,
                costPrice = 1m, salesPrice = 2m, mrp = 2m, taxRatePercent = 0m, isPriceIncludingTax = false,
                inventoryBatchId = stock.BatchId } },
        }, Ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task AssertSaleStoredAsync(Guid shop, Guid batch)
    {
        await using var db = CreateDb();
        await using var tx = await BeginScopedAsync(db, shop);
        Assert.Single(await db.Sales.Where(x => x.ShopId == shop).ToListAsync(Ct));
        Assert.Equal(-1m, await db.StockTransactions.Where(x => x.InventoryBatchId == batch && x.TransactionType == StockTransactionType.Out)
            .Select(x => x.Quantity).SingleAsync(Ct));
    }

    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static byte[] SnapshotBody(Binding binding, DateTimeOffset verified, DateTimeOffset end, long revision = 1) =>
        JsonSerializer.SerializeToUtf8Bytes(new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.AccountId, binding.ShopId, binding.RequestId, new(revision, verified, "Unrestricted", null),
            new(revision, verified, "Active", binding.TermId, "Paid", verified.AddDays(-1), end, end, 2, 3)), EntitlementSnapshotsV1.Json);

    private static byte[] WaitingSnapshotBody(Binding binding, DateTimeOffset verified, string state, DateTimeOffset? boundary) =>
        JsonSerializer.SerializeToUtf8Bytes(new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.AccountId, binding.ShopId, binding.RequestId, new(1, verified, "Unrestricted", null),
            new(1, verified, state, binding.TermId, "Paid", boundary, null, boundary, 2, 3)), EntitlementSnapshotsV1.Json);

    private async Task<byte[]> PostSnapshotAsync(HttpClient client, RSA signer, byte[] body)
    {
        var path = EntitlementSnapshotsV1.SnapshotPath;
        var token = await SignedServiceAssertion.SignAsync(SignedServiceAssertion.LicensingDelivery, "p0402-source", path, body,
            _clock, (input, _) => ValueTask.FromResult(signer.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)), Ct);
        using var response = await LeasedHttpDeliveryPump.PostAsync(client, client.BaseAddress!, path, body, token, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        var receipt = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal("Applied", JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(receipt, EntitlementSnapshotsV1.Json)?.Outcome);
        return receipt;
    }

    private async Task<(Guid AccountProof, Guid ShopProof, DateTimeOffset AccountAt, DateTimeOffset ShopAt)> ReadProofAsync(Binding binding)
    {
        await using var db = CreateDb();
        await using var tx = await BeginScopedAsync(db, binding.ShopId);
        var account = await db.Set<AccountLicenseRestriction>().SingleAsync(x => x.AccountId == binding.AccountId, Ct);
        var shop = await db.Set<ShopLicenseState>().SingleAsync(x => x.ShopId == binding.ShopId, Ct);
        return (account.EvidenceId, shop.EvidenceId, account.UpstreamVerifiedAt, shop.UpstreamVerifiedAt);
    }

    private sealed class FixtureOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class FixtureTransport
    {
        private int _calls;
        public int Calls => _calls;
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } =
            _ => throw new HttpRequestException("Fixture authority unavailable.");

        public HttpMessageHandler CreateHandler() => new Handler(this);

        private sealed class Handler(FixtureTransport transport) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref transport._calls);
                return transport.Respond(request);
            }
        }
    }
}
