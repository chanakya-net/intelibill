import { ActionReducerMap, MetaReducer } from '@ngrx/store';

import { appShellFeatureKey, appShellReducer } from './app-shell.reducer';
import { registerFeatureKey, registerReducer } from '../../features/auth/state/register.reducer';
import { httpUiFeatureKey, httpUiReducer } from './http-ui.reducer';
import { RootState } from './app.state';

export const rootReducers: ActionReducerMap<RootState> = {
  [appShellFeatureKey]: appShellReducer,
  [httpUiFeatureKey]: httpUiReducer,
  [registerFeatureKey]: registerReducer,
};

export const metaReducers: MetaReducer<RootState>[] = [];
