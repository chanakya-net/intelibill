import { Action, ActionReducer, createAction, props } from '@ngrx/store';

import { appShellFeatureKey } from '../state/app-shell.reducer';
import { httpUiFeatureKey } from '../state/http-ui.reducer';
import { registerFeatureKey } from '../../features/auth/state/register.reducer';
import type { ScopeChangeReason } from './shop-scope';

export const ShopContextActions = {
  /** Every feature slice belongs to the scope that loaded it; this drops them all. */
  scopeChanged: createAction(
    '[Shop Context] Scope Changed',
    props<{ readonly epoch: number; readonly reason: ScopeChangeReason }>(),
  ),
};

/** Root slices that hold no shop or user data and survive a scope change. */
const SCOPE_INDEPENDENT_SLICES: readonly string[] = [appShellFeatureKey, httpUiFeatureKey, registerFeatureKey];

/**
 * Resets every slice except the scope-independent ones when the tab's scope changes. Feature
 * reducers receive `undefined` and start again from their initial state.
 */
export function scopeResetMetaReducer<S extends object, A extends Action = Action>(
  reducer: ActionReducer<S, A>,
): ActionReducer<S, A> {
  return (state, action) => {
    if (action.type !== ShopContextActions.scopeChanged.type || !state) {
      return reducer(state, action);
    }

    const kept: Record<string, unknown> = {};
    for (const key of SCOPE_INDEPENDENT_SLICES) {
      if (key in state) {
        kept[key] = (state as Record<string, unknown>)[key];
      }
    }

    return reducer(kept as S, action);
  };
}
