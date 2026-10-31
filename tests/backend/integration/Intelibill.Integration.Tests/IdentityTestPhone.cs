using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

internal static class IdentityTestPhone
{
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
