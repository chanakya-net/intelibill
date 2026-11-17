import { HttpErrorResponse } from '@angular/common/http';
import { isPlatformBrowser } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, PLATFORM_ID, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { formatBillingInstant } from '../status/subscription-status.models';
import type { CheckoutScope, Quote } from './checkout.models';
import type { CheckoutPayment, SimulatorOutcome } from './checkout-payment.models';
import { CheckoutPaymentService } from './checkout-payment.service';
import { DevelopmentSimulatorLauncherComponent } from './development-simulator-launcher.component';
import { resolvePaymentLauncher } from './payment-checkout-launcher';
import { CheckoutDrafts, type PaymentDraft, type PaymentDraftState } from './checkout-drafts';

type PaymentProblem = 'unavailable' | 'unsupported' | 'expired' | 'changed' | 'denied';
const REFRESH_MS = 5000;
const POLL_LIMIT = 12;

type Invocation = {
  readonly scope: CheckoutScope;
  readonly order: string;
  readonly current: () => boolean;
};

const draftStates: Record<Exclude<CheckoutPayment['payment'], 'NotStarted'>, Exclude<PaymentDraftState, 'intent'>> = {
  AwaitingPayment: 'awaiting', PendingVerification: 'pending', Verified: 'verified', Failed: 'failed',
};

@Component({
  selector: 'app-checkout-payment',
  imports: [ButtonDirective, TranslocoPipe, DevelopmentSimulatorLauncherComponent],
  templateUrl: './checkout-payment.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:storage)': 'onStorage($event)' },
})
export class CheckoutPaymentComponent {
  private readonly api = inject(CheckoutPaymentService);
  private readonly context = inject(ShopContextService);
  private readonly locale = inject(LocalizationService);
  private readonly drafts = inject(CheckoutDrafts);
  private generation = 0;
  private polls = 0;
  private ticks = 0;

  readonly quote = input.required<Quote>();
  protected readonly payment = signal<CheckoutPayment | null>(null);
  protected readonly busy = signal(false);
  protected readonly problem = signal<PaymentProblem | null>(null);
  private readonly draft = signal<PaymentDraft | null>(null);
  protected readonly pending = computed(() => {
    const state = this.draft()?.state;
    const payment = this.payment();
    return state === 'intent' || state === 'pending' || (state === 'awaiting' && !payment) ||
      payment?.payment === 'PendingVerification' || (!!payment?.manualPayment && payment.payment === 'AwaitingPayment');
  });
  protected readonly launcher = computed(() => resolvePaymentLauncher(this.payment()?.launch ?? null));
  private readonly scope = computed<CheckoutScope | null>(() => {
    const owner = this.context.scope();
    return owner ? { ...owner, account: this.quote().accountId } : null;
  });

  constructor() {
    if (!isPlatformBrowser(inject(PLATFORM_ID))) return;
    effect(() => {
      this.scope();
      this.quote().orderId;
      untracked(() => this.restore());
    });
    const timer = window.setInterval(() => this.refresh(), REFRESH_MS);
    inject(DestroyRef).onDestroy(() => { this.generation++; window.clearInterval(timer); });
  }

  blocksNewQuote(): boolean {
    const status = this.payment();
    return this.busy() || this.pending() || !status || status.payment === 'AwaitingPayment' || status.restorationHeld === true;
  }

  canReviewNewQuote(): boolean {
    const scope = this.scope();
    if (!scope || this.blocksNewQuote()) return false;
    try {
      const saved = this.drafts.readPayment(scope, this.quote().orderId);
      return !saved || saved.state === 'failed' || saved.state === 'verified';
    } catch {
      this.problem.set('unavailable');
      return false;
    }
  }

  protected onStorage(event: StorageEvent) {
    const scope = this.scope();
    if (scope && event.key === this.drafts.paymentKey(scope, this.quote().orderId) && !this.busy())
      this.restore();
  }

  protected start() {
    const quote = this.quote();
    void this.run(async invocation => {
      const saved = await this.drafts.preparePayment(invocation.scope, invocation.order, quote.revision, quote.snapshotHash);
      if (!invocation.current()) return;
      this.draft.set(saved);
      if (saved.state === 'intent' && saved.command)
        await this.sendStart(invocation, saved.command);
      else
        await this.read(invocation);
    });
  }

  protected simulate(outcome: SimulatorOutcome) {
    void this.run(async invocation => {
      const attempt = this.payment()?.attemptId;
      if (!attempt || !await this.rememberPending(invocation, attempt)) return;
      await this.api.simulateCapture(invocation.scope, invocation.order, attempt, outcome);
      if (invocation.current()) await this.verifyAttempt(invocation, attempt, outcome === 'Captured' ? 'Success' : null);
    });
  }

