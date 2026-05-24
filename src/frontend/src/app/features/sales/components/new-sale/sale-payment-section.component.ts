import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ButtonModule } from 'primeng/button';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputNumberModule } from 'primeng/inputnumber';
import { SelectModule } from 'primeng/select';
import { TranslocoPipe } from '@ngneat/transloco';

import { PaymentMethod } from '../../../../features/sales/services/sale.models';

export interface PaymentMethodOption {
  readonly value: number;
  readonly label: PaymentMethod;
}

@Component({
  selector: 'app-sale-payment-section',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    InputGroupAddonModule,
    InputGroupModule,
    InputNumberModule,
    SelectModule,
    TranslocoPipe,
  ],
  templateUrl: './sale-payment-section.component.html',
})
export class SalePaymentSectionComponent {
  @Input() paymentMethods: PaymentMethodOption[] = [];
  @Input() selectedMethod: PaymentMethod = 'Cash';
  @Input() paidAmount = 0;
  @Input() dueAmount = 0;
  @Input() isOfflineMode = false;
  @Input() totalAmount = 0;
  @Input() currencyInputPt: any;
  @Input() currencyGroupPt: any;
  @Input() currencyAddonPt: any;
  @Input() canUseCredit = true;
  @Input() dueAmountDisabled = false;
  @Input() paymentSplitError = '';
  @Input() isSubmitting = false;
  @Input() cartLength = 0;

  @Output() methodChanged = new EventEmitter<PaymentMethod>();
  @Output() paidAmountChanged = new EventEmitter<number | null>();
  @Output() dueAmountChanged = new EventEmitter<number | null>();
  @Output() submitRequested = new EventEmitter<void>();

  onMethodChange(value: PaymentMethod): void {
    this.methodChanged.emit(value);
  }

  onPaidAmountChange(value: number | null): void {
    this.paidAmountChanged.emit(value);
  }

  onDueAmountChange(value: number | null): void {
    this.dueAmountChanged.emit(value);
  }

  onSubmit(): void {
    this.submitRequested.emit();
  }
}
