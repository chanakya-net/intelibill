import type { AuthSession } from '../auth/auth.models';

/**
 * Who a record, draft or request belongs to: one user working in one shop. A signed-in user who has
 * not chosen a shop yet owns `shopId: ''`.
 */
export interface ScopeOwner {
  readonly userId: string;
  readonly shopId: string;
}

/**
 * The owner this tab is working as, plus the tab-local epoch it was installed under. The epoch moves
 * on every change of owner, so an answer can be matched to the scope that asked for it even when
 * the same shop is entered again later.
 */
export interface ShopScope extends ScopeOwner {
  readonly epoch: number;
}

/** What moved the tab: a credential check, a switch or new shop, a renewal, another tab, or sign-out. */
export type ScopeChangeReason = 'sign-in' | 'scope' | 'refresh' | 'cross-tab' | 'sign-out';

export interface ScopeChange {
  readonly previous: ShopScope | null;
  readonly next: ShopScope | null;
  readonly reason: ScopeChangeReason;
}

export function ownerOfSession(session: AuthSession | null): ScopeOwner | null {
  const userId = session?.user?.id;
  if (!session || !userId) {
    return null;
  }

  return { userId, shopId: session.activeShopId ?? '' };
}

export function sameOwner(left: ScopeOwner | null, right: ScopeOwner | null): boolean {
  if (!left || !right) {
    return left === right;
  }

  return left.userId === right.userId && left.shopId === right.shopId;
}

/** A stable storage key for drafts kept per owner. */
export function ownerKey(owner: ScopeOwner): string {
  return `${owner.userId}|${owner.shopId}`;
}
