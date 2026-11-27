/**
 * Clock consistency evidence for offline issuance.
 *
 * A browser cannot prove physical time: a device whose clock is changed while it is switched off, or whose storage is
 * edited, can present any time it likes. What this evidence does is narrower and honest: once a signed server
 * observation has been stored, issuance is refused whenever the readings the browser itself gives disagree with what
 * it gave before, and stays refused until the device is verified online again.
 */
export const OFFLINE_CLOCK_VERSION = 1;

/** Reading precision allowed between two samples of the same clock; not part of any signed deadline. */
export const OFFLINE_CLOCK_TOLERANCE_MS = 5_000;

/** How far the device wall clock may sit from a freshly signed server time and still be adopted. */
export const OFFLINE_CLOCK_MAX_SERVER_SKEW_MS = 5 * 60_000;

export type OfflineClockBlockReason = 'WallClockMovedBack' | 'WallClockBehindServer' | 'WallClockStalled' | 'InvalidSample';

export interface OfflineClockOwner {
  readonly shopId: string;
  readonly deviceId: string;
  readonly userId: string;
  readonly sessionId: string;
}

export interface OfflineClockCheckpoint extends OfflineClockOwner {
  readonly version: 1;
  /** The verified permit whose signed issue time anchors this checkpoint. */
  readonly permitId: string;
  /** Server time signed into that permit (canonical UTC instant). */
  readonly serverObservedAt: string;
  readonly serverObservedMs: number;
  /** Highest wall-clock reading any tab of this device has recorded for this owner. */
  readonly wallHighWaterMs: number;
  readonly blocked: { readonly reason: OfflineClockBlockReason; readonly detectedAtWallMs: number } | null;
}

/** One reading of both clocks, taken together, in one document. */
export interface OfflineClockSample {
  readonly documentId: string;
  readonly wallMs: number;
  readonly monotonicMs: number;
}

export type OfflineClockVerdict = 'consistent' | 'blocked' | 'unverified';

export interface OfflineClockEvaluation {
  readonly verdict: OfflineClockVerdict;
  /** What to persist; null leaves the stored checkpoint as it is. */
  readonly checkpoint: OfflineClockCheckpoint | null;
}

function isValidSample(sample: OfflineClockSample): boolean {
  return (
    typeof sample.documentId === 'string' &&
    sample.documentId.length > 0 &&
    Number.isFinite(sample.wallMs) &&
    Number.isFinite(sample.monotonicMs) &&
    sample.wallMs > 0 &&
    sample.monotonicMs >= 0
  );
}

function sameOwner(checkpoint: OfflineClockCheckpoint, owner: OfflineClockOwner): boolean {
  return (
    checkpoint.shopId === owner.shopId &&
    checkpoint.deviceId === owner.deviceId &&
    checkpoint.userId === owner.userId &&
    checkpoint.sessionId === owner.sessionId
  );
}

export function isOfflineClockCheckpoint(value: unknown): value is OfflineClockCheckpoint {
  if (typeof value !== 'object' || value === null) return false;
  const c = value as Record<string, unknown>;
  return (
    c['version'] === OFFLINE_CLOCK_VERSION &&
    ['shopId', 'deviceId', 'userId', 'sessionId', 'permitId', 'serverObservedAt'].every(
      (key) => typeof c[key] === 'string' && (c[key] as string).length > 0,
    ) &&
    Number.isFinite(c['serverObservedMs']) &&
    Number.isFinite(c['wallHighWaterMs']) &&
    (c['blocked'] === null || typeof c['blocked'] === 'object')
  );
}

