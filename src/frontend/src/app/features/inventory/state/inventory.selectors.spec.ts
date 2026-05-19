import { Item } from '../services/inventory.service';
import { InventoryState } from './inventory.reducer';
import {
  selectInventoryErrorMessage,
  selectInventoryItems,
  selectInventoryLastMutationSucceeded,
  selectInventoryLastMutationType,
  selectInventoryLoadingItems,
  selectInventorySubmitting,
} from './inventory.selectors';

const itemOne: Item = {
  id: 'item-1',
  name: 'Milk',
  barcode: 'B001',
  description: null,
  uom: 'ltr',
  isActive: true,
  currentStock: 10,
  hsnCode: '0401',
  defaultTaxRatePercent: 5,
  defaultTaxIncluded: false,
};

const itemTwo: Item = {
  id: 'item-2',
  name: 'Bread',
  barcode: 'B002',
  description: null,
  uom: 'pcs',
  isActive: true,
  currentStock: 4,
  hsnCode: null,
  defaultTaxRatePercent: 0,
  defaultTaxIncluded: false,
};

describe('inventory selectors', () => {
  const inventoryState: InventoryState = {
    ids: ['item-1', 'item-2'],
    entities: {
      'item-1': itemOne,
      'item-2': itemTwo,
    },
    loadingItems: true,
    submitting: true,
    errorMessage: 'errors.items.unableToLoadItems',
    lastMutationType: 'add-item',
    lastMutationSucceeded: true,
  };

  const rootState = {
    inventory: inventoryState,
  };

  it('selects inventory items list', () => {
    expect(selectInventoryItems(rootState as never)).toEqual([itemOne, itemTwo]);
  });

  it('selects loading items state', () => {
    expect(selectInventoryLoadingItems(rootState as never)).toBe(true);
  });

  it('selects submitting state', () => {
    expect(selectInventorySubmitting(rootState as never)).toBe(true);
  });

  it('selects error message', () => {
    expect(selectInventoryErrorMessage(rootState as never)).toBe('errors.items.unableToLoadItems');
  });

  it('selects last mutation type', () => {
    expect(selectInventoryLastMutationType(rootState as never)).toBe('add-item');
  });

  it('selects last mutation status', () => {
    expect(selectInventoryLastMutationSucceeded(rootState as never)).toBe(true);
  });
});
