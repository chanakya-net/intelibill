import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';
import type { SimulatorOutcome } from './checkout-payment.models';

/** Development-only payment simulator. Shown only when the server declares the simulator flow. */
@Component({
  selector: 'app-development-simulator-launcher',
  imports: [ButtonDirective, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="ib-panel" data-testid="payment-simulator" aria-labelledby="payment-simulator-heading">
      <h3 id="payment-simulator-heading">{{ 'billing.checkout.payment.simulator.title' | transloco }}</h3>
      <p data-testid="payment-simulator-notice">{{ 'billing.checkout.payment.simulator.notice' | transloco }}</p>
      <button pButton type="button" data-testid="payment-simulator-capture" [disabled]="busy()"
        [label]="'billing.checkout.payment.simulator.capture' | transloco" (click)="simulate.emit('Captured')"></button>
      <button pButton type="button" severity="secondary" data-testid="payment-simulator-return" [disabled]="busy()"
        [label]="'billing.checkout.payment.simulator.return' | transloco" (click)="returnWithoutCapture.emit()"></button>
      <button pButton type="button" severity="secondary" data-testid="payment-simulator-timeout" [disabled]="busy()"
        [label]="'billing.checkout.payment.simulator.timeout' | transloco" (click)="simulate.emit('Timeout')"></button>
      <button pButton type="button" severity="secondary" data-testid="payment-simulator-delayed" [disabled]="busy()"
        [label]="'billing.checkout.payment.simulator.delayed' | transloco" (click)="simulate.emit('Delayed')"></button>
      <button pButton type="button" severity="secondary" data-testid="payment-simulator-failed" [disabled]="busy()"
        [label]="'billing.checkout.payment.simulator.failed' | transloco" (click)="simulate.emit('Failed')"></button>
    </section>
  `,
})
export class DevelopmentSimulatorLauncherComponent {
  readonly busy = input(false);
  readonly simulate = output<SimulatorOutcome>();
  readonly returnWithoutCapture = output<void>();
}
