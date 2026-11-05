import { signal } from '@angular/core';
import { describe, expect, it, vi } from 'vitest';

import type { AvailableBatchDto } from '../../inventory/services/inventory.models';
import type { QuantityChangeResult } from '../services/sale-cart-state.service';
import type { SellableDto } from '../services/sale.models';
import { NewSalePageCartSelectionService } from './new-sale-page.cart-selection.service';

/** The page's cart selection methods over a stand-in cart; nothing else of the page is built. */
function harness(result: QuantityChangeResult = 'ok') {
  const cartState = {
    setQuantity: vi.fn((_lineKey: string, _value: number): QuantityChangeResult => result),
    onIncreaseCartItem: vi.fn(),
    onDecreaseCartItem: vi.fn(),
  };
  const page = Object.assign(Object.create(NewSalePageCartSelectionService.prototype), {
    cart: signal([{ clientLineKey: 'line-1', quantity: 1, quantityScale: 3, availableQuantity: 50 }]),
    cartState,
    batchSearchError: signal(''),
    selectedSellable: signal<SellableDto | null>(null),
    batchPickerForm: { patchValue: vi.fn() },
    onServiceItemQuantityChange: vi.fn(),
    onIncreaseCartItem: vi.fn(),
    onDecreaseCartItem: vi.fn(),
  }) as NewSalePageCartSelectionService & {
    readonly batchPickerForm: { readonly patchValue: ReturnType<typeof vi.fn> };
    readonly onServiceItemQuantityChange: ReturnType<typeof vi.fn>;
    readonly onIncreaseCartItem: ReturnType<typeof vi.fn>;
    readonly onDecreaseCartItem: ReturnType<typeof vi.fn>;
  };
  return { page, cartState };
}

describe('NewSalePageCartSelectionService', () => {
  it('large_change_is_one_operation: 1 to 40 is one exact quantity change', () => {
    const { page, cartState } = harness();

    page.onCartTableQuantityChanged({ itemId: 'line-1', qty: 40 });

    expect(cartState.setQuantity).toHaveBeenCalledTimes(1);
    expect(cartState.setQuantity).toHaveBeenCalledWith('line-1', 40);
    expect(page.onIncreaseCartItem).not.toHaveBeenCalled();
    expect(page.onDecreaseCartItem).not.toHaveBeenCalled();
    expect(cartState.onIncreaseCartItem).not.toHaveBeenCalled();
  });

  it('passes a typed fraction through untouched', () => {
    const { page, cartState } = harness();

    page.onCartTableQuantityChanged({ itemId: 'line-1', qty: 0.25 });

    expect(cartState.setQuantity).toHaveBeenCalledWith('line-1', 0.25);
  });

  it('says why the cart refused a quantity and clears it once a change lands', () => {
    const refused = harness('exceedsStock');
    refused.page.onCartTableQuantityChanged({ itemId: 'line-1', qty: 60 });
    expect(refused.page.batchSearchError()).toBe('sales.newSale.exceedsStock');

    const tooPrecise = harness('tooPrecise');
    tooPrecise.page.onCartTableQuantityChanged({ itemId: 'line-1', qty: 0.0001 });
    expect(tooPrecise.page.batchSearchError()).toBe('sales.newSale.quantityPrecisionExceeded');

    refused.cartState.setQuantity.mockReturnValue('ok');
    refused.page.onCartTableQuantityChanged({ itemId: 'line-1', qty: 2 });
    expect(refused.page.batchSearchError()).toBe('');
  });

  it('leaves an unrelated search message alone', () => {
    const { page } = harness();
    page.batchSearchError.set('sales.newSale.noBatchesFound');

    page.onCartTableQuantityChanged({ itemId: 'line-1', qty: 2 });

    expect(page.batchSearchError()).toBe('sales.newSale.noBatchesFound');
  });

  it('routes a service line to the service quantity handler', () => {
    const { page, cartState } = harness();

    page.onCartTableQuantityChanged({ itemId: 'svc-line', qty: 3 });

    expect(page.onServiceItemQuantityChange).toHaveBeenCalledWith('svc-line', 3);
    expect(cartState.setQuantity).not.toHaveBeenCalled();
  });

  it('keeps the unit and precision of a picked batch', () => {
    const { page } = harness();
    const batch: AvailableBatchDto = {
      barcode: 'RICE',
      itemName: 'Rice',
      batchNumber: 'B1',
      inventoryBatchId: 'batch-1',
      quantity: 12.5,
      salesPrice: 50,
      mrp: 60,
      taxRatePercent: 5,
      taxIncluded: false,
      expiryDate: null,
      uom: 'kg',
      quantityScale: 3,
      documentUnit: 'KGS',
    };

    page.onSelectBatch(batch);

    expect(page.selectedSellable()).toMatchObject({ kind: 'Goods', uom: 'kg', quantityScale: 3, documentUnit: 'KGS' });
    expect(page.batchPickerForm.patchValue).toHaveBeenCalledWith({ batchNumber: 'B1', quantity: 1 });
  });
});
