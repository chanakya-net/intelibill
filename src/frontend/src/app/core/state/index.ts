import { ActionReducerMap, MetaReducer } from '@ngrx/store';

import { appShellFeatureKey, appShellReducer } from './app-shell.reducer';
import { RootState } from './app.state';

export const rootReducers: ActionReducerMap<RootState> = {
  [appShellFeatureKey]: appShellReducer,
};

export const metaReducers: MetaReducer<RootState>[] = [];
