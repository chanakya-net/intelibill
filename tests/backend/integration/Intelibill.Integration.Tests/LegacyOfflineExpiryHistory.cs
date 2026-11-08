using System.IdentityModel.Tokens.Jwt;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Legacy protocol fixtures describe invoices from pre-feature merchants. Keep their original JSON
/// and hashes, while explicitly seeding the same known Ignore baseline as a migrated merchant.
/// New schema-4 and new-shop refusal tests do not call this helper.
/// </summary>
internal static class LegacyOfflineExpiryHistory
{
    public static async Task SeedAsync(ApiWebApplicationFactory factory, string scopedToken)
    {
        var shopId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(scopedToken).Claims.Single(x => x.Type == "active_shop_id").Value);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var state = await db.ShopExpiryStates.AsNoTracking().SingleAsync(x => x.ShopId == shopId);
        Assert.Equal(1, state.CatalogVersion); Assert.Equal(1, state.DefaultPolicyVersion);
        if (state.PreFeatureBaseline) return;
        await using var tx = await db.Database.BeginTransactionAsync();
        var created = new DateTimeOffset(2020, 4, 1, 0, 0, 0, TimeSpan.Zero);
        await db.Database.ExecuteSqlAsync($"UPDATE shops SET created_at = {created} WHERE id = {shopId}");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE expiry_policy_revisions DISABLE TRIGGER expiry_policy_revisions_immutable");
        await db.Database.ExecuteSqlAsync($"UPDATE expiry_policy_revisions SET pre_feature_baseline = true, actor_user_id = NULL WHERE shop_id = {shopId} AND catalog_version = 1");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE expiry_policy_revisions ENABLE TRIGGER expiry_policy_revisions_immutable");
        await db.Database.ExecuteSqlAsync($"UPDATE shop_expiry_states SET pre_feature_baseline = true, initialized_at = {created} WHERE shop_id = {shopId}");
        await tx.CommitAsync();
    }
}
