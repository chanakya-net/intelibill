import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { AffiliationApi, type AffiliationConsent, type ConsentDecision, affiliationError } from './affiliation-api';
import { AffiliationOperation } from './affiliation-operation';

@Component({
  selector: 'app-owner-consent',
  imports: [DatePipe, RouterLink, TranslocoPipe, ButtonModule],
  providers: [AffiliationOperation],
  templateUrl: './owner-consent.component.html',
  styleUrl: './affiliation.scss',
})
export class OwnerConsentComponent {
  private readonly api = inject(AffiliationApi);
  private readonly context = inject(ShopContextService);
  private readonly params = toSignal(inject(ActivatedRoute).paramMap);
  private readonly refresh = signal(0);
  private readonly ids = computed(() => {
    const params = this.params();
    return { account: params?.get('accountId') ?? '', shop: params?.get('shopId') ?? '', id: params?.get('requestId') ?? '' };
  });
  private readonly draftKey = computed(() => {
    const { account, shop, id } = this.ids();
    return [this.context.scope()?.userId ?? '', account, shop, id, 'consent'];
  });

  readonly operation = inject(AffiliationOperation);
  readonly detail = signal<AffiliationConsent | null>(null);
  readonly readError = signal('');

  constructor() {
    effect(() => this.operation.restore(this.draftKey()));
    effect((cleanup) => {
      this.refresh();
      const { account, shop, id } = this.ids();
      const scope = this.context.scope();
      this.detail.set(null);
      this.readError.set('');
      if (!scope || !account || !id) return;
      if (scope.shopId !== shop) {
        this.readError.set('billing.affiliation.ownerRequired');
        return;
      }
      const request = this.api.readConsent(account, shop, id).subscribe({
        next: (data) => this.detail.set(data),
        error: (error: unknown) => this.readError.set(affiliationError(error)),
      });
      cleanup(() => request.unsubscribe());
    });
  }

  reload(): void {
    this.refresh.update((current) => current + 1);
  }

  decide(decision: ConsentDecision, retryKey?: string): void {
    const scope = this.context.scope();
    const { account, shop, id } = this.ids();
    const key = this.draftKey();
    if (!scope || scope.shopId !== shop || !this.detail()) return;
    const intent = retryKey ? { retryKey } : { newDraft: { decision } };
    this.operation.submit(scope, key, intent,
      (draft) => this.api.decide(account, shop, id, decision, draft.operationKey, scope),
      () => this.draftKey() === key, () => this.reload());
  }
}
