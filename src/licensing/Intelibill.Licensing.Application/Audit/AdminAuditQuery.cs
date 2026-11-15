using ErrorOr;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Audit;

public sealed class AdminAuditQuery(IAdminAuditStore store)
{
    public const string IdentityScope = "provider-identity";
    public const int MaxPageSize = 100;

    public Task<ErrorOr<AdminAuditPage>> ReadAsync(Guid actor, string? action, string? fromUtc,
        string? toUtc, string? pageSize, string? cursor, CancellationToken token)
    {
        var filters = AdminAuditFilters.Parse(action, fromUtc, toUtc, pageSize, cursor);
        return filters is null
            ? Task.FromResult<ErrorOr<AdminAuditPage>>(
                Error.Validation("Admin.AuditInvalidFilter", "Check the audit filters and page cursor."))
            : ReadPageAsync(actor, filters, token);
    }

    private async Task<ErrorOr<AdminAuditPage>> ReadPageAsync(
        Guid actor, AdminAuditFilters filters, CancellationToken token)
    {
        var rows = await store.ReadAsync(filters, actor, token);
        var items = rows.Take(filters.PageSize).ToArray();
        var cursor = rows.Count > filters.PageSize
            ? filters.EncodeCursor(items[^1].OccurredAt, items[^1].Id) : null;
        return new AdminAuditPage(items, cursor);
    }
}

public interface IAdminAuditStore
{
    Task<IReadOnlyList<AdminAuditItem>> ReadAsync(AdminAuditFilters filters, Guid actor, CancellationToken token);
    Task DeniedAsync(Guid actor, BootstrapAuditAction action, AuditReason reason, CancellationToken token);
}

public sealed record AdminAuditPage(IReadOnlyList<AdminAuditItem> Items, string? NextCursor);

public sealed record AdminAuditItem(Guid Id, string Scope, string Action, string Outcome,
    DateTimeOffset OccurredAt, string? ActorKind, Guid? ActorId, string? ReasonCode, Guid? CorrelationId,
    AdminAuditReference? Before, AdminAuditReference? After);

public sealed record AdminAuditReference(string TargetKind, Guid? TargetId, string State,
    uint? Version, DateTimeOffset? MilestoneAt, string[]? Capabilities);
