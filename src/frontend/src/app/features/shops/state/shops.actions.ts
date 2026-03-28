import { createActionGroup, emptyProps, props } from '@ngrx/store';

import { UserShop } from '../../../core/auth/auth.models';
import { CreateShopRequest, ShopDetails } from '../services/shop.service';

export type ShopMutationType = 'create' | 'update' | 'set-default';

export const ShopsActions = createActionGroup({
  source: 'Shops',
  events: {
    'Load Shops Requested': emptyProps(),
    'Load Shops Succeeded': props<{ shops: readonly UserShop[] }>(),
    'Load Shops Failed': props<{ errorMessage: string }>(),

    'Select Shop': props<{ shopId: string }>(),

    'Load Shop Details Requested': props<{ shopId: string }>(),
    'Load Shop Details Succeeded': props<{ details: ShopDetails }>(),
    'Load Shop Details Failed': props<{ errorMessage: string }>(),

    'Create Shop Requested': props<{ payload: CreateShopRequest }>(),
    'Create Shop Succeeded': emptyProps(),
    'Create Shop Failed': props<{ errorMessage: string }>(),

    'Update Shop Requested': props<{ shopId: string; payload: CreateShopRequest }>(),
    'Update Shop Succeeded': props<{ details: ShopDetails }>(),
    'Update Shop Failed': props<{ errorMessage: string }>(),

    'Set Default Shop Requested': props<{ shopId: string }>(),
    'Set Default Shop Succeeded': emptyProps(),
    'Set Default Shop Failed': props<{ errorMessage: string }>(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
  },
});
