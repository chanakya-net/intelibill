import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { problemCode } from '@intelibill/api';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';

import { AuthService } from '../../../core/auth/auth.service';
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
  imports: [DatePipe, TranslocoPipe, ButtonModule, InputTextModule],
  templateUrl: './billing-accounts-page.component.html',
  styleUrl: './billing-accounts-page.component.scss',
})
export class BillingAccountsPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly billing = inject(BillingAccountService);
  private readonly drafts = inject(BillingAccountDraftStore);

  readonly nameLimit = NAME_LIMIT;
  readonly accounts = signal<readonly BillingAccount[]>([]);
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

  ngOnInit(): void {
    const owner = this.ownerUserId();
    const draft = owner ? this.drafts.load(owner) : null;
    if (draft) this.adopt(draft);
    this.reload();
  }

  onNameInput(event: Event): void {
    if (!this.pending()) this.name.set((event.target as HTMLInputElement).value);
  }

  reload(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.billing.list().subscribe({
      next: (accounts) => {
        this.accounts.set(accounts);
        this.loading.set(false);
      },
      error: () => {
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
      next: ({ account }) => this.settle(owner, account),
      error: (failure: unknown) => this.fail(owner, failure),
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

  private settle(owner: string, account: BillingAccount): void {
    this.drafts.clear(owner);
    this.pending.set(null);
    this.name.set('');
    this.submitting.set(false);
    this.notice.set('billing.created');
    this.accounts.update((accounts) =>
      accounts.some((item) => item.accountId === account.accountId) ? accounts : [...accounts, account],
    );
  }

  private fail(owner: string, failure: unknown): void {
    this.submitting.set(false);
    const code = failure instanceof HttpErrorResponse ? problemCode(failure.error) : null;
    const message = code ? DEFINITIVE_FAILURES[code] : undefined;
    if (!message) {
      // Unknown outcome: keep the frozen attempt so a retry cannot create a second account.
      this.error.set('billing.pendingRetry');
      return;
    }
    this.drafts.clear(owner);
    this.pending.set(null);
    this.error.set(message);
  }
}
