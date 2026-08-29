import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { InventoryActions } from '../state/inventory.actions';
import {
  selectInventoryErrorMessage,
  selectInventoryLastMutationSucceeded,
  selectInventoryLastMutationType,
  selectInventorySubmitting,
} from '../state/inventory.selectors';
import { AddProductOverlayComponent } from './add-product-overlay.component';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryService } from '../services/inventory.service';
import type { HsnLookupResult } from '../services/inventory.models';

describe('AddProductOverlayComponent', () => {
  const dispatch = vi.fn();
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationTypeSignal = signal<'add-item' | null>(null);
  const lastMutationSucceededSignal = signal(false);

  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectInventorySubmitting) {
        return isSubmittingSignal;
      }

      if (selector === selectInventoryErrorMessage) {
        return errorSignal;
      }

      if (selector === selectInventoryLastMutationType) {
        return lastMutationTypeSignal;
      }

      if (selector === selectInventoryLastMutationSucceeded) {
        return lastMutationSucceededSignal;
      }

      return signal(undefined);
    }),
  };

  const productCatalogSync = {
    filterByName: vi.fn(() => []),
    filterByBarcode: vi.fn(() => []),
    findByName: vi.fn<(name: string) => { name: string; barcode: string } | undefined>(
      () => undefined,
    ),
    findByBarcode: vi.fn<(barcode: string) => { name: string; barcode: string } | undefined>(
      () => undefined,
    ),
  };

  const emptyLookupResult: HsnLookupResult = {
    hsnCodes: [],
    taxScenarios: [],
  };

  const inventoryService = {
    lookupHsn: vi.fn(() => of(emptyLookupResult)),
    generateItemBarcode: vi.fn(() => of({ barcode: 'GEN-0001' })),
  };

  function setup(): AddProductOverlayComponent {
    TestBed.configureTestingModule({
      imports: [
        AddProductOverlayComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [
        { provide: Store, useValue: store },
        { provide: ProductCatalogSyncService, useValue: productCatalogSync },
        { provide: InventoryService, useValue: inventoryService },
      ],
    });

    const fixture = TestBed.createComponent(AddProductOverlayComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    dispatch.mockReset();
    store.selectSignal.mockClear();
    productCatalogSync.filterByName.mockClear();
    productCatalogSync.filterByBarcode.mockClear();
    productCatalogSync.findByName.mockClear();
    productCatalogSync.findByBarcode.mockClear();
    inventoryService.lookupHsn.mockReset();
    inventoryService.lookupHsn.mockReturnValue(of(emptyLookupResult));
    inventoryService.generateItemBarcode.mockReset();
    inventoryService.generateItemBarcode.mockReturnValue(of({ barcode: 'GEN-0001' }));
    isSubmittingSignal.set(false);
    errorSignal.set('');
    lastMutationTypeSignal.set(null);
    lastMutationSucceededSignal.set(false);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not submit when required fields are missing', () => {
    const component = setup();

    component.form.controls.name.setValue('');
    component.form.controls.barcode.setValue('');
    component.form.controls.uom.setValue('');

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: InventoryActions.addItemRequested.type }),
    );
  });

  it('rejects whitespace-only required text', () => {
    const component = setup();

    component.form.patchValue({ name: '   ', barcode: '   ', uom: '   ' });

    expect(component.form.controls.name.hasError('required')).toBe(true);
    expect(component.form.controls.barcode.hasError('required')).toBe(true);
    expect(component.form.controls.uom.hasError('required')).toBe(true);
  });

  it('accepts item text at the backend length limits', () => {
    const component = setup();

    component.form.patchValue({
      name: 'N'.repeat(180),
      barcode: 'B'.repeat(128),
      description: 'D'.repeat(1000),
      uom: 'U'.repeat(32),
    });

    expect(component.form.valid).toBe(true);
  });

  it('dispatches add action with trimmed payload values', () => {
    const component = setup();

    component.form.controls.name.setValue('  Premium Tea  ');
    component.form.controls.barcode.setValue('  B001  ');
    component.form.controls.description.setValue('  Product description  ');
    component.form.controls.uom.setValue('  packet  ');
    component.form.controls.hsnCode.setValue('  0902  ');
    component.form.controls.defaultTaxRatePercent.setValue(5);
    component.form.controls.isActive.setValue(true);

    component.onSubmit();

    expect(dispatch).toHaveBeenCalledWith(InventoryActions.clearError());
    expect(dispatch).toHaveBeenCalledWith(InventoryActions.clearMutationStatus());
    expect(dispatch).toHaveBeenCalledWith(
      InventoryActions.addItemRequested({
        payload: {
          name: 'Premium Tea',
          barcode: 'B001',
          description: 'Product description',
          uom: 'packet',
          hsnCode: '0902',
          defaultTaxRatePercent: 5,
          isActive: true,
        },
      }),
    );
  });

  it('does not submit when HSN code or tax rate are invalid', () => {
    const component = setup();

    component.form.controls.name.setValue('Premium Tea');
    component.form.controls.barcode.setValue('B001');
    component.form.controls.uom.setValue('packet');
    component.form.controls.hsnCode.setValue('ABC');
    component.form.controls.defaultTaxRatePercent.setValue(101);

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: InventoryActions.addItemRequested.type }),
    );
  });

  it('looks up HSN and tax on name blur and autofills when there is a single match', async () => {
    const component = setup();
    const singleLookupResult: HsnLookupResult = {
      hsnCodes: ['0902'],
      taxScenarios: [{ condition: 'General', taxPercentage: '18%' }],
    };
    inventoryService.lookupHsn.mockReturnValue(of(singleLookupResult));

    component.form.controls.name.setValue('Premium Tea');
    component.onNameBlur();

    await Promise.resolve();

    expect(inventoryService.lookupHsn).toHaveBeenCalledWith('Premium Tea');
    expect(component.form.controls.hsnCode.value).toBe('0902');
    expect(component.form.controls.defaultTaxRatePercent.value).toBe(18);
  });

  it('keeps multiple returned HSN and tax slabs as selectable suggestions', async () => {
    const component = setup();
    const multiLookupResult: HsnLookupResult = {
      hsnCodes: ['0902', '2106'],
      taxScenarios: [
        { condition: 'General', taxPercentage: '5%' },
        { condition: 'Premium', taxPercentage: '18%' },
      ],
    };
    inventoryService.lookupHsn.mockReturnValue(of(multiLookupResult));

    component.form.controls.name.setValue('Premium Tea');
    component.onNameBlur();

    await Promise.resolve();

    expect(component.suggestedHsnCodes()).toEqual(['0902', '2106']);
    expect(component.suggestedTaxSlabs()).toEqual(['5%', '18%']);
    expect(component.form.controls.hsnCode.value).toBe('');
    expect(component.form.controls.defaultTaxRatePercent.value).toBe(0);
  });

  it('allows selecting HSN and tax from suggestion list', () => {
    const component = setup();

    component.selectSuggestedHsnCode('3004');
    component.selectSuggestedTaxSlab('12%');

    expect(component.form.controls.hsnCode.value).toBe('3004');
    expect(component.form.controls.defaultTaxRatePercent.value).toBe(12);
  });

  it('looks up HSN and tax when a product name is selected', async () => {
    const component = setup();
    const singleLookupResult: HsnLookupResult = {
      hsnCodes: ['2106'],
      taxScenarios: [{ condition: 'General', taxPercentage: '12%' }],
    };
    inventoryService.lookupHsn.mockReturnValue(of(singleLookupResult));
    productCatalogSync.findByName.mockReturnValue({ name: 'Mix', barcode: 'BAR-12' });

    component.form.controls.name.setValue('Mix');
    component.onNameSelected('Mix');

    await Promise.resolve();

    expect(component.form.controls.barcode.value).toBe('BAR-12');
    expect(inventoryService.lookupHsn).toHaveBeenCalledWith('Mix');
    expect(component.form.controls.hsnCode.value).toBe('2106');
    expect(component.form.controls.defaultTaxRatePercent.value).toBe(12);
  });

  it('looks up HSN and tax when barcode selection resolves to catalog item name', async () => {
    const component = setup();
    const singleLookupResult: HsnLookupResult = {
      hsnCodes: ['3004'],
      taxScenarios: [{ condition: 'General', taxPercentage: '5%' }],
    };
    inventoryService.lookupHsn.mockReturnValue(of(singleLookupResult));
    productCatalogSync.findByBarcode.mockReturnValue({ name: 'Syrup', barcode: 'B-5' });

    component.onBarcodeSelected('B-5');

    await Promise.resolve();

    expect(component.form.controls.name.value).toBe('Syrup');
    expect(inventoryService.lookupHsn).toHaveBeenCalledWith('Syrup');
    expect(component.form.controls.hsnCode.value).toBe('3004');
    expect(component.form.controls.defaultTaxRatePercent.value).toBe(5);
  });

  it('generates barcode and patches form when barcode field is empty', async () => {
    const component = setup();
    component.form.controls.barcode.setValue('');

    await component.onGenerateBarcode();

    expect(inventoryService.generateItemBarcode).toHaveBeenCalled();
    expect(component.form.controls.barcode.value).toBe('GEN-0001');
    expect(component.barcodeReplaceConfirmVisible()).toBe(false);
    expect(component.barcodeGenerateError()).toBe('');
  });

  it('shows replace confirmation when barcode field has value', async () => {
    const component = setup();
    component.form.controls.barcode.setValue('EXISTING-BAR');

    await component.onGenerateBarcode();

    expect(inventoryService.generateItemBarcode).toHaveBeenCalled();
    expect(component.barcodeReplaceConfirmVisible()).toBe(true);
    expect(component.form.controls.barcode.value).toBe('EXISTING-BAR');
  });

  it('patches barcode on confirmation', async () => {
    const component = setup();
    component.form.controls.barcode.setValue('EXISTING-BAR');

    await component.onGenerateBarcode();
    component.confirmBarcodeReplace();

    expect(component.form.controls.barcode.value).toBe('GEN-0001');
    expect(component.barcodeReplaceConfirmVisible()).toBe(false);
  });

  it('keeps existing barcode on cancellation', async () => {
    const component = setup();
    component.form.controls.barcode.setValue('EXISTING-BAR');

    await component.onGenerateBarcode();
    component.cancelBarcodeReplace();

    expect(component.form.controls.barcode.value).toBe('EXISTING-BAR');
    expect(component.barcodeReplaceConfirmVisible()).toBe(false);
  });

  it('sets error signal when generate barcode fails', async () => {
    inventoryService.generateItemBarcode.mockReturnValue(throwError(() => new Error('network')));
    const component = setup();
    component.form.controls.barcode.setValue('');

    await component.onGenerateBarcode();

    expect(component.barcodeGenerateError()).toBe('inventory.generateBarcodeError');
    expect(component.form.controls.barcode.value).toBe('');
  });

  it('does not submit generate while already generating', async () => {
    let resolve!: (v: { barcode: string }) => void;
    inventoryService.generateItemBarcode.mockReturnValue(
      new (await import('rxjs')).Observable((sub) => {
        resolve = (v) => {
          sub.next(v);
          sub.complete();
        };
      }),
    );
    const component = setup();

    const p1 = component.onGenerateBarcode();
    const p2 = component.onGenerateBarcode();
    resolve({ barcode: 'GEN-X' });
    await Promise.all([p1, p2]);

    expect(inventoryService.generateItemBarcode).toHaveBeenCalledTimes(1);
  });

  it('does not call lookup for short product names', async () => {
    const component = setup();

    component.form.controls.name.setValue('AB');
    component.onNameBlur();

    await Promise.resolve();

    expect(inventoryService.lookupHsn).not.toHaveBeenCalled();
  });

  it('does not override manually edited HSN and tax values', async () => {
    const component = setup();
    const singleLookupResult: HsnLookupResult = {
      hsnCodes: ['0902'],
      taxScenarios: [{ condition: 'General', taxPercentage: '18%' }],
    };
    inventoryService.lookupHsn.mockReturnValue(of(singleLookupResult));

    component.form.controls.name.setValue('Premium Tea');
    component.form.controls.hsnCode.setValue('9999');
    component.form.controls.hsnCode.markAsDirty();
    component.form.controls.defaultTaxRatePercent.setValue(2);
    component.form.controls.defaultTaxRatePercent.markAsDirty();

    component.onNameBlur();

    await Promise.resolve();

    expect(inventoryService.lookupHsn).toHaveBeenCalledWith('Premium Tea');
    expect(component.form.controls.hsnCode.value).toBe('9999');
    expect(component.form.controls.defaultTaxRatePercent.value).toBe(2);
  });
});
