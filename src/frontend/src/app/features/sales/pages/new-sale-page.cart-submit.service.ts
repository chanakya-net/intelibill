import { NewSalePageCartSelectionService } from './new-sale-page.cart-selection.service';
import type {
  InstantDiscountRequest,
  RecordSaleItemRequest,
  RecordSaleRequest,
} from '../services/sale.models';
import { CartItem } from '../services/sale-cart-state.service';

export abstract class NewSalePageCartSubmitService extends NewSalePageCartSelectionService {
  onAddToCart(): void {
    const sellable = this.selectedSellable();
    if (!sellable) return;

    const qtyRaw = this.batchPickerForm.controls.quantity.value;
    const qty = Math.max(1, Math.trunc(Number(qtyRaw)));
    if (!Number.isFinite(qty)) {
      this.batchPickerForm.controls.quantity.setErrors({ invalid: true });
      return;
    }

    if (sellable.kind === 'Goods') {
      if (qty > sellable.quantity) {
        this.batchPickerForm.controls.quantity.setErrors({ exceedsStock: true });
        return;
      }

      const batch = {
        barcode: sellable.barcode,
        itemName: sellable.itemName,
        batchNumber: sellable.batchNumber,
        inventoryBatchId: sellable.inventoryBatchId,
        quantity: sellable.quantity,
        salesPrice: sellable.salesPrice,
        mrp: sellable.mrp,
        taxRatePercent: sellable.taxRatePercent,
        taxIncluded: sellable.taxIncluded,
        expiryDate: sellable.expiryDate,
        hsnCode: sellable.hsnCode,
      };

      const result = this.cartState.addBatchToCart(batch, qty);
      if (!result.added) {
        this.batchPickerForm.controls.quantity.setErrors({ exceedsStock: true });
        this.batchSearchError.set('sales.newSale.exceedsStock');
        return;
      }

      if (result.addedLineKey && !this.isOfflineMode()) {
        this.applyProductDefaultsForLine(result.addedLineKey, batch.itemName, batch.barcode);
      }
    } else {
      const lineKey = this.cartState.addServiceToCart(sellable);
      this.cartState.setServiceQuantity(lineKey, qty);
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

  onRemoveServiceItem(clientLineKey: string): void {
    this.cartState.onRemoveServiceItem(clientLineKey);
  }

  onServiceItemPriceChange(clientLineKey: string, value: number | null | undefined): void {
    const normalized = Number(value ?? 0);
    this.cartState.setServiceUnitPrice(clientLineKey, Number.isFinite(normalized) ? normalized : 0);
  }

  onServiceItemQuantityChange(clientLineKey: string, value: number | null | undefined): void {
    const normalized = Number(value ?? 1);
    this.cartState.setServiceQuantity(clientLineKey, Number.isFinite(normalized) ? normalized : 1);
  }

  onClearCart(): void {
    this.cartState.onClearCart();
    this.isSaleDiscountEditorOpen.set(false);
    this.saleDiscountType.set(0);
    this.saleDiscountValue.set(0);
    this.saleDiscountError.set('');
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
    if (this.cart().length === 0 && this.serviceCart().length === 0) return;
    if (this.paymentForm.invalid) {
      this.paymentForm.markAllAsTouched();
      return;
    }

    this.paymentSplitError.set('');

    // OFFLINE BRANCH
    if (this.isOfflineMode()) {
      if (this.isOfflineEligible()) {
        if (this.serviceCart().length > 0) {
          this.paymentSplitError.set('sales.newSale.offline.servicesNotSupported');
          return;
        }
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

    const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';
    const items: RecordSaleItemRequest[] = [
      ...this.cart().map((item) => ({
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
        lineType: 'Goods' as const,
      })),
      ...this.serviceCart().map((svc) => ({
        barcode: svc.serviceCode,
        batchNumber: '',
        itemName: svc.serviceName,
        quantity: svc.quantity,
        costPrice: 0,
        salesPrice: svc.unitPrice,
        mrp: svc.unitPrice,
        taxRatePercent: svc.taxRatePercent,
        isPriceIncludingTax: svc.taxIncluded,
        inventoryBatchId: EMPTY_GUID,
        clientLineKey: svc.clientLineKey,
        itemDiscount: null,
        hsnCode: svc.hsnCode,
        lineType: 'Service' as const,
        serviceId: svc.serviceId,
      })),
    ];

    const customerName = this.customerForm.controls.customerName.value.trim() || null;
    const customerPhone = this.customerForm.controls.customerPhone.value.trim() || null;
    const paidAmount = this.toFiniteAmount(this.paymentForm.controls.paidAmount.value);
    const dueAmount = this.toFiniteAmount(this.paymentForm.controls.dueAmount.value);
    const creditNoteAmount = this.totalAppliedCreditNoteAmount();
    const payableAmount = this.roundAmount(Math.max(0, this.totalAmount() - creditNoteAmount));

    if (
      !Number.isFinite(paidAmount) ||
      !Number.isFinite(dueAmount) ||
      paidAmount < 0 ||
      dueAmount < 0 ||
      !this.areAmountsEqual(paidAmount + dueAmount, payableAmount)
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

    if (this.creditNoteCustomerMismatchWarning() && !this.creditNoteCustomerMismatchConfirmed()) {
      this.paymentSplitError.set('sales.newSale.creditNote.customerMismatchConfirmRequired');
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
      creditNoteAppliedAmount: creditNoteAmount,
      creditNoteRedemptions: this.buildCreditNoteRedemptions(),
      creditNoteCustomerMismatchConfirmed: this.creditNoteCustomerMismatchConfirmed() || undefined,
    };

    this.salesFacade.recordSale(request);
  }

}
