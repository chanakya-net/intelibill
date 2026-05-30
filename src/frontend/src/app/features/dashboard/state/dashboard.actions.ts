import { createActionGroup, emptyProps, props } from '@ngrx/store';

import { DashboardDto } from '../services/dashboard.service';

export type DashboardPreset = 'today' | 'last7' | 'last30' | 'thisMonth' | 'lastMonth' | 'custom';

export const DashboardActions = createActionGroup({
  source: 'Dashboard',
  events: {
    'Load Dashboard Requested': emptyProps(),
    'Load Dashboard Succeeded': props<{ dashboard: DashboardDto }>(),
    'Load Dashboard Failed': props<{ errorMessage: string }>(),
    'Refresh Dashboard': emptyProps(),
    'Apply Range': props<{ startDate: string; endDate: string; preset: DashboardPreset }>(),
  },
});
