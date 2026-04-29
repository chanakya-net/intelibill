import { createFeature, createReducer, on } from '@ngrx/store';

import { DashboardDto } from '../services/dashboard.service';
import { DashboardActions } from './dashboard.actions';

export const dashboardFeatureKey = 'dashboard';

export interface DashboardState {
  readonly loading: boolean;
  readonly data: DashboardDto | null;
  readonly errorMessage: string;
}

const initialState: DashboardState = {
  loading: false,
  data: null,
  errorMessage: '',
};

export const dashboardReducer = createReducer(
  initialState,
  on(DashboardActions.loadDashboardRequested, (state) => ({
    ...state,
    loading: true,
    errorMessage: '',
  })),
  on(DashboardActions.refreshDashboard, (state) => ({
    ...state,
    loading: true,
    errorMessage: '',
  })),
  on(DashboardActions.loadDashboardSucceeded, (state, { dashboard }) => ({
    ...state,
    loading: false,
    data: dashboard,
    errorMessage: '',
  })),
  on(DashboardActions.loadDashboardFailed, (state, { errorMessage }) => ({
    ...state,
    loading: false,
    errorMessage,
  }))
);

export const dashboardFeature = createFeature({
  name: dashboardFeatureKey,
  reducer: dashboardReducer,
});
