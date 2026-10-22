import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { AbstractControl, FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { SelectModule } from 'primeng/select';
import { InputNumberModule } from 'primeng/inputnumber';
import { TranslocoPipe } from '@ngneat/transloco';

import { SalePreviewDto } from '../../../../features/sales/services/sale.models';

@Component({
  selector: 'app-cart-checkout-summary',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, TagModule, SelectModule, InputNumberModule, TranslocoPipe],
  templateUrl: './cart-checkout-summary.component.html',
  styleUrl: './cart-checkout-summary.component.scss',
})
export class CartCheckoutSummaryComponent {
  @Input() preview: SalePreviewDto | null = null;
  @Input() loading = false;
  @Input() totalAmount = 0;
  @Input() subtotalAmount = 0;
  @Input() totalTaxAmount = 0;
  @Input() totalDiscountAmount = 0;
  @Input() saleDiscountType: 0 | 1 | 2 = 0;
  @Input() saleDiscountValue = 0;
  @Input() isSaleDiscountEligible = false;
  @Input() isDiscountEditorOpen = false;
  @Input() instantDiscountTypeOptions: { value: 0 | 1 | 2; labelKey: string }[] = [];
  @Input() saleDiscountError = '';
  @Input() balanceDue = 0;
  @Input() paymentSplitError = '';
  @Input() isSubmitting = false;
  @Input() cartLength = 0;
  @Input() customerForm: AbstractControl | null = null;
  @Input() paymentForm: AbstractControl | null = null;

  @Output() saleDiscountEditorToggled = new EventEmitter<void>();
  @Output() saleDiscountTypeChanged = new EventEmitter<0 | 1 | 2>();
  @Output() saleDiscountValueChanged = new EventEmitter<number>();
  @Output() submitRequested = new EventEmitter<void>();

  get formInvalid(): boolean {
    return !!this.customerForm?.invalid || !!this.paymentForm?.invalid;
  }

  get configuredSaleDiscountPercent(): number | null {
    return this.preview?.configuredSaleRule?.percentage ?? null;
  }

  isDiscountEditorEnabled(): boolean {
    if (this.loading) {
      return false;
    }

    return !this.preview || this.isSaleDiscountEligible;
  }

  get computedSubtotal(): number {
    if (!this.preview) {
      return this.subtotalAmount;
    }

    return this.preview.totalAmount - this.preview.totalTaxAmount;
  }

  toggleEditor(): void {
    this.saleDiscountEditorToggled.emit();
  }

  onDiscountTypeChanged(value: 0 | 1 | 2 | null): void {
    this.saleDiscountTypeChanged.emit((value ?? 0) as 0 | 1 | 2);
  }

  onDiscountValueChanged(value: number | null): void {
    this.saleDiscountValueChanged.emit(Number(value ?? 0));
  }

  onSubmit(): void {
    this.submitRequested.emit();
  }
}
