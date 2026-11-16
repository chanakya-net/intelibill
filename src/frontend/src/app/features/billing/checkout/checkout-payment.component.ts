import { HttpErrorResponse } from '@angular/common/http';
import { isPlatformBrowser } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, PLATFORM_ID, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { formatBillingInstant } from '../status/subscription-status.models';
import type { CheckoutScope, Quote } from './checkout.models';
import type { CheckoutPayment } from './checkout-payment.models';
import { CheckoutPaymentService } from './checkout-payment.service';
import { DevelopmentSimulatorLauncherComponent } from './development-simulator-launcher.component';
import { resolvePaymentLauncher } from './payment-checkout-launcher';

type PaymentProblem = 'unavailable' | 'unsupported' | 'expired' | 'changed' | 'denied';
const REFRESH_MS = 5000;

/** Payment stage of an accepted quote. Every view is derived from server state; a browser report never confirms payment. */
@Component({
  selector: 'app-checkout-payment',
  imports: [ButtonDirective, TranslocoPipe, DevelopmentSimulatorLauncherComponent],
  templateUrl: './checkout-payment.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CheckoutPaymentComponent {
  private readonly api = inject(CheckoutPaymentService);
  private readonly context = inject(ShopContextService);
  private readonly locale = inject(LocalizationService);
  private generation = 0;

  readonly quote = input.required<Quote>();
  protected readonly payment = signal<CheckoutPayment | null>(null);
  protected readonly busy = signal(false);
  protected readonly problem = signal<PaymentProblem | null>(null);
  protected readonly launcher = computed(() => resolvePaymentLauncher(this.payment()?.launch ?? null));
  private readonly scope = computed<CheckoutScope | null>(() => {
    const owner = this.context.scope();
    return owner ? { ...owner, account: this.quote().accountId } : null;
  });

  constructor() {
    effect(() => {
      this.scope();
      this.quote().orderId;
      untracked(() => { this.generation++; this.payment.set(null); void this.run(scope => this.api.read(scope, this.quote().orderId)); });
    });
    if (!isPlatformBrowser(inject(PLATFORM_ID))) return;
    const timer = window.setInterval(() => this.refreshApplying(), REFRESH_MS);
    inject(DestroyRef).onDestroy(() => { this.generation++; window.clearInterval(timer); });
  }

  protected start() {
    const quote = this.quote();
    void this.run(async scope => {
      try {
        return await this.api.start(scope, quote.orderId, {
          operationKey: crypto.randomUUID(), expectedRevision: quote.revision, snapshotHash: quote.snapshotHash,
        });
      } catch (error) {
        // Another tab already started this order's single attempt: adopt it instead of paying twice.
        if (error instanceof HttpErrorResponse && error.error?.code === 'Billing.PaymentAlreadyStarted')
          return this.api.read(scope, quote.orderId);
        throw error;
      }
    });
  }

  protected capture() {
    const attempt = this.payment()?.attemptId;
    if (!attempt) return;
    void this.run(async scope => {
      await this.api.simulateCapture(scope, this.quote().orderId, attempt);
      return this.api.verify(scope, this.quote().orderId, attempt, 'Success');
    });
  }

  protected verify(claim: 'Success' | null) {
    const attempt = this.payment()?.attemptId;
    if (attempt) void this.run(scope => this.api.verify(scope, this.quote().orderId, attempt, claim));
  }

  protected instant(value: string | null) {
    return value ? formatBillingInstant(value, this.locale.currentLanguage(), true) : '';
  }

  private refreshApplying() {
    if (this.payment()?.access === 'Applying' && !this.busy() && document.visibilityState === 'visible')
      void this.run(scope => this.api.read(scope, this.quote().orderId), true);
  }

  private async run(work: (scope: CheckoutScope) => Promise<CheckoutPayment>, quiet = false) {
    const scope = this.scope();
    if (!scope || this.busy()) return;
    const generation = this.generation;
    const current = () => this.scope() === scope && this.generation === generation;
    this.busy.set(true);
    if (!quiet) this.problem.set(null);
    try {
      const next = await work(scope);
      if (current()) this.payment.set(next);
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
