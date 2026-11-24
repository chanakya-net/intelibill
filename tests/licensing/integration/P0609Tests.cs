using System.Net;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P06-09: a Billing operator with recent MFA approves a reasoned full or partial refund amount and an explicit access
/// treatment. Real PostgreSQL, real merchant and licensing APIs, local simulator receipts and fixed clocks. Fixture
/// money only: nothing is dispatched to a provider (P06-10 owns dispatch and reconciliation).
/// </summary>
public sealed partial class P0609Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    internal static readonly DateTimeOffset Instant = P0608Tests.RequestedInstant;
    private const string Reason = "Duplicate branch purchased before go-live";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static async Task<P0308Fixture> StartAsync(LicensingPostgreSqlFixture database, DateTimeOffset at,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var fixture = await P0308Fixture.StartAsync(database, at, settings);
        var clock = fixture.Checkout.Trial.Catalogue.Clock;
        SetCredentialTime(fixture, clock.GetUtcNow());
        clock.Advanced += now => SetCredentialTime(fixture, now);
        return fixture;
    }

    internal static void SetCredentialTime(P0308Fixture fixture, DateTimeOffset now)
    {
        using var connection = new Npgsql.NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString);
        connection.Open();
        using var command = new Npgsql.NpgsqlCommand($"""
            CREATE OR REPLACE FUNCTION public.refund_credential_now() RETURNS timestamptz
            LANGUAGE sql VOLATILE SET search_path = pg_catalog AS $$ SELECT '{now:O}'::timestamptz $$;
            """, connection);
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task ApprovePartialWithPreserve()
    {
        await using var fixture = await StartAsync(database, Instant);
        var package = await fixture.PackageAsync(2, P0608Tests.ShopNames);
        var (purchase, term) = await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(fixture);
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        var request = await RequestRefundAsync(package, purchase.OrderId, confirmation, 6000L);
        var before = await P0608Tests.SnapshotAsync(fixture, package, term);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);

        var preview = await PreviewAsync(billing, package.Account, request, 4500L, "Preserve");
        Assert.Equal(12000L, preview.GetProperty("verifiedAmountPaise").GetInt64());
        Assert.Equal(6000L, preview.GetProperty("requestedAmountPaise").GetInt64());
        Assert.Equal(0L, preview.GetProperty("occupiedAmountPaise").GetInt64());
        Assert.Equal(12000L, preview.GetProperty("remainingAmountPaise").GetInt64());
        Assert.Equal(7500L, preview.GetProperty("proposedRemainingAmountPaise").GetInt64());
        Assert.Equal(0L, preview.GetProperty("receiptVersion").GetInt64());
        Assert.False(string.IsNullOrWhiteSpace(preview.GetProperty("refundPolicyVersion").GetString()));
        Assert.Equal(P0608Tests.ShopNames, preview.GetProperty("access").GetProperty("shops").EnumerateArray()
            .Select(shop => shop.GetProperty("shopName").GetString()).Order().ToArray());

        using var approved = await ApproveAsync(billing, package.Account, request, Command(preview, 4500L, "Preserve"));
        var receipt = await ReadAsync(approved, HttpStatusCode.Created);
        Assert.Equal("Pending", receipt.GetProperty("status").GetString());
        Assert.Equal(4500L, receipt.GetProperty("approvedAmountPaise").GetInt64());
        Assert.Equal(7500L, receipt.GetProperty("remainingAfterPaise").GetInt64());
        Assert.Equal("Preserve", receipt.GetProperty("access").GetProperty("treatment").GetString());
        Assert.NotEqual(receipt.GetProperty("reservationId").GetGuid(), receipt.GetProperty("accessDecisionId").GetGuid());

        // Money and the access decision are two separately recorded, linked facts.
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals", "amount_paise = 4500 AND receipt_revision = 1"));
        Assert.Equal(1L, await fixture.CountAsync("refund_access_decisions", "treatment = 'Preserve' AND term_id IS NOT NULL"));
        Assert.Equal(1L, await fixture.CountAsync("refund_access_decisions d", """
            EXISTS (SELECT FROM public.refund_approvals a WHERE a.account_id = d.account_id AND a.approval_id = d.approval_id)
            """));

        // Both readers derive Pending; the customer still sees the amount and reason they asked for.
        var customer = Assert.Single((await ReadAsync(await package.Payer.Client.GetAsync(
            P0608Tests.RefundsPath(package.Account)), HttpStatusCode.OK)).GetProperty("items").EnumerateArray());
        Assert.Equal("Pending", customer.GetProperty("status").GetString());
        Assert.Equal(6000L, customer.GetProperty("amountPaise").GetInt64());
        var intake = Assert.Single((await ReadAsync(await billing.SendAsync(HttpMethod.Get, AdminPath(package.Account)),
            HttpStatusCode.OK)).GetProperty("items").EnumerateArray());
        Assert.Equal("Pending", intake.GetProperty("status").GetString());
        Assert.Equal(6000L, intake.GetProperty("amountPaise").GetInt64());

        // Preserve writes no access, restriction, entitlement or payment fact.
        Assert.Equal(before, await P0608Tests.SnapshotAsync(fixture, package, term));
    }

    internal static string AdminPath(Guid account) => $"/api/admin/accounts/{account:D}/refund-requests";
    internal static string RequestPath(Guid account, Guid request) => $"{AdminPath(account)}/{request:D}";

    internal static async Task<Guid> RequestRefundAsync(Package package, Guid order, Guid confirmation, long paise,
        string reason = "Customer asked for part of the payment back")
    {
        using var response = await P0608Tests.PostRefundAsync(package.Payer.Client, package.Account,
            P0608Tests.RequestBody(order, confirmation, paise, reason));
        return (await ReadAsync(response, HttpStatusCode.OK)).GetProperty("refundRequestId").GetGuid();
    }

    internal static Task<HttpResponseMessage> TryPreviewAsync(P0110CookieBrowser admin, Guid account, Guid request,
        long paise, string treatment, DateTimeOffset? effectiveAt = null) =>
        P0212Fixture.PostAsync(admin, RequestPath(account, request) + "/approval-preview",
            new { amountPaise = paise, treatment, effectiveAt });

    internal static async Task<JsonElement> PreviewAsync(P0110CookieBrowser admin, Guid account, Guid request,
        long paise, string treatment, DateTimeOffset? effectiveAt = null)
    {
        using var response = await TryPreviewAsync(admin, account, request, paise, treatment, effectiveAt);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await ReadAsync(response, HttpStatusCode.OK);
    }

    /// <summary>The final command: the reviewed preview's version and digest plus key, reason and confirmation.</summary>
    internal static Dictionary<string, object?> Command(JsonElement preview, long paise, string treatment,
        DateTimeOffset? effectiveAt = null, Guid? key = null, string reason = Reason) => new()
        {
            ["operationKey"] = key ?? Guid.NewGuid(),
            ["amountPaise"] = paise,
            ["reason"] = reason,
            ["treatment"] = treatment,
            ["effectiveAt"] = effectiveAt,
            ["expectedVersion"] = preview.GetProperty("receiptVersion").GetInt64(),
            ["impactDigest"] = preview.GetProperty("impactDigest").GetString(),
            ["confirmed"] = true,
        };

    internal static Task<HttpResponseMessage> ApproveAsync(P0110CookieBrowser admin, Guid account, Guid request,
        object command) => P0212Fixture.PostAsync(admin, RequestPath(account, request) + "/approvals", command);

    internal static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {body}");
            return JsonDocument.Parse(body).RootElement.Clone();
        }
    }

    /// <summary>Retains one named observation under <c>P0609_POSTGRES_EVIDENCE_DIR</c>; a no-op when it is unset.</summary>
    internal static async Task RecordAsync(string name, object observation)
    {
        if (Environment.GetEnvironmentVariable("P0609_POSTGRES_EVIDENCE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"),
            JsonSerializer.Serialize(observation, Indented));
    }

    internal static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var problem = await ReadAsync(response, status);
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }
}
