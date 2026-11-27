import { createActionGroup, emptyProps, props } from '@ngrx/store';

import type {
  AddItemRequest,
  InventoryCatalogQuery,
  InventoryCatalogResponse,
  Item,
  UpdateItemRequest,
} from '../services/inventory.models';

export type ItemMutationType = 'add-item' | 'update-item';

export const InventoryActions = createActionGroup({
  source: 'Inventory',
  events: {
    'Load Items Requested': props<{ query?: InventoryCatalogQuery }>(),
    'Load Items Succeeded': props<InventoryCatalogResponse>(),
    'Load Items Failed': props<{ errorMessage: string }>(),

    'Add Item Requested': props<{ payload: AddItemRequest }>(),
    'Add Item Succeeded': props<{ item: Item }>(),
    'Add Item Failed': props<{ errorMessage: string }>(),

    'Update Item Requested': props<{ itemId: string; payload: UpdateItemRequest }>(),
    'Update Item Succeeded': emptyProps(),
    'Update Item Failed': props<{ errorMessage: string }>(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
  },
});
