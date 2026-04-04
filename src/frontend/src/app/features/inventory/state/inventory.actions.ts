import { createActionGroup, emptyProps, props } from '@ngrx/store';

import { AddItemRequest, Item } from '../services/inventory.service';

export type ItemMutationType = 'add-item';

export const InventoryActions = createActionGroup({
  source: 'Inventory',
  events: {
    'Load Items Requested': emptyProps(),
    'Load Items Succeeded': props<{ items: readonly Item[] }>(),
    'Load Items Failed': props<{ errorMessage: string }>(),

    'Add Item Requested': props<{ payload: AddItemRequest }>(),
    'Add Item Succeeded': props<{ item: Item }>(),
    'Add Item Failed': props<{ errorMessage: string }>(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
  },
});
