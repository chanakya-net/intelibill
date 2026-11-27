using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0301Tests
{
    private static readonly JsonSerializerOptions CheckoutJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CheckoutContractPinsPathsCanonicalHashesAndExpiry()
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/checkout-orders-v1.json")));
        var input = corpus.RootElement;
        var account = input.GetProperty("accountId").GetGuid();
        var order = input.GetProperty("orderId").GetGuid();
        var paths = input.GetProperty("paths");
        var hashes = input.GetProperty("hashes");
        var create = input.GetProperty("create").Deserialize<CreateCheckoutQuoteV1>()!;
        var accept = input.GetProperty("accept").Deserialize<AcceptCheckoutQuoteV1>()!;
        var evaluation = input.GetProperty("evaluation");
        Assert.True(create.IsValid());
        Assert.True(accept.IsValid());
        Assert.Equal(CheckoutOrdersV1.Policy, input.GetProperty("policy").GetString());
        Assert.Equal(paths.GetProperty("create").GetString(), CheckoutOrdersV1.Path(account));
        Assert.Equal(paths.GetProperty("accept").GetString(), CheckoutOrdersV1.AcceptPath(account, order));
        Assert.Equal(paths.GetProperty("recover").GetString(), CheckoutOrdersV1.OperationPath(account, "accept", accept.OperationKey));
        Assert.Equal(paths.GetProperty("shops").GetString(), CheckoutOrdersV1.ShopsPath(account, order, create.ShopIds![0]));
        Assert.Equal(hashes.GetProperty("create").GetString(), CheckoutOrdersV1.CreateHash(create));
        Assert.Equal(CheckoutOrdersV1.CreateHash(create), CheckoutOrdersV1.CreateHash(create with { ShopIds = [.. create.ShopIds.Reverse()] }));
        Assert.Equal(hashes.GetProperty("accept").GetString(), CheckoutOrdersV1.AcceptHash(order, accept));
        Assert.Equal(hashes.GetProperty("shops").GetString(), CheckoutOrdersV1.ShopHash(create.ShopIds));
        var expiry = evaluation.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.Equal(evaluation.GetProperty("createdAt").GetDateTimeOffset().AddMinutes(15), expiry);
        Assert.Equal(expiry.AddMicroseconds(-1), evaluation.GetProperty("latestAcceptedAt").GetDateTimeOffset());
        Assert.Equal(evaluation.GetProperty("expiresAt").GetString(), CheckoutOrdersV1.Instant(expiry));
    }

    [Fact]
    public void MaximumSizeShopPageFitsRelayLimit()
    {
        var escaped = string.Concat(Enumerable.Repeat("\u2603", CheckoutOrdersV1.MaxShopName));
        var shops = Enumerable.Range(0, CheckoutOrdersV1.PageSize)
            .Select(_ => new CheckoutShopV1(Guid.NewGuid(), escaped, Guid.NewGuid())).OrderBy(x => x.ShopId).ToArray();
        var page = new CheckoutShopPageV1(Guid.NewGuid(), Guid.NewGuid(), shops, shops[^1].ShopId);
        var data = JsonSerializer.SerializeToUtf8Bytes(page, CheckoutJson);
        Assert.InRange(data.Length, CheckoutOrdersV1.PageSize * CheckoutOrdersV1.MaxShopName * 6, CheckoutOrdersV1.MaxPageBytes);
    }

    [Fact]
    public async Task RelayRejectsMalformedQuoteFacts()
    {
        await database.PrepareMerchantAsync();
        using var signing = new P0201Fixture(database, "unused-checkout-validation");
        signing.Clock.Now = P0202Fixture.WholeSecondNow();
        var account = Guid.NewGuid();
        var order = Guid.NewGuid();
        var quote = SampleQuote(account, order);
        var response = JsonSerializer.Serialize(quote, CheckoutJson);
        await using var merchant = signing.MerchantApi(handler: () => new QuoteTransport(() => response));
        var email = P0202Fixture.UniqueEmail();
        await P0201Tests.SeedMerchantAsync(merchant.Services, email);
        using var client = merchant.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await P0201Tests.LoginAsync(client, email));
        var path = $"{OrdersPath(account)}/{order:D}";
        using var valid = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        foreach (var (key, value) in new (string, JsonNode?)[]
        {
            ("amountPaise", 0), ("currency", "USD"), ("shopCount", 4), ("expiresAt", quote.CreatedAt),
            ("customerTermsVersion", null), ("snapshotHash", new string('f', 64)),
        })
        {
            var malformed = JsonNode.Parse(JsonSerializer.Serialize(quote, CheckoutJson))!;
            malformed[key] = value;
            response = malformed.ToJsonString();
            using var rejected = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
            Assert.True(rejected.Headers.CacheControl?.NoStore);
        }
    }

    private static CheckoutQuoteV1 SampleQuote(Guid account, Guid order)
    {
        var time = P0210Fixture.Start;
        var quote = new CheckoutQuoteV1(account, order, 1, Guid.NewGuid(), Guid.NewGuid(), 1, "Fixture", "Month", null,
            3, 5, 1, CheckoutOrdersV1.ShopHash([Guid.NewGuid()]), new string('0', 64), 27000, "INR", "TaxInclusiveFinal",
            "customer-local-1", "refund-local-1", "Asia/Kolkata", "calendar-month-year/1", "renewal/1", "Fulfilment",
            time, time.AddMonths(1), 0, "2027-02-10T05:30:00", 0, time, time.AddMinutes(15), true, "");
        return quote with { SnapshotHash = CheckoutOrdersV1.SnapshotHash(quote) };
    }

    private sealed class QuoteTransport(Func<string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(), Encoding.UTF8, "application/json") });
    }
}
