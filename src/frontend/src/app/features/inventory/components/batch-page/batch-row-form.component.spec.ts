import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ProductCatalogSyncService } from '../../../../core/services/product-catalog-sync.service';
import { InventoryService } from '../../services/inventory.service';
import { SuppliersFacade } from '../../../suppliers/state/suppliers.facade';
import { BatchRowFormComponent } from './batch-row-form.component';

describe('BatchRowFormComponent', () => {
  const inventoryService = {
    getProductDetailsByNameOrBarcode: vi.fn(() =>
      of({
        name: 'Milk',
        description: 'Fresh milk pack',
        uom: 'ltr',
        costPrice: 42,
        mrp: 50,
        salesPrice: 48,
        supplierId: null,
        supplierName: null,
        hsnCode: null,
        taxIncluded: true,
        taxRatePercent: 18,
      }),
    ),
    lookupHsn: vi.fn(() =>
      of({ hsnCodes: [] as string[], taxScenarios: [] as { condition: string; taxPercentage: string }[] }),
    ),
  };

  const catalogSync = {
    filterByName: vi.fn(() => []),
    filterByBarcode: vi.fn(() => []),
    findByName: vi.fn(() => undefined),
    findByBarcode: vi.fn(() => undefined),
  };

  const suppliersFacade = {
    suppliers: signal<any[]>([]),
    load: vi.fn(),
  };

  function setup() {
    TestBed.configureTestingModule({
      imports: [BatchRowFormComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: InventoryService, useValue: inventoryService },
        { provide: ProductCatalogSyncService, useValue: catalogSync },
        { provide: SuppliersFacade, useValue: suppliersFacade },
      ],
    });

    const fixture = TestBed.createComponent(BatchRowFormComponent);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    inventoryService.getProductDetailsByNameOrBarcode.mockClear();
    inventoryService.lookupHsn.mockClear();
    catalogSync.filterByName.mockClear();
    catalogSync.filterByBarcode.mockClear();
    catalogSync.findByName.mockClear();
    catalogSync.findByBarcode.mockClear();
  });

  it('emits a row when the form is submitted', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    const emitted: unknown[] = [];
    component.rowSubmitted.subscribe((row) => emitted.push(row));

    component.state.form.controls.itemName.setValue('Milk');
    component.state.form.controls.barcode.setValue('B001');
    component.state.form.controls.uom.setValue('ltr');
    component.state.form.controls.batchNumber.setValue('BN-1');
    component.state.form.controls.totalPurchaseCost.setValue(42);
    component.state.form.controls.mrp.setValue(50);
    component.state.form.controls.salesPrice.setValue(48);

    fixture.debugElement.query(By.css('form')).triggerEventHandler('ngSubmit');

    expect(emitted).toHaveLength(1);
    expect((emitted[0] as { itemName: string }).itemName).toBe('Milk');
  });

  it('emits scannerRequested when the camera button is clicked', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    const emitted: unknown[] = [];
    component.scannerRequested.subscribe(() => emitted.push(true));

    fixture.debugElement.query(By.css('.camera-addon button')).triggerEventHandler('click');

    expect(emitted).toEqual([true]);
  });
});
