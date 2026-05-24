import { createActionGroup, emptyProps, props } from '@ngrx/store';

import type {
  PreviewSaleReturnRequest,
  RecordSaleReturnRequest,
  RecordSaleRequest,
  SaleDto,
  SaleListItemDto,
  ProfitLossReportItemDto,
  SaleReturnPreviewDto,
  VoidSaleReturnRequest,
} from '../services/sale.models';

export type SaleMutationType = 'record-sale' | 'record-return' | 'void-return';

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

    'Preview Sale Return Requested': props<{ saleId: string; payload: PreviewSaleReturnRequest }>(),
    'Preview Sale Return Succeeded': props<{ preview: SaleReturnPreviewDto }>(),
    'Preview Sale Return Failed': props<{ errorMessage: string }>(),

    'Record Sale Requested': props<{ payload: RecordSaleRequest }>(),
    'Record Sale Succeeded': props<{ sale: SaleDto }>(),
    'Record Sale Failed': props<{ errorMessage: string }>(),

    'Record Sale Return Requested': props<{ saleId: string; payload: RecordSaleReturnRequest }>(),
    'Record Sale Return Succeeded': props<{ sale: SaleDto }>(),
    'Record Sale Return Failed': props<{ errorMessage: string }>(),

    'Void Sale Return Requested': props<{ saleId: string; saleReturnId: string; payload: VoidSaleReturnRequest }>(),
    'Void Sale Return Succeeded': props<{ sale: SaleDto }>(),
    'Void Sale Return Failed': props<{ errorMessage: string }>(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
    'Clear Sale Detail': emptyProps(),
    'Clear Sale Return Preview': emptyProps(),
    'Clear Last Recorded Sale': emptyProps(),
  },
});
