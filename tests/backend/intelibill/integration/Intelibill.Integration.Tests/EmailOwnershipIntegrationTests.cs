using System.Net;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Services.Auth;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class EmailOwnershipIntegrationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture, services => { services.RemoveAll<IEmailService>(); services.AddSingleton<IEmailService, NoOpEmailService>(); }, enableIdentityMail: false);
    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() { _factory.Dispose(); return Task.CompletedTask; }
    public void Dispose() { _factory.Dispose(); GC.SuppressFinalize(this); }
    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    [Fact]
    public async Task PendingEnrollment_CannotCreateShopOrReceiveSession()
    {
        using var client = Client();
        var email = $"owner-{Guid.NewGuid():N}@test.com";
        var response = await client.PostAsJsonAsync("/api/auth/enrollment/email/request", new { email });
        // Delivery is deliberately disabled in the default integration host.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Email == email));
        var protectedResponse = await client.PostAsJsonAsync("/api/shops", new { name = "Unproved" });
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
    }
    [Fact]
    public async Task ResetDisabled_KnownAndUnknownAddresses_ReturnUniformUnavailable()
    {
        using var client = Client(); const string known = "known@test.com";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = User.CreateWithEmail(known, "hash", "Known", "Owner"); user.VerifyEmail(known); db.Add(user); await db.SaveChangesAsync();
        }
        foreach (var email in new[] { known, "unknown@test.com" })
        {
            var response = await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("Auth.EmailDeliveryUnavailable", await response.Content.ReadAsStringAsync());
        }
    }
}
