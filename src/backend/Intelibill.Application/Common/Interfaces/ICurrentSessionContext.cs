namespace Intelibill.Application.Common.Interfaces;

public interface ICurrentSessionContext
{
    Guid? UserId { get; }
    Guid? ActiveShopId { get; }
}