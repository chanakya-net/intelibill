using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Intelibill.Integration.Tests;

internal static class IdentityTestMailbox
{
    internal static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HttpClient, WebApplicationFactory<Program>> Factories = new();
    internal static Task<HttpResponseMessage> EnrollAsync<T>(this HttpClient client, T request) => Factories.GetValue(client, _ => throw new InvalidOperationException("Test client is not registered.")).EnrollAsync(client, request);
    internal static async Task<HttpResponseMessage> SeedVerifiedStaffAsync(this HttpClient client, HttpRequestMessage request)
    {
        // Fixture-only persisted identity/membership, representing existing verified staff.
        // The public direct staff-creation endpoint is intentionally disabled pending invitations.
        var values = await request.Content!.ReadFromJsonAsync<JsonElement>();
        using var scope = Factories.GetValue(client, _ => throw new InvalidOperationException()).Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = values.GetProperty("email").GetString()!;
        var user = User.CreateWithEmail(email, scope.ServiceProvider.GetRequiredService<Intelibill.Application.Common.Interfaces.IPasswordHasher>().Hash(values.GetProperty("password").GetString()!),
            values.GetProperty("firstName").GetString()!, values.GetProperty("lastName").GetString()!, values.GetProperty("phoneNumber").GetString());
        user.VerifyEmail(email);
        var role = Enum.Parse<Intelibill.Domain.Enums.ShopRole>(values.GetProperty("role").GetString()!);
        foreach (var shop in values.GetProperty("shopIds").EnumerateArray()) user.AddShopMembership(ShopMembership.Create(shop.GetGuid(), user.Id, role, false));
        db.Users.Add(user); await db.SaveChangesAsync();
        return new HttpResponseMessage(System.Net.HttpStatusCode.Created) { Content = JsonContent.Create(new { userId = user.Id, email }) };
    }
    // Public fixture key only; not a deployment secret.
    internal const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    internal static async Task<(Guid Id, string Token)> ReadAsync(WebApplicationFactory<Program> factory, string email, string purpose = "enrollment")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var challenge = await db.Set<EmailOwnershipChallenge>().Where(x => x.Destination == email && x.Purpose == purpose).OrderByDescending(x => x.CreatedAt).FirstAsync();
        var mail = await db.Set<IdentityMail>().SingleAsync(x => x.ChallengeId == challenge.Id);
        return (challenge.Id, ReadToken(mail, email));
    }

    /// <summary>
    /// Reads the single-use token out of the most recent shop-invitation mail for one address. The
    /// token only ever exists in the encrypted outbox row, never in an API response.
    /// </summary>
    internal static async Task<(Guid Id, string Token)> ReadInvitationAsync(WebApplicationFactory<Program> factory, string email)
    {
        email = email.Trim().ToLowerInvariant();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invitation = await db.Set<ShopInvitation>().Where(x => x.Email == email)
            .OrderByDescending(x => x.CreatedAt).FirstAsync();
        var mail = await db.Set<IdentityMail>().Where(x => x.ChallengeId == invitation.Id)
            .OrderByDescending(x => x.CreatedAt).FirstAsync();
        return (invitation.Id, ReadToken(mail, email));
    }

    private static string ReadToken(IdentityMail mail, string destination)
    {
        var data = Convert.FromBase64String(mail.EncryptedContent); var plain = new byte[data.Length - 28];
        using var aes = new AesGcm(Convert.FromBase64String(Key), 16);
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain, Encoding.UTF8.GetBytes(destination));
        using var json = JsonDocument.Parse(plain);
        var body = json.RootElement.GetProperty("Body").GetString()!;
        var token = body.Split("&token=", 2)[1].Split('&', 2)[0];
        return Uri.UnescapeDataString(token);
    }
    internal static async Task<HttpResponseMessage> EnrollAsync<T>(this WebApplicationFactory<Program> factory, HttpClient client, T request)
    {
        var json = JsonSerializer.SerializeToElement(request);
        var email = json.GetProperty("email").GetString()!;
        var requested = await client.PostAsJsonAsync("/api/auth/enrollment/email/request", new { email });
        requested.EnsureSuccessStatusCode();
        var (id, token) = await ReadAsync(factory, email);
        return await client.PostAsJsonAsync("/api/auth/enrollment/email/confirm", new
        {
            challengeId = id, token, password = json.GetProperty("password").GetString(),
            firstName = json.GetProperty("firstName").GetString(), lastName = json.GetProperty("lastName").GetString()
        });
    }
}
