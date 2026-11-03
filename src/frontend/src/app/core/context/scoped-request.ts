import { HttpContext, HttpContextToken } from '@angular/common/http';

import type { ScopeOwner } from './shop-scope';

/**
 * The owner a request must be sent as. Set it on writes that belong to a stored owner (a frozen
 * checkout, an intake chunk): the interceptor refuses to send them under any other scope's token,
 * and reports a late answer as `StaleScopeError` instead of handing it to the new scope.
 */
export const REQUEST_SCOPE = new HttpContextToken<ScopeOwner | null>(() => null);

export function scopedRequestContext(owner: ScopeOwner, context = new HttpContext()): HttpContext {
  return context.set(REQUEST_SCOPE, { userId: owner.userId, shopId: owner.shopId });
}

/** The request was never sent: the tab no longer works as the owner it belongs to. */
export class ScopeMismatchError extends Error {
  constructor() {
    super('The request belongs to another user or shop than this tab is working in.');
    this.name = 'ScopeMismatchError';
  }
}

/**
 * The request was sent, but the tab moved to another scope before the answer arrived. The server may
 * have acted on it in the original scope, so the caller must treat the outcome as unknown there.
 */
export class StaleScopeError extends Error {
  constructor() {
    super('The answer arrived after this tab moved to another user or shop.');
    this.name = 'StaleScopeError';
  }
}

export function isScopeChangeError(error: unknown): error is ScopeMismatchError | StaleScopeError {
  return error instanceof ScopeMismatchError || error instanceof StaleScopeError;
}
