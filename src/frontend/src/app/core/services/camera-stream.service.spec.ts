import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { CameraStreamService } from './camera-stream.service';

describe('CameraStreamService', () => {
  const originalIsSecureContext = window.isSecureContext;
  const originalMediaDevices = navigator.mediaDevices;

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    Object.defineProperty(window, 'isSecureContext', {
      configurable: true,
      value: originalIsSecureContext,
    });
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: originalMediaDevices,
    });
    TestBed.resetTestingModule();
  });

  it('rejects camera start outside secure contexts', async () => {
    Object.defineProperty(window, 'isSecureContext', {
      configurable: true,
      value: false,
    });
    vi.spyOn(window, 'location', 'get').mockReturnValue(new URL('https://example.com') as unknown as Location);

    TestBed.configureTestingModule({});
    const service = TestBed.inject(CameraStreamService);

    await expect(service.startPreferredCamera()).rejects.toThrow('Camera access on mobile needs HTTPS');
  });

  it('falls back to generic video constraints when preferred request fails', async () => {
    const stream = { getTracks: vi.fn(() => []) } as unknown as MediaStream;
    const getUserMedia = vi
      .fn()
      .mockRejectedValueOnce(new Error('rear camera unavailable'))
      .mockResolvedValueOnce(stream);

    Object.defineProperty(window, 'isSecureContext', {
      configurable: true,
      value: true,
    });
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia },
    });

    TestBed.configureTestingModule({});
    const service = TestBed.inject(CameraStreamService);

    await expect(service.startPreferredCamera()).resolves.toBe(stream);
    expect(getUserMedia).toHaveBeenNthCalledWith(
      1,
      expect.objectContaining({ video: expect.objectContaining({ facingMode: { ideal: 'environment' } }) }),
    );
    expect(getUserMedia).toHaveBeenNthCalledWith(2, { audio: false, video: true });
  });

  it('attaches stream to video and stops tracks on cleanup', async () => {
    const stop = vi.fn();
    const stream = {
      getTracks: vi.fn(() => [{ stop }]),
    } as unknown as MediaStream;

    Object.defineProperty(window, 'isSecureContext', {
      configurable: true,
      value: true,
    });
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: {
        getUserMedia: vi.fn(async () => stream),
      },
    });

    TestBed.configureTestingModule({});
    const service = TestBed.inject(CameraStreamService);
    const video = document.createElement('video');
    const play = vi.fn(async () => undefined);
    const pause = vi.fn();

    Object.defineProperty(video, 'play', { configurable: true, value: play });
    Object.defineProperty(video, 'pause', { configurable: true, value: pause });

    const activeStream = await service.startPreferredCamera();
    const attachPromise = service.attachToVideo(activeStream, video);
    video.dispatchEvent(new Event('loadedmetadata'));
    await attachPromise;

    expect(video.srcObject).toBe(stream);
    expect(play).toHaveBeenCalledTimes(1);

    service.detachVideo(video);
    service.stopCurrentStream();

    expect(pause).toHaveBeenCalledTimes(1);
    expect(stop).toHaveBeenCalledTimes(1);
  });
});