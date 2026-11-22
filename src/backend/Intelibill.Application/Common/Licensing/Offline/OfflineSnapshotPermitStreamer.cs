using System.Runtime.CompilerServices;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;

namespace Intelibill.Application.Common.Licensing.Offline;

/// <summary>
/// Streams a snapshot bound to a reserved invoice lease, issues the matching offline licence
/// permit when the capture completes, emits the versioned <c>licencePermit</c> record immediately
/// before <c>complete</c>, and stamps <c>complete.completedAt</c> from the same completion instant.
/// </summary>
public sealed class OfflineSnapshotPermitStreamer(IOfflineLicencePermitIssuer issuer)
{
    public BoundCapture Start(
        SessionClaims session, Guid grantId, Guid leaseId, string deviceId, Guid? tillSessionId) =>
        new(issuer, session, grantId, leaseId, deviceId, tillSessionId);

    public sealed class BoundCapture
    {
        private readonly IOfflineLicencePermitIssuer _issuer;
        private readonly SessionClaims _session;
        private readonly Guid? _tillSessionId;
        private readonly OfflineSnapshotCapture _capture;

        internal BoundCapture(
            IOfflineLicencePermitIssuer issuer, SessionClaims session, Guid grantId, Guid leaseId,
            string deviceId, Guid? tillSessionId)
        {
            _issuer = issuer;
            _session = session;
            _tillSessionId = tillSessionId;
            _capture = issuer.StartSnapshotCapture(session, grantId, leaseId, deviceId);
        }

        public Guid SnapshotId => _capture.SnapshotId;

        public async IAsyncEnumerable<IOfflineSalesSnapshotStreamRecord> StreamAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var record in _capture.StreamAsync(ct))
            {
                if (record is not OfflineSalesSnapshotCompleteRecord done)
                {
                    yield return record;
                    continue;
                }

                var completion = _capture.Completion
                    ?? throw new InvalidOperationException("Offline snapshot capture did not record completion.");
                var issued = await _issuer.IssueAsync(
                    new OfflinePermitRequest(_session, _capture.GrantId, completion, _tillSessionId), ct);

                yield return new OfflineSalesSnapshotLicencePermitRecord(
                    issued.IsError
                        ? OfflineSalesSnapshotLicencePermit.Refused(
                            _capture.LeaseId, _capture.GrantId, _capture.SnapshotId, issued.FirstError.Code)
                        : OfflineSalesSnapshotLicencePermit.Issued(
                            _capture.LeaseId, _capture.GrantId, _capture.SnapshotId, issued.Value));

                yield return new OfflineSalesSnapshotCompleteRecord(
                    done.Complete with { CompletedAt = completion.CompletedAt });
            }
        }
    }
}
