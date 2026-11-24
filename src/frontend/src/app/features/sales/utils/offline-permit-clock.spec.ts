import { describe, expect, it } from 'vitest';

import {
  OFFLINE_CLOCK_MAX_SERVER_SKEW_MS,
  OFFLINE_CLOCK_TOLERANCE_MS,
  evaluateOfflineClock,
  rebaseOfflineClock,
  type OfflineClockCheckpoint,
  type OfflineClockSample,
} from './offline-permit-clock';

/**
 * Fixed-clock boundaries of the clock evidence. These prove the browser refuses once its own readings disagree; they
 * do not, and cannot, prove physical time on a device whose clock is changed while it is off.
 */
describe('offline permit clock evidence', () => {
  const SERVER = Date.parse('2026-11-22T11:59:30.123Z');
  const owner = { shopId: 'shop', deviceId: 'device', userId: 'user', sessionId: 'session' };
  const observation = { ...owner, permitId: 'permit-1', serverObservedAt: '2026-11-22T11:59:30.123456Z', serverObservedMs: SERVER };
  const sample = (wallMs: number, monotonicMs = 0, documentId = 'doc-1'): OfflineClockSample => ({ documentId, wallMs, monotonicMs });
  const established = (wallMs = SERVER): OfflineClockCheckpoint => rebaseOfflineClock(null, observation, sample(wallMs))!;

  it('establishes a checkpoint only from a signed observation the device clock agrees with', () => {
    expect(established()).toMatchObject({ version: 1, permitId: 'permit-1', serverObservedMs: SERVER, wallHighWaterMs: SERVER, blocked: null });
    expect(rebaseOfflineClock(null, observation, sample(SERVER + OFFLINE_CLOCK_MAX_SERVER_SKEW_MS))).not.toBeNull();
    expect(rebaseOfflineClock(null, observation, sample(SERVER + OFFLINE_CLOCK_MAX_SERVER_SKEW_MS + 1))).toBeNull();
    expect(rebaseOfflineClock(null, observation, sample(SERVER - OFFLINE_CLOCK_MAX_SERVER_SKEW_MS - 1))).toBeNull();
    expect(rebaseOfflineClock(null, observation, sample(Number.NaN))).toBeNull();
  });

  it('is unverified without a checkpoint, for another owner or session, and for another permit', () => {
    const now = sample(SERVER + 1000, 1000);
    expect(evaluateOfflineClock(null, owner, 'permit-1', now, null)).toEqual({ verdict: 'unverified', checkpoint: null });
    expect(evaluateOfflineClock({ junk: true }, owner, 'permit-1', now, null).verdict).toBe('unverified');
    expect(evaluateOfflineClock(established(), { ...owner, sessionId: 'session-2' }, 'permit-1', now, null).verdict).toBe('unverified');
    expect(evaluateOfflineClock(established(), owner, 'permit-2', now, null).verdict).toBe('unverified');
  });

  it('advances the wall high-water on a consistent sample', () => {
    const result = evaluateOfflineClock(established(), owner, 'permit-1', sample(SERVER + 60_000, 60_000), sample(SERVER, 0));
    expect(result.verdict).toBe('consistent');
    expect(result.checkpoint?.wallHighWaterMs).toBe(SERVER + 60_000);
  });

  it('allows reading precision and latches one millisecond beyond it when the wall clock moves back', () => {
    const high = { ...established(), wallHighWaterMs: SERVER + 600_000 };
    const within = evaluateOfflineClock(high, owner, 'permit-1', sample(SERVER + 600_000 - OFFLINE_CLOCK_TOLERANCE_MS), null);
    expect(within).toEqual({ verdict: 'consistent', checkpoint: null });

    const back = evaluateOfflineClock(high, owner, 'permit-1', sample(SERVER + 600_000 - OFFLINE_CLOCK_TOLERANCE_MS - 1), null);
    expect(back.verdict).toBe('blocked');
    expect(back.checkpoint?.blocked?.reason).toBe('WallClockMovedBack');
  });

  it('latches a wall clock that stalled while the document measured time passing', () => {
    const baseline = sample(SERVER, 0);
    const stalled = evaluateOfflineClock(established(), owner, 'permit-1', sample(SERVER + 1000, 600_000), baseline);
    expect(stalled.verdict).toBe('blocked');
    expect(stalled.checkpoint?.blocked?.reason).toBe('WallClockStalled');
    // A wall clock that ran ahead of the monotonic clock (a sleeping device) is not a discrepancy.
    expect(evaluateOfflineClock(established(), owner, 'permit-1', sample(SERVER + 600_000, 1000), baseline).verdict).toBe('consistent');
  });

  it('does not compare monotonic readings across documents; a reload relies on the persisted floors', () => {
    const otherDocument = sample(SERVER, 900_000, 'doc-0');
    const high = { ...established(), wallHighWaterMs: SERVER + 600_000 };
    expect(evaluateOfflineClock(high, owner, 'permit-1', sample(SERVER + 600_500, 5, 'doc-1'), otherDocument).verdict).toBe('consistent');
    expect(evaluateOfflineClock(high, owner, 'permit-1', sample(SERVER + 1000, 5, 'doc-1'), otherDocument).verdict).toBe('blocked');
  });

  it('latches invalid samples and a clock earlier than the signed server time allows', () => {
    expect(evaluateOfflineClock(established(), owner, 'permit-1', sample(Number.NaN), null).checkpoint?.blocked?.reason).toBe('InvalidSample');
    expect(evaluateOfflineClock(established(), owner, 'permit-1', sample(SERVER, -1), null).checkpoint?.blocked?.reason).toBe('InvalidSample');
    const low = { ...established(), wallHighWaterMs: 0 };
    const early = sample(SERVER - OFFLINE_CLOCK_MAX_SERVER_SKEW_MS - OFFLINE_CLOCK_TOLERANCE_MS - 1);
    expect(evaluateOfflineClock(low, owner, 'permit-1', early, null).checkpoint?.blocked?.reason).toBe('WallClockBehindServer');
  });

  it('stays latched after the wall clock is restored, and for a replayed or older observation', () => {
    const back = evaluateOfflineClock({ ...established(), wallHighWaterMs: SERVER + 600_000 }, owner, 'permit-1', sample(SERVER), null);
    const latched = back.checkpoint!;

    expect(evaluateOfflineClock(latched, owner, 'permit-1', sample(SERVER + 700_000), null)).toEqual({ verdict: 'blocked', checkpoint: null });
    expect(rebaseOfflineClock(latched, observation, sample(SERVER))).toBeNull();
    expect(rebaseOfflineClock(latched, { ...observation, serverObservedMs: SERVER - 1 }, sample(SERVER))).toBeNull();
  });

  it('releases the latch only on a genuinely later signed observation the corrected clock agrees with', () => {
    const latched = evaluateOfflineClock({ ...established(), wallHighWaterMs: SERVER + 600_000 }, owner, 'permit-1', sample(SERVER), null).checkpoint!;
    const later = { ...observation, permitId: 'permit-2', serverObservedMs: SERVER + 900_000, serverObservedAt: '2026-11-22T12:14:30.123456Z' };

    expect(rebaseOfflineClock(latched, later, sample(SERVER))).toBeNull();
    const fresh = rebaseOfflineClock(latched, later, sample(SERVER + 900_500));
    expect(fresh).toMatchObject({ permitId: 'permit-2', blocked: null, wallHighWaterMs: SERVER + 900_500 });
    expect(evaluateOfflineClock(fresh, owner, 'permit-2', sample(SERVER + 901_000), null).verdict).toBe('consistent');
  });
});
