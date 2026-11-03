using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Repositories;

public sealed class UserRepositoryTests
{
    [Fact]
    public async Task GetDisplayNamesAsync_NamesKnownUsersAndFallsBackToEmail()
    {
        await using var context = await CreateContextAsync();
        var named = User.CreateWithEmail("olive@test.com", "hash", "Olive", "Owner");
        var unnamed = User.CreateWithEmail("sam@test.com", "hash", " ", " ");
        await context.Users.AddRangeAsync(named, unnamed);
        await context.SaveChangesAsync();
        var unknown = Guid.NewGuid();

        var names = await new UserRepository(context).GetDisplayNamesAsync([named.Id, unnamed.Id, unknown, named.Id]);

        Assert.Equal(2, names.Count);
        Assert.Equal("Olive Owner", names[named.Id]);
        Assert.Equal("sam@test.com", names[unnamed.Id]);
        Assert.False(names.ContainsKey(unknown));
        Assert.Empty(await new UserRepository(context).GetDisplayNamesAsync([]));
    }

    private static async Task<ApplicationDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }
}