  protected checkAgain() {
    void this.run(async invocation => {
      const saved = this.drafts.readPayment(invocation.scope, invocation.order);
      const attempt = saved?.attempt ?? this.payment()?.attemptId;
      if (attempt) {
        await this.verifyAttempt(invocation, attempt, null);
      } else if (saved?.state === 'intent' && saved.command) {
        await this.recoverStart(invocation, saved.command);
      } else {
        await this.read(invocation);
      }
    });
  }

  protected verify(claim: 'Success') {
    void this.run(async invocation => {
      const attempt = this.payment()?.attemptId;
      if (attempt && await this.rememberPending(invocation, attempt))
        await this.verifyAttempt(invocation, attempt, claim);
    });
  }

  protected instant(value: string | null) {
    return value ? formatBillingInstant(value, this.locale.currentLanguage(), true) : '';
  }

  private restore() {
    this.generation++;
    this.polls = this.ticks = 0;
    this.busy.set(false);
    this.payment.set(null);
    this.draft.set(null);
    this.problem.set(null);
    void this.run(async invocation => {
      const saved = this.drafts.readPayment(invocation.scope, invocation.order);
      this.draft.set(saved);
      await this.read(invocation);
      const next = this.draft();
      if (invocation.current() && saved?.state === 'pending' && next?.state === 'pending' && next.attempt)
        await this.verifyAttempt(invocation, next.attempt, null);
    });
  }

  private refresh() {
    if (this.busy() || document.visibilityState !== 'visible' || this.polls >= POLL_LIMIT) return;
    if (!this.pending() && this.payment()?.access !== 'Applying') return;
    if (++this.ticks < (this.payment()?.access === 'Applying' ? 1 : Math.min(8, 2 ** this.polls))) return;
    this.ticks = 0;
    this.polls++;
    void this.run(invocation => this.read(invocation), true);
  }

  private async recoverStart(invocation: Invocation, command: NonNullable<PaymentDraft['command']>) {
    try {
      await this.read(invocation);
    } catch (error) {
      if (!(error instanceof HttpErrorResponse) || error.status !== 404) throw error;
    }
    if (!invocation.current()) return;
    const saved = this.drafts.readPayment(invocation.scope, invocation.order);
    this.draft.set(saved);
    if (saved?.state === 'intent' && saved.command?.operationKey === command.operationKey)
      await this.sendStart(invocation, command);
  }

  private async sendStart(invocation: Invocation, command: NonNullable<PaymentDraft['command']>) {
    try {
      await this.show(invocation, await this.api.start(invocation.scope, invocation.order, command));
    } catch (error) {
      if (!invocation.current()) return;
      if (error instanceof HttpErrorResponse && error.error?.code === 'Billing.PaymentAlreadyStarted')
        await this.read(invocation);
      else
        throw error;
    }
  }

  private async rememberPending(invocation: Invocation, attempt: string) {
    const saved = await this.drafts.recordPayment(invocation.scope, invocation.order, attempt, 'pending');
    if (!invocation.current()) return false;
    this.draft.set(saved);
    return saved.state === 'pending';
  }

  private async read(invocation: Invocation) {
    await this.show(invocation, await this.api.read(invocation.scope, invocation.order));
  }

  private async verifyAttempt(invocation: Invocation, attempt: string, claim: 'Success' | null) {
    await this.show(invocation, await this.api.verify(invocation.scope, invocation.order, attempt, claim));
  }

  private async show(invocation: Invocation, status: CheckoutPayment) {
    if (!invocation.current()) return;
    if (status.payment !== 'NotStarted' && status.attemptId) {
      const saved = await this.drafts.recordPayment(invocation.scope, invocation.order, status.attemptId, draftStates[status.payment]);
      if (!invocation.current()) return;
      this.draft.set(saved);
      if (saved.state !== draftStates[status.payment]) return;
    } else {
      const saved = this.drafts.readPayment(invocation.scope, invocation.order);
      this.draft.set(saved);
      if (saved && !status.manualPayment) return;
    }
    this.payment.set(status);
    this.problem.set(null);
  }

  private async run(work: (invocation: Invocation) => Promise<void>, quiet = false) {
    const scope = this.scope();
    if (!scope || this.busy()) return;
    const order = this.quote().orderId;
    const generation = this.generation;
    const current = () => this.scope() === scope && this.quote().orderId === order && generation === this.generation;
    this.busy.set(true);
    if (!quiet) this.problem.set(null);
    try {
      await work({ scope, order, current });
    } catch (error) {
      if (current() && !quiet) this.problem.set(paymentFailure(error));
    } finally {
      if (current()) this.busy.set(false);
    }
  }
}

function paymentFailure(error: unknown): PaymentProblem {
  if (!(error instanceof HttpErrorResponse)) return 'unavailable';
  if (error.status === 401 || error.status === 403) return 'denied';
  switch (error.error?.code) {
    case 'Billing.PaymentUnsupported': return 'unsupported';
    case 'Billing.CheckoutExpired': return 'expired';
    case 'Billing.CheckoutSourceChanged': return 'changed';
    default: return 'unavailable';
  }
}
