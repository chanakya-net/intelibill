import { Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import { Supplier } from '../services/supplier.service';
import { SuppliersFacade } from '../state/suppliers.facade';
import type { MakePaymentRequest } from '../services/supplier-ledger.service';
import { SupplierPaymentFormComponent } from './supplier-detail/supplier-payment-form.component';

@Component({
  selector: 'app-make-payment-overlay',
  standalone: true,
  imports: [SupplierPaymentFormComponent, TranslocoPipe],
  templateUrl: './make-payment-overlay.component.html',
  styleUrl: './make-payment-overlay.component.scss',
})
export class MakePaymentOverlayComponent implements OnInit {
  private readonly suppliersFacade = inject(SuppliersFacade);

  readonly isSubmitting = this.suppliersFacade.isSubmitting;
  readonly serverError = this.suppliersFacade.errorMessage;

  @Input({ required: true }) supplier!: Supplier;
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly today = new Date();

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

  onPaymentSubmitted(payload: MakePaymentRequest): void {
    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
    this.suppliersFacade.makePayment(this.supplier.supplierId, payload);
  }
}
