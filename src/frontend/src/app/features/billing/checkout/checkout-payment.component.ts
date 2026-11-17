import { HttpErrorResponse } from '@angular/common/http';
import { isPlatformBrowser } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, PLATFORM_ID, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { formatBillingInstant } from '../status/subscription-status.models';
import type { CheckoutScope, Quote } from './checkout.models';
import { paymentFailure, type CheckoutPayment, type PaymentProblem, type SimulatorOutcome } from './checkout-payment.models';
import { CheckoutPaymentService } from './checkout-payment.service';
import { DevelopmentSimulatorLauncherComponent } from './development-simulator-launcher.component';
import { razorpayLaunch, type RazorpayLaunch, type RazorpayOutcome } from './razorpay-checkout';
import { RazorpayLauncherComponent } from './razorpay-launcher.component';
import { resolvePaymentLauncher } from './payment-checkout-launcher';
import { CheckoutDrafts, type PaymentDraft, type PaymentDraftState } from './checkout-drafts';

const REFRESH_MS = 5000;
const POLL_LIMIT = 12;

type Invocation = {
  readonly scope: CheckoutScope;
  readonly order: string;
  readonly current: () => boolean;
};

type LaunchTicket = {
  readonly scope: CheckoutScope | null;
  readonly order: string;
  readonly generation: number;
  readonly attempt: string | null | undefined;
  readonly providerOrder: string;
  /** The launch the click captured: a failed open releases only its own invocation's hold. */
  readonly launch: RazorpayLaunch;
};
/**
 * A signed success relays its proof; a success without valid proof is relayed as an unverified claim the server
 * refuses; a dismissed window (`claimed: false`) asks for a proof-free check of the order.
 */
type QueuedOutcome = {
  readonly ticket: LaunchTicket; readonly proof: Readonly<Record<string, string>> | null; readonly claimed: boolean;
};

const draftStates: Record<Exclude<CheckoutPayment['payment'], 'NotStarted'>, Exclude<PaymentDraftState, 'intent'>> = {
  AwaitingPayment: 'awaiting', PendingVerification: 'pending', Verified: 'verified', Failed: 'failed',
};
const draftOrder: readonly PaymentDraftState[] = ['intent', 'awaiting', 'pending', 'failed', 'verified'];

/**
 * The saved draft reached a terminal result past the attempt state on screen, as another tab's later result would
 * move it. (A pending draft over an awaiting projection is already shown as pending.)
 */
function draftAhead(state: PaymentDraftState | undefined, payment: CheckoutPayment | null) {
  return (state === 'failed' || state === 'verified') && !!payment?.attemptId && payment.payment !== 'NotStarted' &&
    draftOrder.indexOf(state) > draftOrder.indexOf(draftStates[payment.payment]);
}

