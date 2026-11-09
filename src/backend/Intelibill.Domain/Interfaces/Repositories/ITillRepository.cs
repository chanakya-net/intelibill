using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ITillRepository
{
    Task<ShopTillSetting?> GetSettingsAsync(Guid shopId, CancellationToken ct = default);
    Task<(int Open, int AwaitingReview, int PendingOffline)> GetSettingsBlockersAsync(Guid shopId, CancellationToken ct = default);
    Task SaveSettingsAsync(ShopTillSetting settings, CancellationToken ct = default);
    Task AddSettingsEventAsync(ShopTillSettingEvent audit, CancellationToken ct = default);
    /// <summary>Shared shop gate held to source commit, after period fences and before business locks.</summary>
    Task<RowLockOutcome> LockSettingsActorAsync(Guid actorId, CancellationToken ct = default);
    Task<RowLockOutcome> LockSettingsReadAsync(Guid shopId, CancellationToken ct = default);
    Task<Till?> GetTillAsync(Guid shopId, Guid tillId, CancellationToken ct = default);
    Task<IReadOnlyList<Till>> ListTillsAsync(Guid shopId, CancellationToken ct = default);
    Task<TillSession?> GetSessionAsync(Guid shopId, Guid sessionId, CancellationToken ct = default);
    Task<TillSession?> GetOpenSessionAsync(Guid shopId, Guid tillId, CancellationToken ct = default);
    Task<(IReadOnlyList<TillSession> Items, int TotalCount)> ListSessionsAsync(Guid shopId, Guid? cashierId,
        TillSessionState? state, DateOnly? businessDate, int page, int pageSize, CancellationToken ct = default);
    /// <summary>Requires an open transaction; timeout aborts the transaction. Caller rolls back.</summary>
    Task<RowLockOutcome> LockCatalogAsync(Guid shopId, CancellationToken ct = default);
    Task<RowLockOutcome> LockTillAsync(Guid shopId, Guid tillId, CancellationToken ct = default);
    Task<RowLockOutcome> LockSessionAsync(Guid shopId, Guid sessionId, CancellationToken ct = default);
    Task<TillSession?> ReloadSessionAsync(Guid shopId, Guid sessionId, CancellationToken ct = default);
    Task<TillTotals> GetTotalsAsync(Guid shopId, Guid sessionId, CancellationToken ct = default);
    Task<(IReadOnlyList<TillMovement> Items, int TotalCount)> GetMovementsAsync(Guid shopId, Guid sessionId, int page, int pageSize, CancellationToken ct = default);
    Task<(IReadOnlyList<TillSourceRecord> Items, int TotalCount)> GetSourcesAsync(Guid shopId, Guid sessionId, int page, int pageSize, CancellationToken ct = default);
    /// <summary>Tenant-bounded, read-only navigation identities; financial parents are omitted for Staff.</summary>
    Task<IReadOnlyDictionary<(TillMovementSource Type, Guid Id), TillSourceReference>> GetSourceReferencesAsync(
        Guid shopId, IReadOnlyCollection<(TillMovementSource Type, Guid Id)> sources, bool includeFinancial, CancellationToken ct = default);
    Task<TillSourceRecord?> FindSourceAsync(Guid shopId, TillMovementSource sourceType, Guid sourceId, string purpose, CancellationToken ct = default);
    Task<TillClose?> GetCloseAsync(Guid shopId, Guid sessionId, CancellationToken ct = default);
    Task<TillCloseReview?> GetReviewAsync(Guid shopId, Guid closeId, CancellationToken ct = default);
    Task<IReadOnlyList<TillLatePosting>> GetLatePostingsAsync(Guid shopId, Guid sessionId, CancellationToken ct = default);
    Task<TillLatePosting?> FindLatePostingAsync(Guid shopId, Guid submissionId, Guid saleId, CancellationToken ct = default);
    Task AddTillAsync(Till till, CancellationToken ct = default);
    Task AddSessionAsync(TillSession session, CancellationToken ct = default);
    Task AddSourceAsync(TillSourceRecord source, CancellationToken ct = default);
    Task AddMovementAsync(TillMovement movement, CancellationToken ct = default);
    Task AddCloseAsync(TillClose close, CancellationToken ct = default);
    Task AddReviewAsync(TillCloseReview review, CancellationToken ct = default);
    Task AddLatePostingAsync(TillLatePosting latePosting, CancellationToken ct = default);
}

public sealed record TillSourceReference(Guid? ParentSaleId = null, Guid? PaymentReceiptId = null, Guid? ExpenseId = null);
