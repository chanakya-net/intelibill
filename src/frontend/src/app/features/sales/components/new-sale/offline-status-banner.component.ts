import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

export interface SyncStatus {
  readonly snapshotAgeHours: number | null;
  readonly offlineInvoiceRemaining: number;
  readonly pendingSyncCount: number;
  readonly needsReviewCount: number;
  readonly staleWarningCount: number;
  readonly isSnapshotTooOld: boolean;
  readonly isOfflineEligible: boolean;
}

@Component({
  selector: 'app-offline-status-banner',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  templateUrl: './offline-status-banner.component.html',
  styleUrl: './offline-status-banner.component.scss',
})
export class OfflineStatusBannerComponent {
  @Input() isOffline = false;
  @Input() syncStatus: SyncStatus | null = null;
}
