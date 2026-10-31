using Intelibill.Application.Common.Interfaces;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Stands in for the JWT-backed session context so a test can decide what the
/// session interceptor writes onto the next connection, including the empty
/// state of an account with no active shop.
/// </summary>
internal sealed class MutableSessionContext : ICurrentSessionContext
{
    public Guid? UserId { get; set; }

    public Guid? ActiveShopId { get; set; }

    public string ExpectedShopSetting => ActiveShopId?.ToString() ?? string.Empty;

    public string ExpectedUserSetting => UserId?.ToString() ?? string.Empty;
}
