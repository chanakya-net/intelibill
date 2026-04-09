import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AudioService } from './audio.service';

describe('AudioService', () => {
  const originalAudioContext = window.AudioContext;

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    Object.defineProperty(window, 'AudioContext', {
      configurable: true,
      value: originalAudioContext,
    });
    TestBed.resetTestingModule();
  });

  it('no-ops when audio context is unavailable', async () => {
    Object.defineProperty(window, 'AudioContext', {
      configurable: true,
      value: undefined,
    });

    TestBed.configureTestingModule({});
    const service = TestBed.inject(AudioService);

    await expect(service.beep()).resolves.toBeUndefined();
  });

  it('resumes and plays a short beep when audio context is supported', async () => {
    const resume = vi.fn(async () => undefined);
    const gainSetValueAtTime = vi.fn();
    const gainRamp = vi.fn();
    const gainConnect = vi.fn();
    const frequencySetValueAtTime = vi.fn();
    const oscillatorConnect = vi.fn();
    const start = vi.fn();
    const stop = vi.fn();

    class FakeAudioContext {
      readonly state = 'suspended';
      readonly currentTime = 1;
      readonly destination = {} as AudioDestinationNode;

      resume = resume;

      createOscillator() {
        return {
          type: 'square',
          frequency: { setValueAtTime: frequencySetValueAtTime },
          connect: oscillatorConnect,
          start,
          stop,
        } as unknown as OscillatorNode;
      }

      createGain() {
        return {
          gain: {
            setValueAtTime: gainSetValueAtTime,
            exponentialRampToValueAtTime: gainRamp,
          },
          connect: gainConnect,
        } as unknown as GainNode;
      }
    }

    Object.defineProperty(window, 'AudioContext', {
      configurable: true,
      value: FakeAudioContext,
    });

    TestBed.configureTestingModule({});
    const service = TestBed.inject(AudioService);

    await service.beep();

    expect(resume).toHaveBeenCalledTimes(1);
    expect(frequencySetValueAtTime).toHaveBeenCalledTimes(1);
    expect(gainSetValueAtTime).toHaveBeenCalledTimes(1);
    expect(gainRamp).toHaveBeenCalledTimes(2);
    expect(oscillatorConnect).toHaveBeenCalledTimes(1);
    expect(gainConnect).toHaveBeenCalledTimes(1);
    expect(start).toHaveBeenCalledTimes(1);
    expect(stop).toHaveBeenCalledTimes(1);
  });
});