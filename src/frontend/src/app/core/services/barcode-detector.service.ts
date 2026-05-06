import { Injectable } from '@angular/core';
import { BrowserMultiFormatReader } from '@zxing/browser';
import {
  BarcodeFormat,
  ChecksumException,
  DecodeHintType,
  FormatException,
  NotFoundException,
} from '@zxing/library';

export type DetectionEngine = 'native' | 'zxing';

export interface BarcodeDetection {
  value: string;
  format: string;
  engine: DetectionEngine;
}

type NativeBarcodeFormat = 'code_128' | 'code_39' | 'ean_13' | 'ean_8' | 'qr_code' | 'upc_a' | 'upc_e';

interface NativeDetectedBarcode {
  rawValue?: string;
  format?: string;
}

interface NativeBarcodeDetectorInstance {
  detect(source: HTMLVideoElement): Promise<NativeDetectedBarcode[]>;
}

interface NativeBarcodeDetectorConstructor {
  new (options?: { formats?: NativeBarcodeFormat[] }): NativeBarcodeDetectorInstance;
  getSupportedFormats?: () => Promise<string[]>;
}

@Injectable({
  providedIn: 'root',
})
export class BarcodeDetectorService {
  private readonly nativeFormats: NativeBarcodeFormat[] = [
    'code_128',
    'code_39',
    'ean_13',
    'ean_8',
    'qr_code',
    'upc_a',
    'upc_e',
  ];

  private readonly zxingFormats: BarcodeFormat[] = [
    BarcodeFormat.CODE_128,
    BarcodeFormat.CODE_39,
    BarcodeFormat.EAN_13,
    BarcodeFormat.EAN_8,
    BarcodeFormat.QR_CODE,
    BarcodeFormat.UPC_A,
    BarcodeFormat.UPC_E,
  ];

  private lastEngine: DetectionEngine = this.supportsNativeDetection() ? 'native' : 'zxing';

  get preferredEngine(): DetectionEngine {
    return this.lastEngine;
  }

  get preferredEngineLabel(): string {
    return this.lastEngine === 'native' ? 'Native detector' : 'ZXing fallback';
  }

  async start(
    videoElement: HTMLVideoElement,
    onDetected: (detection: BarcodeDetection) => void,
    onFailure: (message: string) => void,
  ): Promise<() => void> {
    const nativeDetector = await this.createNativeDetector();

    if (nativeDetector) {
      this.lastEngine = 'native';
      return this.startNativeDetection(videoElement, nativeDetector, onDetected, onFailure);
    }

    this.lastEngine = 'zxing';
    return this.startZxingDetection(videoElement, onDetected, onFailure);
  }

  private async createNativeDetector(): Promise<NativeBarcodeDetectorInstance | null> {
    const BarcodeDetectorConstructor = (globalThis as { BarcodeDetector?: NativeBarcodeDetectorConstructor })
      .BarcodeDetector;

    if (!BarcodeDetectorConstructor) {
      return null;
    }

    let requestedFormats = this.nativeFormats;

    if (typeof BarcodeDetectorConstructor.getSupportedFormats === 'function') {
      const supportedFormats = await BarcodeDetectorConstructor.getSupportedFormats();
      requestedFormats = this.nativeFormats.filter((format) => supportedFormats.includes(format));

      if (requestedFormats.length === 0) {
        return null;
      }
    }

    return new BarcodeDetectorConstructor({ formats: requestedFormats });
  }

  private startNativeDetection(
    videoElement: HTMLVideoElement,
    detector: NativeBarcodeDetectorInstance,
    onDetected: (detection: BarcodeDetection) => void,
    onFailure: (message: string) => void,
  ): () => void {
    let active = true;
    let frameHandle = 0;
    let detectionInFlight = false;
    let lastAttemptTime = 0;
    let reportedFailure = false;

    const scanFrame = async (): Promise<void> => {
      if (!active) {
        return;
      }

      frameHandle = requestAnimationFrame(() => {
        void scanFrame();
      });

      if (detectionInFlight || videoElement.readyState < HTMLMediaElement.HAVE_CURRENT_DATA) {
        return;
      }

      const now = performance.now();
      if (now - lastAttemptTime < 80) {
        return;
      }

      lastAttemptTime = now;
      detectionInFlight = true;

      try {
        const detection = (await detector.detect(videoElement)).find((entry) => !!entry.rawValue?.trim());

        if (detection?.rawValue) {
          onDetected({
            value: detection.rawValue,
            format: this.normalizeNativeFormat(detection.format),
            engine: 'native',
          });
        }
      } catch (error) {
        if (!reportedFailure && !this.isIgnorableNativeError(error)) {
          reportedFailure = true;
          onFailure('Native barcode detection failed on this browser.');
        }
      } finally {
        detectionInFlight = false;
      }
    };

    frameHandle = requestAnimationFrame(() => {
      void scanFrame();
    });

    return () => {
      active = false;
      cancelAnimationFrame(frameHandle);
    };
  }

  private async startZxingDetection(
    videoElement: HTMLVideoElement,
    onDetected: (detection: BarcodeDetection) => void,
    onFailure: (message: string) => void,
  ): Promise<() => void> {
    const hints = new Map<DecodeHintType, BarcodeFormat[]>();
    hints.set(DecodeHintType.POSSIBLE_FORMATS, this.zxingFormats);

    const reader = new BrowserMultiFormatReader(hints);
    let reportedFailure = false;

    const controls = await reader.decodeFromVideoElement(videoElement, (result, error) => {
      if (result) {
        onDetected({
          value: result.getText(),
          format: this.normalizeZxingFormat(result.getBarcodeFormat()),
          engine: 'zxing',
        });
        return;
      }

      if (!reportedFailure && error && !this.isIgnorableZxingError(error)) {
        reportedFailure = true;
        onFailure('ZXing could not keep decoding the video stream.');
      }
    });

    return () => {
      controls.stop();
    };
  }

  private supportsNativeDetection(): boolean {
    return typeof (globalThis as { BarcodeDetector?: unknown }).BarcodeDetector !== 'undefined';
  }

  private normalizeNativeFormat(format?: string): string {
    if (!format) {
      return 'Unknown';
    }

    return format.replaceAll('_', '-').toUpperCase();
  }

  private normalizeZxingFormat(format: BarcodeFormat): string {
    return BarcodeFormat[format]?.replaceAll('_', '-') ?? 'Unknown';
  }

  private isIgnorableNativeError(error: unknown): boolean {
    return (
      error instanceof DOMException &&
      (error.name === 'AbortError' || error.name === 'InvalidStateError' || error.name === 'NotSupportedError')
    );
  }

  private isIgnorableZxingError(error: unknown): boolean {
    return (
      error instanceof NotFoundException ||
      error instanceof ChecksumException ||
      error instanceof FormatException
    );
  }
}