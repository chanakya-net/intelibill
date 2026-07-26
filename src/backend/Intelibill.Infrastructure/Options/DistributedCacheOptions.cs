namespace Intelibill.Infrastructure.Options;

public class DistributedCacheOptions
{
    public const string SectionName = "DistributedCache";

    public string SchemaName { get; set; } = "public";
    public string TableName { get; set; } = "cache_entries";

    /// <summary>
    /// Off by default: the table is created by the AddDistributedCacheTable
    /// migration in every environment. Deployed runtime identities hold no CREATE
    /// on the schema, so leaving this on would turn a missing table into a
    /// permission error at the first cache write instead of a migration that
    /// never ran.
    /// </summary>
    public bool CreateIfNotExists { get; set; }
    public TimeSpan? ExpiredItemsDeletionInterval { get; set; } = TimeSpan.FromMinutes(30);
}
