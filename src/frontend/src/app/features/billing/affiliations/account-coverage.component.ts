import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { AffiliationApi, AffiliationList, affiliationError } from './affiliation-api';
import { AffiliationOperation } from './affiliation-operation';

@Component({
  selector: 'app-account-coverage',
  imports: [DatePipe, FormsModule, RouterLink, TranslocoPipe, ButtonModule, InputTextModule],
  providers: [AffiliationOperation],
  templateUrl: './account-coverage.component.html',
  styleUrl: './affiliation.scss',
})
export class AccountCoverageComponent {
  private readonly api = inject(AffiliationApi);
  private readonly context = inject(ShopContextService);
  private readonly params = toSignal(inject(ActivatedRoute).paramMap);
  private readonly refresh = signal(0);
  private readonly accountId = computed(() => this.params()?.get('accountId') ?? '');
  private readonly draftKey = computed(() => [this.context.scope()?.userId ?? '', this.accountId(), 'request']);

  readonly operation = inject(AffiliationOperation);
  readonly data = signal<AffiliationList | null>(null);
  readonly readError = signal('');
  readonly shop = signal('');
  readonly selection = signal('');
  readonly canSubmit = computed(() => !this.operation.saving() && (this.operation.pending() !== null ||
    (this.data() !== null && this.selection() !== '' && /^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i.test(this.shop().trim()))));

  constructor() {
    effect(() => {
      this.operation.restore(this.draftKey());
      this.shop.set('');
      this.selection.set('');
    });
    effect((cleanup) => {
      this.refresh();
      const account = this.accountId();
      const scope = this.context.scope();
      this.data.set(null);
      this.readError.set('');
      if (!scope || !account) return;
      const request = this.api.list(account).subscribe({
        next: (data) => this.data.set(data),
        error: (error: unknown) => this.readError.set(affiliationError(error)),
      });
      cleanup(() => request.unsubscribe());
    });
  }

  reload(): void {
    this.refresh.update((current) => current + 1);
  }

  request(): void {
    const scope = this.context.scope();
    const key = this.draftKey();
    const account = this.accountId();
    if (!scope || !account || !this.canSubmit()) return;
    const termId = this.selection() === 'none' ? null : this.selection();
    this.operation.submit(scope, key, { shopId: this.shop().trim(), termId, withoutCoverage: termId === null },
      (draft) => this.api.request(account, draft, scope), () => this.draftKey() === key, (succeeded) => {
        if (succeeded) {
          this.shop.set('');
          this.selection.set('');
        }
        this.reload();
      });
  }
}
