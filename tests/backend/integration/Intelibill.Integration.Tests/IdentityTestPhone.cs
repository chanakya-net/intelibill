using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

internal static partial class IdentityTestPhone
{
    /// <summary>Public fixture key only; not a deployment secret.</summary>
    internal const string CodeKey = "YWJjZGVmMDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODk=";

    /// <summary>
    /// Reads the code out of the encrypted outbox row, which is the only place it exists. The
    /// delivery worker is removed in the test factory, so the row is still pending and still holds
    /// its body.
    /// </summary>
    internal static async Task<(Guid Id, string Code)> ReadCodeAsync(
        WebApplicationFactory<Program> factory,
        string destination,
        string purpose = PhoneChallengePurposes.Enrollment)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var challenge = await db.Set<PhoneChallenge>()
            .Where(x => x.Destination == destination && x.Purpose == purpose)
            .OrderByDescending(x => x.CreatedAt)
            .FirstAsync();
        var message = await db.Set<IdentitySms>()
            .Where(x => x.ChallengeId == challenge.Id)
            .OrderByDescending(x => x.CreatedAt)
            .FirstAsync();

        return (challenge.Id, ExtractCode(message, destination));
    }

    /// <summary>Requests a code and confirms it, returning the API's own response.</summary>
    internal static async Task<HttpResponseMessage> EnrollPhoneAsync(
        this WebApplicationFactory<Program> factory,
        HttpClient client,
        string phone,
        string firstName = "Phone",
        string lastName = "Owner")
    {
        var requested = await client.PostAsJsonAsync("/api/auth/phone/enrollment/request", new { phoneNumber = phone });
        requested.EnsureSuccessStatusCode();
        var (id, code) = await ReadCodeAsync(factory, phone);
        return await client.PostAsJsonAsync("/api/auth/phone/enrollment/confirm", new { challengeId = id, code, firstName, lastName });
    }

    private static string ExtractCode(IdentitySms message, string destination)
    {
        var data = Convert.FromBase64String(message.EncryptedContent);
        var plain = new byte[data.Length - 28];
        using var aes = new AesGcm(Convert.FromBase64String(IdentityTestMailbox.Key), 16);
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain, Encoding.UTF8.GetBytes(destination));
        using var json = JsonDocument.Parse(plain);
        var body = json.RootElement.GetProperty("Body").GetString()!;
        return CodePattern().Match(body).Value;
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex CodePattern();

    /// <summary>
    /// Seeds the kind of account the closed <c>register/phone</c> route used to create — a
    /// phone-only identity with no email and no password — and returns a live session for it.
    /// Tests that describe how such an account behaves need one without the route that made it.
    /// </summary>
    internal static async Task<AuthResult> SeedLegacyPhoneSessionAsync(
        WebApplicationFactory<Program> factory,
        string phone,
        string firstName = "Phone",
        string lastName = "Owner")
    {
        Guid userId;
        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = User.CreateWithPhone(phone, firstName, lastName);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        using var scope = factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAuthSessionService>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var result = await sessions.StartAsync(
            AuthenticationProof.FromRegistration(userId, clock.GetUtcNow()),
            preferredShopId: null,
            CancellationToken.None);

        Assert.False(result.IsError);
        return result.Value;
    }
}
