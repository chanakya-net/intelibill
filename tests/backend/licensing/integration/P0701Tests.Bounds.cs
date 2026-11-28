using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0701Tests
{
    [Fact]
    public async Task LargeMultibyteRosterAndPagedContactsStayWithinDelegatedHttpLimits()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        var feb29 = Utc("2028-02-29T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(database, jan31);
        var owner = await SignInAsync(paid.Hosts);
        var account = await CreateAccountAsync(owner.Client, "Large Multibyte Roster Account");

        // Valid 1,150-shop grouped paid term with long multibyte shop names (Devanagari + Tamil + emoji).
        const int shopCount = 1150;
        var termId = await SeedTermAsync(
            paid.Hosts,
            account,
            new TermSeed(jan31, feb29, Grouped: true, Capacity: shopCount, AnchorLocal: "2028-01-31 10:00:00"));
        await SeedBulkMultibyteAffiliationsAsync(paid.Hosts.LicensingOwner, account, owner.UserId, termId, jan31, feb29, shopCount);

        // Plan at jan31: commercial notices store exact coveredShopCount (1150) and bounded sample (<= 5).
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        paid.At(Utc("2028-02-22T04:30:00Z"));

        using var noticeResp = await owner.Client.GetAsync($"/api/billing/accounts/{account:D}/notices");
        Assert.Equal(HttpStatusCode.OK, noticeResp.StatusCode);
        var noticeBytes = await noticeResp.Content.ReadAsByteArrayAsync();
        Assert.True(noticeBytes.Length < LicenseNoticesV1.AccountResponseLimit);
        var notices = JsonSerializer.Deserialize<AccountLicenseNoticesV1>(noticeBytes, WebJson)!;
        var notice = Assert.Single(notices.Notices);
        Assert.Equal(shopCount, notice.CoveredShopCount);
        Assert.True(notice.CoveredShopNamesTruncated);
        Assert.Equal(LicenseNoticesV1.MaxShopNameSampleCount, notice.CoveredShopNames.Length);
        Assert.All(notice.CoveredShopNames, name =>
        {
            Assert.True(name.Length <= LicenseNoticesV1.MaxShopNamePreviewCodeUnits);
            Assert.True(Encoding.UTF8.GetByteCount(name) <= 256);
        });

        // Contact list exceeding one page (32 > ContactPageSize=25) paginates cleanly under same authorization.
        const int contactCount = 32;
        await SeedPagedContactsAsync(owner.Client, account, contactCount);
        await AssertPagedContactsContinuationAsync(owner.Client, account, contactCount);
    }

    private static async Task SeedBulkMultibyteAffiliationsAsync(
        NpgsqlConnectionStringBuilder settings,
        Guid accountId,
        Guid ownerId,
        Guid termId,
        DateTimeOffset appliedAt,
        DateTimeOffset endsAt,
        int count)
    {
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await CreateBulkShopsTempTableAsync(conn, tx, count);
        await InsertBulkShopRequestsAndAffiliationsAsync(conn, tx, accountId, ownerId, termId, appliedAt, endsAt);
        await tx.CommitAsync();
    }

    private static async Task CreateBulkShopsTempTableAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int count)
    {
        await using var cmd = new NpgsqlCommand("""
            CREATE TEMP TABLE tmp_bulk_shops ON COMMIT DROP AS
            SELECT
                gen_random_uuid() AS request_id,
                gen_random_uuid() AS shop_id,
                gen_random_uuid() AS event_id,
                gen_random_uuid() AS membership_id,
                'दुकान-தமிழ்-🏪-' || lpad(i::text, 4, '0') || '-' || repeat('अ', 90) AS shop_name
            FROM generate_series(1, $1) AS i
            """, conn, tx);
        cmd.Parameters.AddWithValue(count);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertBulkShopRequestsAndAffiliationsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid accountId,
        Guid ownerId,
        Guid termId,
        DateTimeOffset appliedAt,
        DateTimeOffset endsAt)
    {
        await using (var insertReq = new NpgsqlCommand("""
            INSERT INTO public.shop_affiliation_requests (
                request_id, account_id, account_name, shop_id, requester_id, requester_revision,
                operation_key, term_id, created_at, expires_at)
            SELECT
                request_id, $1, 'Large Multibyte Roster Account', shop_id, $2, 1,
                gen_random_uuid(), $3, $4, $4 + interval '7 days'
            FROM tmp_bulk_shops
            """, conn, tx))
        {
            insertReq.Parameters.AddWithValue(accountId);
            insertReq.Parameters.AddWithValue(ownerId);
            insertReq.Parameters.AddWithValue(termId);
            insertReq.Parameters.AddWithValue(appliedAt);
            await insertReq.ExecuteNonQueryAsync();
        }

        await using var insertAff = new NpgsqlCommand("""
            INSERT INTO public.shop_affiliations (
                request_id, account_id, shop_id, term_id, event_id, actor_id, membership_id,
                authority_version, shop_name, owner_name, accepted_at, applied_at, coverage_ends_at)
            SELECT
                request_id, $1, shop_id, $3, event_id, $2, membership_id,
                1, shop_name, 'Bulk Owner', $4, $4, $5
            FROM tmp_bulk_shops
            """, conn, tx);
        insertAff.Parameters.AddWithValue(accountId);
        insertAff.Parameters.AddWithValue(ownerId);
        insertAff.Parameters.AddWithValue(termId);
        insertAff.Parameters.AddWithValue(appliedAt);
        insertAff.Parameters.AddWithValue(endsAt);
        await insertAff.ExecuteNonQueryAsync();
    }

    private static async Task SeedPagedContactsAsync(HttpClient ownerClient, Guid accountId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var email = $"paged.contact.{i:D3}@large-roster-domain.example.test";
            using var resp = await ownerClient.PostAsJsonAsync(
                $"/api/billing/accounts/{accountId:D}/notice-contacts",
                new ApproveBillingContactRequestV1(Guid.NewGuid(), MerchantUserId: null, email, "en-IN"));
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        }
    }

    private static async Task AssertPagedContactsContinuationAsync(
        HttpClient ownerClient,
        Guid accountId,
        int expectedTotal)
    {
        using var page1Resp = await ownerClient.GetAsync($"/api/billing/accounts/{accountId:D}/notice-contacts");
        Assert.Equal(HttpStatusCode.OK, page1Resp.StatusCode);
        var page1Bytes = await page1Resp.Content.ReadAsByteArrayAsync();
        Assert.True(page1Bytes.Length < LicenseNoticesV1.ContactResponseLimit);
        var page1 = JsonSerializer.Deserialize<BillingContactsPageV1>(page1Bytes, WebJson)!;
        Assert.Equal(expectedTotal, page1.TotalCount);
        Assert.Equal(LicenseNoticesV1.ContactPageSize, page1.Items.Length);
        Assert.NotNull(page1.NextCursor);

        using var page2Resp = await ownerClient.GetAsync(
            $"/api/billing/accounts/{accountId:D}/notice-contacts?after={page1.NextCursor:D}");
        Assert.Equal(HttpStatusCode.OK, page2Resp.StatusCode);
        var page2Bytes = await page2Resp.Content.ReadAsByteArrayAsync();
        Assert.True(page2Bytes.Length < LicenseNoticesV1.ContactResponseLimit);
        var page2 = JsonSerializer.Deserialize<BillingContactsPageV1>(page2Bytes, WebJson)!;
        Assert.Equal(expectedTotal, page2.TotalCount);
        Assert.Equal(expectedTotal - LicenseNoticesV1.ContactPageSize, page2.Items.Length);
        Assert.Null(page2.NextCursor);

        var combinedIds = page1.Items.Concat(page2.Items).Select(c => c.ContactId).ToHashSet();
        Assert.Equal(expectedTotal, combinedIds.Count);

        // Foreign/invalid cursor returns 400.
        using var invalidCursor = await ownerClient.GetAsync(
            $"/api/billing/accounts/{accountId:D}/notice-contacts?after={Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
    }
}
