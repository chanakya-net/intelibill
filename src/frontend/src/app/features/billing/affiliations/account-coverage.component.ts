import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { tap } from 'rxjs';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { AffiliationApi, AffiliationList, affiliationError } from './affiliation-api';
import { BillingAccountAuthority } from '../services/billing-account-authority.service';
import { AffiliationOperation } from './affiliation-operation';

@Component({
  selector: 'app-account-coverage',
  imports: [DatePipe, FormsModule, RouterLink, TranslocoPipe, ButtonModule, InputTextModule],
  providers: [AffiliationOperation],
  templateUrl: './account-coverage.component.html',
  styleUrl: './affiliation.scss',
  host: {
    '(window:focus)': 'revalidate()',
    '(document:visibilitychange)': 'revalidate()',
  },
})
export class AccountCoverageComponent {
  private readonly api = inject(AffiliationApi);
  private readonly authority = inject(BillingAccountAuthority);
  private readonly context = inject(ShopContextService);
  private readonly params = toSignal(inject(ActivatedRoute).paramMap);
  private readonly refresh = signal(0);
  private readonly accountId = computed(() => this.params()?.get('accountId') ?? '');
  private readonly draftKey = computed(() => [this.context.scope()?.userId ?? '', this.accountId(), 'request']);

  readonly operation = inject(AffiliationOperation);
  readonly data = signal<AffiliationList | null>(null);
  readonly loading = signal(false);
  readonly readError = signal('');
  readonly shop = signal('');
  readonly selection = signal('');
  readonly canSubmit = computed(() => !this.operation.saving() && this.operation.pending() === null &&
    this.data() !== null && this.selection() !== '' && /^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i.test(this.shop().trim()));
  readonly unlistedPendingTerm = computed(() => {
    const termId = this.operation.pending()?.termId;
    return termId && !this.data()?.terms.some((term) => term.termId === termId) ? termId : null;
  });

  constructor() {
    effect(() => {
      this.draftKey();
      this.operation.suspend();
      this.shop.set('');
      this.selection.set('');
    });
    effect((cleanup) => {
      this.refresh();
      const account = this.accountId();
      const scope = this.context.scope();
      const key = this.draftKey();
      this.data.set(null);
      this.readError.set('');
      if (!scope || !account) return;
      const request = untracked(() => this.load(scope.userId, account, key));
      cleanup(() => request.unsubscribe());
    });
    effect(() => {
      const scope = this.context.scope();
      if (scope && this.authority.state(scope.userId, this.accountId()).denied) this.clearAccount();
    });
  }

  revalidate(): void {
    if (!document.hidden && !this.loading() && !this.operation.saving()) this.reload();
  }

  reload(): void {
    this.refresh.update((current) => current + 1);
  }

  private load(user: string, account: string, key: readonly string[]) {
    const revision = this.authority.state(user, account).revision;
    const refresh = this.refresh();
    const current = () => this.isCurrent(key, user, account, revision) && refresh === this.refresh();
    this.loading.set(true);
    return this.api.list(account).subscribe({
      next: (data) => {
        if (!current()) return;
        this.authority.confirm(user, account);
        if (!this.operation.saving()) this.operation.restore(key);
        this.data.set(data);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        if (!current()) return;
        this.loading.set(false);
        if (this.authority.deny(user, account, failure)) this.clearAccount();
        else this.readError.set(affiliationError(failure));
      },
    });
  }

  private clearAccount(): void {
    this.data.set(null);
    this.operation.suspend();
    this.shop.set('');
    this.selection.set('');
    this.loading.set(false);
    this.readError.set('billing.denied');
  }

  private isCurrent(key: readonly string[], user: string, account: string, revision: number): boolean {
    return this.draftKey() === key && this.authority.state(user, account).revision === revision;
  }

  request(retryKey?: string): void {
    const scope = this.context.scope();
    const key = this.draftKey();
    const account = this.accountId();
    if (!scope || !account || !this.data() || (!retryKey && !this.canSubmit())) return;
    const authority = this.authority.state(scope.userId, account);
    if (authority.denied) return;
    const current = () => this.isCurrent(key, scope.userId, account, authority.revision);
    const termId = this.selection() === 'none' ? null : this.selection();
    const intent = retryKey ? { retryKey } : {
      newDraft: { shopId: this.shop().trim(), termId, withoutCoverage: termId === null },
    };
    this.operation.submit(scope, key, intent,
      (draft) => this.api.request(account, draft, scope).pipe(tap({
        error: (failure: unknown) => {
          if (current() && this.context.isCurrent(scope) &&
              this.authority.deny(scope.userId, account, failure)) this.clearAccount();
        },
      })), current, (succeeded) => {
        if (succeeded) {
          this.shop.set('');
          this.selection.set('');
        }
        this.reload();
      });
  }
}
