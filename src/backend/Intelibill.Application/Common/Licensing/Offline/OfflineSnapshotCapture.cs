using System.Runtime.CompilerServices;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;

namespace Intelibill.Application.Common.Licensing.Offline;

/// <summary>
/// Immutable server-recorded completion of one snapshot stream. Produced only by
/// <see cref="OfflineSnapshotCapture"/> when its matching stream emits its completion record;
/// external callers cannot construct or mutate it. The object itself is the proof: the issuing host
/// recognizes this instance, never an equal copy.
/// </summary>
public sealed class OfflineSnapshotCompletion
{
    internal OfflineSnapshotCompletion(
        Guid snapshotId, Guid grantId, Guid shopId, Guid userId, Guid sessionId, string deviceId, Guid leaseId,
        long catalogVersion, int batchCount, DateTimeOffset startedAt, DateTimeOffset completedAt)
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
        if (Completion is { } recorded)
            _registry.Invalidate(recorded);
        Completion = null;
    }
}

/// <summary>
/// Host-owned proof of genuine completions, keyed by the completion object's identity and kept only
/// while that object is alive. A caller holding its completion can issue and replay on this host for
/// as long as it holds it, whatever other captures begin; a dropped completion costs nothing. A copy,
/// a completion made on another host and an invalidated one are all unknown here.
/// </summary>
public sealed class OfflineSnapshotCaptureRegistry
{
    private static readonly object Recorded = new();
    private readonly ConditionalWeakTable<OfflineSnapshotCompletion, object> _completed = new();

    internal OfflineSnapshotCompletion Complete(OfflineSnapshotCapture capture, long catalogVersion, int batchCount,
        DateTimeOffset completedAt)
    {
        var completion = new OfflineSnapshotCompletion(capture.SnapshotId, capture.GrantId, capture.ShopId,
            capture.UserId, capture.SessionId, capture.DeviceId, capture.LeaseId, catalogVersion, batchCount,
            capture.StartedAt, completedAt);
        _completed.Add(completion, Recorded);
        return completion;
    }

    internal void Invalidate(OfflineSnapshotCompletion completion) => _completed.Remove(completion);

    internal bool IsGenuine(OfflineSnapshotCompletion completion) => _completed.TryGetValue(completion, out _);
}
