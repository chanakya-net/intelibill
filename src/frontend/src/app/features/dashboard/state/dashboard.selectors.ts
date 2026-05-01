import { createSelector } from '@ngrx/store';

import { dashboardFeature } from './dashboard.reducer';

export const {
  selectDashboardState,
  selectLoading,
  selectData,
  selectErrorMessage,
  selectStartDate,
  selectEndDate,
  selectSelectedPreset,
} = dashboardFeature;

export const selectDashboardData = selectData;
export const selectDashboardLoading = selectLoading;
export const selectDashboardError = selectErrorMessage;
export const selectDashboardStartDate = selectStartDate;
export const selectDashboardEndDate = selectEndDate;
export const selectDashboardPreset = selectSelectedPreset;

export const selectHasDashboardData = createSelector(
  selectData,
  (data) => data !== null
);
