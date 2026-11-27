namespace Intelibill.Infrastructure.Options;

public sealed class SaleCancellationOptions
{
    public const string SectionName = "SaleCancellation";
    public bool Enabled { get; set; }
    public Guid[] AllowedShopIds { get; set; } = [];
}
