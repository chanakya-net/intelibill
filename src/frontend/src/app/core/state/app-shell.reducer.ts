import { createReducer, on } from '@ngrx/store';

import { AppShellActions } from './app-shell.actions';
import { AppShellState } from './app.state';

export const appShellFeatureKey = 'appShell';

const initialState: AppShellState = {
  sidebarCollapsed: false,
  sidebarPinned: false,
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
  on(AppShellActions.toggleSidebarPinned, (state) => ({
    ...state,
    sidebarPinned: !state.sidebarPinned,
  })),
  on(AppShellActions.setSidebarPinned, (state, { pinned }) => ({
    ...state,
    sidebarPinned: pinned,
  })),
  on(AppShellActions.setLanguage, (state, { language }) => ({
    ...state,
    currentLanguage: language,
  }))
);
