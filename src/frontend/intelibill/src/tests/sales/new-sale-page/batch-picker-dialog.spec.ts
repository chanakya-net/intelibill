import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: batch picker dialog', () => {
  beforeEach(() => {
    setupNewSalePageTestBed();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('tracks quantity changes for picker', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    expect(vm.batchPickerQuantity()).toBe(1);
    vm.onBatchQuantityChanged(3);
    expect(vm.batchPickerQuantity()).toBe(3);
  });
});

