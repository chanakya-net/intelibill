import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi, describe, expect, it } from 'vitest';

import { BatchPickerDialogComponent } from './batch-picker-dialog.component';
import type { SellableGoodsDto } from '../../../sales/services/sale.models';

describe('BatchPickerDialogComponent', () => {
  const batch: SellableGoodsDto = {
    kind: 'Goods',
    itemName: 'Milk',
    barcode: 'MILK123',
    batchNumber: 'B-01',
    inventoryBatchId: 'inv-batch-1',
    quantity: 10,
    mrp: 50,
    salesPrice: 55,
    taxRatePercent: 18,
    taxIncluded: true,
    expiryDate: null,
  };

  it('emits selected batch when add is confirmed', () => {
    TestBed.configureTestingModule({
      imports: [BatchPickerDialogComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(BatchPickerDialogComponent);
    const component = fixture.componentInstance;
    const spy = vi.fn();

    component.visible = true;
    component.batches = [batch];
    component.batchSelected.subscribe(spy);

    component.onSelectBatch(batch);
    component.onAddToCart();

    expect(spy).toHaveBeenCalledWith(batch);
  });

  it('emits quantity changes', () => {
    TestBed.configureTestingModule({
      imports: [BatchPickerDialogComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(BatchPickerDialogComponent);
    const component = fixture.componentInstance;
    const spy = vi.fn();
    component.quantityChanged.subscribe(spy);

    component.onQuantityChanged(5);

    expect(spy).toHaveBeenCalledWith(5);
  });

  function renderWithSelection(selected: SellableGoodsDto) {
    TestBed.configureTestingModule({
      imports: [BatchPickerDialogComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(BatchPickerDialogComponent);
    const component = fixture.componentInstance;
    const selectedSpy = vi.fn();
    const quantitySpy = vi.fn();
    component.batchSelected.subscribe(selectedSpy);
    component.quantityChanged.subscribe(quantitySpy);
    component.visible = true;
    component.batches = [selected];
    component.onSelectBatch(selected);
    fixture.detectChanges();

    const input = document.querySelector('app-quantity-field input') as HTMLInputElement;
    const type = (text: string): void => {
      input.value = text;
      input.dispatchEvent(new Event('input'));
    };
    return { fixture, component, input, type, selectedSpy, quantitySpy };
  }

  it('takes an exact fraction of a weighed batch', () => {
    const kg: SellableGoodsDto = { ...batch, uom: 'kg', quantityScale: 3, quantity: 2.5 };
    const { component, input, type, selectedSpy, quantitySpy } = renderWithSelection(kg);

    expect(input.getAttribute('inputmode')).toBe('decimal');
    type('0.25');
    input.dispatchEvent(new Event('blur'));
    component.onAddToCart();

    expect(quantitySpy).toHaveBeenCalledWith(0.25);
    expect(selectedSpy).toHaveBeenCalledWith(kg);
  });

  it('adds nothing while the quantity typed is one the unit cannot hold', () => {
    const pcs: SellableGoodsDto = { ...batch, uom: 'PCS', quantityScale: 0 };
    const { fixture, component, input, type, selectedSpy, quantitySpy } = renderWithSelection(pcs);

    type('0.5');
    component.onAddToCart();
    fixture.detectChanges();

    expect(quantitySpy).not.toHaveBeenCalled();
    expect(selectedSpy).not.toHaveBeenCalled();
    expect(input.getAttribute('aria-invalid')).toBe('true');
  });

  it('adds nothing beyond the stock of the selected batch', () => {
    const { component, type, selectedSpy } = renderWithSelection({ ...batch, quantity: 3 });

    type('4');
    component.onAddToCart();

    expect(selectedSpy).not.toHaveBeenCalled();
  });

  it('emits closed on hidden event', () => {
    TestBed.configureTestingModule({
      imports: [BatchPickerDialogComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(BatchPickerDialogComponent);
    const component = fixture.componentInstance;
    const spy = vi.fn();
    component.closed.subscribe(spy);

    component.onClose();

    expect(spy).toHaveBeenCalled();
  });
});
