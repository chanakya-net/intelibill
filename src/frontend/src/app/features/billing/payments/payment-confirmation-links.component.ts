import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { BlobDownloadService } from '../../../shared/utils/blob-download.service';
import { PaymentConfirmationService } from './payment-confirmation.service';
import type { PaymentConfirmation } from './payment-confirmation.models';

@Component({
  selector: 'app-payment-confirmation-links',
  imports: [TranslocoPipe, ButtonDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `section { display: grid; gap: 0.5rem; overflow-wrap: anywhere; }`,
  template: `
    <section [attr.aria-label]="'billing.checkout.payment.confirmation.view' | transloco">
      <button pButton type="button" data-testid="confirmation-list" [disabled]="busy()"
        [label]="'billing.checkout.payment.confirmation.view' | transloco" (click)="load()"></button>
      <p>{{ 'billing.checkout.payment.confirmation.disclaimer' | transloco }}</p>
      @for (row of rows(); track row.confirmationId) {
        <button pButton type="button" data-testid="confirmation-download" [disabled]="busy()"
          [label]="'billing.checkout.payment.confirmation.download' | transloco"
          (click)="download(row)"></button>
        <small>{{ row.source }} · {{ row.confirmationId }}</small>
      }
      @if (error()) { <p role="alert">{{ 'billing.checkout.payment.confirmation.error' | transloco }}</p> }
    </section>
  `,
})
export class PaymentConfirmationLinksComponent {
  readonly account = input.required<string>();
  readonly order = input.required<string>();
  private readonly context = inject(ShopContextService);
  private readonly api = inject(PaymentConfirmationService);
  private readonly blobs = inject(BlobDownloadService);
  private readonly scope = computed(() => {
    const scope = this.context.scope();
    return scope ? { ...scope, account: this.account(), order: this.order() } : null;
  });
  protected readonly rows = signal<readonly PaymentConfirmation[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal(false);
  private destroyed = false;
  constructor() {
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; });
    effect(() => { this.scope(); this.rows.set([]); this.busy.set(false); this.error.set(false); });
  }
  protected load() {
    void this.run(async scope => {
      const rows = await this.api.list(scope, scope.order);
      if (!this.destroyed && this.scope() === scope) this.rows.set(rows);
    });
  }
  protected download(row: PaymentConfirmation) {
    void this.run(async scope => {
      const blob = await this.api.document(scope, scope.order, row.confirmationId);
      if (!this.destroyed && this.scope() === scope) this.blobs.download(blob, `payment-confirmation-${row.confirmationId}.txt`);
    });
  }
  private async run(work: (scope: NonNullable<ReturnType<typeof this.scope>>) => Promise<void>) {
    const scope = this.scope();
    if (!scope || this.busy()) return;
    this.busy.set(true);
    this.error.set(false);
    try { await work(scope); }
    catch { if (!this.destroyed && this.scope() === scope) this.error.set(true); }
    finally { if (!this.destroyed && this.scope() === scope) this.busy.set(false); }
  }
}
