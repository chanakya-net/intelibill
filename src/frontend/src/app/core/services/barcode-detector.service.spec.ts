import { TestBed } from '@angular/core/testing';
import { BrowserMultiFormatReader } from '@zxing/browser';
import { BarcodeFormat, NotFoundException } from '@zxing/library';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { BarcodeDetectorService } from './barcode-detector.service';

describe('BarcodeDetectorService', () => {
  const originalBarcodeDetector = (globalThis as { BarcodeDetector?: unknown }).BarcodeDetector;
  const originalRequestAnimationFrame = globalThis.requestAnimationFrame;
  const originalCancelAnimationFrame = globalThis.cancelAnimationFrame;

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    (globalThis as { BarcodeDetector?: unknown }).BarcodeDetector = originalBarcodeDetector;
    globalThis.requestAnimationFrame = originalRequestAnimationFrame;
    globalThis.cancelAnimationFrame = originalCancelAnimationFrame;
    TestBed.resetTestingModule();
  });

  it('falls back to ZXing and reports QR detections', async () => {
    (globalThis as { BarcodeDetector?: unknown }).BarcodeDetector = undefined;
    const stop = vi.fn();
    const controls = { stop };

    vi.spyOn(BrowserMultiFormatReader.prototype, 'decodeFromVideoElement').mockImplementation(async (_video, callback) => {
      callback(
        {
          getText: () => 'QR-123',
          getBarcodeFormat: () => BarcodeFormat.QR_CODE,
        } as never,
        undefined,
        controls as never,
      );
      callback(undefined, new NotFoundException(), controls as never);
      return controls as never;
    });

    TestBed.configureTestingModule({});
    const service = TestBed.inject(BarcodeDetectorService);
    const onDetected = vi.fn();
    const onFailure = vi.fn();

    const stopHandler = await service.start(document.createElement('video'), onDetected, onFailure);
    stopHandler();

    expect(service.preferredEngineLabel).toBe('ZXing fallback');
    expect(onDetected).toHaveBeenCalledWith({
      value: 'QR-123',
      format: 'QR-CODE',
      engine: 'zxing',
    });
    expect(onFailure).not.toHaveBeenCalled();
    expect(stop).toHaveBeenCalledTimes(1);
  });

  it('uses native detector when QR is supported', async () => {
    const detect = vi.fn(async () => [{ rawValue: 'NATIVE-QR-1', format: 'qr_code' }]);
    const rafCallbacks: FrameRequestCallback[] = [];

    class FakeBarcodeDetector {
      static async getSupportedFormats(): Promise<string[]> {
        return ['qr_code'];
      }

      constructor(_options?: { formats?: string[] }) {}

      detect = detect;
    }

    (globalThis as { BarcodeDetector?: unknown }).BarcodeDetector = FakeBarcodeDetector;
    globalThis.requestAnimationFrame = vi.fn((callback: FrameRequestCallback) => {
      rafCallbacks.push(callback);
      return rafCallbacks.length;
    });
    globalThis.cancelAnimationFrame = vi.fn();
    vi.spyOn(performance, 'now').mockReturnValue(250);

    TestBed.configureTestingModule({});
    const service = TestBed.inject(BarcodeDetectorService);
    const onDetected = vi.fn();

    const video = document.createElement('video');
    Object.defineProperty(video, 'readyState', {
      configurable: true,
      value: HTMLMediaElement.HAVE_CURRENT_DATA,
    });

    const stopHandler = await service.start(video, onDetected, vi.fn());
    await rafCallbacks[0]?.(0);
    stopHandler();

    expect(service.preferredEngineLabel).toBe('Native detector');
    expect(detect).toHaveBeenCalledTimes(1);
    expect(onDetected).toHaveBeenCalledWith({
      value: 'NATIVE-QR-1',
      format: 'QR-CODE',
      engine: 'native',
    });
  });
});
