import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { OfflineStatusBannerComponent, SyncStatus } from './offline-status-banner.component';

describe('OfflineStatusBannerComponent', () => {
  const syncStatus: SyncStatus = {
    snapshotAgeHours: 5,
    offlineInvoiceRemaining: 8,
    pendingSyncCount: 1,
    needsReviewCount: 0,
    staleWarningCount: 1,
    isSnapshotTooOld: false,
    isOfflineEligible: true,
  };

  it('renders offline status details when offline', () => {
    TestBed.configureTestingModule({
      imports: [OfflineStatusBannerComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(OfflineStatusBannerComponent);
    fixture.componentInstance.isOffline = true;
    fixture.componentInstance.syncStatus = syncStatus;
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('sales.newSale.offline.banner.offlineMode');
    expect(text).toContain('sales.newSale.offline.banner.snapshotAge');
  });

  it('renders nothing when offline is disabled', () => {
    TestBed.configureTestingModule({
      imports: [OfflineStatusBannerComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(OfflineStatusBannerComponent);
    fixture.componentInstance.isOffline = false;
    fixture.componentInstance.syncStatus = syncStatus;
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('.offline-status-banner');
    expect(banner).toBeNull();
  });
});
