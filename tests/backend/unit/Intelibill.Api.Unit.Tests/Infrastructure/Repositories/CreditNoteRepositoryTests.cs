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

    private static CreditNote CreateCreditNote(Guid shopId, Guid saleReturnId, string code = "CN-001")
    {
        var result = CreditNote.Issue(shopId, saleReturnId, 100m, "Test reason", code, null);
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
    public async Task GetByCodeAsync_ReturnsNote_WhenCodeHasLeadingTrailingWhitespace()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var note = CreateCreditNote(shopId, Guid.NewGuid(), "CN-002");

        await context.CreditNotes.AddAsync(note);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var found = await repository.GetByCodeAsync(shopId, "  CN-002  ");

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
    public async Task GetByCodeAsync_IncludesRedemptionsForVoidChecks()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var note = CreateCreditNote(shopId, Guid.NewGuid(), "CN-RED");
        var redemptionResult = note.Redeem(shopId, Guid.NewGuid(), 25m);
        Assert.False(redemptionResult.IsError);

        await context.CreditNotes.AddAsync(note);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var repository = new CreditNoteRepository(context);
        var found = await repository.GetByCodeAsync(shopId, "CN-RED");

        Assert.NotNull(found);
        Assert.Single(found.Redemptions);
        Assert.Equal(25m, found.Redemptions[0].Amount);
    }

    [Fact]
    public async Task GetByReturnIdAsync_ReturnsNotesForMatchingReturn()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var returnId = Guid.NewGuid();
        var otherReturnId = Guid.NewGuid();
        var note = CreateCreditNote(shopId, returnId, "CN-001");
        var other = CreateCreditNote(shopId, otherReturnId, "CN-002");

        await context.CreditNotes.AddRangeAsync(note, other);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var results = await repository.GetByReturnIdAsync(shopId, returnId);

        Assert.Single(results);
        Assert.Equal(note.Id, results[0].Id);
    }

    [Fact]
    public async Task GetByReturnIdAsync_ReturnsEmpty_WhenNoNotesForReturn()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();

        var repository = new CreditNoteRepository(context);
        var results = await repository.GetByReturnIdAsync(shopId, Guid.NewGuid());

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetByReturnIdAsync_IsolatesReturnsSharingSameSale()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var returnId1 = Guid.NewGuid();
        var returnId2 = Guid.NewGuid();
        var note1 = CreateCreditNote(shopId, returnId1, "CN-010");
        var note2 = CreateCreditNote(shopId, returnId2, "CN-011");

        await context.CreditNotes.AddRangeAsync(note1, note2);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var results1 = await repository.GetByReturnIdAsync(shopId, returnId1);
        var results2 = await repository.GetByReturnIdAsync(shopId, returnId2);

        Assert.Single(results1);
        Assert.Equal(note1.Id, results1[0].Id);
        Assert.Single(results2);
        Assert.Equal(note2.Id, results2[0].Id);
    }

    [Fact]
    public async Task GetByIdWithRedemptionsAsync_IncludesRedemptions()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var saleReturnId = Guid.NewGuid();
        var redemptionSaleId = Guid.NewGuid();
        var note = CreateCreditNote(shopId, saleReturnId, "CN-001");
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
