import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { problemCode } from '@intelibill/api';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';

import { AuthService } from '../../../core/auth/auth.service';
import { BillingAccountAuthority } from '../services/billing-account-authority.service';
import { BillingAccountDraftStore } from '../services/billing-account-draft.store';
import { BillingAccount, BillingAccountDraft } from '../services/billing-account.models';
import { BillingAccountService } from '../services/billing-account.service';

const NAME_LIMIT = 120;

/** Answers that settle an attempt: retrying the same key could never succeed differently. */
const DEFINITIVE_FAILURES: Readonly<Record<string, string>> = {
  'Billing.AccountNameInvalid': 'billing.nameInvalid',
  'Billing.OperationKeyInvalid': 'billing.unavailable',
  'Billing.OperationPayloadMismatch': 'billing.payloadMismatch',
  'Billing.AccountAccessDenied': 'billing.denied',
};

/**
 * The signed-in person's billing accounts and explicit creation of a new one. Nothing is created on
 * load; an attempt is frozen (key, name, owner) before it is sent and retried unchanged until the
 * server gives a definitive answer.
 */
@Component({
  selector: 'app-billing-accounts-page',
  standalone: true,
  imports: [DatePipe, RouterLink, TranslocoPipe, ButtonModule, InputTextModule],
  templateUrl: './billing-accounts-page.component.html',
  styleUrl: './billing-accounts-page.component.scss',
  host: {
    '(window:focus)': 'revalidate()',
    '(document:visibilitychange)': 'revalidate()',
  },
})
export class BillingAccountsPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly billing = inject(BillingAccountService);
  private readonly drafts = inject(BillingAccountDraftStore);
  private readonly authority = inject(BillingAccountAuthority);
  private readonly listedAccounts = signal<readonly BillingAccount[]>([]);

  readonly nameLimit = NAME_LIMIT;
  readonly accounts = computed(() => this.listedAccounts().filter(
    account => !this.authority.state(this.ownerUserId() ?? '', account.accountId).denied,
  ));
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly submitting = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly pending = signal<BillingAccountDraft | null>(null);
  readonly name = signal('');
  readonly canSubmit = computed(
    () => !this.submitting() && (this.pending() !== null || this.normalizedName() !== null),
  );
  private readonly ownerUserId = computed(() => this.auth.session()?.user.id ?? null);
  /** Only the newest list request may replace the shown accounts. */
  private listGeneration = 0;
  private destroyed = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => (this.destroyed = true));
  }

  ngOnInit(): void {
    this.adoptOldestDraft();
    this.reload();
  }

  onNameInput(event: Event): void {
    if (!this.pending()) this.name.set((event.target as HTMLInputElement).value);
  }

  revalidate(): void {
    if (!document.hidden && !this.loading()) this.reload();
  }

  reload(): void {
    const generation = ++this.listGeneration;
    this.loading.set(true);
    this.loadFailed.set(false);
    const user = this.ownerUserId();
    this.billing.list().subscribe({
      next: (accounts) => {
        if (this.destroyed || user !== this.ownerUserId() || generation !== this.listGeneration) return;
        this.listedAccounts.set(accounts);
        this.loading.set(false);
      },
      error: () => {
        if (this.destroyed || user !== this.ownerUserId() || generation !== this.listGeneration) return;
        this.loadFailed.set(true);
        this.loading.set(false);
      },
    });
  }

  submit(): void {
    const owner = this.ownerUserId();
    if (!owner || this.submitting()) return;
    const draft = this.pending() ?? this.newDraft(owner);
    if (!draft) {
      this.error.set('billing.nameInvalid');
      return;
    }

    this.drafts.save(draft);
    this.adopt(draft);
    this.submitting.set(true);
    this.error.set('');
    this.notice.set('');
    this.billing.create({ operationKey: draft.operationKey, name: draft.name }).subscribe({
      next: ({ account }) => this.settle(draft, account),
      error: (failure: unknown) => this.fail(draft, failure),
    });
  }

  private newDraft(owner: string): BillingAccountDraft | null {
    const name = this.normalizedName();
    return name ? { ownerUserId: owner, operationKey: crypto.randomUUID(), name } : null;
  }

  private normalizedName(): string | null {
    const name = this.name().trim();
    return name.length > 0 && name.length <= NAME_LIMIT ? name : null;
  }

  private adopt(draft: BillingAccountDraft): void {
    this.pending.set(draft);
    this.name.set(draft.name);
  }

  /** Surfaces another unfinished attempt (another tab's, or an earlier lost one) for retry. */
  private adoptOldestDraft(): void {
    const owner = this.ownerUserId();
    const draft = owner ? this.drafts.load(owner) : null;
    if (draft) this.adopt(draft);
  }

  private isCurrent(draft: BillingAccountDraft): boolean {
    return !this.destroyed && this.pending()?.operationKey === draft.operationKey;
  }

  /** Ends this attempt only; a left page or another tab's attempt is never touched. */
  private finish(draft: BillingAccountDraft): void {
    this.drafts.clear(draft);
    if (!this.isCurrent(draft)) return;
    this.submitting.set(false);
    this.pending.set(null);
    this.name.set('');
    this.adoptOldestDraft();
  }

  private settle(draft: BillingAccountDraft, account: BillingAccount): void {
    const current = this.isCurrent(draft);
    this.finish(draft);
    if (this.destroyed) return;
    if (current) this.notice.set('billing.created');
    this.listedAccounts.update((accounts) =>
      accounts.some((item) => item.accountId === account.accountId) ? accounts : [...accounts, account],
    );
    // Supersedes any list read started before the creation committed.
    this.reload();
  }

  private fail(draft: BillingAccountDraft, failure: unknown): void {
    const code = failure instanceof HttpErrorResponse ? problemCode(failure.error) : null;
    const message = code ? DEFINITIVE_FAILURES[code] : undefined;
    if (!message) {
      // Unknown outcome: keep the frozen attempt so a retry cannot create a second account.
      if (this.isCurrent(draft)) {
        this.submitting.set(false);
        this.error.set('billing.pendingRetry');
      }
      return;
    }
    const current = this.isCurrent(draft);
    this.finish(draft);
    if (current) this.error.set(message);
  }
}
