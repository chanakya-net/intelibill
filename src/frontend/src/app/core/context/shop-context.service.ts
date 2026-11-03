import { Injectable, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';
import { Observable, Subject } from 'rxjs';

import { AppReloadService } from '../services/app-reload.service';
import { ShopContextActions } from './shop-context.actions';
import { ScopeChange, ScopeChangeReason, ScopeOwner, ShopScope, sameOwner } from './shop-scope';

export type LeaveReadiness =
  | { readonly ok: true }
  | { readonly ok: false; readonly reason: 'mutation-in-flight' | 'draft-save-failed' };

export interface ScopedMutation {
  /** Call once the outcome is known or stored as uncertain under its owner. */
  release(): void;
}

/**
 * The one place a tab changes the user and shop it works as.
 *
 * Every session write passes through `transition`. When the owner changes, the tab's epoch moves,
 * detach hooks run while the old session is still installed (drafts are saved under the owner that
 * captured them and dropped from memory), every NgRx feature slice is reset, routed content is
 * hidden, and only then is the new session installed. A move between two signed-in scopes then
 * restarts the app at its root: pages load their rows when they are created, and the route left
 * behind may address a record of the shop being left. Answers that arrive later are matched to the
 * epoch that asked for them (see `authInterceptor`), so cancelling them is an optimisation only.
 */
@Injectable({ providedIn: 'root' })
export class ShopContextService {
  private readonly appReload = inject(AppReloadService);
  private readonly store = inject(Store, { optional: true });

  private readonly scopeSignal = signal<ShopScope | null>(null);
  private readonly transitioningSignal = signal(false);
  private readonly changesSubject = new Subject<ScopeChange>();
  private readonly detachHooks = new Set<(change: ScopeChange) => void>();
  private readonly leaveChecks = new Set<() => Promise<boolean>>();
  private readonly mutations = new Set<symbol>();
  private currentEpoch = 0;
  private leaving: Promise<LeaveReadiness> | null = null;

  /** The owner this tab works as, or null when signed out. */
  readonly scope = this.scopeSignal.asReadonly();
  /** True from the moment a scope is left until the app restarts or signs out. */
  readonly transitioning = this.transitioningSignal.asReadonly();
  /** Emits after each completed change of owner. */
  readonly changes$: Observable<ScopeChange> = this.changesSubject.asObservable();

  epoch(): number {
    return this.currentEpoch;
  }

  isCurrent(owner: ScopeOwner): boolean {
    return sameOwner(this.scopeSignal(), owner);
  }

  /** Adopts the session restored at startup; nothing was shown under another scope yet. */
  initialize(owner: ScopeOwner | null): void {
    this.scopeSignal.set(owner ? { userId: owner.userId, shopId: owner.shopId, epoch: this.currentEpoch } : null);
  }

  /**
   * Installs a session. `install` writes it; it runs after the old scope is detached when the owner
   * changes, and on its own when only credentials or metadata change. Returns the change, or null
   * when the owner stayed the same.
   */
  transition(next: ScopeOwner | null, reason: ScopeChangeReason, install: () => void): ScopeChange | null {
    const previous = this.scopeSignal();
    if (sameOwner(previous, next)) {
      install();
      return null;
    }

    this.currentEpoch += 1;
    const change: ScopeChange = {
      previous,
      next: next ? { userId: next.userId, shopId: next.shopId, epoch: this.currentEpoch } : null,
      reason,
    };
    const restart = change.previous !== null && change.next !== null;

    this.transitioningSignal.set(change.previous !== null);
    for (const hook of [...this.detachHooks]) {
      try {
        hook(change);
      } catch {
        // A failing hook must not leave the old scope installed beside the new credentials.
      }
    }
    this.store?.dispatch(ShopContextActions.scopeChanged({ epoch: this.currentEpoch, reason }));
    this.scopeSignal.set(change.next);
    install();
    this.changesSubject.next(change);

    if (restart) {
      this.appReload.restartAtAppRoot();
    }

    return change;
  }

  /** Runs synchronously on every change of owner, before the new session is readable. */
  onDetach(hook: (change: ScopeChange) => void): () => void {
    this.detachHooks.add(hook);
    return () => this.detachHooks.delete(hook);
  }

  /** Saves work before a deliberate move; resolving false keeps the tab where it is. */
  onLeave(check: () => Promise<boolean>): () => void {
    this.leaveChecks.add(check);
    return () => this.leaveChecks.delete(check);
  }

  /**
   * Whether this tab may leave its scope now: no owner-bound write is waiting for its answer, and
   * every draft was saved under its owner. Concurrent callers share one check.
   */
  prepareToLeave(): Promise<LeaveReadiness> {
    this.leaving ??= this.checkLeave().finally(() => {
      this.leaving = null;
    });
    return this.leaving;
  }

  /** Marks an owner-bound write as in flight; a deliberate switch waits for it. */
  beginMutation(): ScopedMutation {
    const token = Symbol('scoped-mutation');
    this.mutations.add(token);
    return { release: () => this.mutations.delete(token) };
  }

  hasMutationsInFlight(): boolean {
    return this.mutations.size > 0;
  }

  private async checkLeave(): Promise<LeaveReadiness> {
    if (this.hasMutationsInFlight()) {
      return { ok: false, reason: 'mutation-in-flight' };
    }

    const saved = await Promise.all([...this.leaveChecks].map((check) => check().catch(() => false)));
    if (saved.some((ok) => !ok)) {
      return { ok: false, reason: 'draft-save-failed' };
    }

    return this.hasMutationsInFlight() ? { ok: false, reason: 'mutation-in-flight' } : { ok: true };
  }
}
