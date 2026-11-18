import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { AdminSessionService } from '../auth/admin-session.service';
import { ManualPaymentApi, type ConfirmationReference } from './manual-payment-api.service';

@Component({
  selector: 'adm-payment-confirmations', imports: [ButtonDirective], changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `section { display: grid; gap: 0.75rem; overflow-wrap: anywhere; }`,
  template: `
    <section class="ib-panel" aria-label="Payment confirmations">
      <h2>Payment confirmations</h2>
      <p>Verified receipt facts only; not a tax invoice or merchant sale receipt. Applied access is shown separately.</p>
      <button pButton type="button" data-testid="admin-confirmation-list" label="View payment confirmations" [disabled]="busy()" (click)="load()"></button>
      @for (row of rows(); track row.confirmationId) {
        <button pButton type="button" data-testid="admin-confirmation-download"
          label="Download payment confirmation" [disabled]="busy()" (click)="download(row)"></button>
        <small>{{ row.source }} · {{ row.confirmationId }}</small>
      }
      @if (error()) { <p role="alert">Could not load the confirmation. Try again.</p> }
    </section>
  `,
})
export class PaymentConfirmationsComponent {
  readonly account = input.required<string>();
  readonly order = input.required<string>();
  private readonly api = inject(ManualPaymentApi);
  private readonly session = inject(AdminSessionService);
  private readonly scope = computed(() => ({ account: this.account(), order: this.order(), admin: this.session.adminId() ?? '', session: this.session.sessionId() ?? '' }));
  protected readonly rows = signal<readonly ConfirmationReference[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal(false);
  private destroyed = false;
  constructor() {
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; });
    effect(() => { this.scope(); this.rows.set([]); this.busy.set(false); this.error.set(false); });
  }
  protected load() {
    void this.run(async scope => {
      const rows = await this.api.confirmations(scope, scope.session);
      if (!this.destroyed && this.scope() === scope) this.rows.set(rows);
    });
  }
  protected download(row: ConfirmationReference) {
    void this.run(async scope => {
      const blob = await this.api.confirmationDocument(scope, scope.session, row.confirmationId);
      if (this.destroyed || this.scope() !== scope) return;
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `payment-confirmation-${row.confirmationId}.txt`;
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    });
  }
  private async run(work: (scope: { account: string; order: string; admin: string; session: string }) => Promise<void>) {
    const scope = this.scope();
    if (!scope.admin || !scope.session || !this.session.capabilities().includes('billing') || this.busy()) return;
    this.busy.set(true);
    this.error.set(false);
    try { await work(scope); }
    catch { if (!this.destroyed && this.scope() === scope) this.error.set(true); }
    finally { if (!this.destroyed && this.scope() === scope) this.busy.set(false); }
  }
}
