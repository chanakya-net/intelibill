import { createReducer, on } from '@ngrx/store';

import { HttpUiActions } from './http-ui.actions';

export const httpUiFeatureKey = 'httpUi';

export interface HttpUiState {
  readonly pendingRequests: number;
}

const initialState: HttpUiState = {
  pendingRequests: 0,
};

export const httpUiReducer = createReducer(
  initialState,
  on(HttpUiActions.requestStarted, (state) => ({
    ...state,
    pendingRequests: state.pendingRequests + 1,
  })),
  on(HttpUiActions.requestEnded, (state) => ({
    ...state,
    pendingRequests: Math.max(0, state.pendingRequests - 1),
  }))
);
