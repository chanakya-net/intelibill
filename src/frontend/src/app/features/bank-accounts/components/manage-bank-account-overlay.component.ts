import { Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { BankAccount } from '../services/bank-account.service';
import { BankAccountsFacade } from '../state/bank-accounts.facade';
import { BankAccountFormComponent } from '../../../shared/components/bank-account-form/bank-account-form.component';
import { InputValidators } from '../../../shared/forms/input-validation';

@Component({
  selector: 'app-manage-bank-account-overlay',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    ButtonModule,
    ProgressSpinnerModule,
    TranslocoPipe,
    BankAccountFormComponent,
  ],
  templateUrl: './manage-bank-account-overlay.component.html',
  styleUrl: './manage-bank-account-overlay.component.scss',
})
export class ManageBankAccountOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly bankAccountsFacade = inject(BankAccountsFacade);

  readonly isSubmitting = this.bankAccountsFacade.isSubmitting;
  readonly serverError = this.bankAccountsFacade.errorMessage;

  @Input() bankAccount: BankAccount | null = null;
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    bankName: ['', InputValidators.requiredText(100)],
    accountNumber: ['', InputValidators.requiredText(50)],
    accountType: ['', InputValidators.requiredText()],
    ifscCode: ['', InputValidators.ifscCode()],
    accountHolderName: ['', InputValidators.optionalText(100)],
  });

  ngOnInit(): void {
    this.bankAccountsFacade.clearError();
    this.bankAccountsFacade.clearMutationStatus();

    if (this.bankAccount) {
      this.form.patchValue({
        bankName: this.bankAccount.bankName,
        accountNumber: this.bankAccount.accountNumber,
        accountType: this.bankAccount.accountType ?? '',
        ifscCode: this.bankAccount.ifscCode ?? '',
        accountHolderName: this.bankAccount.accountHolderName ?? '',
      });
    }
  }

  onClose(): void {
    if (this.isSubmitting()) {
      return;
    }
    this.closeRequested.emit();
  }

  onSubmit(): void {
    if (this.isSubmitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const payload = {
      bankName: this.form.controls.bankName.value.trim(),
      accountNumber: this.form.controls.accountNumber.value.trim(),
      accountType: this.form.controls.accountType.value,
      ifscCode: this.nullableTrimmed(this.form.controls.ifscCode.value),
      accountHolderName: this.nullableTrimmed(this.form.controls.accountHolderName.value),
    };

    if (this.bankAccount) {
      this.bankAccountsFacade.updateBankAccount(this.bankAccount.id, payload);
    } else {
      this.bankAccountsFacade.addBankAccount(payload);
    }
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}
