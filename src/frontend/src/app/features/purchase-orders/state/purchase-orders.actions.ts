import { createActionGroup, emptyProps, props } from '@ngrx/store';

import {
  CreatePurchaseOrderDraftRequest,
  PurchaseOrderDetail,
  PurchaseOrderListFilters,
  PurchaseOrderListItem,
  PurchaseOrderListResult,
} from '../services/purchase-order.service';

export const PurchaseOrdersActions = createActionGroup({
  source: 'PurchaseOrders',
  events: {
    'Load Purchase Orders Requested': props<{ filters: Partial<PurchaseOrderListFilters> }>(),
    'Load Purchase Orders Succeeded': props<{ result: PurchaseOrderListResult }>(),
    'Load Purchase Orders Failed': props<{ errorMessage: string }>(),

    'Load Purchase Order Detail Requested': props<{ purchaseOrderId: string }>(),
    'Load Purchase Order Detail Succeeded': props<{ order: PurchaseOrderDetail }>(),
    'Load Purchase Order Detail Failed': props<{ errorMessage: string }>(),

    'Create Draft Requested': props<{ payload: CreatePurchaseOrderDraftRequest }>(),
    'Create Draft Succeeded': props<{ order: PurchaseOrderDetail }>(),
    'Create Draft Failed': props<{ errorMessage: string }>(),

    'Update Draft Requested': props<{ purchaseOrderId: string; payload: CreatePurchaseOrderDraftRequest }>(),
    'Update Draft Succeeded': props<{ order: PurchaseOrderDetail }>(),
    'Update Draft Failed': props<{ errorMessage: string }>(),

    'Clear Detail': emptyProps(),
    'Clear Error': emptyProps(),
    'Reset List Filters': emptyProps(),
  },
});
