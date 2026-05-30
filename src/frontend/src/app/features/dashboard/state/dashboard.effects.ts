import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { catchError, map, of, switchMap, withLatestFrom } from 'rxjs';

import { DashboardService } from '../services/dashboard.service';
import { DashboardActions } from './dashboard.actions';
import { selectDashboardEndDate, selectDashboardStartDate } from './dashboard.selectors';

@Injectable()
export class DashboardEffects {
  private readonly actions$ = inject(Actions);
  private readonly dashboardService = inject(DashboardService);
  private readonly store = inject(Store);

  loadDashboard$ = createEffect(() =>
    this.actions$.pipe(
      ofType(DashboardActions.loadDashboardRequested, DashboardActions.refreshDashboard),
      withLatestFrom(
        this.store.select(selectDashboardStartDate),
        this.store.select(selectDashboardEndDate)
      ),
      switchMap(([, startDate, endDate]) =>
        this.dashboardService.getDashboard(startDate, endDate).pipe(
          map((dashboard) => DashboardActions.loadDashboardSucceeded({ dashboard })),
          catchError((error: Error) =>
            of(DashboardActions.loadDashboardFailed({ errorMessage: error.message }))
          )
        )
      )
    )
  );

  applyRange$ = createEffect(() =>
    this.actions$.pipe(
      ofType(DashboardActions.applyRange),
      switchMap(({ startDate, endDate }) =>
        this.dashboardService.getDashboard(startDate, endDate).pipe(
          map((dashboard) => DashboardActions.loadDashboardSucceeded({ dashboard })),
          catchError((error: Error) =>
            of(DashboardActions.loadDashboardFailed({ errorMessage: error.message }))
          )
        )
      )
    )
  );
}
