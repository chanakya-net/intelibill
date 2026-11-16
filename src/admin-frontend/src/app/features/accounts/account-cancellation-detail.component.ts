import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { firstValueFrom } from 'rxjs';
import { AccountStatusApi, type CancellationSummary, type ProviderCancellationDetail, formatIstExact } from './account-status-api.service';

@Component({
  selector: 'adm-account-cancellation-detail',
  imports: [ButtonDirective],
  styles: [`
    :host { display: grid; gap: 0.5rem; }
    button { justify-self: start; }
    p, ul { margin: 0; }
    ul { list-style: disc; padding-inline-start: 1.5rem; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p data-testid="provider-cancellation-end">Cancellation scheduled for {{ exact(change().effectiveAt) }}.</p>
    <button type="button" pButton severity="secondary" size="small" data-testid="provider-cancellation-review"
      [disabled]="waiting()" (click)="read()">View affected shops</button>
    @if (detail(); as saved) {
      <div data-testid="provider-cancellation-detail">
        <p>No refund or grace period.</p>
        <p>Affected shops ({{ saved.affectedShopCount }})</p>
        <ul>
          @for (shop of saved.affectedShops; track shop.shopId) {
            <li [attr.data-shop-id]="shop.shopId">{{ shop.shopName }}</li>
          }
        </ul>
      </div>
    }
    @if (failed()) { <p role="alert">Could not verify cancellation details.</p> }
  `,
})
export class AccountCancellationDetailComponent {
  readonly account = input.required<string>();
  readonly term = input.required<string>();
  readonly change = input.required<CancellationSummary>();
  protected readonly detail = signal<ProviderCancellationDetail | null>(null);
  protected readonly waiting = signal(false);
  protected readonly failed = signal(false);
  protected readonly exact = formatIstExact;
  private readonly api = inject(AccountStatusApi);
  private destroyed = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.destroyed = true);
  }

  protected async read() {
    const [account, term, change] = [this.account(), this.term(), this.change()];
    const current = () => !this.destroyed && account === this.account() && term === this.term() && change === this.change();
    this.waiting.set(true);
    this.detail.set(null);
    this.failed.set(false);
    try {
      const first = await firstValueFrom(this.api.cancellation(account, term));
      if (!current()) return;
      const shops = new Map(first.affectedShops.map(shop => [shop.shopId, shop]));
      let after = first.nextCursor;
      while (after && shops.size < first.affectedShopCount) {
        const page = await firstValueFrom(this.api.cancellation(account, term, after));
        if (!current()) return;
        if (!this.matches(page, account, term, change) || page.affectedShopSetHash !== first.affectedShopSetHash
          || page.affectedShopCount !== first.affectedShopCount || page.affectedShops.length === 0)
          throw new Error('Cancellation scope changed');
        for (const shop of page.affectedShops) {
          if (shops.has(shop.shopId)) throw new Error('Repeated cancellation shop');
          shops.set(shop.shopId, shop);
        }
        after = page.nextCursor;
      }
      if (!this.matches(first, account, term, change) || after || shops.size !== first.affectedShopCount)
        throw new Error('Incomplete cancellation scope');
      this.detail.set({ ...first, affectedShops: [...shops.values()] });
    } catch {
      if (current()) this.failed.set(true);
    } finally {
      if (current()) this.waiting.set(false);
    }
  }

  private matches(page: ProviderCancellationDetail, account: string, term: string, change: CancellationSummary) {
    return page.accountId === account && page.termId === term && page.changeId === change.changeId
      && page.version === change.version && page.effectiveAt === change.effectiveAt && page.policyVersion === change.policyVersion;
  }
}
