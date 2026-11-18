import { PaymentConfirmationsComponent } from './payment-confirmations.component';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { AdminSessionService } from '../auth/admin-session.service';
import { formatIstExact } from '../accounts/account-status-api.service';
import { ManualPaymentApi, type ManualPaymentCommand, type ManualPaymentPreview,
  type ManualPaymentEvidence, type ManualPaymentReceipt, type ManualPaymentView, type PaymentRoutingDetail, type PaymentScope } from './manual-payment-api.service';
import { ManualPaymentDrafts } from './manual-payment-drafts';

const identifier = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;
interface Invocation {
  readonly scope: PaymentScope;
  readonly session: string;
  readonly current: () => boolean;
}

@Component({
  selector: 'adm-manual-payment-page',
  imports: [ButtonDirective, RouterLink, PaymentConfirmationsComponent],
  templateUrl: './manual-payment-page.component.html',
  styleUrl: '../form-page.css',
  styles: `
    section { overflow-wrap: anywhere; }
    fieldset { display: grid; gap: 0.75rem; border: 0; margin: 0; padding: 0; min-inline-size: 0; }
    textarea { display: block; inline-size: 100%; }
    input[type=checkbox] { width: auto; min-height: 0; accent-color: var(--p-primary-color); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ManualPaymentPageComponent {
  private readonly api = inject(ManualPaymentApi);
  private readonly drafts = inject(ManualPaymentDrafts);
  private readonly session = inject(AdminSessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly params = toSignal(this.route.queryParamMap);
  protected readonly account = computed(() => this.params()?.get('account') ?? '');
  protected readonly order = computed(() => this.params()?.get('order') ?? '');
  protected readonly allowed = computed(() => this.session.capabilities().includes('billing'));
  protected readonly view = signal<ManualPaymentView | null>(null);
  protected readonly routing = signal<PaymentRoutingDetail | null>(null);
  protected readonly preview = signal<ManualPaymentPreview | null>(null);
  protected readonly pending = signal<ManualPaymentCommand | null>(null);
  protected readonly receipt = signal<ManualPaymentReceipt | null>(null);
  protected readonly evidence = signal<{ receipt: string; details: ManualPaymentEvidence } | null>(null);
  protected readonly busy = signal(false);
  protected readonly confirmed = signal(false);
  protected readonly error = signal('');
  protected readonly reauthenticate = signal(false);
  protected readonly instant = formatIstExact;
  protected readonly money = (amount: number) => new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(amount / 100);
  private command: ManualPaymentCommand | null = null;
  private draftRevision = 0;
  private generation = 0;

  constructor() {
    effect(() => {
      this.account();
      this.order();
      this.session.adminId();
      this.session.sessionId();
      this.allowed();
      untracked(() => this.restore());
    });
    inject(DestroyRef).onDestroy(() => this.generation++);
  }

  protected search(event: Event, account: string, order: string) {
    event.preventDefault();
    if (!identifier.test(account.trim()) || !identifier.test(order.trim())) {
      this.error.set('Enter both the billing account and accepted order IDs.');
      return;
    }
    void this.router.navigate([], { relativeTo: this.route, queryParams: { account: account.trim(), order: order.trim() } });
  }

  protected invalidatePreview() {
    this.preview.set(null);
    this.command = null;
    this.confirmed.set(false);
  }

  protected requestPreview(event: Event, form: HTMLFormElement) {
    event.preventDefault();
    const view = this.view();
    if (!view?.receivingAccount || this.pending()) return;
    const fields = new FormData(form);
    const local = String(fields.get('receivedAt'));
    const received = Date.parse(local + (local.length === 16 ? ':00' : '') + '+05:30');
    if (!Number.isFinite(received)) {
      this.error.set('Enter a valid receipt date and time in IST.');
      return;
    }
    const command: ManualPaymentCommand = {
      operationKey: crypto.randomUUID(), expectedRevision: view.order.revision, snapshotHash: view.order.snapshotHash,
      receivingAccountId: view.receivingAccount.id, configurationVersion: view.receivingAccount.configurationVersion,
      reference: String(fields.get('reference')).trim(), evidence: String(fields.get('evidence')).trim(),
      receivedAt: new Date(received).toISOString(), amountPaise: Number(fields.get('amountPaise')),
      currency: view.order.currency, reason: String(fields.get('reason')).trim(), verified: false,
    };
    this.invalidatePreview();
    void this.run(async ({ scope, session, current }) => {
      const draft = await this.drafts.read(scope);
      if (!current()) return;
      if (draft.pending) {
        this.pending.set(draft.pending);
        return;
      }
      const preview = await this.api.preview(scope, session, command);
      if (!current()) return;
      this.draftRevision = draft.revision;
      this.command = command;
      this.preview.set(preview);
    });
  }

  protected nextShops() {
    const preview = this.preview();
    const after = preview?.order.nextShop;
    if (!preview || !after) return;
    void this.run(async ({ scope, session, current }) => {
      const { order } = await this.api.read(scope, session, after);
      if (current() && this.preview() === preview && order.snapshotHash === preview.order.snapshotHash)
        this.preview.set({ ...preview, order });
    });
  }

  protected confirm() {
    const command = this.command;
    if (!command || !this.confirmed() || this.preview()?.order.nextShop || this.pending()) return;
    void this.run(async invocation => {
      const claimed = await this.drafts.claim(invocation.scope, this.draftRevision, { ...command, verified: true });
      if (!invocation.current()) return;
      this.invalidatePreview();
      this.pending.set(claimed.pending ?? null);
      if (claimed.pending?.operationKey !== command.operationKey) {
        this.error.set(claimed.pending ? 'Check the existing confirmation.' : 'The receipt changed in another tab. Load a fresh preview.');
        return;
      }
      await this.send(invocation, claimed.pending, false);
    });
  }

  protected recover(retry: boolean) {
    const saved = this.pending();
    if (!saved) return;
    void this.run(invocation => this.send(invocation, saved, !retry));
  }

  protected inspect(receipt: string) {
    const open = this.evidence()?.receipt === receipt;
    this.evidence.set(null);
    if (open) return;
    void this.run(async ({ scope, session, current }) => {
      const details = await this.api.evidence(scope, session, receipt);
      if (current()) this.evidence.set({ receipt, details });
    });
  }

  protected reload() {
    this.restore();
  }

  private async send(invocation: Invocation, command: ManualPaymentCommand, lookup: boolean) {
    const { scope, session, current } = invocation;
    const receipt = lookup ? await this.api.recover(scope, session, command.operationKey)
      : await this.api.confirm(scope, session, command);
    if (receipt.operationKey !== command.operationKey || receipt.adminId !== scope.admin)
      throw new Error('Unexpected receipt identity');
    // A released hold leaves the historical receipt unchanged. Read the current order before clearing its draft.
    const held = receipt.reviewReason === 'RestorationPending' ? await this.api.read(scope, session) : null;
    if (!held?.status.restorationHeld)
      await this.drafts.settle(scope, command.operationKey);
    if (!current()) return;
    this.receipt.set(receipt);
    if (!held?.status.restorationHeld) this.pending.set(null);
    const view = held ?? await this.api.read(scope, session);
    if (current()) this.view.set(view);
  }

  private restore() {
    this.generation++;
    this.busy.set(false);
    this.error.set('');
    this.reauthenticate.set(false);
    this.invalidatePreview();
    this.pending.set(null);
    this.receipt.set(null);
    this.evidence.set(null);
    this.view.set(null);
    this.routing.set(null);
    if (!this.allowed() || !identifier.test(this.account()) || !identifier.test(this.order())) return;
    void this.run(async invocation => {
      const { scope, session, current } = invocation;
      const saved = await this.drafts.read(scope);
      if (!current()) return;
      this.pending.set(saved.pending ?? null);
      const view = await this.api.read(scope, session);
      if (!current()) return;
      this.view.set(view);
      if (view.status.attemptId) await this.loadRouting(invocation, view.status.attemptId);
    });
  }

  private async loadRouting({ scope, session, current }: Invocation, attempt: string) {
    const detail = await this.api.routing(scope, session);
    if (!current()) return;
    if (detail.schemaVersion !== 1 || detail.accountId !== scope.account || detail.orderId !== scope.order || detail.attemptId !== attempt)
      throw new Error('Unexpected payment routing identity');
    this.routing.set(detail);
  }

  private async run(work: (invocation: Invocation) => Promise<void>) {
    const admin = this.session.adminId();
    const session = this.session.sessionId();
    if (!admin || !session || !this.allowed() || this.busy()) return;
    const generation = this.generation;
    const scope = { admin, account: this.account(), order: this.order() };
    const current = () => generation === this.generation && this.allowed() && this.account() === scope.account &&
      this.order() === scope.order && this.session.adminId() === admin && this.session.sessionId() === session;
    this.busy.set(true);
    this.error.set('');
    this.reauthenticate.set(false);
    try {
      await work({ scope, session, current });
    } catch (error) {
      if (current()) await this.refuse(error, scope, current);
    } finally {
      if (current()) this.busy.set(false);
    }
  }

  private async refuse(error: unknown, scope: PaymentScope, current: () => boolean) {
    const code = error instanceof HttpErrorResponse ? problemCode(error.error) : null;
    if (code === 'ManualPayment.RecentMfaRequired') this.reauthenticate.set(true);
    const rejected = code === 'ManualPayment.Invalid' || code === 'ManualPayment.ReferenceConflict' ||
      code === 'Billing.CheckoutSourceChanged';
    const saved = this.pending();
    if (rejected && saved) {
      try {
        await this.drafts.settle(scope, saved.operationKey);
        if (current()) this.pending.set(null);
      } catch {
        if (current()) this.error.set('Saved confirmation could not be cleared. Check the original key.');
        return;
      }
    }
    if (!current()) return;
    this.error.set(this.reauthenticate() ? 'Sign in with MFA again to continue.' :
      this.pending() ? 'Confirmation is unresolved. Check or retry the original receipt.' :
      code === 'ManualPayment.ReferenceConflict' ? 'This reference has already been recorded.' :
      rejected ? 'Check the complete frozen amount and receipt evidence, then preview again.' :
      'Could not load or save this payment. Check browser storage and try again.');
  }
}
