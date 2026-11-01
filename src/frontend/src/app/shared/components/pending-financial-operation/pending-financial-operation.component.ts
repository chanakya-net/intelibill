import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';

import type { FinancialOperationIntent } from '../../../core/services/financial-operation-intent.service';

/**
 * Shows a payment whose outcome is not yet confirmed. It offers only safe actions: a read-only
 * status check and a retry that resends the same frozen payment under the same key.
 */
@Component({
  selector: 'app-pending-financial-operation',
  standalone: true,
  imports: [ButtonModule, CurrencyPipe, DatePipe, TranslocoPipe],
  templateUrl: './pending-financial-operation.component.html',
  styleUrl: './pending-financial-operation.component.scss',
})
export class PendingFinancialOperationComponent {
  readonly intent = input.required<FinancialOperationIntent<{ readonly amount: number }>>();
  readonly busy = input(false);
  /** Translation key for the latest check result, if any. */
  readonly notice = input<string | null>(null);

  readonly checkStatus = output<void>();
  readonly retry = output<void>();
}
