using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0203Tests(LicensingPostgreSqlFixture database)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task OwnerIssuesInvitation_OnlyNamedVerifiedActorCanSeeIt()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var named = await host.SignInAsync();
        var stranger = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);

        using var issue = await IssueAsync(owner.Client, account, named.Email);
        Assert.Equal(HttpStatusCode.Accepted, issue.StatusCode);
        Assert.Single(await InboxAsync(named.Client));
        Assert.Empty(await InboxAsync(stranger.Client));
        Assert.Empty(await InboxAsync(owner.Client));
    }

    private async Task<InvitationHost> StartAsync()
    {
        await database.PrepareMerchantAsync();
        // Cases share a disposable merchant database and TestServer address, so start each case
        // with its own real rate-limit budget.
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            "TRUNCATE public.rate_limit_buckets");
        var fixture = await P0201Fixture.CreateAsync(database);
        fixture.Clock.Now = P0202Fixture.WholeSecondNow();
        var keys = Directory.CreateTempSubdirectory("intelibill-p0203-keys-");
        var settings = fixture.LicensingSettings();
        settings["AdminBootstrap:DataProtectionKeyDirectory"] = keys.FullName;
        var licensing = P0202Fixture.LicensingApi(fixture, settings: settings);
        var merchant = fixture.MerchantApi(handler: () => licensing.Server.CreateHandler(),
            configure: services => services.AddSingleton<TimeProvider>(fixture.Clock));
        return new InvitationHost(fixture, licensing, merchant, keys, settings);
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(Path("/api/billing/accounts"),
            new { operationKey = Guid.NewGuid(), name = "Billing invitation fixture" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("accountId").GetGuid();
    }

    private static Task<HttpResponseMessage> IssueAsync(HttpClient client, Guid account, string email, Guid? key = null) =>
        client.PostAsJsonAsync(Path($"/api/billing/accounts/{account:D}/invitations"),
            new { operationKey = key ?? Guid.NewGuid(), expectedOwnerRevision = 1, recipientKind = "email", recipient = email });

    private static async Task<JsonElement[]> InboxAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Path("/api/billing/invitations"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await JsonAsync(response)).GetProperty("invitations").EnumerateArray()];
    }

    private static Task<JsonElement> JsonAsync(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();
    private static Uri Path(string path) => new(path, UriKind.Relative);

    private sealed class InvitationHost(P0201Fixture fixture, LicensingApiFactory licensing,
        MerchantStartupProbeFactory merchant, DirectoryInfo keys, Dictionary<string, string?> settings) : IAsyncDisposable
    {
        private readonly List<HttpClient> _clients = [];
        public P0201Fixture Fixture => fixture;
        public LicensingApiFactory Licensing => licensing;
        public LicensingApiFactory Replica() => P0202Fixture.LicensingApi(fixture, settings: settings);

        public async Task<(Guid Actor, string Email, HttpClient Client)> SignInAsync()
        {
            var client = merchant.CreateClient();
            _clients.Add(client);
            var email = P0202Fixture.UniqueEmail();
            var actor = await P0201Tests.SeedMerchantAsync(merchant.Services, email);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await P0201Tests.LoginAsync(client, email));
            return (actor, email, client);
        }

        public async ValueTask DisposeAsync()
        {
            _clients.ForEach(client => client.Dispose());
            await merchant.DisposeAsync();
            await licensing.DisposeAsync();
            fixture.Dispose();
            keys.Delete(recursive: true);
        }
    }
}
