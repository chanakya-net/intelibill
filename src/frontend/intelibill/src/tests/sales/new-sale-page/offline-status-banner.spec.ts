import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: offline status banner vm', () => {
  beforeEach(() => {
    setupNewSalePageTestBed();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('exposes syncStatus shape for banner', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    const status = vm.syncStatus();
    expect(status).toHaveProperty('snapshotAgeHours');
    expect(status).toHaveProperty('offlineInvoiceRemaining');
    expect(status).toHaveProperty('pendingSyncCount');
    expect(status).toHaveProperty('needsReviewCount');
    expect(status).toHaveProperty('staleWarningCount');
    expect(status).toHaveProperty('isSnapshotTooOld');
    expect(status).toHaveProperty('isOfflineEligible');
  });

  it('isOfflineMode is false when API is reachable (normal online state)', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    expect(vm.isOfflineMode()).toBe(false);
  });
});

describe('new-sale-page: offline status banner vm — offline state', () => {
  beforeEach(() => {
    setupNewSalePageTestBed({
      networkStatus: {
        isOnline: signal(false),
        canReachApi: signal(false),
        lastVerifiedAt: signal<Date | null>(null),
        isChecking: signal(false),
        checkConnectivity: vi.fn(async () => undefined),
      },
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('isOfflineMode is true when API is not reachable', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    expect(vm.isOfflineMode()).toBe(true);
  });
});
