import { NewSalePageCartSelectionService } from './new-sale-page.cart-selection.service';
import type {
  InstantDiscountRequest,
  RecordSaleItemRequest,
  RecordSaleRequest,
} from '../services/sale.models';
import { CartItem } from '../services/sale-cart-state.service';

export abstract class NewSalePageCartSubmitService extends NewSalePageCartSelectionService {
  onAddToCart(): void {
    if (this.batchPickerForm.invalid) {
      this.batchPickerForm.markAllAsTouched();
      return;
    }
    const batch = this.selectedBatch();
    if (!batch) return;

    const qty = this.batchPickerForm.controls.quantity.value;
    if (qty > batch.quantity) {
      this.batchPickerForm.controls.quantity.setErrors({ exceedsStock: true });
      return;
    }

    const result = this.cartState.addBatchToCart(batch, qty);
    if (!result.added) {
      this.batchPickerForm.controls.quantity.setErrors({ exceedsStock: true });
      this.batchSearchError.set('sales.newSale.exceedsStock');
      return;
    }

    if (result.addedLineKey && !this.isOfflineMode()) {
      this.applyProductDefaultsForLine(result.addedLineKey, batch.itemName, batch.barcode);
    }

    this.resetSearchAndPickerState();
  }

  onIncreaseCartItem(index: number): void {
    this.cartState.onIncreaseCartItem(index);
  }

  onDecreaseCartItem(index: number): void {
    this.cartState.onDecreaseCartItem(index);
  }

  canIncreaseCartItem(item: CartItem): boolean {
    return this.cartState.canIncreaseCartItem(item);
  }

  onRemoveCartItem(index: number): void {
    this.cartState.onRemoveCartItem(index);
  }

  onClearCart(): void {
    this.cartState.onClearCart();
  }

  hasTax(item: CartItem): boolean {
    return this.cartState.hasTax(item);
  }

  getLineSubtotal(item: CartItem): number {
    return this.cartState.getLineSubtotal(item);
  }

  getLineTaxAmount(item: CartItem): number {
    return this.cartState.getLineTaxAmount(item);
  }

  getLineTotal(item: CartItem): number {
    return this.cartState.getLineTotal(item);
  }

  getUnitSubtotal(item: CartItem): number {
    return this.cartState.getUnitSubtotal(item);
  }

  getUnitTaxAmount(item: CartItem): number {
    return this.cartState.getUnitTaxAmount(item);
  }

  getUnitFinalPrice(item: CartItem): number {
    return this.cartState.getUnitFinalPrice(item);
  }

  async onSubmit(): Promise<void> {
    if (this.isSubmitting()) return;
    if (this.isOfflineSubmitting()) return;
    if (this.cart().length === 0) return;
    if (this.paymentForm.invalid) {
      this.paymentForm.markAllAsTouched();
      return;
    }

    this.paymentSplitError.set('');

    // OFFLINE BRANCH
    if (this.isOfflineMode()) {
      if (this.isOfflineEligible()) {
        const blockingErrorKey = this.getBlockingCartValidationErrorKey(this.cart());
        if (blockingErrorKey) {
          this.paymentSplitError.set(blockingErrorKey);
          return;
        }
        await this.onOfflineSubmit();
      } else {
        this.paymentSplitError.set('sales.newSale.offline.blockDeviceNotEnabled');
      }
      return;
    }

    if (this.checkoutPreview() === null) {
      this.paymentSplitError.set(this.previewError() || 'sales.newSale.previewRequired');
      return;
    }

    const blockingErrorKey = this.getBlockingCartValidationErrorKey(this.cart());
    if (blockingErrorKey) {
      this.paymentSplitError.set(blockingErrorKey);
      return;
    }

    const items: RecordSaleItemRequest[] = this.cart().map((item) => ({
      barcode: item.barcode,
      batchNumber: item.batchNumber,
      itemName: item.itemName,
      quantity: item.quantity,
      costPrice: item.costPrice,
      salesPrice: item.salesPrice,
      mrp: item.mrp,
      taxRatePercent: item.taxRatePercent,
      isPriceIncludingTax: item.taxIncluded,
      inventoryBatchId: item.inventoryBatchId,
      clientLineKey: item.clientLineKey,
      itemDiscount: { type: item.itemDiscountType as 0 | 1 | 2, value: item.itemDiscountValue } satisfies InstantDiscountRequest,
      hsnCode: item.hsnCode ?? null,
    }));

    const customerName = this.customerForm.controls.customerName.value.trim() || null;
    const customerPhone = this.customerForm.controls.customerPhone.value.trim() || null;
    const paidAmount = this.toFiniteAmount(this.paymentForm.controls.paidAmount.value);
    const dueAmount = this.toFiniteAmount(this.paymentForm.controls.dueAmount.value);
    const totalAmount = this.roundAmount(this.totalAmount());

    if (
      !Number.isFinite(paidAmount) ||
      !Number.isFinite(dueAmount) ||
      paidAmount < 0 ||
      dueAmount < 0 ||
      !this.areAmountsEqual(paidAmount + dueAmount, totalAmount)
    ) {
      this.paymentSplitError.set('sales.newSale.invalidPaymentSplit');
      return;
    }

    if (dueAmount > 0 && !this.selectedCustomerId()) {
      this.paymentSplitError.set('sales.newSale.customerRequiredForDue');
      return;
    }

    if (this.paymentForm.controls.paymentMethod.value === 4 && !this.canUseCredit()) {
      this.paymentSplitError.set('sales.newSale.creditRequiresCustomerPhone');
      return;
    }

    const request: RecordSaleRequest = {
      idempotencyKey: this.createSaleIdempotencyKey(),
      customerId: this.selectedCustomerId(),
      customerName,
      customerPhone,
      paymentMethod: this.paymentForm.controls.paymentMethod.value,
      paidAmount,
      dueAmount,
      items,
      saleDiscount: { type: this.saleDiscountType(), value: this.saleDiscountValue() },
    };

    this.salesFacade.recordSale(request);
  }

}
