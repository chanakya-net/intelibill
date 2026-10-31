using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The manifest is only worth having if it cannot drift. One test compares it
/// to the EF model for exact set equality, so a table added by a future
/// migration fails here until someone has decided how the tenant boundary
/// treats it.
/// </summary>
[Collection("Integration Tests")]
public sealed class TenantPolicyManifestTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EveryMappedTable_HasExplicitSecurityClassification()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var mapped = context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(table => table is not null)
            .Select(table => table!)
            .ToHashSet(StringComparer.Ordinal);

        var classified = TenantPolicyManifest.Entries
            .Select(entry => entry.Table)
            .ToHashSet(StringComparer.Ordinal);

        var unclassified = mapped.Except(classified).Order(StringComparer.Ordinal).ToList();
        var stale = classified.Except(mapped).Order(StringComparer.Ordinal).ToList();

        Assert.Empty(unclassified);
        Assert.Empty(stale);
        Assert.Equal(TenantPolicyManifest.Entries.Count, classified.Count);
    }

    [Fact]
    public void EveryExemptTable_RecordsWhyItIsExempt()
    {
        var unexplained = TenantPolicyManifest.ExemptTables
            .Where(entry => string.IsNullOrWhiteSpace(entry.ExemptionReason))
            .Select(entry => entry.Table)
            .ToList();

        Assert.Empty(unexplained);
    }

    [Fact]
    public void EveryTenantTable_NamesItsPolicyPredicateAndIndex()
    {
        var incomplete = TenantPolicyManifest.TenantTables
            .Where(entry =>
                string.IsNullOrWhiteSpace(entry.PolicyName) ||
                string.IsNullOrWhiteSpace(entry.PredicateColumn) ||
                string.IsNullOrWhiteSpace(entry.RequiredIndexColumn) ||
                entry.ExemptionReason is not null)
            .Select(entry => entry.Table)
            .ToList();

        Assert.Empty(incomplete);

        var childrenWithoutParent = TenantPolicyManifest.TenantTables
            .Where(entry => entry.Classification == TenantSecurityClassification.TenantChildData)
            .Where(entry => string.IsNullOrWhiteSpace(entry.ParentTable))
            .Select(entry => entry.Table)
            .ToList();

        Assert.Empty(childrenWithoutParent);
    }
}
