import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
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
    findByName: vi.fn(() => undefined),
    findByBarcode: vi.fn(() => undefined),
  };

  function setup(): AddProductOverlayComponent {
    TestBed.configureTestingModule({
      imports: [AddProductOverlayComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: Store, useValue: store },
        { provide: ProductCatalogSyncService, useValue: productCatalogSync },
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
      expect.objectContaining({ type: InventoryActions.addItemRequested.type })
    );
  });

  it('dispatches add action with trimmed payload values', () => {
    const component = setup();

    component.form.controls.name.setValue('  Premium Tea  ');
    component.form.controls.barcode.setValue('  B001  ');
    component.form.controls.description.setValue('  Product description  ');
    component.form.controls.uom.setValue('  packet  ');
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
          isActive: true,
        },
      })
    );
  });
});
