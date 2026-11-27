import { createFeatureSelector, createSelector } from '@ngrx/store';

import { appShellFeatureKey } from './app-shell.reducer';
import { AppShellState } from './app.state';

export const selectAppShellState = createFeatureSelector<AppShellState>(appShellFeatureKey);

export const selectCurrentLanguage = createSelector(
  selectAppShellState,
  (state) => state.currentLanguage
);

export const selectSidebarCollapsed = createSelector(
  selectAppShellState,
  (state) => state.sidebarCollapsed
);

export const selectSidebarPinned = createSelector(
  selectAppShellState,
  (state) => state.sidebarPinned
);
