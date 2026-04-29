import { inject, Injectable } from '@angular/core';
import { Store } from '@ngrx/store';

import { DashboardActions } from './dashboard.actions';
import {
  selectDashboardData,
  selectDashboardError,
  selectDashboardLoading,
  selectHasDashboardData,
} from './dashboard.selectors';

@Injectable({ providedIn: 'root' })
export class DashboardFacade {
  private readonly store = inject(Store);

  readonly data$ = this.store.select(selectDashboardData);
  readonly loading$ = this.store.select(selectDashboardLoading);
  readonly error$ = this.store.select(selectDashboardError);
  readonly hasDashboardData$ = this.store.select(selectHasDashboardData);

  loadDashboard(): void {
    this.store.dispatch(DashboardActions.loadDashboardRequested());
  }

  refresh(): void {
    this.store.dispatch(DashboardActions.refreshDashboard());
  }
}
