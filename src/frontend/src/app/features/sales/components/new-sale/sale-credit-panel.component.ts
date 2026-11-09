import { LocaleMoneyPipe } from '../../../../shared/pipes/locale-money.pipe';
import { CommonModule } from '@angular/common';
import {
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  inject,
  signal,
  computed,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { Subscription } from 'rxjs';

import {
  CustomerCreditStatus,
  CustomerService,
} from '../../../customers/services/customer.service';
import { creditPolicyLabelKey } from '../../../customers/utils/credit-policy.util';
import { SaleCreditOverride } from '../../services/sale.models';

const CREDIT_ERROR_PREFIX = 'sales.newSale.credit.errors.';

/**
 * The checkout's credit preview for the selected customer. It is advisory: the server decides
 * again under the customer's lock when the sale posts. An owner or manager may approve going over
 * an enforced limit with a reason; the approval covers the balance shown here and is dropped
 * whenever the customer or the due changes.
 */
@Component({
  selector: 'app-sale-credit-panel',
  standalone: true,
  imports: [
    LocaleMoneyPipe,
    CommonModule,
    FormsModule,
    ButtonModule,
    InputTextModule,
    TranslocoPipe,
  ],
  templateUrl: './sale-credit-panel.component.html',
})
export class SaleCreditPanelComponent implements OnChanges {
  private readonly customerService = inject(CustomerService);
  private request?: Subscription;

  @Input() customerId: string | null = null;
  @Input() dueAmount = 0;
  @Input() isOfflineMode = false;
  @Input() canOverride = false;
  /**
   * The checkout's last server error. A credit refusal means the preview (and any approval built on
   * it) is stale, so it is fetched again and the approval dropped.
   */
  @Input() serverError = '';

  @Output() readonly overrideChange = new EventEmitter<SaleCreditOverride | null>();

  readonly status = signal<CustomerCreditStatus | null>(null);
  readonly due = signal(0);
  readonly reason = signal('');
  readonly approved = signal(false);

  readonly policyLabelKey = computed(() => creditPolicyLabelKey(this.status()?.creditPolicyMode));
  readonly isAdvisory = computed(() => this.status()?.creditPolicyMode === 'LegacyAdvisory');
  /** The due cannot be taken at all: an inactive or no-credit customer (no override). */
  readonly isBlocked = computed(() => {
    const status = this.status();
    return (
      !!status && this.due() > 0 && (!status.isActive || status.creditPolicyMode === 'NoCredit')
    );
  });
  /** An enforced limit would be exceeded: refused unless an owner or manager approves. */
  readonly exceedsLimit = computed(() => {
    const status = this.status();
    return (
      !!status &&
      status.isActive &&
      status.creditPolicyMode === 'Enforced' &&
      status.availableCredit !== null &&
      this.due() > status.availableCredit
    );
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }

  ngOnChanges(changes: SimpleChanges): void {
    this.due.set(Number(this.dueAmount) || 0);
    if (changes['customerId'] || changes['isOfflineMode']) {
      this.load();
    }
    const creditRefused =
      !!changes['serverError'] && this.serverError.startsWith(CREDIT_ERROR_PREFIX);
    if (creditRefused && !changes['customerId'] && !changes['isOfflineMode']) {
      this.load();
    }
    if (changes['customerId'] || changes['dueAmount'] || creditRefused) {
      this.clearApproval();
    }
  }

  onReasonChange(value: string): void {
    this.reason.set(value);
  }

  onApprove(): void {
    const status = this.status();
    const reason = this.reason().trim();
    if (!status || !this.canOverride || reason.length === 0 || reason.length > 500) {
      return;
    }
    this.approved.set(true);
    this.overrideChange.emit({ reason, observedExposure: status.currentExposure });
  }

  onWithdraw(): void {
    this.clearApproval();
  }

  private clearApproval(): void {
    if (this.approved()) {
      this.approved.set(false);
      this.overrideChange.emit(null);
    }
  }

  private load(): void {
    this.request?.unsubscribe();
    this.status.set(null);
    if (!this.customerId || this.isOfflineMode) {
      return;
    }
    this.request = this.customerService.getCustomerCredit(this.customerId).subscribe({
      next: (status) => this.status.set(status),
      // A preview that cannot load hides itself; the server still decides at posting.
      error: () => this.status.set(null),
    });
  }
}
