import { signal } from '@angular/core';
import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';

import type { AddBatchResult } from '../services/sale-cart-state.service';
import type { SellableGoodsDto } from '../services/sale.models';
import { NewSalePageLifecycleService } from './new-sale-page.lifecycle.service';

const weighed: SellableGoodsDto = {
  kind: 'Goods',
  inventoryBatchId: 'batch-1',
  barcode: 'RICE',
  itemName: 'Rice',
  batchNumber: 'B1',
  quantity: 3,
  salesPrice: 50,
  mrp: 60,
  taxRatePercent: 5,
  taxIncluded: false,
  expiryDate: null,
  hsnCode: null,
  uom: 'kg',
  quantityScale: 3,
  documentUnit: 'KGS',
};

/** The scan of a single weighed item over a stand-in cart; nothing else of the page is built. */
function harness(options: { sellable?: SellableGoodsDto; inCart: number; add: AddBatchResult }) {
  const addBatchToCart = vi.fn(() => options.add);
  const sellable = options.sellable ?? weighed;
  const page = Object.assign(Object.create(NewSalePageLifecycleService.prototype), {
    searchInput: signal('RICE'),
    isOfflineMode: () => false,
    isOfflineEligible: () => false,
    batchSearchError: signal(''),
    batchSearchErrorParams: signal<unknown>(null),
    isSearchingBatches: signal(false),
    availableBatches: signal<unknown[]>([]),
    availableSellables: signal<unknown[]>([]),
    showBatchPicker: signal(false),
    transloco: { getActiveLang: () => 'en' },
    cart: signal(
      options.inCart > 0
        ? [{ inventoryBatchId: 'batch-1', quantity: options.inCart, availableQuantity: sellable.quantity }]
        : [],
    ),
    saleService: { getSellables: () => of([sellable]) },
    cartState: { addBatchToCart },
    applyProductDefaultsForLine: vi.fn(),
    resetSearchAndPickerState: vi.fn(),
  }) as NewSalePageLifecycleService & {
    readonly batchSearchError: ReturnType<typeof signal<string>>;
    readonly showBatchPicker: ReturnType<typeof signal<boolean>>;
    readonly availableBatches: ReturnType<typeof signal<unknown[]>>;
  };
  return { page, addBatchToCart };
}

describe('NewSalePageLifecycleService barcode scan of a weighed item', () => {
  it('explains a barcode with one blocked lot in the diagnostic picker without attempting Add', async () => {
    const { page, addBatchToCart } = harness({ inCart: 0, add: { added: false, addedLineKey: null, refusal: 'exceedsStock' },
      sellable: { ...weighed, availableQuantity: 0, sellability: { allowed: false, reasonCode: 'Expiry.Expired', warning: false, requiresApproval: false,
        policyVersion: 2, catalogVersion: 3, businessDate: '2026-11-09', physicalQuantity: 3, quarantinedQuantity: 0, availableQuantity: 0 } } });
    await page.onBarcodeSearch();
    expect(page.showBatchPicker()).toBe(true); expect(addBatchToCart).not.toHaveBeenCalled();
  });
  it('opens the picker when less than one unit is left', async () => {
    const { page } = harness({
      sellable: { ...weighed, quantity: 3 },
      inCart: 2.5,
      add: { added: false, addedLineKey: null, refusal: 'exceedsStock' },
    });

    await page.onBarcodeSearch();

    expect(page.showBatchPicker()).toBe(true);
    expect(page.availableBatches()).toHaveLength(1);
    expect(page.batchSearchError()).toBe('');
  });

  it('opens the picker for a batch of less than one unit that is not yet in the cart', async () => {
    const { page } = harness({
      sellable: { ...weighed, quantity: 0.4 },
      inCart: 0,
      add: { added: false, addedLineKey: null, refusal: 'exceedsStock' },
    });

    await page.onBarcodeSearch();

    expect(page.showBatchPicker()).toBe(true);
  });

  it('shows the stock refusal and no picker when nothing is left', async () => {
    const { page } = harness({
      sellable: { ...weighed, quantity: 3 },
      inCart: 3,
      add: { added: false, addedLineKey: null, refusal: 'exceedsStock' },
    });

    await page.onBarcodeSearch();

    expect(page.showBatchPicker()).toBe(false);
    expect(page.batchSearchError()).toBe('sales.newSale.exceedsStock');
  });
});
