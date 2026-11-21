import { Injectable, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';
import { Observable, Subject } from 'rxjs';

import { AppReloadService } from '../services/app-reload.service';
import { ShopContextActions } from './shop-context.actions';
import { ScopeChange, ScopeChangeReason, ScopeOwner, ShopScope, sameOwner, shopRestrictionConfirmationKey, shopRestrictionKey } from './shop-scope';

export type LeaveReadiness =
  | { readonly ok: true }
  | { readonly ok: false; readonly reason: 'mutation-in-flight' | 'draft-save-failed' };

/** How long a restart waits for detach hooks to finish saving under the owner being left. */
export const RESTART_SAVE_WAIT_MS = 1_500;

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
  private readonly restrictedSignal = signal(false);
  private readonly changesSubject = new Subject<ScopeChange>();
  private readonly detachHooks = new Set<(change: ScopeChange) => void | Promise<unknown>>();
  private readonly leaveChecks = new Set<() => Promise<boolean>>();
  private readonly mutations = new Set<symbol>();
  private currentEpoch = 0;
  private restrictionGeneration = 0;
  private leaving: Promise<LeaveReadiness> | null = null;

  /** The owner this tab works as, or null when signed out. */
  readonly scope = this.scopeSignal.asReadonly();
  /** True from the moment a scope is left until the app restarts or signs out. */
  readonly transitioning = this.transitioningSignal.asReadonly();
  readonly dataRestricted = this.restrictedSignal.asReadonly();
  /** Emits after an owner or data-access change. */
  readonly changes$: Observable<ScopeChange> = this.changesSubject.asObservable();

  epoch(): number {
    return this.currentEpoch;
  }

  isCurrent(owner: ScopeOwner): boolean {
    return sameOwner(this.scopeSignal(), owner);
  }

  /** Adopts the session restored at startup; nothing was shown under another scope yet. */
  initialize(owner: ScopeOwner | null): void {
    this.restrictedSignal.set(readRestriction(owner) !== null);
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
    const saves = this.detach(change);
    this.restrictedSignal.set(readRestriction(next) !== null);
    this.scopeSignal.set(change.next);
    install();
    this.changesSubject.next(change);

    if (restart) {
      this.restartAfter(saves);
    }

    return change;
  }

  /** Captures the local and shared restriction evidence at the start of an authority read. */
  observeDataAuthority(observed: ShopScope): (restricted: boolean) => void {
    const generation = this.restrictionGeneration;
    const marker = readRestriction(observed);
    return (restricted) => {
      if (restricted) {
        this.setDataRestricted(observed, true);
      } else if (generation === this.restrictionGeneration && marker === readRestriction(observed)) {
        this.confirmDataRelease(observed, marker);
      }
    };
  }

  /** Sibling tabs retire their data immediately. Release still needs a local authority read. */
  applyRestrictionNotice(observed: ShopScope): void {
    if (!this.canChangeDataAccess(observed)) return;
    this.restrictionGeneration++;
    this.changeDataRestriction(observed, true);
  }

  setDataRestricted(observed: ShopScope, restricted: boolean): boolean {
    if (!this.canChangeDataAccess(observed)) return false;
    if (!restricted) return this.confirmDataRelease(observed, readRestriction(observed));
    this.restrictionGeneration++;
    saveRestriction(observed);
    return this.changeDataRestriction(observed, true);
  }

  private confirmDataRelease(observed: ShopScope, marker: string | null): boolean {
    if (!this.canChangeDataAccess(observed) || !this.restrictedSignal()) return false;
    if (marker) confirmRestriction(observed, marker);
    if (readRestriction(observed) !== null) return false;
    return this.changeDataRestriction(observed, false);
  }

  private canChangeDataAccess(observed: ShopScope): boolean {
    return this.scopeSignal() === observed && !!observed.shopId && !this.transitioning();
  }

  private changeDataRestriction(observed: ShopScope, restricted: boolean): boolean {
    if (this.restrictedSignal() === restricted) return false;

    const change: ScopeChange = {
      previous: observed,
      next: { ...observed, epoch: ++this.currentEpoch },
      reason: 'data-access',
    };
    this.restrictedSignal.set(restricted);
    void Promise.allSettled(this.detach(change));
    this.scopeSignal.set(change.next);
    this.changesSubject.next(change);
    return true;
  }

  private detach(change: ScopeChange): Promise<unknown>[] {
    const saves: Promise<unknown>[] = [];
    for (const hook of [...this.detachHooks]) {
      try {
        const saving = hook(change);
        if (saving) {
          saves.push(saving);
        }
      } catch {
        // A failing hook must not leave the old scope installed beside the new credentials.
      }
    }
    this.store?.dispatch(ShopContextActions.scopeChanged({ epoch: this.currentEpoch, reason: change.reason }));
    return saves;
  }

  /**
   * Runs synchronously on every change of owner, before the new session is readable. A hook may
   * return the save it started; a restart waits for those saves, up to `RESTART_SAVE_WAIT_MS`.
   */
  onDetach(hook: (change: ScopeChange) => void | Promise<unknown>): () => void {
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

  /** Restarts once the old owner's drafts are written, so unloading the page cannot cut them off. */
  private restartAfter(saves: readonly Promise<unknown>[]): void {
    if (saves.length === 0) {
      this.appReload.restartAtAppRoot();
      return;
    }

    const saved = Promise.allSettled(saves);
    const waitLimit = new Promise<void>((resolve) => setTimeout(resolve, RESTART_SAVE_WAIT_MS));
    void Promise.race([saved, waitLimit]).then(() => this.appReload.restartAtAppRoot());
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

function readRestriction(owner: ScopeOwner | null): string | null {
  try {
    if (!owner?.shopId) return null;
    const marker = localStorage.getItem(shopRestrictionKey(owner));
    return marker === localStorage.getItem(shopRestrictionConfirmationKey(owner)) ? null : marker;
  } catch {
    return null;
  }
}

function saveRestriction(owner: ScopeOwner): void {
  try {
    const marker = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`;
    localStorage.setItem(shopRestrictionKey(owner), marker);
  } catch {
    // Restricted storage leaves the in-memory decision in effect for this tab.
  }
}

function confirmRestriction(owner: ScopeOwner, marker: string): void {
  try {
    // A release only confirms the marker this read observed. A sibling's newer denial survives.
    localStorage.setItem(shopRestrictionConfirmationKey(owner), marker);
  } catch {
    // Leave persisted evidence in force until it can be confirmed.
  }
}
