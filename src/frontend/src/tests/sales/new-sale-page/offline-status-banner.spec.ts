import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

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
});
