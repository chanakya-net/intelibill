import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';

import { AuthService } from '../../../core/auth/auth.service';
import { BillingOwnershipTransferDrafts } from './billing-ownership-transfer-drafts';
import { BillingOwnershipTransfers, type OwnershipDraft, type OwnershipProposal } from './billing-ownership-transfer.service';

@Component({
  selector: 'app-billing-ownership-inbox',
  imports: [RouterLink, TranslocoPipe, ButtonModule],
  templateUrl: './billing-ownership-inbox.component.html',
  styleUrls: ['../pages/billing-accounts-page.component.scss', './billing-members.scss'],
})
export class BillingOwnershipInboxComponent {
  private readonly api = inject(BillingOwnershipTransfers);
  private readonly drafts = inject(BillingOwnershipTransferDrafts);
  private readonly destroy = inject(DestroyRef);
  private readonly auth = inject(AuthService);
  private readonly actor = computed(() => this.auth.session()?.user.id ?? '');
  private loadRevision = 0;

  protected readonly transfers = signal<readonly OwnershipProposal[]>([]);
  protected readonly pending = signal<readonly OwnershipDraft[]>([]);
  protected readonly cursor = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly storageUnavailable = signal(false);

  constructor() {
    effect(() => {
      const actor = this.actor();
      untracked(() => this.reset(actor));
    });
  }

  protected load(after?: string): void {
    const actor = this.actor();
    if (!actor) return;
    const revision = ++this.loadRevision;
    const current = () => this.actor() === actor && this.loadRevision === revision;
    this.error.set('');
    this.loading.set(true);
    this.api.inbox(after).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: (page) => {
        if (!current()) return;
        this.transfers.update(items => after ? [...items, ...page.transfers] : page.transfers);
        this.cursor.set(page.nextCursor);
        this.loading.set(false);
      },
      error: () => {
        if (!current()) return;
        this.error.set('billing.transfer.unavailable');
        this.loading.set(false);
      },
    });
  }

  private reset(actor: string): void {
    this.loadRevision++;
    this.transfers.set([]);
    this.pending.set([]);
    this.cursor.set(null);
    this.loading.set(false);
    this.error.set('');
    this.storageUnavailable.set(false);
    if (!actor) return;
    try {
      this.pending.set(this.drafts.list(actor));
    } catch {
      this.storageUnavailable.set(true);
    }
    this.load();
  }
}
