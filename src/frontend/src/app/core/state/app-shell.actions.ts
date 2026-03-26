import { createActionGroup, emptyProps, props } from '@ngrx/store';

export const AppShellActions = createActionGroup({
  source: 'App Shell',
  events: {
    'Toggle Sidebar': emptyProps(),
    'Set Sidebar Collapsed': props<{ collapsed: boolean }>(),
  },
});
