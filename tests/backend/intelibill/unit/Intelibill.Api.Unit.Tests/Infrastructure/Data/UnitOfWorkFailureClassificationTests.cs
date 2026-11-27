using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Update;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Data;

public sealed class UnitOfWorkFailureClassificationTests : IDisposable
{
    private readonly ApplicationDbContext _context = new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private UnitOfWork CreateSut() => new(_context);

    public void Dispose() => _context.Dispose();

    [Fact]
    public void ConcurrencyException_IsConcurrencyConflictAndWriteFailure()
    {
        var sut = CreateSut();
        var exception = new DbUpdateConcurrencyException("conflict", new List<IUpdateEntry>());

        Assert.True(sut.IsConcurrencyConflict(exception));
        Assert.True(sut.IsWriteFailure(exception));
    }

    [Fact]
    public void PlainDbUpdateException_IsWriteFailureOnly()
    {
        var sut = CreateSut();
        var exception = new DbUpdateException("failed");

        Assert.False(sut.IsConcurrencyConflict(exception));
        Assert.True(sut.IsWriteFailure(exception));
        Assert.Empty(sut.ConflictedEntities(exception));
    }

    [Fact]
    public void UnrelatedException_IsNeitherConflictNorWriteFailure()
    {
        var sut = CreateSut();
        var exception = new InvalidOperationException("other");

        Assert.False(sut.IsConcurrencyConflict(exception));
        Assert.False(sut.IsWriteFailure(exception));
        Assert.Empty(sut.ConflictedEntities(exception));
    }

    [Fact]
    public void ExceptionWrappingConcurrencyConflict_IsNotUnwrapped()
    {
        var sut = CreateSut();
        var exception = new InvalidOperationException(
            "wrapper", new DbUpdateConcurrencyException("conflict", new List<IUpdateEntry>()));

        Assert.False(sut.IsConcurrencyConflict(exception));
        Assert.False(sut.IsWriteFailure(exception));
        Assert.Empty(sut.ConflictedEntities(exception));
    }

    [Fact]
    public void AggregateWrappingWriteFailure_IsNotUnwrapped()
    {
        var sut = CreateSut();
        var exception = new AggregateException(new DbUpdateException("failed"));

        Assert.False(sut.IsConcurrencyConflict(exception));
        Assert.False(sut.IsWriteFailure(exception));
    }

    [Fact]
    public void ConflictedEntities_ReturnsEntriesEntitiesInOrder()
    {
        var sut = CreateSut();
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var first = Item.Create(shopId, "Rice", "Premium Rice", "kg", "111", true, actorId);
        var second = Item.Create(shopId, "Dal", "Toor Dal", "kg", "222", true, actorId);
        var exception = new ConcurrencyConflictWithEntries([_context.Entry(first), _context.Entry(second)]);

        var entities = sut.ConflictedEntities(exception);

        Assert.Equal(new object[] { first, second }, entities);
    }

    [Fact]
    public void ConflictedEntities_ConcurrencyWithoutEntries_ReturnsEmpty()
    {
        var sut = CreateSut();

        Assert.Empty(sut.ConflictedEntities(new DbUpdateConcurrencyException("conflict", new List<IUpdateEntry>())));
    }

    [Fact]
    public async Task ReloadConflictedEntitiesAsync_ForNonConcurrencyFailure_DoesNothing()
    {
        var sut = CreateSut();
        var item = Item.Create(Guid.NewGuid(), "Rice", "Premium Rice", "kg", "111", true, Guid.NewGuid());
        _context.Add(item);
        var entry = _context.Entry(item);

        await sut.ReloadConflictedEntitiesAsync(new WriteFailureWithEntries([entry]));
        await sut.ReloadConflictedEntitiesAsync(new InvalidOperationException("other"));

        Assert.Equal(EntityState.Added, entry.State);
    }

    [Fact]
    public async Task ReloadConflictedEntitiesAsync_ForConcurrencyConflict_ReloadsEachEntry()
    {
        var sut = CreateSut();
        var item = Item.Create(Guid.NewGuid(), "Rice", "Premium Rice", "kg", "111", true, Guid.NewGuid());
        _context.Add(item);
        await _context.SaveChangesAsync();
        var entry = _context.Entry(item);
        entry.State = EntityState.Modified;

        await sut.ReloadConflictedEntitiesAsync(
            new ConcurrencyConflictWithEntries([entry]));

        // Reloading takes the stored values again, so the pending modification is gone.
        Assert.Equal(EntityState.Unchanged, entry.State);
    }

    // EF Core builds entry lists only from its internal update entries, so the tests supply them directly.
    private sealed class ConcurrencyConflictWithEntries(IReadOnlyList<EntityEntry> entries)
        : DbUpdateConcurrencyException("conflict")
    {
        public override IReadOnlyList<EntityEntry> Entries => entries;
    }

    private sealed class WriteFailureWithEntries(IReadOnlyList<EntityEntry> entries)
        : DbUpdateException("failed")
    {
        public override IReadOnlyList<EntityEntry> Entries => entries;
    }
}
