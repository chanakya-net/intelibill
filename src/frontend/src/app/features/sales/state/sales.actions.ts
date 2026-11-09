import type { LocalizedError } from '../../../core/i18n/api-error-message.service';
import { createActionGroup, emptyProps, props } from '@ngrx/store';

import type {
  PreviewSaleReturnRequest,
  ProfitLossReportQueryParams,
  ProfitLossReportResultDto,
  RecordSaleReturnRequest,
  RecordSaleRequest,
  SaleDto,
  SalesHistoryQueryParams,
  SalesHistorySummaryDto,
  SaleListItemDto,
  SaleReturnPreviewDto,
  VoidSaleReturnRequest,
} from '../services/sale.models';

export type SaleMutationType = 'record-sale' | 'record-return' | 'void-return';

export const SalesActions = createActionGroup({
  source: 'Sales',
  events: {
    'Load Sales Requested': props<{ queryParams?: SalesHistoryQueryParams }>(),
    'Load Sales Succeeded': props<{
      sales: readonly SaleListItemDto[];
      totalCount: number;
      pageNumber: number;
      pageSize: number;
      summary: SalesHistorySummaryDto;
    }>(),
    'Load Sales Failed': props<{ errorMessage: string; localizedError?: LocalizedError }>(),

    'Load Profit Loss Report Requested': props<{ queryParams?: ProfitLossReportQueryParams }>(),
    'Load Profit Loss Report Succeeded': props<{ result: ProfitLossReportResultDto }>(),
    'Load Profit Loss Report Failed': props<{
      errorMessage: string;
      localizedError?: LocalizedError;
    }>(),

    'Load Sale Detail Requested': props<{ saleId: string }>(),
    'Load Sale Detail Succeeded': props<{ sale: SaleDto }>(),
    'Load Sale Detail Failed': props<{ errorMessage: string; localizedError?: LocalizedError }>(),

    'Preview Sale Return Requested': props<{ saleId: string; payload: PreviewSaleReturnRequest }>(),
    'Preview Sale Return Succeeded': props<{ preview: SaleReturnPreviewDto }>(),
    'Preview Sale Return Failed': props<{
      errorMessage: string;
      localizedError?: LocalizedError;
    }>(),

    'Record Sale Requested': props<{ payload: RecordSaleRequest; intentId?: string }>(),
    'Record Sale Succeeded': props<{ sale: SaleDto; intentId?: string }>(),
    'Record Sale Failed': props<{
      errorMessage: string;
      localizedError?: LocalizedError;
      intentId?: string;
    }>(),
    /** The send ended without a definitive answer; the stored intent must be reconciled, not rebuilt. */
    'Record Sale Uncertain': props<{
      intentId: string;
      errorMessage: string;
      localizedError?: LocalizedError;
    }>(),
    /**
     * The send settled after this tab left the user or shop the intent belongs to. Its outcome is
     * stored on the intent; nothing about it is reported to the scope the tab works in now.
     */
    'Record Sale Detached': props<{ intentId: string }>(),

    'Record Sale Return Requested': props<{ saleId: string; payload: RecordSaleReturnRequest }>(),
    'Record Sale Return Succeeded': props<{ sale: SaleDto }>(),
    'Record Sale Return Failed': props<{ errorMessage: string; localizedError?: LocalizedError }>(),

    'Void Sale Return Requested': props<{
      saleId: string;
      saleReturnId: string;
      payload: VoidSaleReturnRequest;
    }>(),
    'Void Sale Return Succeeded': props<{ sale: SaleDto }>(),
    'Void Sale Return Failed': props<{ errorMessage: string; localizedError?: LocalizedError }>(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
    'Clear Sale Detail': emptyProps(),
    'Clear Sale Return Preview': emptyProps(),
    'Clear Last Recorded Sale': emptyProps(),
  },
});
