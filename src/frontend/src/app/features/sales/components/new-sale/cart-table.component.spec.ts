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
      imports: [
        CartTableComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(CartTableComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('sales.newSale.emptyCart');
  });

  it('keeps advanced line controls hidden until toggled open', async () => {
    TestBed.configureTestingModule({
      imports: [
        CartTableComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(CartTableComponent);
    fixture.componentInstance.cartItems = [item as never];
    fixture.componentInstance.hasTax = (it) => it.taxRatePercent > 0;
    fixture.componentInstance.getUnitSubtotal = () => 100;
    fixture.componentInstance.getUnitTaxAmount = () => 18;
    fixture.componentInstance.getLineTotal = () => 118;
    fixture.componentInstance.getPreviewLine = () =>
      ({
        configuredBatchRulePercentage: 12,
      }) as never;
    fixture.componentInstance.getCartItemHsnError = () => 'sales.newSale.hsnInvalid';
    fixture.componentInstance.getCartItemTaxError = () => 'sales.newSale.taxInvalid';
    fixture.componentInstance.getCartItemDiscountError = () => 'sales.newSale.discountInvalid';
    const openLines = new Set<string>();
    fixture.componentInstance.isLineDiscountEditorOpen = (itemId) => openLines.has(itemId);
    const toggleSpy = vi.fn();
    fixture.componentInstance.lineDiscountEditorToggled.subscribe(toggleSpy);

    fixture.detectChanges();
    await fixture.whenStable();

    const text = (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ');
    expect(text).toContain('Coffee');
    expect(text).toContain('B1');
    expect(text).toContain('en.sales.newSale.price');
    expect(text).toContain('en.sales.newSale.advancedLineEdit');
    const advancedRow = fixture.nativeElement.querySelector(
      '.advanced-line-edit-row',
    ) as HTMLTableRowElement;
    expect(advancedRow.hidden).toBe(true);

    fixture.componentInstance.toggleLineDiscountEditor('line-1');
    expect(toggleSpy).toHaveBeenCalledWith('line-1');
    openLines.add('line-1');
    fixture.detectChanges();
    await fixture.whenStable();

    const advancedPanel = fixture.nativeElement.querySelector('.advanced-line-edit') as HTMLElement;
    expect(advancedRow.hidden).toBe(false);
    expect(advancedPanel).toBeTruthy();
    const expandedText = (advancedPanel.textContent as string).replace(/\s+/g, ' ');
    expect(expandedText).toContain('en.sales.newSale.hsnCode');
    expect(expandedText).toContain('en.sales.newSale.taxRatePercent');
    const discountInput = fixture.nativeElement.querySelector(
      'input[placeholder="en.sales.newSale.discounts.value"]',
    ) as HTMLInputElement;
    expect(discountInput).toBeTruthy();
    expect(expandedText).toContain('en.sales.newSale.hsnInvalid');
    expect(expandedText).toContain('en.sales.newSale.taxInvalid');
    expect(expandedText).toContain('en.sales.newSale.discountInvalid');
  });

  it('emits item quantity updates and removal', () => {
    TestBed.configureTestingModule({
      imports: [
        CartTableComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(CartTableComponent);
    fixture.componentInstance.cartItems = [item as never];
    fixture.componentInstance.hasTax = (it) => it.taxRatePercent > 0;
    fixture.componentInstance.getUnitSubtotal = () => 100;
    fixture.componentInstance.getUnitTaxAmount = () => 18;
    fixture.componentInstance.getLineTotal = () => 118;

    const quantitySpy = vi.fn<(event: CartQuantityChangedEvent) => void>();
    const removedSpy = vi.fn();
    fixture.componentInstance.quantityChanged.subscribe(quantitySpy);
    fixture.componentInstance.itemRemoved.subscribe(removedSpy);
    fixture.detectChanges();

    fixture.componentInstance.increase('line-1', 2);
    fixture.componentInstance.decrease('line-1', 2);
    fixture.componentInstance.remove('line-1');

    expect(quantitySpy).toHaveBeenCalledWith({ itemId: 'line-1', qty: 3 });
    expect(quantitySpy).toHaveBeenCalledWith({ itemId: 'line-1', qty: 1 });
    expect(removedSpy).toHaveBeenCalledWith('line-1');
  });

  it('normalizes tax rate to 2 decimal places before emitting', () => {
    TestBed.configureTestingModule({
      imports: [
        CartTableComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(CartTableComponent);
    const taxSpy = vi.fn();
    fixture.componentInstance.cartItemTaxRateChange.subscribe(taxSpy);

    fixture.componentInstance.emitTaxRate('line-1', 12.345);
    expect(taxSpy).toHaveBeenCalledWith({ itemId: 'line-1', value: 12.35 });

    taxSpy.mockClear();
    fixture.componentInstance.emitTaxRate('line-1', 5.999);
    expect(taxSpy).toHaveBeenCalledWith({ itemId: 'line-1', value: 6.0 });

    taxSpy.mockClear();
    fixture.componentInstance.emitTaxRate('line-1', 2.675);
    expect(taxSpy).toHaveBeenCalledWith({ itemId: 'line-1', value: 2.67 });

    taxSpy.mockClear();
    fixture.componentInstance.emitTaxRate('line-1', null);
    expect(taxSpy).toHaveBeenCalledWith({ itemId: 'line-1', value: 0 });
  });

  it('normalizes discount value to 2 decimal places before emitting', () => {
    TestBed.configureTestingModule({
      imports: [
        CartTableComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(CartTableComponent);
    const discountSpy = vi.fn();
    fixture.componentInstance.cartItemDiscountValueChange.subscribe(discountSpy);

    fixture.componentInstance.emitDiscountValue('line-1', 5.678);
    expect(discountSpy).toHaveBeenCalledWith({ itemId: 'line-1', value: '5.68' });

    discountSpy.mockClear();
    fixture.componentInstance.emitDiscountValue('line-1', 10.001);
    expect(discountSpy).toHaveBeenCalledWith({ itemId: 'line-1', value: '10' });

    discountSpy.mockClear();
    fixture.componentInstance.emitDiscountValue('line-1', 2.675);
    expect(discountSpy).toHaveBeenCalledWith({ itemId: 'line-1', value: '2.67' });

    discountSpy.mockClear();
    fixture.componentInstance.emitDiscountValue('line-1', null);
    expect(discountSpy).toHaveBeenCalledWith({ itemId: 'line-1', value: '0' });
  });

  it('renders per-unit final price in breakdown', () => {
    TestBed.configureTestingModule({
      imports: [
        CartTableComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(CartTableComponent);
    fixture.componentInstance.cartItems = [item as never];
    fixture.componentInstance.hasTax = () => true;
    fixture.componentInstance.getUnitSubtotal = () => 50;
    fixture.componentInstance.getUnitTaxAmount = () => 9;
    fixture.componentInstance.getUnitFinalPrice = () => 59;
    fixture.componentInstance.getLineTotal = () => 118;
    fixture.detectChanges();

    const text = (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ');
    expect(text).toContain('₹59.00');
    expect(text).toContain('₹118.00');
  });
});
