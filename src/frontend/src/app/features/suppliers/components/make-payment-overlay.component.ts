import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { Supplier } from '../services/supplier.service';
import { SuppliersFacade } from '../state/suppliers.facade';

@Component({
  selector: 'app-make-payment-overlay',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    InputNumberModule,
    InputTextModule,
    DatePickerModule,
    ButtonModule,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './make-payment-overlay.component.html',
  styleUrl: './make-payment-overlay.component.scss',
})
export class MakePaymentOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly suppliersFacade = inject(SuppliersFacade);

  readonly isSubmitting = this.suppliersFacade.isSubmitting;
  readonly serverError = this.suppliersFacade.errorMessage;

  @Input({ required: true }) supplier!: Supplier;
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly today = new Date();

  readonly form = this.formBuilder.nonNullable.group({
    amount: [null as number | null, [Validators.required, Validators.min(0.01)]],
    paymentDate: [new Date(), [Validators.required]],
    notes: ['', [Validators.maxLength(500)]],
  });

  ngOnInit(): void {
    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
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
    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
    this.suppliersFacade.makePayment(this.supplier.supplierId, {
      amount: this.form.controls.amount.value!,
      paymentDate: this.toIsoDateString(this.form.controls.paymentDate.value),
      notes: this.nullableTrimmed(this.form.controls.notes.value),
    });
  }

  private toIsoDateString(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}
