import { createReducer, on } from '@ngrx/store';

import { AppShellActions } from './app-shell.actions';
import { AppShellState } from './app.state';

export const appShellFeatureKey = 'appShell';

const initialState: AppShellState = {
  sidebarCollapsed: false,
  currentLanguage: 'en-IN',
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
  })),
  on(AppShellActions.setLanguage, (state, { language }) => ({
    ...state,
    currentLanguage: language,
  }))
);
