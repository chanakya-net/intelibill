import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';

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
        [label]="'billing.checkout.payment.simulator.capture' | transloco" (click)="capture.emit()"></button>
      <button pButton type="button" severity="secondary" data-testid="payment-simulator-return" [disabled]="busy()"
        [label]="'billing.checkout.payment.simulator.return' | transloco" (click)="returnWithoutCapture.emit()"></button>
    </section>
  `,
})
export class DevelopmentSimulatorLauncherComponent {
  readonly busy = input(false);
  readonly capture = output<void>();
  readonly returnWithoutCapture = output<void>();
}
