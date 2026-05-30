using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class ServiceCodeSequence : BaseEntity
{
    public const string CodePrefix = "SRV";

    public Guid ShopId { get; private set; }
    public int NextNumber { get; private set; }
    public string Prefix { get; private set; } = string.Empty;

    private ServiceCodeSequence() { }

    public static ServiceCodeSequence Create(Guid shopId)
    {
        return new ServiceCodeSequence
        {
            ShopId = shopId,
            NextNumber = 1,
            Prefix = CodePrefix,
        };
    }

    public string NextCode()
    {
        var code = $"{Prefix}-{NextNumber:D6}";
        NextNumber++;
        return code;
    }
}
