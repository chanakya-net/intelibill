import {
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { DialogModule } from 'primeng/dialog';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import {
  BarcodeDetection,
  BarcodeDetectorService,
} from '../../core/services/barcode-detector.service';
import { CameraStreamService } from '../../core/services/camera-stream.service';

@Component({
  selector: 'app-barcode-scanner-dialog',
  standalone: true,
  imports: [DialogModule, ProgressSpinnerModule, TranslocoPipe],
  template: `
    <p-dialog
      [visible]="visible"
      (visibleChange)="onVisibleChange($event)"
      [header]="headerKey | transloco"
      [modal]="true"
      [draggable]="false"
      [style]="{ width: '100%', maxWidth: '42rem' }"
    >
      <div class="scanner-dialog-content">
        <p class="scanner-subtitle">{{ subtitleKey | transloco }}</p>

        <div class="scanner-preview-shell">
          <video #scannerVideo class="scanner-video" autoplay muted playsinline></video>
          <div class="scanner-guides"></div>
          @if (scannerInitializing()) {
            <div class="scanner-loading">
              <p-progressSpinner styleClass="h-8 w-8" strokeWidth="6"></p-progressSpinner>
            </div>
          }
        </div>

        @if (scannerError()) {
          <p class="scanner-error">
            {{
              scannerError().startsWith('inventory.')
                ? (scannerError() | transloco)
                : scannerError()
            }}
          </p>
        }
      </div>
    </p-dialog>
  `,
  styles: `
    .scanner-dialog-content {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }

    .scanner-subtitle {
      margin: 0;
      color: #475569;
      font-size: 0.875rem;
    }

    .scanner-preview-shell {
      position: relative;
      border-radius: 0.75rem;
      overflow: hidden;
      background: #020617;
      border: 1px solid #334155;
      min-height: 240px;
    }

    .scanner-video {
      width: 100%;
      height: auto;
      display: block;
    }

    .scanner-guides {
      position: absolute;
      inset: 0;
      pointer-events: none;
      border: 2px dashed rgba(248, 250, 252, 0.7);
      border-radius: 0.75rem;
      margin: 1.25rem;
    }

    .scanner-loading {
      position: absolute;
      inset: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      background: rgba(2, 6, 23, 0.45);
    }

    .scanner-error {
      margin: 0;
      color: #dc2626;
      font-size: 0.875rem;
    }
  `,
})
export class BarcodeScannerDialogComponent implements OnChanges, OnDestroy {
  private readonly barcodeDetectorService = inject(BarcodeDetectorService);
  private readonly cameraStreamService = inject(CameraStreamService);

  @Input() visible = false;
  @Input() headerKey = 'inventory.scannerTitle';
  @Input() subtitleKey = 'inventory.scannerSubtitle';

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() detected = new EventEmitter<BarcodeDetection>();

  @ViewChild('scannerVideo')
  scannerVideo?: ElementRef<HTMLVideoElement>;

  readonly scannerError = signal('');
  readonly scannerInitializing = signal(false);

  private scannerStopHandler: (() => void) | null = null;
  private lastDetectedBarcode = '';
  private lastDetectedAt = 0;

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['visible']) {
      return;
    }

    if (this.visible) {
      this.scannerError.set('');
      queueMicrotask(() => {
        const element = this.scannerVideo?.nativeElement;
        if (element) {
          void this.startScannerSession(element);
        }
      });
      return;
    }

    const element = this.scannerVideo?.nativeElement;
    void this.stopScannerSession(element);
  }

  ngOnDestroy(): void {
    const element = this.scannerVideo?.nativeElement;
    void this.stopScannerSession(element);
  }

  onVisibleChange(visible: boolean): void {
    this.visibleChange.emit(visible);
  }

  private async startScannerSession(videoElement: HTMLVideoElement): Promise<void> {
    if (this.scannerStopHandler) {
      return;
    }

    this.scannerInitializing.set(true);
    this.scannerError.set('');

    try {
      const stream = await this.cameraStreamService.startPreferredCamera();
      await this.cameraStreamService.attachToVideo(stream, videoElement);
      this.scannerStopHandler = await this.barcodeDetectorService.start(
        videoElement,
        (detection) => {
          if (this.shouldIgnoreScan(detection.value)) {
            return;
          }

          this.detected.emit(detection);
        },
        () => {
          this.scannerError.set('inventory.scannerDetectionError');
        },
      );
    } catch {
      this.scannerError.set('inventory.scannerOpenError');
    } finally {
      this.scannerInitializing.set(false);
    }
  }

  private async stopScannerSession(videoElement?: HTMLVideoElement): Promise<void> {
    try {
      this.scannerStopHandler?.();
      this.scannerStopHandler = null;

      this.cameraStreamService.detachVideo(videoElement);
    } catch {
      // Ignore teardown errors while closing dialog or destroying view.
    }

    this.cameraStreamService.stopCurrentStream();
  }

  private shouldIgnoreScan(barcode: string): boolean {
    const normalized = barcode.trim();
    if (!normalized) {
      return true;
    }

    const now = Date.now();
    if (this.lastDetectedBarcode === normalized && now - this.lastDetectedAt < 1200) {
      return true;
    }

    this.lastDetectedBarcode = normalized;
    this.lastDetectedAt = now;
    return false;
  }
}
