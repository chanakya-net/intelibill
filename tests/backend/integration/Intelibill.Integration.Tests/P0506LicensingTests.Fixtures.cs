using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0506LicensingTests
{
    private const string LegacyDevice = "legacy-counter-1";
    private const string LegacyVectorsFile = "p05-06-legacy-queue-vectors.json";

    private static JsonElement LoadLegacyVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, LegacyVectorsFile);
        Assert.True(File.Exists(path), $"Missing legacy vectors file at {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        return doc.RootElement.Clone();
    }

    private static void AssertPinnedVectorHashes(JsonElement vectors)
    {
        var scope = vectors.GetProperty("scope");
        var shopId = scope.GetProperty("shopId").GetGuid();
        var deviceId = scope.GetProperty("deviceId").GetString()!;

        foreach (var name in new[] { "unversionedPreDocument", "v1FrozenDocument" })
        {
            var entry = vectors.GetProperty(name);
            var syncJson = entry.GetProperty("expectedSyncRequestJson").GetString()!;
            var cmd = JsonSerializer.Deserialize<OfflineSaleSyncCommand>(syncJson, JsonSerializerOptions.Web);
            Assert.NotNull(cmd);
            Assert.Equal(entry.GetProperty("expectedServerIdempotencyKey").GetString(),
                OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, cmd!.ClientSaleId));
            Assert.Equal(entry.GetProperty("expectedServerPayloadHash").GetString(),
                OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, deviceId, cmd));
        }

        var exported = vectors.GetProperty("v1FrozenDocument").GetProperty("exportedBundleText").GetString()!;
        Assert.Contains(vectors.GetProperty("v1FrozenDocument").GetProperty("expectedBundleChecksum").GetString()!, exported);

        var wsControl = vectors.GetProperty("syntheticPrettyWhitespaceControl");
        Assert.True(wsControl.GetProperty("syntheticControl").GetBoolean());
        Assert.Equal(wsControl.GetProperty("checksum").GetString(),
            Sha256Lower(wsControl.GetProperty("invoicesRawJson").GetString()!));
    }

    private async Task<Actor> OpenPinnedVectorShopAsync(
        ApiWebApplicationFactory host,
        HttpClient client,
        JsonElement vectors,
        DateTimeOffset applyAt,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset shopVerifiedAt,
        DateTimeOffset accountVerifiedAt,
        bool include671dLease = true,
        bool include7ff1Lease = false,
        bool seed7ff1Batch = true,
        bool seed671dBatch = true)
    {
        var scope = vectors.GetProperty("scope");
        var shopId = scope.GetProperty("shopId").GetGuid();
        var userId = scope.GetProperty("userId").GetGuid();
        var email = "owner-p0506-pinned@fixture.test";
        var v7 = vectors.GetProperty("unversionedPreDocument");
        var v6 = vectors.GetProperty("v1FrozenDocument");
        var evidenceAt = applyAt <= At("2027-01-20T11:50:00Z") ? applyAt : At("2027-01-20T11:20:00Z");

        await SeedPinnedIdentityAndCatalogAsync(host, shopId, userId, email, v7, v6, evidenceAt, seed7ff1Batch, seed671dBatch);
        var session = await LoginPinnedOwnerAsync(client, email, shopId, applyAt.AddDays(400));

        _clock.Now = applyAt;
        var binding = await BindAsync(shopId);
        await ApplyCustomSnapshotAsync(binding, startsAt, endsAt, shopVerifiedAt, accountVerifiedAt, issuedAt: applyAt);
        await EnforceAsync(shopId);
        var actor = new Actor(userId, shopId, session, email, binding);

        if (include671dLease)
            await SeedUnanchoredLeaseAsync(actor, LegacyDevice, At("2027-01-20T11:00:00Z"), At("2027-01-23T11:00:00Z"), 1, "A/2627/", withReserver: true);
        if (include7ff1Lease)
            await SeedUnanchoredLeaseAsync(actor, LegacyDevice, At("2027-01-20T11:00:00Z"), At("2027-01-23T11:00:00Z"), 1, "INV-2026-27-", withReserver: false);
        return actor;
    }

    private async Task SeedPinnedIdentityAndCatalogAsync(
        ApiWebApplicationFactory host,
        Guid shopId,
        Guid userId,
        string email,
        JsonElement v7,
        JsonElement v6,
        DateTimeOffset evidenceAt,
        bool seed7ff1Batch,
        bool seed671dBatch)
    {
        using var diScope = host.Services.CreateScope();
        var hasher = diScope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await using var db = Open(Migrator);

        var user = User.CreateWithEmail(email, hasher.Hash(OwnerPassword), "Frozen", "Owner");
        user.VerifyEmail(email);
        db.Entry(user).Property(x => x.Id).CurrentValue = userId;
        db.Users.Add(user);

        var shop = Shop.Create("Frozen Legacy Traders", "42 Old MG Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        db.Entry(shop).Property(x => x.Id).CurrentValue = shopId;
        Assert.False(shop.DeclareTaxProfile("Frozen Legacy Traders Pvt Ltd", "29", TaxRegime.Regular,
            "29AAPFU0939F1ZR", "A", EInvoiceStatus.BelowThreshold, userId, evidenceAt).IsError);
        db.Shops.Add(shop);
        db.ShopMemberships.Add(ShopMembership.Create(shopId, userId, ShopRole.Owner, true));
        db.ShopExpiryStates.Add(ShopExpiryState.Create(shopId, evidenceAt));
        db.ExpiryPolicyRevisions.Add(ExpiryPolicyRevision.Create(shopId, null, 1, ExpiryPolicy.Default, userId, "Initial compatible policy", evidenceAt));
        await db.SaveChangesAsync(Ct);

        await LegacyOfflineExpiryHistory.SeedAsync(db, shopId);
        if (seed7ff1Batch)
            await SeedPinnedBatchAsync(db, shopId, userId, v7.GetProperty("itemId").GetGuid(), v7.GetProperty("inventoryBatchId").GetGuid(),
                "Legacy Rice 1kg", "LEG-7FF1", "1006", "LOT-OLD", 0m, null, evidenceAt);
        if (seed671dBatch)
            await SeedPinnedBatchAsync(db, shopId, userId, v6.GetProperty("itemId").GetGuid(), v6.GetProperty("inventoryBatchId").GetGuid(),
                "Steel Bottle 1L", "LEG-671D", "7323", "LOT-V1", 18m, v6.GetProperty("expiryEvidenceId").GetGuid(), evidenceAt);
    }

    private static async Task SeedPinnedBatchAsync(
        Infrastructure.Data.ApplicationDbContext db,
        Guid shopId,
        Guid userId,
        Guid itemId,
        Guid batchId,
        string itemName,
        string barcode,
        string hsn,
        string batchNo,
        decimal taxRate,
        Guid? expiryEvidenceId,
        DateTimeOffset capturedAt)
    {
        var item = Item.Create(shopId, itemName, null, "pcs", barcode, true, userId, hsn, taxRate, false);
        db.Entry(item).Property(x => x.Id).CurrentValue = itemId;
        db.Items.Add(item);

        var batch = InventoryBatch.Create(shopId, itemId, batchNo, 10m, 70m, 120m, 100m, taxRate, false, null, null, null, userId).Value;
        db.Entry(batch).Property(x => x.Id).CurrentValue = batchId;
        db.InventoryBatches.Add(batch);
        db.Inventory.Add(Inventory.Create(shopId, itemId, 10m, 0m, 0m, userId).Value);
        await db.SaveChangesAsync(Ct);

        if (expiryEvidenceId is { } evId)
        {
            var loaded = await db.InventoryBatches.Include(b => b.Item).SingleAsync(b => b.Id == batchId, Ct);
            var ev = OfflineExpiryEvidence.Capture(Guid.NewGuid(), loaded, ExpiryPolicy.Default, 1, capturedAt);
            db.Entry(ev).Property(x => x.Id).CurrentValue = evId;
            db.Set<OfflineExpiryEvidence>().Add(ev);
            await db.SaveChangesAsync(Ct);
        }
    }

    private async Task<Guid> LoginPinnedOwnerAsync(HttpClient client, string email, Guid shopId, DateTimeOffset authUntil)
    {
        var saved = _clock.Now;
        _clock.Now = EntitlementSnapshotsV1.Instant(TimeProvider.System);
        try
        {
            using var login = await client.PostAsJsonAsync("/api/auth/login/email", new { email, password = OwnerPassword }, Ct);
            var initialToken = (await ReadSuccessAsync(login)).GetProperty("accessToken").GetString()!;
            await SwitchClientShopAsync(client, initialToken, shopId, authUntil);
            var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
            return Guid.Parse(new JwtSecurityToken(token).Claims.Single(x => x.Type == "sid").Value);
        }
        finally { _clock.Now = saved; }
    }

    private async Task<Actor> OpenHistoricalShopWithCustomVerificationAsync(
        HttpClient client,
        DateTimeOffset applyAt,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset shopVerifiedAt,
        DateTimeOffset accountVerifiedAt,
        bool enforce = true)
    {
        var actor = await OpenShopAsync(client, applyAt, endsAt, licensed: false);
        _clock.Now = applyAt;
        var binding = await BindAsync(actor.Shop);
        await ApplyCustomSnapshotAsync(binding, startsAt, endsAt, shopVerifiedAt, accountVerifiedAt, issuedAt: applyAt);
        await using (var db = Open(Migrator))
            await LegacyOfflineExpiryHistory.SeedAsync(db, actor.Shop);
        if (enforce)
            await EnforceAsync(actor.Shop);
        return actor with { Binding = binding };
    }

    private async Task ApplyCustomSnapshotAsync(
        Binding binding,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset shopVerifiedAt,
        DateTimeOffset accountVerifiedAt,
        DateTimeOffset issuedAt,
        long revision = 1,
        string restriction = "Unrestricted",
        string state = "Active")
    {
        await using var db = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        var snapshot = new EntitlementSnapshotV1(
            1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.Account, binding.Shop, binding.Request,
            new(revision, accountVerifiedAt, restriction, null),
            new(revision, shopVerifiedAt, state, binding.Term, "Paid", startsAt, endsAt, endsAt, 2, 3));
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(
            Guid.NewGuid(), "test-key", "test-public-key", issuedAt, _clock.Now.AddMinutes(5),
            SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        var result = await new EntitlementProjectionStore(db, _clock, new ShopLicenceFence(db, new LicenceActionPolicy(), _clock))
            .ApplyAsync(snapshot, body, proof, Ct);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.Equal("Applied", JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(result.Value, EntitlementSnapshotsV1.Json)?.Outcome);
    }

    private async Task<InvoiceLease> SeedUnanchoredLeaseAsync(
        Actor actor,
        string deviceId,
        DateTimeOffset reservedAt,
        DateTimeOffset expiresAt,
        int rangeStart,
        string prefix = "A/2627/",
        bool withReserver = true)
    {
        await using var db = Open(Migrator);
        var year = FiscalYear.ForDate(reservedAt);
        var sequence = await db.InvoiceSequences.FirstOrDefaultAsync(
            s => s.ShopId == actor.Shop && s.FiscalYearStart == year.StartYear && s.SeriesCode == "A", Ct);
        if (sequence is null)
        {
            sequence = InvoiceSequence.Create(actor.Shop, year);
            db.InvoiceSequences.Add(sequence);
        }
        var lease = InvoiceLease.Create(
            actor.Shop, sequence.Id, deviceId, year.StartYear, prefix, rangeStart, rangeStart + 24, 6,
            reservedAt, expiresAt, withReserver ? actor.User : null, withReserver ? actor.Session : null);
        db.InvoiceLeases.Add(lease);
        await db.SaveChangesAsync(Ct);
        return lease;
    }

    private static string BuildCompactBundle(Guid shopId, string deviceId, DateTimeOffset exportedAt, params JsonNode[] invoices)
    {
        var rawInvoices = "[" + string.Join(",", invoices.Select(i => i.ToJsonString())) + "]";
        var checksum = Sha256Lower(rawInvoices);
        return "{\"format\":\"intelibill.offline-recovery\",\"version\":1,\"shopId\":" + JsonSerializer.Serialize(shopId) +
               ",\"deviceId\":" + JsonSerializer.Serialize(deviceId) +
               ",\"exportedAt\":" + JsonSerializer.Serialize(exportedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)) +
               ",\"checksum\":" + JsonSerializer.Serialize(checksum) +
               ",\"invoices\":" + rawInvoices + "}";
    }

    private static Task<HttpResponseMessage> PostRawRecoveryBundleAsync(HttpClient client, string operationId, string rawBundleText)
    {
        var body = "{\"operationId\":" + JsonSerializer.Serialize(operationId) + ",\"bundle\":" + rawBundleText + "}";
        return client.PostAsync("/api/offline-reconciliation/recovery-submissions", new StringContent(body, Encoding.UTF8, "application/json"), Ct);
    }

    private static async Task<JsonElement> SubmitRawRecoveryBundleAsync(HttpClient client, string operationId, string rawBundleText)
    {
        using var response = await PostRawRecoveryBundleAsync(client, operationId, rawBundleText);
        return await ReadSuccessAsync(response);
    }

    private static async Task<JsonElement> ResolveCaseAsync(HttpClient client, Guid caseId, uint expectedVersion, string operationId, string action, string reason, Guid? linkedOperationId = null)
    {
        using var response = await client.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
        {
            operationId, expectedVersion, action, reason, linkedOperationId,
        }, Ct);
        return await ReadSuccessAsync(response);
    }

    private static async Task<T[]> RunBarrierRaceAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        var ready = new CountdownEvent(2);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<T> RunAsync(Func<Task<T>> op) { ready.Signal(); await go.Task.WaitAsync(TimeSpan.FromSeconds(10)); return await op(); }
        var t1 = Task.Run(() => RunAsync(first));
        var t2 = Task.Run(() => RunAsync(second));
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "Race participants failed to reach barrier.");
        go.SetResult();
        return await Task.WhenAll(t1, t2);
    }

    private static string Sha256Lower(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
