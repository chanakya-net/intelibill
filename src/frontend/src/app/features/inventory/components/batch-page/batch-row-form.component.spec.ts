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

  it('starts with optional details collapsed', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    expect(component.optionalDetailsExpanded()).toBe(false);
    expect(fixture.debugElement.query(By.css('#batch-row-form-optional-details'))).toBeNull();
  });

  it('toggles optional details via icon button', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    const button = fixture.debugElement.query(By.css('.optional-details-toggle'));

    expect(button).toBeTruthy();

    button.triggerEventHandler('click');
    fixture.detectChanges();
    expect(component.optionalDetailsExpanded()).toBe(true);

    button.triggerEventHandler('click');
    fixture.detectChanges();
    expect(component.optionalDetailsExpanded()).toBe(false);
  });

  it('blocks manual add when MRP or sales price is missing', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    const submitted: unknown[] = [];
    component.rowSubmitted.subscribe((row) => submitted.push(row));

    component.state.form.controls.itemName.setValue('Milk');
    component.state.form.controls.barcode.setValue('B001');
    component.state.form.controls.mrp.setValue(0);
    component.state.form.controls.salesPrice.setValue(0);

    fixture.debugElement.query(By.css('form')).triggerEventHandler('ngSubmit');
    fixture.detectChanges();

    expect(submitted).toHaveLength(0);
    expect(component.optionalDetailsExpanded()).toBe(true);
    expect(component.pricingGuardVisible()).toBe(true);
    expect(component.state.form.controls.mrp.touched).toBe(true);
    expect(component.state.form.controls.salesPrice.touched).toBe(true);
    expect(fixture.debugElement.query(By.css('#batch-row-form-optional-details'))).not.toBeNull();
    expect(fixture.debugElement.query(By.css('.pricing-guard'))).not.toBeNull();
  });

  it('collapses optional details and clears pricing guard after successful add', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    const submitted: unknown[] = [];
    component.rowSubmitted.subscribe((row) => submitted.push(row));

    component.state.form.controls.itemName.setValue('Milk');
    component.state.form.controls.barcode.setValue('B001');

    fixture.debugElement.query(By.css('form')).triggerEventHandler('ngSubmit');
    fixture.detectChanges();

    expect(component.pricingGuardVisible()).toBe(true);
    expect(component.optionalDetailsExpanded()).toBe(true);

    component.state.form.controls.mrp.setValue(50);
    component.state.form.controls.salesPrice.setValue(48);

    fixture.debugElement.query(By.css('form')).triggerEventHandler('ngSubmit');
    fixture.detectChanges();

    expect(submitted).toHaveLength(1);
    expect(component.pricingGuardVisible()).toBe(false);
    expect(component.optionalDetailsExpanded()).toBe(false);
    expect(fixture.debugElement.query(By.css('#batch-row-form-optional-details'))).toBeNull();
  });

  it('expands optional details when populating from an existing row', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    component.populateFromRow({
      clientRowId: 'row-1',
      itemName: 'Milk',
      barcode: 'B001',
      itemDescription: null,
      uom: 'PCS',
      batchNumber: 'BN-1',
      quantity: 1,
      totalPurchaseCost: 0,
      mrp: 50,
      salesPrice: 48,
      taxRatePercent: 18,
      taxIncluded: true,
      purchaseTaxIncluded: true,
      hsnCode: null,
      expiryDate: null,
      manufacturingDate: null,
      supplierId: null,
      referenceNumber: null,
      notes: null,
      performedAt: new Date().toISOString(),
    });

    fixture.detectChanges();

    expect(component.optionalDetailsExpanded()).toBe(true);
    expect(fixture.debugElement.query(By.css('#batch-row-form-optional-details'))).not.toBeNull();
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
