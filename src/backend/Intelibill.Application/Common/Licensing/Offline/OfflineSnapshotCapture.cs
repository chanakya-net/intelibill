using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;

namespace Intelibill.Application.Common.Licensing.Offline;

/// <summary>
/// Immutable server-recorded completion of one snapshot stream. Produced only by
/// <see cref="OfflineSnapshotCapture"/> when its matching stream emits its completion record;
/// external callers cannot construct or mutate it.
/// </summary>
public sealed class OfflineSnapshotCompletion
{
    internal OfflineSnapshotCompletion(
        Guid snapshotId, Guid grantId, Guid shopId, Guid userId, Guid sessionId, string deviceId, Guid leaseId,
        long catalogVersion, int batchCount, DateTimeOffset startedAt, DateTimeOffset completedAt, Guid proofToken)
    {
        SnapshotId = snapshotId;
        GrantId = grantId;
        ShopId = shopId;
        UserId = userId;
        SessionId = sessionId;
        DeviceId = deviceId;
        LeaseId = leaseId;
        CatalogVersion = catalogVersion;
        BatchCount = batchCount;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        ProofToken = proofToken;
    }

    public Guid SnapshotId { get; }
    public Guid GrantId { get; }
    public Guid ShopId { get; }
    public Guid UserId { get; }
    public Guid SessionId { get; }
    public string DeviceId { get; }
    public Guid LeaseId { get; }
    public long CatalogVersion { get; }
    public int BatchCount { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset CompletedAt { get; }
    internal Guid ProofToken { get; }

    internal bool Matches(OfflineSnapshotCompletion other) =>
        SnapshotId == other.SnapshotId && GrantId == other.GrantId && ShopId == other.ShopId &&
        UserId == other.UserId && SessionId == other.SessionId &&
        string.Equals(DeviceId, other.DeviceId, StringComparison.Ordinal) && LeaseId == other.LeaseId &&
        CatalogVersion == other.CatalogVersion && BatchCount == other.BatchCount &&
        StartedAt == other.StartedAt && CompletedAt == other.CompletedAt && ProofToken == other.ProofToken;
}

/// <summary>
/// Server-owned capture context that fixes the snapshot, grant and lease scope at start and produces
/// <see cref="Completion"/> only when the matching snapshot stream reaches its completion record.
/// </summary>
public sealed class OfflineSnapshotCapture
{
    private readonly IOfflineSalesSnapshotStreamingService _streaming;
    private readonly OfflineSnapshotCaptureRegistry _registry;
    private readonly TimeProvider _clock;
    private readonly bool _validScope;
    private int _started;

    internal OfflineSnapshotCapture(
        SessionClaims? session, Guid grantId, Guid leaseId, string deviceId,
        IOfflineSalesSnapshotStreamingService streaming, OfflineSnapshotCaptureRegistry registry, TimeProvider clock)
    {
        _streaming = streaming;
        _registry = registry;
        _clock = clock;
        SnapshotId = Guid.NewGuid();
        GrantId = grantId;
        ShopId = session?.ActiveShopId ?? Guid.Empty;
        UserId = session?.UserId ?? Guid.Empty;
        SessionId = session?.SessionId ?? Guid.Empty;
        DeviceId = deviceId ?? string.Empty;
        LeaseId = leaseId;
        StartedAt = OfflinePermitContractV1.Instant(clock.GetUtcNow());
        _validScope = ShopId != Guid.Empty && UserId != Guid.Empty && SessionId != Guid.Empty &&
            GrantId != Guid.Empty && LeaseId != Guid.Empty && !string.IsNullOrWhiteSpace(DeviceId);
        if (_validScope)
            _registry.Begin(ShopId, SnapshotId);
    }

    public Guid SnapshotId { get; }
    public Guid GrantId { get; }
    public Guid ShopId { get; }
    public Guid UserId { get; }
    public Guid SessionId { get; }
    public string DeviceId { get; }
    public Guid LeaseId { get; }
    public DateTimeOffset StartedAt { get; }
    public OfflineSnapshotCompletion? Completion { get; private set; }
    public bool IsCompleted => Completion is not null;

