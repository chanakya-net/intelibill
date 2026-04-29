import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';

import { DashboardService } from '../services/dashboard.service';
import { DashboardActions } from './dashboard.actions';

@Injectable()
export class DashboardEffects {
  private readonly actions$ = inject(Actions);
  private readonly dashboardService = inject(DashboardService);

  loadDashboard$ = createEffect(() =>
    this.actions$.pipe(
      ofType(DashboardActions.loadDashboardRequested, DashboardActions.refreshDashboard),
      switchMap(() =>
        this.dashboardService.getDashboard().pipe(
          map((dashboard) => DashboardActions.loadDashboardSucceeded({ dashboard })),
          catchError((error: Error) =>
            of(DashboardActions.loadDashboardFailed({ errorMessage: error.message }))
          )
        )
      )
    )
  );
}
