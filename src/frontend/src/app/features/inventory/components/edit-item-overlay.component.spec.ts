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
import { EditItemOverlayComponent } from './edit-item-overlay.component';
import type { Item } from '../services/inventory.models';

describe('EditItemOverlayComponent', () => {
  const dispatch = vi.fn();
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationTypeSignal = signal<'update-item' | null>(null);
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

  const mockItem: Item = {
    id: '123',
    name: 'Rice',
    barcode: 'B001',
    description: 'Premium rice',
    uom: 'kg',
    isActive: true,
    currentStock: 50,
    hsnCode: '10063090',
    defaultTaxRatePercent: 5,
    defaultTaxIncluded: false,
  };

  function setup(item: Item = mockItem): EditItemOverlayComponent {
    TestBed.configureTestingModule({
      imports: [EditItemOverlayComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [{ provide: Store, useValue: store }],
    });

    const fixture = TestBed.createComponent(EditItemOverlayComponent);
    fixture.componentInstance.item = item;
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    dispatch.mockReset();
    store.selectSignal.mockClear();
    isSubmittingSignal.set(false);
    errorSignal.set('');
    lastMutationTypeSignal.set(null);
    lastMutationSucceededSignal.set(false);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('initializes form with item data on ngOnInit', () => {
    const component = setup();

    component.ngOnInit();

    expect(component.form.controls.name.value).toBe('Rice');
    expect(component.form.controls.barcode.value).toBe('B001');
    expect(component.form.controls.description.value).toBe('Premium rice');
    expect(component.form.controls.uom.value).toBe('kg');
    expect(component.form.controls.hsnCode.value).toBe('10063090');
    expect(component.form.controls.defaultTaxRatePercent.value).toBe(5);
  });

  it('initializes form with null description as empty string', () => {
    const itemWithoutDescription: Item = {
      ...mockItem,
      description: null,
    };
    const component = setup(itemWithoutDescription);

    component.ngOnInit();

    expect(component.form.controls.description.value).toBe('');
  });

  it('does not submit when required fields are missing', () => {
    const component = setup();
    component.ngOnInit();

    component.form.controls.name.setValue('');
    component.form.controls.barcode.setValue('');
    component.form.controls.uom.setValue('');

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: InventoryActions.updateItemRequested.type })
    );
  });

  it('does not submit when form fields exceed maximum length', () => {
    const component = setup();
    component.ngOnInit();

    component.form.controls.name.setValue('a'.repeat(181));
    component.form.controls.barcode.setValue('b'.repeat(129));
    component.form.controls.description.setValue('c'.repeat(1001));
    component.form.controls.uom.setValue('d'.repeat(33));
    component.form.controls.hsnCode.setValue('ABC');
    component.form.controls.defaultTaxRatePercent.setValue(101);

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: InventoryActions.updateItemRequested.type })
    );
  });

  it('dispatches update action with trimmed payload values', () => {
    const component = setup();
    component.ngOnInit();

    component.form.controls.name.setValue('  Premium Rice  ');
    component.form.controls.barcode.setValue('  B002  ');
    component.form.controls.description.setValue('  High quality  ');
    component.form.controls.uom.setValue('  kg  ');
    component.form.controls.hsnCode.setValue('  10063090  ');
    component.form.controls.defaultTaxRatePercent.setValue(12);

    component.onSubmit();

    expect(dispatch).toHaveBeenCalledWith(InventoryActions.clearError());
    expect(dispatch).toHaveBeenCalledWith(InventoryActions.clearMutationStatus());
    expect(dispatch).toHaveBeenCalledWith(
      InventoryActions.updateItemRequested({
        itemId: '123',
        payload: {
          name: 'Premium Rice',
          barcode: 'B002',
          description: 'High quality',
          uom: 'kg',
          hsnCode: '10063090',
          defaultTaxRatePercent: 12,
        },
      })
    );
  });

  it('emits closeRequested when close button is clicked', () => {
    const component = setup();
    const closeRequestedSpy = vi.spyOn(component.closeRequested, 'emit');

    component.onClose();

    expect(closeRequestedSpy).toHaveBeenCalled();
  });

  it('does not close when submitting', () => {
    isSubmittingSignal.set(true);
    const component = setup();
    const closeRequestedSpy = vi.spyOn(component.closeRequested, 'emit');

    component.onClose();

    expect(closeRequestedSpy).not.toHaveBeenCalled();
  });

  it('returns correct stock severity based on current stock', () => {
    const lowStockItem: Item = { ...mockItem, currentStock: 3 };
    const component = setup(lowStockItem);

    expect(component.getStockSeverity()).toBe('danger');

    const mediumStockItem: Item = { ...mockItem, currentStock: 30 };
    component.item = mediumStockItem;
    expect(component.getStockSeverity()).toBe('warn');

    const highStockItem: Item = { ...mockItem, currentStock: 100 };
    component.item = highStockItem;
    expect(component.getStockSeverity()).toBe('success');
  });

  it('converts empty description to null on submit', () => {
    const component = setup();
    component.ngOnInit();

    component.form.controls.name.setValue('Rice');
    component.form.controls.barcode.setValue('B001');
    component.form.controls.description.setValue('   ');
    component.form.controls.uom.setValue('kg');

    component.onSubmit();

    expect(dispatch).toHaveBeenCalledWith(
      expect.objectContaining({
        type: InventoryActions.updateItemRequested.type,
        itemId: '123',
        payload: expect.objectContaining({
          description: null,
        }),
      })
    );
  });

  it('does not submit when already submitting', () => {
    isSubmittingSignal.set(true);
    const component = setup();
    component.ngOnInit();

    component.form.controls.name.setValue('Updated');
    component.form.controls.barcode.setValue('B002');
    component.form.controls.uom.setValue('kg');

    component.onSubmit();

    const callCount = dispatch.mock.calls.filter(
      (call: any[]) => call[0]?.type === InventoryActions.updateItemRequested.type
    ).length;

    expect(callCount).toBe(0);
  });
});
