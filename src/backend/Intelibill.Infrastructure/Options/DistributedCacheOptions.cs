namespace Intelibill.Infrastructure.Options;

public class DistributedCacheOptions
{
    public const string SectionName = "DistributedCache";

    public string SchemaName { get; set; } = "public";
    public string TableName { get; set; } = "cache_entries";
    public bool CreateIfNotExists { get; set; } = true;
    public TimeSpan? ExpiredItemsDeletionInterval { get; set; } = TimeSpan.FromMinutes(30);
}
