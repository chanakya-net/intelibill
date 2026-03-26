import { createFeature, createReducer } from '@ngrx/store';

export const operationsFeatureKey = 'operations';

export interface OperationsState {
  readonly initialized: boolean;
}

const initialState: OperationsState = {
  initialized: true,
};

export const operationsFeature = createFeature({
  name: operationsFeatureKey,
  reducer: createReducer(initialState),
});
