using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Checkout clock + local simulator payments + the real signed entitlement transport with manual pumps.
/// Licensing and merchant each keep their own PostgreSQL database.
/// </summary>
internal sealed class P0302Fixture(P0213Fixture delivery, P0201Clock checkout) : IAsyncDisposable
{
    internal static readonly DateTimeOffset WholeSecondStart = Utc("2027-02-10T00:00:01Z");

    internal static readonly Dictionary<string, string?> SimulatorSettings = new()
    {
        ["Payments:Mode"] = "LocalSimulator",
        ["Payments:ReconcileInBackground"] = "false",
    };

    /// <summary>The runtime grant step for tables a test re-creates by migrating down and up again.</summary>
    internal const string RuntimeGrant = """
        GRANT SELECT, INSERT, UPDATE, DELETE ON public.checkout_orders, public.checkout_order_shops,
            public.checkout_order_acceptances, public.payment_attempts, public.payment_attempt_sessions,
            public.payment_events, public.verified_payments, public.payment_fulfilments, public.payment_fulfilment_shops,
            public.payment_fulfilment_deliveries, public.development_gateway_sessions, public.development_gateway_payments,
            public.development_gateway_controls, public.manual_payment_receipts, public.checkout_trial_sources,
            public.paid_fresh_starts, public.payment_trial_conversions, public.payment_restoration_holds,
            public.service_restoration_adjustments
            TO licensing_runtime;
        GRANT EXECUTE ON FUNCTION public.claim_pending_payment(timestamptz,uuid) TO licensing_runtime;
        GRANT EXECUTE ON FUNCTION public.lock_pending_payment(uuid,timestamptz) TO licensing_runtime;
        GRANT EXECUTE ON FUNCTION public.finish_pending_payment(uuid,timestamptz,boolean) TO licensing_runtime;
        GRANT EXECUTE ON FUNCTION public.complete_pending_payment() TO licensing_runtime;
        """;

    internal P0213Fixture Delivery { get; } = delivery;
    internal P0210Fixture Trial => Delivery.Trial;
    internal Hosts Hosts => Delivery.Hosts;
    internal P0201Clock CheckoutClock { get; } = checkout;

    internal static async Task<P0302Fixture> StartAsync(LicensingPostgreSqlFixture database, P0201Clock checkout,
        IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null, bool browser = false)
    {
        var delivery = await P0213Fixture.StartAsync(database, browser, settings ?? SimulatorSettings, collection =>
        {
            collection.AddKeyedSingleton<TimeProvider>(CheckoutOrdersV1.Policy, checkout);
            collection.AddKeyedSingleton<TimeProvider>("pending-payment-lease", checkout);
            services?.Invoke(collection);
        }, prepareProvider: true, businessClock: checkout);
        await LicensingPostgreSqlFixture.ExecuteAsync(delivery.Hosts.LicensingOwner, RuntimeGrant);
        return new(delivery, checkout);
    }

    /// <summary>One billing owner, <paramref name="count"/> really affiliated shops and an accepted monthly quote.</summary>
    internal async Task<Purchase> AcceptedQuoteAsync(int count = 3, string[]? names = null, bool legacyQuote = false,
        long monthlyPricePaise = 12000L)
    {
        var payer = await SignInAsync(Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var shops = new List<Merchant> { payer };
        for (var index = 1; index < count; index++)
            shops.Add(await SignInAsync(Hosts, ShopRole.Owner));
        if (names is not null)
            await NameFixtureShopsAsync(shops, names);
        foreach (var shop in shops)
            await P0213Fixture.LinkAsync(account, payer, shop);
        await Delivery.DrainAsync(source: true);
        await Delivery.DrainAsync(source: false);
        var plan = await P0301Tests.PublishPlanAsync(Trial, monthlyPricePaise: monthlyPricePaise);
        var ids = shops.Select(shop => shop.ShopId).ToArray();
        var quote = await P0301Tests.CreateQuoteAsync(payer.Client, account,
            new(Guid.NewGuid(), plan, "Month", ids, Math.Max(0, count - 3), 1));
        if (legacyQuote) quote = await P0309Fixture.RepresentLegacyQuoteAsync(Hosts.LicensingOwner, quote);
        using var accepted = await payer.Client.PostAsJsonAsync(P0301Tests.AcceptPath(quote), P0301Tests.Accept(quote));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        return new(payer, account, [.. shops], (await accepted.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!);
    }

    private async Task NameFixtureShopsAsync(List<Merchant> shops, string[] names)
    {
        Assert.Equal(shops.Count, names.Length);
        await using var connection = new NpgsqlConnection(Delivery.MerchantDb.ConnectionString);
        await connection.OpenAsync();
        foreach (var (shop, name) in shops.Zip(names))
        {
            await using var update = new NpgsqlCommand("UPDATE public.shops SET name = $1 WHERE id = $2", connection);
            update.Parameters.AddWithValue(name);
            update.Parameters.AddWithValue(shop.ShopId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }
    }

    internal static string PaymentPath(CheckoutQuoteV1 quote) => $"{P0301Tests.OrdersPath(quote.AccountId)}/{quote.OrderId:D}/payment";
    internal static string AttemptsPath(CheckoutQuoteV1 quote) => $"{PaymentPath(quote)}/attempts";
    internal static string VerifyPath(CheckoutQuoteV1 quote, Guid attempt) => $"{AttemptsPath(quote)}/{attempt:D}/verify";
    internal static string CapturePath(CheckoutQuoteV1 quote, Guid attempt) => $"{AttemptsPath(quote)}/{attempt:D}/simulator/capture";

    internal static object Start(CheckoutQuoteV1 quote, Guid? key = null) =>
        new { operationKey = key ?? Guid.NewGuid(), expectedRevision = quote.Revision, snapshotHash = quote.SnapshotHash };

    internal static async Task<JsonElement> StartAsync(HttpClient client, CheckoutQuoteV1 quote, HttpStatusCode expected,
        Guid? key = null)
    {
        using var response = await client.PostAsJsonAsync(AttemptsPath(quote), Start(quote, key));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {body}");
        Assert.True(response.Headers.CacheControl?.NoStore);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    internal static Task<JsonElement> CaptureAsync(HttpClient client, CheckoutQuoteV1 quote, Guid attempt) =>
        PostAsync(client, CapturePath(quote, attempt), new { outcome = "Captured" });

    internal static Task<JsonElement> VerifyAsync(HttpClient client, CheckoutQuoteV1 quote, Guid attempt,
        string? launcherOutcome = null) =>
        PostAsync(client, VerifyPath(quote, attempt), new { launcherOutcome });

    internal static async Task<JsonElement> StatusAsync(HttpClient client, CheckoutQuoteV1 quote)
    {
        using var response = await client.GetAsync(new Uri(PaymentPath(quote), UriKind.Relative));
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await ReadAsync(response);
    }

    internal static async Task<JsonElement> PostAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await ReadAsync(response);
    }

    internal async Task<long> CountAsync(string table, string where = "true") =>
        (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(Hosts.LicensingOwner,
            $"SELECT count(*) FROM public.{table} WHERE {where}"))!;

    internal static void AssertStatus(JsonElement status, string payment, string fulfilment, string access)
    {
        Assert.Equal(payment, status.GetProperty("payment").GetString());
        Assert.Equal(fulfilment, status.GetProperty("fulfilment").GetString());
        Assert.Equal(access, status.GetProperty("access").GetString());
    }

    public ValueTask DisposeAsync() => Delivery.DisposeAsync();

    internal sealed record Purchase(Merchant Payer, Guid Account, Merchant[] Shops, CheckoutQuoteV1 Quote);
}
