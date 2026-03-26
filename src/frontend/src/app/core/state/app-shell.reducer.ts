import { createReducer, on } from '@ngrx/store';

import { AppShellActions } from './app-shell.actions';
import { AppShellState } from './app.state';

export const appShellFeatureKey = 'appShell';

const initialState: AppShellState = {
  sidebarCollapsed: false,
};

export const appShellReducer = createReducer(
  initialState,
  on(AppShellActions.toggleSidebar, (state) => ({
    ...state,
    sidebarCollapsed: !state.sidebarCollapsed,
  })),
  on(AppShellActions.setSidebarCollapsed, (state, { collapsed }) => ({
    ...state,
    sidebarCollapsed: collapsed,
  }))
);
