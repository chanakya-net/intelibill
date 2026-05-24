import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: confirmation dialog orchestration', () => {
  let deps: ReturnType<typeof setupNewSalePageTestBed>['deps'];

  beforeEach(() => {
    deps = setupNewSalePageTestBed().deps;
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('maps last recorded sale into confirmation result', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    deps.salesFacade.lastRecordedSale.set({
      saleId: 'sale-1',
      invoiceNumber: 'INV-1',
      totalAmount: 50,
    });

    const result = vm.saleConfirmationResult();
    expect(result).toEqual(
      expect.objectContaining({
        saleId: 'sale-1',
        invoiceNumber: 'INV-1',
        totalAmount: 50,
        isOffline: false,
      })
    );
  });
});
