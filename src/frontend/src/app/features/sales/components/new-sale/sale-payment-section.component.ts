import type { LocalizedError } from '../../../../core/i18n/api-error-message.service';
import { LocaleInstantPipe } from '../../../../shared/pipes/locale-instant.pipe';
import { LocaleMoneyPipe } from '../../../../shared/pipes/locale-money.pipe';
import { TillPostingFieldsComponent } from '../../../tills/components/till-posting-fields.component';
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

import {
  AppliedCreditNote,
  CreditNoteVerifyResponseDto,
  PaymentMethod,
} from '../../../../features/sales/services/sale.models';

export interface PaymentMethodOption {
  readonly value: number;
  readonly label: PaymentMethod;
}

let paymentSectionSequence = 0;

@Component({
  selector: 'app-sale-payment-section',
  standalone: true,
  imports: [
    LocaleMoneyPipe,
    LocaleInstantPipe,
    TillPostingFieldsComponent,
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
  readonly controlPrefix = `sale-payment-${++paymentSectionSequence}`;
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
  @Input() paymentSplitError = '';
  @Input() localizedError?: LocalizedError;

  fieldError(field: string) {
    return this.localizedError?.fieldErrors.find((error) => error.field === field);
  }
  paymentDescriptions(field: string): string | undefined {
    return (
      [
        this.paymentSplitError ? this.controlPrefix + '-split-error' : '',
        this.fieldError(field) ? this.controlPrefix + '-' + field + '-server-error' : '',
      ]
        .filter(Boolean)
        .join(' ') || undefined
    );
  }
  @Input() isSubmitting = false;

  amountInputPt(field: string): any {
    return {
      ...this.currencyInputPt,
      pcInputText: {
        ...this.currencyInputPt?.pcInputText,
        root: {
          ...this.currencyInputPt?.pcInputText?.root,
          'aria-invalid': this.paymentSplitError || this.fieldError(field) ? 'true' : undefined,
        },
      },
    };
  }

  // Credit note verification inputs
  @Input() creditNoteCode = '';
  @Input() isCreditNoteVerifying = false;
  @Input() verifiedCreditNote: CreditNoteVerifyResponseDto | null = null;
  @Input() creditNoteError = '';
  @Input() canApplyCreditNote = false;
  @Input() appliedCreditNotes: readonly AppliedCreditNote[] = [];
  @Input() appliedCreditNotesTotal = 0;
  @Input() remainingPayableAmount = 0;
  @Input() creditNoteCustomerMismatchWarning = false;
  @Input() creditNoteCustomerMismatchConfirmed = false;

  @Output() methodChanged = new EventEmitter<PaymentMethod>();
  @Output() paidAmountChanged = new EventEmitter<number | null>();
  @Output() dueAmountChanged = new EventEmitter<number | null>();

  // Credit note verification outputs
  @Output() creditNoteCodeChanged = new EventEmitter<string>();
  @Output() creditNoteVerifyRequested = new EventEmitter<void>();
  @Output() creditNoteApplyRequested = new EventEmitter<void>();
  @Output() appliedCreditNoteAmountChanged = new EventEmitter<{
    creditNoteId: string;
    amount: number | null;
  }>();
  @Output() appliedCreditNoteRemoved = new EventEmitter<string>();
  @Output() creditNoteCustomerMismatchConfirmedChanged = new EventEmitter<boolean>();
  @Output() creditNoteCustomerMismatchCancelled = new EventEmitter<void>();

  onMethodChange(value: PaymentMethod): void {
    this.methodChanged.emit(value);
  }

  paymentMethodLabelKey(method: PaymentMethod): string {
    return `sales.newSale.paymentMethods.${method.toLowerCase()}`;
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

  onCreditNoteApplyClick(): void {
    this.creditNoteApplyRequested.emit();
  }

  onAppliedCreditNoteAmountChange(creditNoteId: string, amount: number | null): void {
    this.appliedCreditNoteAmountChanged.emit({ creditNoteId, amount });
  }

  getMaxAmountForAppliedCreditNote(note: AppliedCreditNote): number {
    return Math.min(note.availableBalance, this.remainingPayableAmount + note.amount);
  }

  onAppliedCreditNoteRemoveClick(creditNoteId: string): void {
    this.appliedCreditNoteRemoved.emit(creditNoteId);
  }

  onCreditNoteCustomerMismatchConfirmedChange(confirmed: boolean): void {
    this.creditNoteCustomerMismatchConfirmedChanged.emit(confirmed);
  }

  onCreditNoteCustomerMismatchCancelClick(): void {
    this.creditNoteCustomerMismatchCancelled.emit();
  }
}
