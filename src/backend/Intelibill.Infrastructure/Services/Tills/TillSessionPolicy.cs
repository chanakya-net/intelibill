using ErrorOr;
using Intelibill.Application.Features.Tills;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;
using TillErrors = Intelibill.Application.Common.Errors.Errors.Till;

namespace Intelibill.Infrastructure.Services.Tills;

internal sealed class TillSessionPolicy : ITillSessionPolicy
{
    private readonly TillSessionOptions _options;
    private readonly IShopRepository _shops;

    public TillSessionPolicy(IOptions<TillSessionOptions> options, IShopRepository shops)
    {
        _options = options.Value;
        _shops = shops;
        if (_options.MixedTenderEnabled)
            throw new OptionsValidationException(TillSessionOptions.SectionName, typeof(TillSessionOptions), ["Mixed tender is not supported."]);
    }

    public bool MixedTenderEnabled { get; }
    public bool IsEnabled(Guid shopId) => shopId != Guid.Empty && _options.Enabled && _options.AllowedShopIds.Contains(shopId);

    public async Task<ErrorOr<Success>> AuthorizeAsync(Guid shopId, Guid actorId, Guid cashierId, string? overrideReason, CancellationToken ct = default)
    {
        var role = await _shops.GetActiveRoleAsync(actorId, shopId, ct);
        if (role is null) return TillErrors.Forbidden;
        if (actorId == cashierId) return Result.Success;
        if (role is not (ShopRole.Owner or ShopRole.Manager) || await _shops.GetActiveRoleAsync(cashierId, shopId, ct) is null)
            return TillErrors.Forbidden;
        if (string.IsNullOrWhiteSpace(overrideReason) || overrideReason.Trim().Length > 500)
            return TillErrors.OverrideReasonRequired;
        return Result.Success;
    }

    public ErrorOr<Success> ValidateTradingDate(TillSession session, DateOnly sourceDate, DateOnly today)
    {
        if (session.State != TillSessionState.Open) return TillErrors.Closed;
        if (session.BusinessDate < today) return TillErrors.Overdue;
        if (session.BusinessDate != today || sourceDate != session.BusinessDate) return TillErrors.DateMismatch;
        return Result.Success;
    }
}