function detect(
  checkpoint: OfflineClockCheckpoint,
  sample: OfflineClockSample,
  baseline: OfflineClockSample | null,
): OfflineClockBlockReason | null {
  if (!isValidSample(sample)) return 'InvalidSample';
  if (sample.wallMs < checkpoint.wallHighWaterMs - OFFLINE_CLOCK_TOLERANCE_MS) return 'WallClockMovedBack';
  // The clock was within the allowed skew of the signed server time when adopted; it can never be earlier than that.
  if (sample.wallMs < checkpoint.serverObservedMs - OFFLINE_CLOCK_MAX_SERVER_SKEW_MS - OFFLINE_CLOCK_TOLERANCE_MS) {
    return 'WallClockBehindServer';
  }
  if (baseline && baseline.documentId === sample.documentId) {
    const elapsed = sample.monotonicMs - baseline.monotonicMs;
    const wallAdvance = sample.wallMs - baseline.wallMs;
    if (elapsed < 0) return 'InvalidSample';
    // A wall clock that advanced less than the time the document measured was held or set back. Advancing more is
    // not flagged: a sleeping device may pause its monotonic clock, and a later wall clock only shortens the window.
    if (wallAdvance < elapsed - OFFLINE_CLOCK_TOLERANCE_MS) return 'WallClockStalled';
  }
  return null;
}

/**
 * Judges one sample against the stored checkpoint. A detected discrepancy is latched in the returned checkpoint, so
 * it must be persisted even when the sale is refused for another reason. `baseline` is the first sample this document
 * took for the same checkpoint; a new document starts a new baseline and relies on the persisted floors.
 */
export function evaluateOfflineClock(
  stored: unknown,
  owner: OfflineClockOwner,
  permitId: string,
  sample: OfflineClockSample,
  baseline: OfflineClockSample | null,
): OfflineClockEvaluation {
  if (!isOfflineClockCheckpoint(stored) || !sameOwner(stored, owner) || stored.permitId !== permitId) {
    return { verdict: 'unverified', checkpoint: null };
  }
  if (stored.blocked) return { verdict: 'blocked', checkpoint: null };

  const reason = detect(stored, sample, baseline);
  if (reason) {
    const detectedAtWallMs = Number.isFinite(sample.wallMs) ? sample.wallMs : stored.wallHighWaterMs;
    return { verdict: 'blocked', checkpoint: { ...stored, blocked: { reason, detectedAtWallMs } } };
  }
  return {
    verdict: 'consistent',
    checkpoint: sample.wallMs > stored.wallHighWaterMs ? { ...stored, wallHighWaterMs: sample.wallMs } : null,
  };
}

export interface OfflineClockObservation extends OfflineClockOwner {
  readonly permitId: string;
  /** The signed server issue time of a permit whose signature was just verified. */
  readonly serverObservedAt: string;
  readonly serverObservedMs: number;
}

/**
 * Adopts a freshly verified server observation. Returns the checkpoint to store, or null to keep the stored one:
 * a replayed or older observation changes nothing (a latch included), and a device clock that disagrees with the
 * signed server time is not adopted, so issuance stays refused until the clock is corrected and verified again.
 */
export function rebaseOfflineClock(
  stored: unknown,
  observation: OfflineClockObservation,
  sample: OfflineClockSample,
): OfflineClockCheckpoint | null {
  if (!isValidSample(sample) || !Number.isFinite(observation.serverObservedMs)) return null;
  if (Math.abs(sample.wallMs - observation.serverObservedMs) > OFFLINE_CLOCK_MAX_SERVER_SKEW_MS) return null;
  if (
    isOfflineClockCheckpoint(stored) &&
    sameOwner(stored, observation) &&
    observation.serverObservedMs <= stored.serverObservedMs
  ) {
    return null;
  }
  return {
    version: OFFLINE_CLOCK_VERSION,
    shopId: observation.shopId,
    deviceId: observation.deviceId,
    userId: observation.userId,
    sessionId: observation.sessionId,
    permitId: observation.permitId,
    serverObservedAt: observation.serverObservedAt,
    serverObservedMs: observation.serverObservedMs,
    wallHighWaterMs: sample.wallMs,
    blocked: null,
  };
}
