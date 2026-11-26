using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607aTests
{
    private static readonly string[] ParticipantFields =
    [
        "transferId", "proposalRevision", "termsDigest", "sourceAccountId", "sourceAccountName", "destinationAccountId",
        "destinationAccountName", "shopId", "shopName", "treatment", "cutoverAt", "sourcePaidThroughAt",
        "observedPermitCount", "observedPermitDeadline", "disconnectedQueuesUnknown", "createdAt", "expiresAt", "state",
        "stateReason", "consents", "consentsCollected", "awaitingProviderReview", "viewerPurposes", "viewerAuthorityRevision",
    ];

    [Fact]
    public async Task RequestReplayAndPrivateHistoryAreScoped()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var before = await s.BaselineAsync();
        var preview = await PreviewAsync(s);
        var path = RequestPath(s.Source.Account, s.Term);

        // Two different fresh keys race for the named shop: one proposal wins, the other gets a typed conflict.
        var keys = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var raced = await Task.WhenAll(keys.Select(key => s.Source.Payer.Client.PostAsJsonAsync(path, RequestBody(preview, key))));
        var winningIndex = Array.FindIndex(raced, r => r.StatusCode == HttpStatusCode.OK);
        Assert.True(winningIndex >= 0, string.Join(" | ", await Task.WhenAll(raced.Select(async response =>
            $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}"))));
        var winner = keys[winningIndex];
        var loser = raced.Single(r => r.StatusCode != HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
        Assert.Equal("Coverage.TransferConflict", await CodeAsync(loser));
        var created = await raced.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("transferId").GetGuid();

        // A dropped response is recovered by the identical request and by the key lookup; a changed payload conflicts.
        using var replay = await s.Source.Payer.Client.PostAsJsonAsync(path, RequestBody(preview, winner));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transferId").GetGuid());
        using var recovered = await s.Source.Payer.Client.GetAsync($"{path}/operations/{winner}");
        Assert.Equal(id, (await recovered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transferId").GetGuid());
        using var changed = await s.Source.Payer.Client.PostAsJsonAsync(path, RequestBody(preview, winner, "A different reason"));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("Billing.OperationPayloadMismatch", await CodeAsync(changed));
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_proposals"));

        // Outsiders and the other party cannot use the source's request or recovery routes.
        var outsider = s.Source.Shops[1].Client;
        foreach (var client in new[] { outsider, s.Destination.Payer.Client })
        {
            using var denied = await client.PostAsJsonAsync(path, RequestBody(preview, Guid.NewGuid()));
            using var lookup = await client.GetAsync($"{path}/operations/{winner}");
            using var list = await client.GetAsync($"/api/billing/accounts/{s.Source.Account}/coverage-transfers");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, lookup.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        }
        using var outsiderRead = await outsider.GetAsync(TransferPath(s.Destination.Account, id));
        Assert.Equal(HttpStatusCode.Forbidden, outsiderRead.StatusCode);

        await AssertNarrowDestinationViewAsync(s, id);
        await AssertBillsStayWithTheSourceAsync(s);
        await AssertEvidenceIsPrivateAsync(s, id);
        Assert.Equal(before, await s.BaselineAsync());
    }

    [Fact]
    public async Task RequestKeyBelongsToTheOriginalOwnerMembership()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var path = RequestPath(s.Source.Account, s.Term);
        var owner = s.Source.Payer;
        var preview = await PreviewAsync(s);
        Assert.Equal(1L, preview.GetProperty("sourceOwnerRevision").GetInt64());
        var key = Guid.NewGuid();
        var request = RequestBody(preview, key);
        using var created = await owner.Client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        await LicensingPostgreSqlFixture.ExecuteAsync(s.F.Hosts.LicensingOwner, $"""
            UPDATE billing_account_members SET status = 2, role = 2, revision = revision + 1
            WHERE account_id = '{s.Source.Account}' AND merchant_user_id = '{owner.UserId}' AND status = 1;
            INSERT INTO billing_account_members(id, account_id, merchant_user_id, role, status, revision, created_at)
            VALUES (gen_random_uuid(), '{s.Source.Account}', '{owner.UserId}', 1, 1, 1, now());
            """);
        using var replay = await owner.Client.PostAsJsonAsync(path, request);
        using var lookup = await owner.Client.GetAsync($"{path}/operations/{key}");
        foreach (var response in new[] { replay, lookup })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("Coverage.TransferAuthorityChanged", await CodeAsync(response));
        }
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_proposals"));
    }

    // The destination finds its incoming request through its own account and sees only the allow-listed fields.
    private static async Task AssertNarrowDestinationViewAsync(Scenario s, Guid id)
    {
        using var response = await s.Destination.Payer.Client.GetAsync(
            $"/api/billing/accounts/{s.Destination.Account}/coverage-transfers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        var item = Assert.Single(JsonSerializer.Deserialize<JsonElement>(raw).GetProperty("items").EnumerateArray());
        Assert.Equal(id, item.GetProperty("transferId").GetGuid());
        Assert.Equal(ParticipantFields.Order(), item.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(["DestinationBillingOwner"], item.GetProperty("viewerPurposes").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("North", item.GetProperty("shopName").GetString());
        var order = (Guid)(await s.F.ScalarAsync($"SELECT order_id FROM payment_fulfilments WHERE term_id='{s.Term}'"))!;
        foreach (var hidden in new[] { s.Term.ToString(), order.ToString(), "Central", "East", "Branch sold", "paise", "price" })
            Assert.DoesNotContain(hidden, raw, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertBillsStayWithTheSourceAsync(Scenario s)
    {
        var order = (Guid)(await s.F.ScalarAsync($"SELECT order_id FROM payment_fulfilments WHERE term_id='{s.Term}'"))!;
        var bills = $"/orders/{order}/payment/confirmations";
        using var own = await s.Source.Payer.Client.GetAsync($"/api/billing/accounts/{s.Source.Account}{bills}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Single((await own.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        foreach (var client in new[] { s.Destination.Payer.Client, s.North.Client })
        {
            using var viaSource = await client.GetAsync($"/api/billing/accounts/{s.Source.Account}{bills}");
            using var viaDestination = await client.GetAsync($"/api/billing/accounts/{s.Destination.Account}{bills}");
            Assert.Equal(HttpStatusCode.Forbidden, viaSource.StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, viaDestination.StatusCode);
        }
    }

    // One private evidence row per independent subscription on each side, readable only in its own account scope.
    private static async Task AssertEvidenceIsPrivateAsync(Scenario s, Guid id)
    {
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_evidence", $"transfer_id='{id}' AND side='Source' AND term_id='{s.Term}'"));
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_evidence",
            $"transfer_id='{id}' AND side='Destination' AND term_id='{s.DestinationTerm}'"));
        Assert.Equal(1L, await RuntimeCountAsync(s, s.Source.Account, "coverage_transfer_evidence", "side='Source'"));
        Assert.Equal(0L, await RuntimeCountAsync(s, s.Source.Account, "coverage_transfer_evidence", "side='Destination'"));
        Assert.Equal(0L, await RuntimeCountAsync(s, s.Destination.Account, "coverage_transfer_evidence", "side='Source'"));
        Assert.Equal(0L, await RuntimeCountAsync(s, null, "coverage_transfer_evidence", "true"));
        Assert.Equal(0L, await RuntimeCountAsync(s, null, "coverage_transfer_proposals", "true"));
        Assert.Equal(0L, await RuntimeCountAsync(s, Guid.NewGuid(), "coverage_transfer_proposals", "true"));
        Assert.Equal(1L, await RuntimeCountAsync(s, s.Destination.Account, "coverage_transfer_proposals", "true"));
    }

    /// <summary>Counts rows as the licensing runtime role under forced row-level security in one account scope.</summary>
    internal static async Task<long> RuntimeCountAsync(Scenario s, Guid? account, string table, string where)
    {
        await using var connection = new NpgsqlConnection(s.F.Hosts.LicensingOwner.ConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand($"""
            SET LOCAL ROLE licensing_runtime;
            SELECT set_config('app.billing_account_id', '{account?.ToString() ?? ""}', true);
            SELECT count(*) FROM public.{table} WHERE {where};
            """, connection, tx);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.NextResultAsync();
        await reader.ReadAsync();
        return reader.GetInt64(0);
    }
}
