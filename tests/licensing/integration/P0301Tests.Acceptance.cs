using System.Net;
using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0301Tests
{
    [Fact]
    public async Task ExpiredUnacceptedQuoteFailsButAcceptedReplayRetainsOriginalFacts()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await StartCheckoutAsync(database, clock);
        var (payer, account, command) = await PrepareAsync(fixture);
        var expired = await CreateQuoteAsync(payer.Client, account, command);
        var accepted = await CreateQuoteAsync(payer.Client, account, command with { OperationKey = Guid.NewGuid() });
        await RetirePlanAsync(fixture, accepted);
        using var unavailable = await payer.Client.PostAsJsonAsync(OrdersPath(account), command with { OperationKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, unavailable.StatusCode);
        clock.Now = accepted.ExpiresAt.AddMicroseconds(-1);
        var acceptance = Accept(accepted);
        using var confirmed = await payer.Client.PostAsJsonAsync(AcceptPath(accepted), acceptance);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var receipt = (await confirmed.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!;
        Assert.Equal(accepted with { Acceptance = receipt.Acceptance }, receipt);
        Assert.Equal(clock.Now, receipt.Acceptance!.AcceptedAt);

        clock.Now = expired.ExpiresAt;
        using var tooLate = await payer.Client.PostAsJsonAsync(AcceptPath(expired), Accept(expired));
        Assert.Equal(HttpStatusCode.Conflict, tooLate.StatusCode);
        Assert.Equal(CheckoutOrdersV1.Expired, await P0206Fixture.CodeAsync(tooLate));
        clock.Now = expired.ExpiresAt.AddMicroseconds(1);
        using var replay = await payer.Client.PostAsJsonAsync(AcceptPath(accepted), acceptance);
        Assert.Equal(receipt, await replay.Content.ReadFromJsonAsync<CheckoutQuoteV1>());
        using var changed = await payer.Client.PostAsJsonAsync(AcceptPath(accepted), acceptance with { ExpectedRevision = 2 });
        Assert.Equal("Billing.OperationPayloadMismatch", await P0206Fixture.CodeAsync(changed));
        using var changedCreate = await payer.Client.PostAsJsonAsync(OrdersPath(account), command with { Period = "Year" });
        Assert.Equal("Billing.OperationPayloadMismatch", await P0206Fixture.CodeAsync(changedCreate));
        Assert.Equal(1L, await fixture.CountAsync("checkout_order_acceptances"));
        Assert.Equal(0L, await fixture.CountAsync("subscription_term_facts"));
    }

    [Fact]
    public async Task QuoteCreationBudgetRefusesNextRequestWithoutRetainingAnOrder()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await StartCheckoutAsync(database, clock);
        var (payer, account, command) = await PrepareAsync(fixture);

        for (var attempt = 0; attempt < 30; attempt++)
            await CreateQuoteAsync(payer.Client, account, command with { OperationKey = Guid.NewGuid() });

        using var limited = await payer.Client.PostAsJsonAsync(OrdersPath(account),
            command with { OperationKey = Guid.NewGuid() });
        var retained = await fixture.CountAsync("checkout_orders");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(30L, retained);
    }

    [Fact]
    public async Task FirstAcceptanceRequiresConfirmationAndMatchingReviewFacts()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await StartCheckoutAsync(database, clock);
        var (payer, account, command) = await PrepareAsync(fixture);
        var quote = await CreateQuoteAsync(payer.Client, account, command);
        var path = AcceptPath(quote);
        var differentHash = (quote.SnapshotHash[0] == '0' ? "1" : "0") + quote.SnapshotHash[1..];
        var refusals = new[]
        {
            (Accept(quote) with { Confirmed = false }, HttpStatusCode.BadRequest, CheckoutOrdersV1.Invalid),
            (Accept(quote) with { SnapshotHash = differentHash }, HttpStatusCode.Conflict, CheckoutOrdersV1.SourceChanged),
            (Accept(quote) with { CustomerTermsVersion = "different-terms" }, HttpStatusCode.Conflict, CheckoutOrdersV1.SourceChanged),
            (Accept(quote) with { RefundPolicyVersion = "different-refund" }, HttpStatusCode.Conflict, CheckoutOrdersV1.SourceChanged),
        };

        foreach (var (request, status, code) in refusals)
        {
            using var refused = await payer.Client.PostAsJsonAsync(path, request);
            Assert.Equal(status, refused.StatusCode);
            Assert.Equal(code, await P0206Fixture.CodeAsync(refused));
        }
        Assert.Equal(0L, await fixture.CountAsync("checkout_order_acceptances"));

        var correct = Accept(quote);
        using var accepted = await payer.Client.PostAsJsonAsync(path, correct);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var receipt = await accepted.Content.ReadFromJsonAsync<CheckoutQuoteV1>();
        Assert.Equal(correct.OperationKey, receipt?.Acceptance?.OperationKey);
        Assert.Equal(1L, await fixture.CountAsync("checkout_order_acceptances"));
    }

    internal static async Task RetirePlanAsync(P0210Fixture fixture, CheckoutQuoteV1 quote)
    {
        var manager = fixture.CatalogueManager!;
        using var retired = await manager.SendAsync(HttpMethod.Post,
            $"/api/admin/plans/{quote.PlanId:D}/versions/{quote.PlanVersionId:D}/retire",
            new { operationKey = Guid.NewGuid(), expectedVersion = 2, reason = "Local checkout fixture retirement", confirmed = true },
            await P0206Fixture.ProofAsync(manager));
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
    }

    internal static Task<P0210Fixture> StartCheckoutAsync(LicensingPostgreSqlFixture db, P0201Clock clock, bool browser = false) =>
        P0210Fixture.StartAsync(db, browser: browser, setup: new P0210HostSetup(
            new Dictionary<string, string?>(), new Dictionary<string, string?>(),
            services => services.AddKeyedSingleton<TimeProvider>(CheckoutOrdersV1.Policy, clock), _ => { }));

    internal static async Task<(Merchant Payer, Guid Account, CreateCheckoutQuoteV1 Command)> PrepareAsync(P0210Fixture fixture)
    {
        var payer = await SignInAsync(fixture.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        var shop = await SignInAsync(fixture.Hosts, Intelibill.Domain.Enums.ShopRole.Owner);
        await fixture.LinkAsync(account, payer.UserId, shop);
        var plan = await PublishPlanAsync(fixture);
        return (payer, account, new(Guid.NewGuid(), plan, "Month", [shop.ShopId], 0, 1));
    }

    internal static async Task<CheckoutQuoteV1> CreateQuoteAsync(HttpClient client, Guid account, CreateCheckoutQuoteV1 request)
    {
        using var result = await client.PostAsJsonAsync(OrdersPath(account), request);
        Assert.True(result.StatusCode == HttpStatusCode.Created, await result.Content.ReadAsStringAsync());
        return (await result.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!;
    }

    internal static string OrdersPath(Guid account) => $"/api/billing/accounts/{account:D}/orders";
    internal static string AcceptPath(CheckoutQuoteV1 quote) => $"{OrdersPath(quote.AccountId)}/{quote.OrderId:D}/accept";
    internal static AcceptCheckoutQuoteV1 Accept(CheckoutQuoteV1 quote) => new(Guid.NewGuid(), quote.Revision, quote.SnapshotHash,
        quote.CustomerTermsVersion, quote.RefundPolicyVersion, true);
}
