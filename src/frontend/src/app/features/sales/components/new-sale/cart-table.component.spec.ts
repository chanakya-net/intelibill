import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { CartQuantityChangedEvent, CartTableComponent } from './cart-table.component';

const item = {
  clientLineKey: 'line-1',
  barcode: 'B1',
  itemName: 'Coffee',
  batchNumber: 'X-1',
  inventoryBatchId: 'batch-1',
  quantity: 2,
  availableQuantity: 10,
  salesPrice: 120,
  mrp: 120,
  taxRatePercent: 18,
  taxIncluded: true,
  costPrice: 0,
  itemDiscountType: 0,
  itemDiscountValue: 0,
  hsnCode: '0902',
};

describe('CartTableComponent', () => {
  it('renders empty cart state when no rows are present', () => {
    TestBed.configureTestingModule({
      imports: [CartTableComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(CartTableComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('sales.newSale.emptyCart');
  });

  it('emits item quantity updates', () => {
    TestBed.configureTestingModule({
      imports: [CartTableComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(CartTableComponent);
    fixture.componentInstance.cartItems = [item as never];
    fixture.componentInstance.hasTax = (it) => it.taxRatePercent > 0;
    fixture.componentInstance.getUnitSubtotal = () => 100;
    fixture.componentInstance.getUnitTaxAmount = () => 18;
    fixture.componentInstance.getLineTotal = () => 118;

    const spy = vi.fn<(event: CartQuantityChangedEvent) => void>();
    fixture.componentInstance.quantityChanged.subscribe(spy);

    fixture.detectChanges();
    fixture.componentInstance.increase('line-1', 2);

    expect(spy).toHaveBeenCalled();
  });

  it('emits item removal', () => {
    TestBed.configureTestingModule({
      imports: [CartTableComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(CartTableComponent);
    fixture.componentInstance.cartItems = [item as never];
    fixture.componentInstance.hasTax = (it) => it.taxRatePercent > 0;
    fixture.componentInstance.getUnitSubtotal = () => 100;
    fixture.componentInstance.getUnitTaxAmount = () => 18;
    fixture.componentInstance.getLineTotal = () => 118;

    const spy = vi.fn();
    fixture.componentInstance.itemRemoved.subscribe(spy);
    fixture.detectChanges();

    fixture.componentInstance.remove('line-1');

    expect(spy).toHaveBeenCalledWith('line-1');
  });
});
