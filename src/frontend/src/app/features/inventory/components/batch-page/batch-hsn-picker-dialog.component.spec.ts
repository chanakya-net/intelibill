import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ProductCatalogSyncService } from '../../../../core/services/product-catalog-sync.service';
import { InventoryService } from '../../services/inventory.service';
import { SuppliersFacade } from '../../../suppliers/state/suppliers.facade';
import { BatchHsnPickerDialogComponent } from './batch-hsn-picker-dialog.component';

describe('BatchHsnPickerDialogComponent', () => {
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
      of({
        hsnCodes: ['0401'],
        taxScenarios: [{ condition: 'General dairy', taxPercentage: '18%' }],
      }),
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
      imports: [BatchHsnPickerDialogComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: InventoryService, useValue: inventoryService },
        { provide: ProductCatalogSyncService, useValue: catalogSync },
        { provide: SuppliersFacade, useValue: suppliersFacade },
      ],
    });

    const fixture = TestBed.createComponent(BatchHsnPickerDialogComponent);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    inventoryService.lookupHsn.mockClear();
    inventoryService.getProductDetailsByNameOrBarcode.mockClear();
  });

  it('renders the selected HSN chip and picker card', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    component.state.selectedHsnCode.set('0401');
    component.state.form.controls.taxRatePercent.setValue(18);
    component.state.pickerOpen.set(true);
    component.state.hsnResult.set({
      hsnCodes: ['0401'],
      taxScenarios: [{ condition: 'General dairy', taxPercentage: '18%' }],
    });
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.hsn-chip'))).not.toBeNull();
    expect(fixture.debugElement.query(By.css('.hsn-picker-card'))).not.toBeNull();
  });

  it('requests a fresh HSN lookup when change is clicked', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    const spy = vi.spyOn(component.state, 'onChangeHsnClick').mockResolvedValue(undefined);
    component.state.selectedHsnCode.set('0401');
    fixture.detectChanges();

    fixture.debugElement.query(By.css('.hsn-chip-change')).triggerEventHandler('click');

    expect(spy).toHaveBeenCalledTimes(1);
  });
});
