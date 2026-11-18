using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantLogCaptureTests.Name)]
public sealed partial class P0201Tests(LicensingPostgreSqlFixture database)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task MerchantOrigin_DelegatesOnlyTheAuthorizedAccount()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        fixture.Clock.Now = DateTimeOffset.UtcNow;
        await using var licensing = fixture.LicensingApi();
        using var licensingClient = licensing.CreateClient();
        await using var merchant = fixture.MerchantApi(handler: () => licensing.Server.CreateHandler(),
            configure: services => services.AddSingleton<TimeProvider>(fixture.Clock));
        using var client = merchant.CreateClient();
        var actor = await SeedMerchantAsync(merchant.Services);
        var account = Guid.NewGuid();
        await fixture.GrantAsync(account, actor);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client));

        using var response = await client.GetAsync(MerchantPath(account));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var access = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(account, access.GetProperty("accountId").GetGuid());
        Assert.Equal(P0201Fixture.Action, Assert.Single(access.GetProperty("actions").EnumerateArray()).GetString());
        Assert.Equal(2, access.EnumerateObject().Count());

        using var denied = await client.GetAsync(MerchantPath(Guid.NewGuid()));
        await AssertProblemAsync(denied, HttpStatusCode.Forbidden, "Billing.AccountAccessDenied");
    }

    [Fact]
    public async Task MissingMembershipAdapter_FailsClosed()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var licensing = fixture.LicensingApi(membership: false);
        using var client = licensing.CreateClient();
        var assertion = CreateAssertion(fixture, Guid.NewGuid(), Guid.NewGuid());
        using var response = await P0201Fixture.SendAssertionAsync(client, assertion.Account, assertion.Request, assertion.Token);
        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "Billing.MembershipUnavailable");
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    internal const string Email = "billing-fixture@example.test";
    internal const string Password = "BillingFixture!789";
    private static Uri MerchantPath(Guid account) => new($"/api/billing/accounts/{account:D}/access", UriKind.Relative);

    internal static async Task<Guid> SeedMerchantAsync(IServiceProvider services, string email = Email)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var existing = await db.Users.SingleOrDefaultAsync(user => user.Email == email);
        if (existing is not null)
            return existing.Id;

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var actor = User.CreateWithEmail(email, hasher.Hash(Password), "Billing", "Fixture");
        actor.VerifyEmail(email);
        db.Users.Add(actor);
        await db.SaveChangesAsync();
        return actor.Id;
    }

    internal static async Task<string> LoginAsync(HttpClient client, string email = Email)
    {
        using var response = await client.PostAsJsonAsync(new Uri("/api/auth/login/email", UriKind.Relative),
            new { email, password = Password });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("accessToken").GetString()!;
    }
}
