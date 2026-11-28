using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0701Tests
{
    [Fact]
    public async Task VerifiedEmailAddsExactlyOneChannelToAnExistingPhoneOnlyNotice()
    {
        await using var paid = await P0308Fixture.StartAsync(database, Utc("2028-01-31T04:30:00Z"));
        var package = await paid.PackageAsync(1, ["Phone contact"]);
        var (_, term) = await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);
        var member = await SignInAsync(paid.Hosts);
        await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, package.Payer.Client, package.Account, member);
        await SetPhoneOnlyUserAsync(paid.Checkout.Delivery.MerchantDb, member.UserId, "+919876543219");
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);

        var initial = await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, "Account", $"user:{member.UserId:D}");
        Assert.Equal(5, initial.Count);
        Assert.All(initial, row => Assert.Equal(["InApp"], row.Channels));
        await LicensingPostgreSqlFixture.ExecuteAsync(paid.Checkout.Delivery.MerchantDb,
            $"UPDATE users SET is_email_verified = true WHERE id = '{member.UserId:D}'");
        await Task.WhenAll(RunPlannerPassAsync(paid.Hosts.Licensing.Services), RunPlannerPassAsync(paid.Hosts.Licensing.Services));
        await AssertLogicalNoticesAsync(paid, package.Account, term, member);
        var retained = await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, "Account", $"user:{member.UserId:D}");
        Assert.Equal(initial.Select(row => row.Id), retained.Select(row => row.Id));
    }

    private static async Task AssertLogicalNoticesAsync(P0308Fixture paid, Guid account, Guid term, Merchant recipient)
    {
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var store = paid.Hosts.LicensingOwner;
        await using var conn = new NpgsqlConnection(store.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, threshold, allowed_channels, protected_destination FROM public.effective_notification_jobs
            WHERE account_id = $1 AND source_id = $2 AND scope_kind = 'Account' AND (recipient_user_id = $3 OR destination_hash = $4)
            """, conn);
        cmd.Parameters.AddWithValue(account);
        cmd.Parameters.AddWithValue(term);
        cmd.Parameters.AddWithValue(recipient.UserId);
        cmd.Parameters.AddWithValue(Intelibill.Licensing.Application.Notifications.NotificationPlanner.HashEmail(recipient.Email));
        var ids = new HashSet<Guid>();
        var thresholds = new HashSet<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Assert.True(ids.Add(reader.GetGuid(0)));
            Assert.True(thresholds.Add(reader.GetString(1)));
            Assert.Equal(["InApp", "Email"], reader.GetFieldValue<string[]>(2));
            Assert.False(reader.IsDBNull(3));
        }
        Assert.Equal(5, ids.Count);
        await reader.CloseAsync();
        await using var delivery = new NpgsqlCommand("""
            SELECT count(*) FROM notification_email_deliveries WHERE account_id = $1 AND job_id = ANY($2)
            """, conn);
        delivery.Parameters.AddWithValue(account);
        delivery.Parameters.AddWithValue(ids.ToArray());
        Assert.Equal(5L, await delivery.ExecuteScalarAsync());
        await AssertPreviewChannelsAsync(paid, account, ids);
    }

    private static async Task AssertPreviewChannelsAsync(P0308Fixture paid, Guid account, HashSet<Guid> ids)
    {
        var catalogue = paid.Checkout.Trial.Catalogue;
        using var owner = await catalogue.SignInOwnerAsync();
        using var billing = await SignInAdminWithAsync(catalogue, owner, $"preview-{Guid.NewGuid():N}@example.test", "billing");
        Guid? after = null;
        var found = new HashSet<Guid>();
        do
        {
            var path = $"/api/admin/accounts/{account:D}/notifications" + (after is null ? "" : $"?after={after:D}");
            using var response = await billing.SendAsync(HttpMethod.Get, path);
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<ProviderNotificationsPageV1>();
            foreach (var job in page!.Items.Where(item => ids.Contains(item.JobId)))
            {
                Assert.True(found.Add(job.JobId));
                Assert.Equal(["InApp", "Email"], job.AllowedChannels);
            }
            after = page.NextCursor;
        } while (after.HasValue);
        Assert.True(found.SetEquals(ids));
    }
}
