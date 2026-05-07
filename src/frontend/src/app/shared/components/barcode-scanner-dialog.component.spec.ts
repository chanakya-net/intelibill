import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { BarcodeDetectorService } from '../../core/services/barcode-detector.service';
import { CameraStreamService } from '../../core/services/camera-stream.service';
import { BarcodeScannerDialogComponent } from './barcode-scanner-dialog.component';

describe('BarcodeScannerDialogComponent', () => {
  const stopHandler = vi.fn();
  let onDetectedCallback: ((detection: { value: string; format: string; engine: 'native' | 'zxing' }) => void) | null = null;

  const barcodeDetectorService = {
    start: vi.fn(async (_video: HTMLVideoElement, onDetected: typeof onDetectedCallback) => {
      onDetectedCallback = onDetected;
      return stopHandler;
    }),
  };

  const cameraStreamService = {
    startPreferredCamera: vi.fn(async () => ({ id: 'stream-1' } as unknown as MediaStream)),
    attachToVideo: vi.fn(async () => undefined),
    detachVideo: vi.fn(),
    stopCurrentStream: vi.fn(),
  };

  beforeEach(() => {
    stopHandler.mockClear();
    onDetectedCallback = null;
    barcodeDetectorService.start.mockClear();
    cameraStreamService.startPreferredCamera.mockClear();
    cameraStreamService.attachToVideo.mockClear();
    cameraStreamService.detachVideo.mockClear();
    cameraStreamService.stopCurrentStream.mockClear();

    TestBed.configureTestingModule({
      imports: [
        BarcodeScannerDialogComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [
        { provide: BarcodeDetectorService, useValue: barcodeDetectorService },
        { provide: CameraStreamService, useValue: cameraStreamService },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('emits detected QR once after scanner starts', async () => {
    const fixture = TestBed.createComponent(BarcodeScannerDialogComponent);
    const component = fixture.componentInstance;
    const detectedSpy = vi.fn();
    component.detected.subscribe(detectedSpy);
    const video = document.createElement('video');

    await component['startScannerSession'](video);

    expect(barcodeDetectorService.start).toHaveBeenCalledTimes(1);

    onDetectedCallback?.({ value: 'QR-001', format: 'QR-CODE', engine: 'native' });
    onDetectedCallback?.({ value: 'QR-001', format: 'QR-CODE', engine: 'native' });

    expect(detectedSpy).toHaveBeenCalledTimes(1);
    expect(detectedSpy).toHaveBeenCalledWith({
      value: 'QR-001',
      format: 'QR-CODE',
      engine: 'native',
    });
  });

  it('stops scanner resources when dialog closes', async () => {
    const fixture = TestBed.createComponent(BarcodeScannerDialogComponent);
    const component = fixture.componentInstance;
    const video = document.createElement('video');

    await component['startScannerSession'](video);
    await component['stopScannerSession'](video);

    expect(stopHandler).toHaveBeenCalledTimes(1);
    expect(cameraStreamService.detachVideo).toHaveBeenCalledTimes(1);
    expect(cameraStreamService.stopCurrentStream).toHaveBeenCalledTimes(1);
  });

  it('emits visibleChange false when camera fails to open', async () => {
    cameraStreamService.startPreferredCamera.mockRejectedValueOnce(new Error('camera unavailable'));

    const fixture = TestBed.createComponent(BarcodeScannerDialogComponent);
    const component = fixture.componentInstance;
    const visibleSpy = vi.fn();
    component.visibleChange.subscribe(visibleSpy);
    const video = document.createElement('video');

    await component['startScannerSession'](video);

    expect(visibleSpy).toHaveBeenCalledWith(false);
  });
});
