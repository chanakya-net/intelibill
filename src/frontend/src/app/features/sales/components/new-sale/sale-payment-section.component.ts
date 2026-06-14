import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ButtonModule } from 'primeng/button';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TranslocoPipe } from '@ngneat/transloco';

import { CreditNoteVerifyResponseDto, PaymentMethod } from '../../../../features/sales/services/sale.models';

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
    InputTextModule,
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
  @Input() showDueAmount = false;
  @Input() dueAmountDisabled = false;
  @Input() appliedCreditNoteAmount = 0;
  @Input() maxAppliedCreditNoteAmount = 0;

  // Credit note verification inputs
  @Input() creditNoteCode = '';
  @Input() isCreditNoteVerifying = false;
  @Input() verifiedCreditNote: CreditNoteVerifyResponseDto | null = null;
  @Input() creditNoteError = '';

  @Output() methodChanged = new EventEmitter<PaymentMethod>();
  @Output() paidAmountChanged = new EventEmitter<number | null>();
  @Output() dueAmountChanged = new EventEmitter<number | null>();
  @Output() creditNoteAppliedAmountChanged = new EventEmitter<number | null>();
  @Output() creditNoteRemovalRequested = new EventEmitter<void>();

  // Credit note verification outputs
  @Output() creditNoteCodeChanged = new EventEmitter<string>();
  @Output() creditNoteVerifyRequested = new EventEmitter<void>();

  onMethodChange(value: PaymentMethod): void {
    this.methodChanged.emit(value);
  }

  onPaidAmountChange(value: number | null): void {
    this.paidAmountChanged.emit(value);
  }

  onDueAmountChange(value: number | null): void {
    this.dueAmountChanged.emit(value);
  }

  onCreditNoteCodeChange(value: string): void {
    this.creditNoteCodeChanged.emit(value);
  }

  onCreditNoteVerifyClick(): void {
    this.creditNoteVerifyRequested.emit();
  }

  onCreditNoteAppliedAmountChange(value: number | null): void {
    this.creditNoteAppliedAmountChanged.emit(value);
  }

  onCreditNoteRemoveClick(): void {
    this.creditNoteRemovalRequested.emit();
  }
}