    /// <summary>
    /// Streams the shop's snapshot under this capture's fixed scope. Only reaching the matching
    /// completion record marks <see cref="Completion"/>; early exit, cancellation or error leaves it null.
    /// </summary>
    public async IAsyncEnumerable<IOfflineSalesSnapshotStreamRecord> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("An offline snapshot capture can only be streamed once.");
        if (!_validScope)
            throw new InvalidOperationException("The offline snapshot capture scope is incomplete.");

        var state = new StreamProgress();
        try
        {
            await foreach (var record in _streaming.StreamAsync(ShopId, SnapshotId, StartedAt, ct))
            {
                AcceptRecord(record, state);
                yield return record;
            }
        }
        finally
        {
            if (!state.SawComplete)
                Invalidate();
        }
    }

    private void AcceptRecord(IOfflineSalesSnapshotStreamRecord record, StreamProgress state)
    {
        if (state.SawComplete)
        {
            Invalidate();
            throw new InvalidOperationException("Snapshot stream emitted records after completion.");
        }

        if (record is OfflineSalesSnapshotMetadataRecord meta)
        {
            if (state.CatalogVersion is not null || meta.Metadata.ShopId != ShopId ||
                meta.Metadata.SnapshotId != SnapshotId || meta.Metadata.CatalogVersion is not { } version || version < 0)
                throw new InvalidOperationException("Snapshot metadata does not match capture scope.");
            state.CatalogVersion = version;
            return;
        }

        if (record is OfflineSalesSnapshotBatchRecord)
        {
            if (state.CatalogVersion is null)
                throw new InvalidOperationException("Snapshot batch preceded metadata.");
            state.BatchCount++;
            return;
        }

        if (record is not OfflineSalesSnapshotCompleteRecord done)
            return;

        if (state.CatalogVersion is not { } catalogVersion || done.Complete.SnapshotId != SnapshotId ||
            done.Complete.BatchCount != state.BatchCount)
            throw new InvalidOperationException("Snapshot completion does not match emitted records.");
        var completedAt = OfflinePermitContractV1.Instant(_clock.GetUtcNow());
        if (completedAt < StartedAt)
            throw new InvalidOperationException("Snapshot completion time preceded capture start.");

        Completion = _registry.Complete(this, catalogVersion, state.BatchCount, completedAt);
        state.SawComplete = true;
    }

    private sealed class StreamProgress
    {
        public long? CatalogVersion { get; set; }
        public int BatchCount { get; set; }
        public bool SawComplete { get; set; }
    }

    private void Invalidate()
    {
        Completion = null;
        _registry.Abandon(ShopId, SnapshotId);
    }
}

/// <summary>Host-owned registry of server-started and completed snapshot captures.</summary>
public sealed class OfflineSnapshotCaptureRegistry
{
    private const int MaxEntries = 16384;
    private readonly ConcurrentDictionary<(Guid ShopId, Guid SnapshotId), Entry> _entries = new();
    private long _sequence;

    internal void Begin(Guid shopId, Guid snapshotId)
    {
        TrimIfNeeded();
        _entries[(shopId, snapshotId)] = new(Interlocked.Increment(ref _sequence), null);
    }

    internal OfflineSnapshotCompletion Complete(OfflineSnapshotCapture capture, long catalogVersion, int batchCount,
        DateTimeOffset completedAt)
    {
        var completion = new OfflineSnapshotCompletion(capture.SnapshotId, capture.GrantId, capture.ShopId,
            capture.UserId, capture.SessionId, capture.DeviceId, capture.LeaseId, catalogVersion, batchCount,
            capture.StartedAt, completedAt, Guid.NewGuid());
        _entries[(capture.ShopId, capture.SnapshotId)] = new(Interlocked.Increment(ref _sequence), completion);
        return completion;
    }

    internal void Abandon(Guid shopId, Guid snapshotId) => _entries.TryRemove((shopId, snapshotId), out _);

    internal bool TryVerifyCompleted(OfflineSnapshotCompletion completion) =>
        _entries.TryGetValue((completion.ShopId, completion.SnapshotId), out var entry) &&
        entry.Completion is { } recorded && recorded.Matches(completion);

    private void TrimIfNeeded()
    {
        if (_entries.Count < MaxEntries)
            return;
        foreach (var stale in _entries.OrderBy(pair => pair.Value.Order).Take(MaxEntries / 4).Select(pair => pair.Key))
            _entries.TryRemove(stale, out _);
    }

    private sealed record Entry(long Order, OfflineSnapshotCompletion? Completion);
}
