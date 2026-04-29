import { TestBed } from '@angular/core/testing';
import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { DashboardService } from '../services/dashboard.service';
import { DashboardActions } from './dashboard.actions';
import { DashboardEffects } from './dashboard.effects';

describe('DashboardEffects', () => {
  let actions$: Subject<Action>;
  let effects: DashboardEffects;

  const dashboardService = {
    getDashboard: vi.fn<DashboardService['getDashboard']>(),
  };

  const makeDashboardDto = () => ({
    generatedAt: '2026-04-29T11:00:00Z',
    salesCount: 5,
  });

  beforeEach(() => {
    actions$ = new Subject<Action>();
    dashboardService.getDashboard.mockReset();

    TestBed.configureTestingModule({
      providers: [
        DashboardEffects,
        { provide: DashboardService, useValue: dashboardService },
        {
          provide: Actions,
          useFactory: (): Observable<Action> => new Actions(actions$),
        },
      ],
    });

    effects = TestBed.inject(DashboardEffects);
  });

  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
  });

  it('dispatches loadDashboardSucceeded when service call succeeds', async () => {
    const dashboard = makeDashboardDto();
    dashboardService.getDashboard.mockReturnValue(of(dashboard));

    const result$ = effects.loadDashboard$;
    const resultPromise = firstValueFrom(result$);
    actions$.next(DashboardActions.loadDashboardRequested());

    const action = await resultPromise;
    expect(action).toEqual(DashboardActions.loadDashboardSucceeded({ dashboard }));
  });

  it('dispatches loadDashboardFailed when service call fails', async () => {
    dashboardService.getDashboard.mockReturnValue(
      throwError(() => new Error('Connection refused'))
    );

    const result$ = effects.loadDashboard$;
    const resultPromise = firstValueFrom(result$);
    actions$.next(DashboardActions.loadDashboardRequested());

    const action = await resultPromise;
    expect(action).toEqual(
      DashboardActions.loadDashboardFailed({ errorMessage: 'Connection refused' })
    );
  });

  it('dispatches loadDashboardSucceeded when refresh is requested', async () => {
    const dashboard = makeDashboardDto();
    dashboardService.getDashboard.mockReturnValue(of(dashboard));

    const result$ = effects.loadDashboard$;
    const resultPromise = firstValueFrom(result$);
    actions$.next(DashboardActions.refreshDashboard());

    const action = await resultPromise;
    expect(action).toEqual(DashboardActions.loadDashboardSucceeded({ dashboard }));
  });
});
