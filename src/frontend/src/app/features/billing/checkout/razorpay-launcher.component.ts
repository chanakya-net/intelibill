import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';
import { RAZORPAY_OPENER, type RazorpayLaunch, type RazorpayOutcome } from './razorpay-checkout';

/** Razorpay launcher behind the neutral checkout interface. Reports an unverified outcome; never grants access. */
@Component({
  selector: 'app-razorpay-launcher',
  imports: [ButtonDirective, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="ib-panel" data-testid="payment-razorpay" aria-labelledby="payment-razorpay-heading">
      <h3 id="payment-razorpay-heading">{{ 'billing.checkout.payment.razorpay.title' | transloco }}</h3>
      <p data-testid="payment-razorpay-notice">{{ 'billing.checkout.payment.razorpay.notice' | transloco }}</p>
      @if (failed()) {
        <p role="alert" data-testid="payment-razorpay-unavailable">{{ 'billing.checkout.payment.razorpay.unavailable' | transloco }}</p>
      }
      <button pButton type="button" data-testid="payment-razorpay-pay" [disabled]="busy() || opening()"
        [label]="(opening() ? 'billing.checkout.payment.razorpay.loading' : 'billing.checkout.payment.razorpay.pay') | transloco"
        (click)="open()"></button>
    </section>
  `,
})
export class RazorpayLauncherComponent {
  private readonly opener = inject(RAZORPAY_OPENER);
  readonly launch = input.required<RazorpayLaunch>();
  readonly busy = input(false);
  /** Durably records that this checkout is being opened; the window opens only once that is saved. */
  readonly prepare = input<(launch: RazorpayLaunch, lifetime: AbortSignal) => Promise<boolean>>(() => Promise.resolve(true));
  /** Fires synchronously on the click, so the owner can bind the eventual outcome to what was on screen. */
  readonly opened = output<RazorpayLaunch>();
  readonly outcome = output<RazorpayOutcome>();
  /** The window could not open after the checkout was saved as opened; names the launch the click captured. */
  readonly launchFailed = output<RazorpayLaunch>();
  private readonly lifetime = new AbortController();
  constructor() {
    inject(DestroyRef).onDestroy(() => this.lifetime.abort());
  }

  protected readonly opening = signal(false);
  protected readonly failed = signal(false);

  protected async open() {
    this.failed.set(false);
    this.opening.set(true);
    const launch = this.launch();
    this.opened.emit(launch);
    let prepared = false;
    try {
      if (!await this.prepare()(launch, this.lifetime.signal) || this.lifetime.signal.aborted) return;
      prepared = true;
      const result = await this.opener(launch, this.lifetime.signal);
      if (!this.lifetime.signal.aborted) this.outcome.emit(result);
    } catch {
      this.failed.set(true);
      if (prepared && !this.lifetime.signal.aborted) this.launchFailed.emit(launch);
    } finally {
      this.opening.set(false);
    }
  }
}
