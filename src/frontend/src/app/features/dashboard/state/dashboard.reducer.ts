import { createFeature, createReducer, on } from '@ngrx/store';

import { DashboardDto } from '../services/dashboard.service';
import { DashboardActions, DashboardPreset } from './dashboard.actions';

export const dashboardFeatureKey = 'dashboard';

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

function last30StartIso(): string {
  const d = new Date();
  d.setDate(d.getDate() - 29);
  return d.toISOString().slice(0, 10);
}

export interface DashboardState {
  readonly loading: boolean;
  readonly data: DashboardDto | null;
  readonly errorMessage: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly selectedPreset: DashboardPreset;
}

const initialState: DashboardState = {
  loading: false,
  data: null,
  errorMessage: '',
  startDate: last30StartIso(),
  endDate: todayIso(),
  selectedPreset: 'last30',
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
  on(DashboardActions.applyRange, (state, { startDate, endDate, preset }) => ({
    ...state,
    loading: true,
    errorMessage: '',
    startDate,
    endDate,
    selectedPreset: preset,
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
