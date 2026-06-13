using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
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

    private static async Task<(Sale sale, SaleReturn saleReturn)> SeedSaleAndReturnAsync(
        ApplicationDbContext context,
        Guid shopId,
        string invoiceNumber = "INV-001",
        string returnNumber = "RET-001",
        string? customerName = null)
    {
        var sale = Sale.Create(
            shopId, invoiceNumber, null, customerName, null,
            PaymentMethod.Cash, DateTimeOffset.UtcNow, 100m, 0m, 100m, 0m, []);
        await context.Sales.AddAsync(sale);

        var saleReturn = SaleReturn.Record(
            shopId, sale.Id, returnNumber,
            DateTimeOffset.UtcNow, Guid.NewGuid(), null,
            0m, 0m, 0m, null, 0m, 0m, null, null, []).Value;
        await context.SaleReturns.AddAsync(saleReturn);

        await context.SaveChangesAsync();
        return (sale, saleReturn);
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
    public async Task GetByCodeAsync_DoesNotIncludeRedemptions()
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
        Assert.Empty(found.Redemptions);
    }

    [Fact]
    public async Task GetByCodeWithRedemptionsAsync_IncludesRedemptions()
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
        var found = await repository.GetByCodeWithRedemptionsAsync(shopId, "CN-RED");

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

    [Fact]
    public async Task GetPagedAsync_ReturnsAll_WhenNoFilters()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var (_, saleReturn) = await SeedSaleAndReturnAsync(context, shopId);
        var note = CreateCreditNote(shopId, saleReturn.Id, "CN-100");
        await context.CreditNotes.AddAsync(note);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var (items, totalCount) = await repository.GetPagedAsync(shopId, null, null, 1, 20);

        Assert.Equal(1, totalCount);
        Assert.Single(items);
        Assert.Equal("CN-100", items[0].Code);
    }

    [Fact]
    public async Task GetPagedAsync_IsolatesOtherShops()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var otherShopId = Guid.NewGuid();
        var (_, sr1) = await SeedSaleAndReturnAsync(context, shopId);
        var (_, sr2) = await SeedSaleAndReturnAsync(context, otherShopId, "INV-002", "RET-002");
        var note1 = CreateCreditNote(shopId, sr1.Id, "CN-001");
        var note2 = CreateCreditNote(otherShopId, sr2.Id, "CN-002");
        await context.CreditNotes.AddRangeAsync(note1, note2);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var (items, totalCount) = await repository.GetPagedAsync(shopId, null, null, 1, 20);

        Assert.Equal(1, totalCount);
        Assert.Equal("CN-001", items[0].Code);
    }

    [Fact]
    public async Task GetPagedAsync_Paginates()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var (_, sr1) = await SeedSaleAndReturnAsync(context, shopId, "INV-001", "RET-001");
        var (_, sr2) = await SeedSaleAndReturnAsync(context, shopId, "INV-002", "RET-002");
        var note1 = CreateCreditNote(shopId, sr1.Id, "CN-001");
        var note2 = CreateCreditNote(shopId, sr2.Id, "CN-002");
        await context.CreditNotes.AddRangeAsync(note1, note2);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var (items, totalCount) = await repository.GetPagedAsync(shopId, null, null, 1, 1);

        Assert.Equal(2, totalCount);
        Assert.Single(items);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersVoidedStatus()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var (_, sr1) = await SeedSaleAndReturnAsync(context, shopId, "INV-001", "RET-001");
        var (_, sr2) = await SeedSaleAndReturnAsync(context, shopId, "INV-002", "RET-002");
        var active = CreateCreditNote(shopId, sr1.Id, "CN-A01");
        var voided = CreateCreditNote(shopId, sr2.Id, "CN-V01");
        voided.Void("test void");
        await context.CreditNotes.AddRangeAsync(active, voided);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var (items, totalCount) = await repository.GetPagedAsync(shopId, null, CreditNoteStatus.Voided, 1, 20);

        Assert.Equal(1, totalCount);
        Assert.Equal("CN-V01", items[0].Code);
        Assert.True(items[0].IsVoided);
    }

    [Fact]
    public async Task GetPagedAsync_IncludesReturnNumberAndInvoiceNumber()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var (_, saleReturn) = await SeedSaleAndReturnAsync(context, shopId, "INV-XYZ", "RET-XYZ");
        var note = CreateCreditNote(shopId, saleReturn.Id, "CN-X01");
        await context.CreditNotes.AddAsync(note);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var (items, _) = await repository.GetPagedAsync(shopId, null, null, 1, 20);

        Assert.Single(items);
        Assert.Equal("RET-XYZ", items[0].ReturnNumber);
        Assert.Equal("INV-XYZ", items[0].InvoiceNumber);
    }

    [Theory]
    [InlineData("CN-SRCH")]
    [InlineData("cn-srch")]
    [InlineData("CN-")]
    public async Task GetPagedAsync_SearchMatchesCreditNoteCode(string searchTerm)
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var (_, sr1) = await SeedSaleAndReturnAsync(context, shopId, "INV-001", "RET-001");
        var (_, sr2) = await SeedSaleAndReturnAsync(context, shopId, "INV-002", "RET-002");
        var match = CreateCreditNote(shopId, sr1.Id, "CN-SRCH");
        var noMatch = CreateCreditNote(shopId, sr2.Id, "XX-999");
        await context.CreditNotes.AddRangeAsync(match, noMatch);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var (items, totalCount) = await repository.GetPagedAsync(shopId, searchTerm, null, 1, 20);

        Assert.Equal(1, totalCount);
        Assert.Equal("CN-SRCH", items[0].Code);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesReturnNumber()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var (_, sr1) = await SeedSaleAndReturnAsync(context, shopId, "INV-001", "RET-FIND");
        var (_, sr2) = await SeedSaleAndReturnAsync(context, shopId, "INV-002", "RET-OTHER");
        var match = CreateCreditNote(shopId, sr1.Id, "CN-001");
        var noMatch = CreateCreditNote(shopId, sr2.Id, "CN-002");
        await context.CreditNotes.AddRangeAsync(match, noMatch);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var (items, totalCount) = await repository.GetPagedAsync(shopId, "RET-FIND", null, 1, 20);

        Assert.Equal(1, totalCount);
        Assert.Equal("CN-001", items[0].Code);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesInvoiceNumber()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var (_, sr1) = await SeedSaleAndReturnAsync(context, shopId, "INV-FIND", "RET-001");
        var (_, sr2) = await SeedSaleAndReturnAsync(context, shopId, "INV-OTHER", "RET-002");
        var match = CreateCreditNote(shopId, sr1.Id, "CN-001");
        var noMatch = CreateCreditNote(shopId, sr2.Id, "CN-002");
        await context.CreditNotes.AddRangeAsync(match, noMatch);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var (items, totalCount) = await repository.GetPagedAsync(shopId, "INV-FIND", null, 1, 20);

        Assert.Equal(1, totalCount);
        Assert.Equal("CN-001", items[0].Code);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesCustomerName()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var (_, sr1) = await SeedSaleAndReturnAsync(context, shopId, "INV-001", "RET-001", "Alice Smith");
        var (_, sr2) = await SeedSaleAndReturnAsync(context, shopId, "INV-002", "RET-002", "Bob Jones");
        var match = CreateCreditNote(shopId, sr1.Id, "CN-001");
        var noMatch = CreateCreditNote(shopId, sr2.Id, "CN-002");
        await context.CreditNotes.AddRangeAsync(match, noMatch);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var (items, totalCount) = await repository.GetPagedAsync(shopId, "alice", null, 1, 20);

        Assert.Equal(1, totalCount);
        Assert.Equal("CN-001", items[0].Code);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersActiveStatus()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var future = DateTimeOffset.UtcNow.AddDays(7);
        var past = DateTimeOffset.UtcNow.AddDays(-1);
        var (_, sr1) = await SeedSaleAndReturnAsync(context, shopId, "INV-001", "RET-001");
        var (_, sr2) = await SeedSaleAndReturnAsync(context, shopId, "INV-002", "RET-002");
        var (_, sr3) = await SeedSaleAndReturnAsync(context, shopId, "INV-003", "RET-003");
        var active = CreditNote.Issue(shopId, sr1.Id, 100m, "r", "CN-A", future).Value;
        var expired = CreditNote.Issue(shopId, sr2.Id, 100m, "r", "CN-E", past).Value;
        var voided = CreateCreditNote(shopId, sr3.Id, "CN-V");
        voided.Void("reason");
        await context.CreditNotes.AddRangeAsync(active, expired, voided);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var (items, totalCount) = await repository.GetPagedAsync(shopId, null, CreditNoteStatus.Active, 1, 20);

        Assert.Equal(1, totalCount);
        Assert.Equal("CN-A", items[0].Code);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersExpiredStatus()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var future = DateTimeOffset.UtcNow.AddDays(7);
        var past = DateTimeOffset.UtcNow.AddDays(-1);
        var (_, sr1) = await SeedSaleAndReturnAsync(context, shopId, "INV-001", "RET-001");
        var (_, sr2) = await SeedSaleAndReturnAsync(context, shopId, "INV-002", "RET-002");
        var active = CreditNote.Issue(shopId, sr1.Id, 100m, "r", "CN-A", future).Value;
        var expired = CreditNote.Issue(shopId, sr2.Id, 100m, "r", "CN-E", past).Value;
        await context.CreditNotes.AddRangeAsync(active, expired);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var (items, totalCount) = await repository.GetPagedAsync(shopId, null, CreditNoteStatus.Expired, 1, 20);

        Assert.Equal(1, totalCount);
        Assert.Equal("CN-E", items[0].Code);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersFullyRedeemedStatus()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        var (_, sr1) = await SeedSaleAndReturnAsync(context, shopId, "INV-001", "RET-001");
        var (_, sr2) = await SeedSaleAndReturnAsync(context, shopId, "INV-002", "RET-002");
        var active = CreateCreditNote(shopId, sr1.Id, "CN-A");
        var redeemed = CreateCreditNote(shopId, sr2.Id, "CN-R");
        redeemed.Redeem(shopId, Guid.NewGuid(), 100m);
        await context.CreditNotes.AddRangeAsync(active, redeemed);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var (items, totalCount) = await repository.GetPagedAsync(shopId, null, CreditNoteStatus.FullyRedeemed, 1, 20);

        Assert.Equal(1, totalCount);
        Assert.Equal("CN-R", items[0].Code);
    }

    [Fact]
    public async Task GetPagedAsync_NoteExpiringExactlyNow_TreatedAsActive()
    {
        await using var context = await CreateContextAsync();
        var shopId = Guid.NewGuid();
        // Use a near-future timestamp: repository Active filter uses >= now, so a note expiring
        // just after now is Active (not Expired). This pins the >= vs > boundary distinction.
        var nearFuture = DateTimeOffset.UtcNow.AddMilliseconds(500);
        var (_, sr) = await SeedSaleAndReturnAsync(context, shopId);
        var note = CreditNote.Issue(shopId, sr.Id, 100m, "r", "CN-NOW", nearFuture).Value;
        await context.CreditNotes.AddAsync(note);
        await context.SaveChangesAsync();

        var repository = new CreditNoteRepository(context);
        var (activeItems, activeCount) = await repository.GetPagedAsync(shopId, null, CreditNoteStatus.Active, 1, 20);
        var (_, expiredCount) = await repository.GetPagedAsync(shopId, null, CreditNoteStatus.Expired, 1, 20);

        Assert.Equal(1, activeCount);
        Assert.Equal("CN-NOW", activeItems[0].Code);
        Assert.Equal(0, expiredCount);
    }
}