@Component({
  selector: 'app-checkout-payment',
  imports: [ButtonDirective, TranslocoPipe, DevelopmentSimulatorLauncherComponent, RazorpayLauncherComponent],
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
  private ticket: LaunchTicket | null = null;
  private queued: QueuedOutcome | null = null;
  /** A cross-tab draft change that arrived while a request ran; reread before Pay can be offered again. */
  private storageDeferred = false;
  /** Releases this page's hosted-window hold; held from before the pending claim until the outcome is saved. */
  private releaseWindow: (() => void) | null = null;
  /** The saved draft revision this page already read the authoritative status for to catch up; one read each. */
  private caughtUp: number | null = null;

  readonly quote = input.required<Quote>();
  protected readonly payment = signal<CheckoutPayment | null>(null);
  protected readonly busy = signal(false);
  protected readonly problem = signal<PaymentProblem | null>(null);
  private readonly draft = signal<PaymentDraft | null>(null);
  /** This tab's hosted window is open: its own saved pending claim keeps the launcher (and window) on screen. */
  private readonly windowOpen = signal(false);
  protected readonly pending = computed(() => {
    const state = this.draft()?.state;
    const payment = this.payment();
    if (this.windowOpen() && state === 'pending' && payment?.payment === 'AwaitingPayment') return false;
    return state === 'intent' || state === 'pending' || (state === 'awaiting' && !payment) || draftAhead(state, payment) ||
      payment?.payment === 'PendingVerification' || (!!payment?.manualPayment && payment.payment === 'AwaitingPayment');
  });
  protected readonly launcher = computed(() => resolvePaymentLauncher(this.payment()?.launch ?? null));
  protected readonly razorpay = computed<RazorpayLaunch | null>(() => razorpayLaunch(this.payment()?.launch ?? null));
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
    inject(DestroyRef).onDestroy(() => { this.generation++; window.clearInterval(timer); this.endWindowHold(); });
  }

  blocksNewQuote(): boolean {
    const status = this.payment();
    return this.busy() || this.pending() || !status || status.payment === 'AwaitingPayment' || status.fulfilment === 'NeedsReview';
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
    if (!scope || event.key !== this.drafts.paymentKey(scope, this.quote().orderId)) return;
    if (this.busy() || this.windowOpen()) this.storageDeferred = true;
    else this.restore();
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

  /** Holds the order until its outcome is saved or this specific launcher is torn down; teardown keeps the draft. */
  protected readonly prepareOpen = async (launch: RazorpayLaunch, lifetime: AbortSignal) => {
    const ticket = this.ticket;
    if (!ticket?.attempt || ticket.launch !== launch || lifetime.aborted) return false;
    const attempt = ticket.attempt;
    let opened = false;
    await this.run(async invocation => {
      this.windowOpen.set(true);
      const release = await this.drafts.holdWindow(invocation.scope, invocation.order);
      if (!invocation.current() || this.ticket !== ticket || lifetime.aborted) { release?.(); return; }
      this.releaseWindow = release;
      lifetime.addEventListener('abort', () => {
        if (this.ticket !== ticket) return;
        this.ticket = null;
        this.windowOpen.set(false);
        this.endWindowHold();
      }, { once: true });
      opened = !!release && await this.rememberPending(invocation, attempt) && this.ticket === ticket;
    });
    if (!opened && this.ticket === ticket) {
      this.windowOpen.set(false);
      this.endWindowHold();
    }
    return opened;
  };

  private endWindowHold() {
    const release = this.releaseWindow;
    this.releaseWindow = null;
    release?.();
  }

  protected razorpayOpened(launch: RazorpayLaunch) {
    this.ticket = {
      scope: this.scope(), order: this.quote().orderId, generation: this.generation,
      attempt: this.payment()?.attemptId, providerOrder: launch.orderId, launch,
    };
  }

  /**
   * Relays the browser's unverified report for the attempt that was on screen when the window opened. A report that
   * outlives its scope or order is dropped, and one that lands while another request runs waits for it. A dismissed
   * window is an unknown outcome: a payment may already be in flight, so the original order is checked server-side,
   * proof-free, and offered again only if that check proves it idle.
   */
  protected razorpayOutcome(outcome: RazorpayOutcome) {
    const ticket = this.ticket;
    this.ticket = null;
    this.windowOpen.set(false);
    if (!ticket || !this.ticketCurrent(ticket) || !ticket.attempt) return;
    this.queued = { ticket, proof: outcome.kind === 'success' ? outcome.proof : null,
      claimed: outcome.kind === 'success' || outcome.kind === 'unverified' };
    this.drainQueued();
  }

  /** A failed open ends only its captured invocation, then checks the original order proof-free before offering Pay. */
  protected razorpayLaunchFailed(launch: RazorpayLaunch) {
    if (this.ticket?.launch !== launch) return;
    this.razorpayOutcome({ kind: 'cancelled' });
  }

  private ticketCurrent(ticket: LaunchTicket) {
    return ticket.generation === this.generation && ticket.scope === this.scope() && ticket.order === this.quote().orderId;
  }

  private drainQueued() {
    const queued = this.queued;
    if (!queued || this.busy()) return;
    this.queued = null;
    if (!this.ticketCurrent(queued.ticket) || !queued.ticket.attempt) return this.endWindowHold();
    const attempt = queued.ticket.attempt;
    void this.run(async invocation => {
      const claimed = await this.rememberPending(invocation, attempt);
      // The outcome is saved (and supersedes any recheck sent while the window was open): the hold can go.
      this.endWindowHold();
      if (!claimed) return;
      if (queued.proof) await this.verifyAttempt(invocation, attempt, 'Success', queued.proof);
      else await this.verifyAttempt(invocation, attempt, queued.claimed ? 'Success' : null);
    });
  }

  protected refreshReview() {
    void this.run(invocation => this.read(invocation));
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
    this.ticket = this.queued = null;
    this.windowOpen.set(false);
    this.endWindowHold();
    this.caughtUp = null;
    this.storageDeferred = false;
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
    const saved = await this.drafts.recordPayment(invocation.scope, invocation.order, attempt, 'pending', undefined, true);
    if (!invocation.current()) return false;
    this.draft.set(saved);
    return saved.state === 'pending';
  }

  private async read(invocation: Invocation) {
    await this.show(invocation, await this.api.read(invocation.scope, invocation.order));
  }

  /** Only a proof-free recheck may reopen a pending attempt, and only if no tab saved anything since it was sent. */
  private async verifyAttempt(invocation: Invocation, attempt: string, claim: 'Success' | 'Cancelled' | null,
    proof?: Readonly<Record<string, string>>) {
    const fence = claim === null && !proof ? this.drafts.paymentRevision(invocation.scope, invocation.order) : undefined;
    await this.show(invocation, await this.api.verify(invocation.scope, invocation.order, attempt, claim, proof), fence);
  }

  private async show(invocation: Invocation, status: CheckoutPayment, freshSince?: number) {
    if (!invocation.current()) return;
    if (status.payment !== 'NotStarted' && status.attemptId) {
      const saved = await this.drafts.recordPayment(invocation.scope, invocation.order, status.attemptId,
        draftStates[status.payment], freshSince);
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
      if (current()) this.applyDeferredStorage(scope, order);
      if (current()) this.busy.set(false);
      if (current()) queueMicrotask(() => { this.drainQueued(); this.catchUp(); });
    }
  }

  /**
   * Reads the authoritative status once a deferred cross-tab change left the draft ahead of what is on screen: once
   * per saved revision. A failed or stale answer keeps Pay hidden and leaves the rest to the bounded refresh or
   * Check again.
   */
  private catchUp() {
    const draft = this.draft();
    const revision = draft?.revision ?? 0;
    if (this.busy() || !draftAhead(draft?.state, this.payment()) || this.caughtUp === revision) return;
    this.caughtUp = revision;
    void this.run(invocation => this.read(invocation));
  }

  /** Rereads the owner-bound draft another tab changed mid-request, so a newer pending claim keeps Pay hidden. */
  private applyDeferredStorage(scope: CheckoutScope, order: string) {
    if (!this.storageDeferred) return;
    this.storageDeferred = false;
    try {
      this.draft.set(this.drafts.readPayment(scope, order));
    } catch {
      this.problem.set('unavailable');
    }
  }
}
