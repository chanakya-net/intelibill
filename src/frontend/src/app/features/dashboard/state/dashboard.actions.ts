import { createActionGroup, emptyProps, props } from '@ngrx/store';

import { DashboardDto } from '../services/dashboard.service';

export const DashboardActions = createActionGroup({
  source: 'Dashboard',
  events: {
    'Load Dashboard Requested': emptyProps(),
    'Load Dashboard Succeeded': props<{ dashboard: DashboardDto }>(),
    'Load Dashboard Failed': props<{ errorMessage: string }>(),
    'Refresh Dashboard': emptyProps(),
  },
});
