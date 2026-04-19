import { inject, Injectable, Signal } from '@angular/core';
import { Store } from '@ngrx/store';

import { RecordSaleRequest, SaleDto, SaleListItemDto } from '../services/sale.service';
import { SalesActions } from './sales.actions';
import * as SalesSelectors from './sales.selectors';

@Injectable({ providedIn: 'root' })
export class SalesFacade {
  private readonly store = inject(Store);

  readonly allSales: Signal<readonly SaleListItemDto[]> = this.store.selectSignal(SalesSelectors.selectAllSales);
  readonly loadingSales: Signal<boolean> = this.store.selectSignal(SalesSelectors.selectLoadingSales);
  readonly submitting: Signal<boolean> = this.store.selectSignal(SalesSelectors.selectSubmitting);
  readonly errorMessage: Signal<string> = this.store.selectSignal(SalesSelectors.selectErrorMessage);
  readonly lastMutationType: Signal<'record-sale' | null> = this.store.selectSignal(SalesSelectors.selectLastMutationType);
  readonly lastMutationSucceeded: Signal<boolean> = this.store.selectSignal(SalesSelectors.selectLastMutationSucceeded);
  readonly selectedSale: Signal<SaleDto | null> = this.store.selectSignal(SalesSelectors.selectSelectedSale);
  readonly loadingSaleDetail: Signal<boolean> = this.store.selectSignal(SalesSelectors.selectLoadingSaleDetail);

  loadSales(): void {
    this.store.dispatch(SalesActions.loadSalesRequested());
  }

  loadSaleDetail(saleId: string): void {
    this.store.dispatch(SalesActions.loadSaleDetailRequested({ saleId }));
  }

  recordSale(payload: RecordSaleRequest): void {
    this.store.dispatch(SalesActions.recordSaleRequested({ payload }));
  }

  clearError(): void {
    this.store.dispatch(SalesActions.clearError());
  }

  clearMutationStatus(): void {
    this.store.dispatch(SalesActions.clearMutationStatus());
  }

  clearSaleDetail(): void {
    this.store.dispatch(SalesActions.clearSaleDetail());
  }
}
