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
