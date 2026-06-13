using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Repositories;

public sealed class CreditNoteRepositoryTests
{
    private static async Task<ApplicationDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static CreditNote CreateCreditNote(Guid shopId, Guid saleId, string code = "CN-001")
    {
        var result = CreditNote.Issue(shopId, saleId, 100m, "Test reason", code, null);
        return result.Value;
    }

    [Fact]
    public async Task GetByCodeAsync_ReturnsNote_WhenCodeMatchesShop()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var note = CreateCreditNote(shopId, Guid.NewGuid(), "CN-001");

        await context.CreditNotes.AddAsync(note);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var found = await repository.GetByCodeAsync(shopId, "CN-001");

        Assert.NotNull(found);
        Assert.Equal(note.Id, found.Id);
    }

    [Fact]
    public async Task GetByCodeAsync_ReturnsNull_WhenShopDoesNotMatch()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var otherShopId = Guid.NewGuid();
        var note = CreateCreditNote(shopId, Guid.NewGuid(), "CN-001");

        await context.CreditNotes.AddAsync(note);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var found = await repository.GetByCodeAsync(otherShopId, "CN-001");

        Assert.Null(found);
    }

    [Fact]
    public async Task GetBySaleIdAsync_ReturnsNotesForMatchingSale()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var otherSaleId = Guid.NewGuid();
        var note = CreateCreditNote(shopId, saleId, "CN-001");
        var other = CreateCreditNote(shopId, otherSaleId, "CN-002");

        await context.CreditNotes.AddRangeAsync(note, other);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var results = await repository.GetBySaleIdAsync(shopId, saleId);

        Assert.Single(results);
        Assert.Equal(note.Id, results[0].Id);
    }

    [Fact]
    public async Task GetBySaleIdAsync_ReturnsEmpty_WhenNoNotesForSale()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();

        var repository = new CreditNoteRepository(context);
        var results = await repository.GetBySaleIdAsync(shopId, Guid.NewGuid());

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetByIdWithRedemptionsAsync_IncludesRedemptions()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var redemptionSaleId = Guid.NewGuid();
        var note = CreateCreditNote(shopId, saleId, "CN-001");
        var redemptionResult = note.Redeem(shopId, redemptionSaleId, 50m);
        Assert.False(redemptionResult.IsError);

        await context.CreditNotes.AddAsync(note);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();

        var repository = new CreditNoteRepository(context);
        var found = await repository.GetByIdWithRedemptionsAsync(shopId, note.Id);

        Assert.NotNull(found);
        Assert.Single(found.Redemptions);
        Assert.Equal(50m, found.Redemptions[0].Amount);
    }

    [Fact]
    public async Task GetByIdWithRedemptionsAsync_ReturnsNull_WhenShopDoesNotMatch()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var otherShopId = Guid.NewGuid();
        var note = CreateCreditNote(shopId, Guid.NewGuid(), "CN-001");

        await context.CreditNotes.AddAsync(note);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var found = await repository.GetByIdWithRedemptionsAsync(otherShopId, note.Id);

        Assert.Null(found);
    }
}
