import { createActionGroup, emptyProps, props } from '@ngrx/store';

import { RecordSaleRequest, SaleDto, SaleListItemDto, ProfitLossReportItemDto } from '../services/sale.service';

export type SaleMutationType = 'record-sale';

export const SalesActions = createActionGroup({
  source: 'Sales',
  events: {
    'Load Sales Requested': emptyProps(),
    'Load Sales Succeeded': props<{ sales: readonly SaleListItemDto[] }>(),
    'Load Sales Failed': props<{ errorMessage: string }>(),

    'Load Profit Loss Report Requested': emptyProps(),
    'Load Profit Loss Report Succeeded': props<{ report: readonly ProfitLossReportItemDto[] }>(),
    'Load Profit Loss Report Failed': props<{ errorMessage: string }>(),

    'Load Sale Detail Requested': props<{ saleId: string }>(),
    'Load Sale Detail Succeeded': props<{ sale: SaleDto }>(),
    'Load Sale Detail Failed': props<{ errorMessage: string }>(),

    'Record Sale Requested': props<{ payload: RecordSaleRequest }>(),
    'Record Sale Succeeded': props<{ sale: SaleDto }>(),
    'Record Sale Failed': props<{ errorMessage: string }>(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
    'Clear Sale Detail': emptyProps(),
  },
});
