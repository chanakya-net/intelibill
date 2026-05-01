import { inject, Injectable } from '@angular/core';
import { Store } from '@ngrx/store';

import { DashboardActions, DashboardPreset } from './dashboard.actions';
import {
  selectDashboardData,
  selectDashboardEndDate,
  selectDashboardError,
  selectDashboardLoading,
  selectDashboardPreset,
  selectDashboardStartDate,
  selectHasDashboardData,
} from './dashboard.selectors';

@Injectable({ providedIn: 'root' })
export class DashboardFacade {
  private readonly store = inject(Store);

  readonly data$ = this.store.select(selectDashboardData);
  readonly loading$ = this.store.select(selectDashboardLoading);
  readonly error$ = this.store.select(selectDashboardError);
  readonly hasDashboardData$ = this.store.select(selectHasDashboardData);
  readonly startDate$ = this.store.select(selectDashboardStartDate);
  readonly endDate$ = this.store.select(selectDashboardEndDate);
  readonly selectedPreset$ = this.store.select(selectDashboardPreset);

  loadDashboard(): void {
    this.store.dispatch(DashboardActions.loadDashboardRequested());
  }

  refresh(): void {
    this.store.dispatch(DashboardActions.refreshDashboard());
  }

  applyRange(startDate: string, endDate: string, preset: DashboardPreset): void {
    this.store.dispatch(DashboardActions.applyRange({ startDate, endDate, preset }));
  }
}
