import { createSelector } from '@ngrx/store';

import { dashboardFeature } from './dashboard.reducer';

export const {
  selectDashboardState,
  selectLoading,
  selectData,
  selectErrorMessage,
} = dashboardFeature;

export const selectDashboardData = selectData;
export const selectDashboardLoading = selectLoading;
export const selectDashboardError = selectErrorMessage;

export const selectHasDashboardData = createSelector(
  selectData,
  (data) => data !== null
);
