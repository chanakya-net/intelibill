namespace Intelibill.Infrastructure.Options;

public sealed class TillSessionOptions
{
    public const string SectionName = "TillSessions";
    public bool Enabled { get; set; }
    public Guid[] AllowedShopIds { get; set; } = [];
    public bool MixedTenderEnabled { get; set; }
}
