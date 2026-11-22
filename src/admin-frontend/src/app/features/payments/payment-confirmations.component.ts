import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { AdminSessionService } from '../auth/admin-session.service';
import { ManualPaymentApi, type ConfirmationReference } from './manual-payment-api.service';

@Component({
  selector: 'adm-payment-confirmations', imports: [ButtonDirective], changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    section { display: grid; gap: 0.9rem; overflow-wrap: anywhere; }
    .confirmation-list { display: grid; gap: 0.65rem; }
    .confirmation-row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0.65rem 1rem;
      padding: 0.75rem 0.95rem;
      border: 1px solid var(--adm-border);
      border-radius: 0.7rem;
      background: var(--adm-surface-subtle);
    }
  `,
  template: `
    <section class="ib-panel" aria-label="Payment confirmations">
      <div class="adm-panel-head">
        <div class="adm-panel-head__copy">
          <h2 class="ib-panel__title">Payment confirmations</h2>
          <p class="adm-panel-sub">Verified receipt facts only; not a tax invoice or merchant sale receipt. Applied access is shown separately.</p>
        </div>
        <button pButton severity="secondary" [outlined]="true" type="button" data-testid="admin-confirmation-list" label="View payment confirmations" [disabled]="busy()" (click)="load()"></button>
      </div>
      @if (rows().length) {
        <div class="confirmation-list">
          @for (row of rows(); track row.confirmationId) {
            <div class="confirmation-row">
              <small>{{ row.source }} · <code>{{ row.confirmationId }}</code></small>
              <button pButton severity="secondary" [outlined]="true" size="small" type="button" data-testid="admin-confirmation-download"
                label="Download payment confirmation" [disabled]="busy()" (click)="download(row)"></button>
            </div>
          }
        </div>
      }
      @if (hasMore()) {
        <div class="adm-actions">
          <button pButton severity="secondary" [outlined]="true" type="button" data-testid="admin-confirmation-more" label="Load more confirmations"
            [disabled]="busy()" (click)="load(true)"></button>
        </div>
      }
      @if (error()) { <p class="ib-error-banner" role="alert">Could not load the confirmation. Try again.</p> }
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
  protected readonly hasMore = signal(false);
  protected readonly error = signal(false);
  private destroyed = false;
  constructor() {
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; });
    effect(() => { this.scope(); this.rows.set([]); this.hasMore.set(false); this.busy.set(false); this.error.set(false); });
  }
  protected load(more = false) {
    void this.run(async scope => {
      const prior = more ? this.rows() : [];
      const rows = await this.api.confirmations(scope, scope.session, prior.at(-1)?.confirmationId);
      if (this.destroyed || this.scope() !== scope) return;
      const combined = [...prior, ...rows];
      if (new Set(combined.map(row => row.confirmationId)).size !== combined.length) throw new Error('Repeated confirmation page');
      this.rows.set(combined);
      this.hasMore.set(rows.length === 25);
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
