import { Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { MonoTypeOperatorFunction, Observable } from 'rxjs';
import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import { AffiliationDrafts } from './affiliation-drafts';
import { affiliationError, definitiveAffiliationFailure } from './affiliation-api';

type Draft = NonNullable<ReturnType<AffiliationDrafts['read']>>;
type NewDraft = Parameters<AffiliationDrafts['freeze']>[1];
type AffiliationIntent = { newDraft: NewDraft } | { retryKey: string };

@Injectable()
export class AffiliationOperation {
  private readonly drafts = inject(AffiliationDrafts);
  private readonly context = inject(ShopContextService);
  private readonly untilDestroyed: MonoTypeOperatorFunction<unknown> = takeUntilDestroyed();

  readonly pending = signal<Draft | null>(null);
  readonly saving = signal(false);
  readonly error = signal('');

  restore(key: readonly string[]): void {
    this.saving.set(false);
    this.error.set('');
    try {
      this.pending.set(this.drafts.read(key));
    } catch {
      this.pending.set(null);
      this.error.set('billing.affiliation.storageUnavailable');
    }
  }

  submit<T>(scope: ShopScope, key: readonly string[], intent: AffiliationIntent,
    send: (draft: Draft) => Observable<T>, samePage: () => boolean, completed: (succeeded: boolean) => void): void {
    if (this.saving()) return;
    const draft = this.prepare(key, intent);
    if (!draft) return;
    this.saving.set(true);
    this.error.set('');
    send(draft).pipe(this.untilDestroyed).subscribe({
      next: () => this.finish(scope, key, draft, samePage, completed),
      error: (error: unknown) => this.finish(scope, key, draft, samePage, completed, error),
    });
  }

  private prepare(key: readonly string[], intent: AffiliationIntent): Draft | null {
    try {
      if ('retryKey' in intent) {
        const draft = this.drafts.read(key);
        this.pending.set(draft);
        if (draft?.operationKey === intent.retryKey) return draft;
      } else {
        const { draft, created } = this.drafts.freeze(key, intent.newDraft);
        this.pending.set(draft);
        if (created) return draft;
      }
      this.error.set('billing.affiliation.reviewPending');
    } catch {
      this.error.set('billing.affiliation.storageUnavailable');
    }
    return null;
  }

  private finish(scope: ShopScope, key: readonly string[], draft: Draft, samePage: () => boolean,
    completed: (succeeded: boolean) => void, error?: unknown): void {
    if (!this.context.isCurrent(scope) || this.context.epoch() !== scope.epoch || !samePage()) return;
    this.saving.set(false);
    if (error) this.error.set(affiliationError(error));
    if (!error || definitiveAffiliationFailure(error)) {
      try {
        this.drafts.settle(key, draft.operationKey);
        this.pending.set(this.drafts.read(key));
      } catch {
        this.error.set('billing.affiliation.storageUnavailable');
      }
      completed(!error);
    }
  }
}
