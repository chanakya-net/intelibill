using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using TillErrors = Intelibill.Application.Common.Errors.Errors.Till;

namespace Intelibill.Application.Features.Tills.Services;

public sealed class TillReadService(ITillRepository tills, IShopRepository shops, IOperationReceiptRepository receipts,
    ITillSessionPolicy policy, IBusinessClock clock, TillDtoBuilder builder)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<ErrorOr<TillCapabilitiesDto>> CapabilitiesAsync(Guid shopId, Guid actorId, CancellationToken ct)
    {
        if (await shops.GetActiveRoleAsync(actorId, shopId, ct) is null) return TillErrors.Forbidden;
        var shop = await shops.GetByIdAsync(shopId, ct);
        return shop is null ? TillErrors.NotFound : new TillCapabilitiesDto(await policy.IsEnabledAsync(shopId, ct), false, clock.Today(shop.BusinessTimeZoneId));
    }
    public async Task<ErrorOr<IReadOnlyList<TillDto>>> TillsAsync(Guid shopId, Guid actorId, CancellationToken ct)
    {
        if (await shops.GetActiveRoleAsync(actorId, shopId, ct) is null) return TillErrors.Forbidden;
        return (await tills.ListTillsAsync(shopId, ct)).Select(x => new TillDto(x.Id, x.Name)).ToArray();
    }
    public async Task<ErrorOr<TillPage<TillSessionDto>>> SessionsAsync(Guid shopId, Guid actorId, TillSessionState? state,
        DateOnly? date, int page, int pageSize, CancellationToken ct)
    {
        if (!ValidPage(page, pageSize) || (state.HasValue && !Enum.IsDefined(state.Value))) return Error.Validation("Till.QueryInvalid", "Use valid state and paging values.");
        var role = await shops.GetActiveRoleAsync(actorId, shopId, ct);
        if (role is null) return TillErrors.Forbidden;
        var rows = await tills.ListSessionsAsync(shopId, role == ShopRole.Staff ? actorId : null, state, date, page, pageSize, ct);
        var dtos = new List<TillSessionDto>();
        foreach (var row in rows.Items) dtos.Add(await builder.BuildAsync(row, ct));
        return new TillPage<TillSessionDto>(dtos, page, pageSize, rows.TotalCount);
    }
    public async Task<ErrorOr<TillSessionDetailDto>> SessionAsync(Guid shopId, Guid actorId, Guid sessionId, int page, int pageSize, CancellationToken ct)
    {
        if (!ValidPage(page, pageSize)) return Error.Validation("Till.QueryInvalid", "Use valid paging values.");
        var role = await shops.GetActiveRoleAsync(actorId, shopId, ct);
        if (role is null) return TillErrors.Forbidden;
        var row = await tills.GetSessionAsync(shopId, sessionId, ct);
        if (row is null) return TillErrors.SessionNotFound;
        if (role == ShopRole.Staff && row.CashierId != actorId) return TillErrors.Forbidden;
        return await builder.DetailAsync(row, page, pageSize, role is ShopRole.Owner or ShopRole.Manager, ct);
    }
    public async Task<ErrorOr<TillOperationDto>> OperationAsync(Guid shopId, Guid actorId, string type, string key, CancellationToken ct)
    {
        var role = await shops.GetActiveRoleAsync(actorId, shopId, ct);
        if (role is null) return TillErrors.Forbidden;
        var operationType = type switch
        {
            "create" => FinancialOperationType.TillCreate,
            "open" => FinancialOperationType.TillOpen,
            "cash-movement" => FinancialOperationType.TillCashMovement,
            "close" => FinancialOperationType.TillClose,
            "review" => FinancialOperationType.TillCloseReview,
            _ => (FinancialOperationType?)null
        };
        if (operationType is null) return Error.Validation("Operation.UnknownType", "Unknown till operation.");
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 128) return Error.Validation("Operation.InvalidKey", "A valid operation key is required.");
        var receipt = await receipts.GetCompletedAsync(shopId, operationType.Value, key.Trim(), ct);
        if (receipt?.ResponseBody is null || receipt.CompletedAt is null) return Error.NotFound("Operation.NotFound", "No committed outcome is visible.");
        if (role == ShopRole.Staff && receipt.ActorUserId != actorId) return TillErrors.Forbidden;
        object response = operationType == FinancialOperationType.TillCreate
            ? JsonSerializer.Deserialize<TillDto>(receipt.ResponseBody, JsonOptions)!
            : JsonSerializer.Deserialize<TillSessionDto>(receipt.ResponseBody, JsonOptions)!;
        return new TillOperationDto(type, receipt.OperationKey, receipt.TargetId, receipt.ActorUserId, receipt.CompletedAt.Value, response);
    }
    private static bool ValidPage(int page, int pageSize) => page >= 1 && pageSize is >= 1 and <= 100 && page <= int.MaxValue / pageSize;
}
