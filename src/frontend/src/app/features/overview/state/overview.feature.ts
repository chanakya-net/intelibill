import { createFeature, createReducer } from '@ngrx/store';

export const overviewFeatureKey = 'overview';

export interface OverviewState {
  readonly initialized: boolean;
}

const initialState: OverviewState = {
  initialized: true,
};

export const overviewFeature = createFeature({
  name: overviewFeatureKey,
  reducer: createReducer(initialState),
});
