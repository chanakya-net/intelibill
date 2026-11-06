import { NewSalePageCartSelectionService } from './new-sale-page.cart-selection.service';
import type {
  InstantDiscountRequest,
  RecordSaleItemRequest,
  RecordSaleRequest,
} from '../services/sale.models';
import { CartItem } from '../services/sale-cart-state.service';
import { goodsSellableToBatch } from '../utils/sellable-goods.mapper';
import type { CheckoutIntent } from '../../../core/storage/checkout-intent-indexeddb.service';

export abstract class NewSalePageCartSubmitService extends NewSalePageCartSelectionService {
  private isPreparingCheckout = false;

  /**
   * Adds the picked sellable with the picker's quantity exactly as entered. A quantity its unit cannot hold (or more
   * than the batch's stock) adds nothing and says why; it is never truncated or rounded to fit.
   */
  onAddToCart(): void {
    const sellable = this.selectedSellable();
    if (!sellable) return;

    const qty = Number(this.batchPickerForm.controls.quantity.value);

    if (sellable.kind === 'Goods') {
      const batch = goodsSellableToBatch(sellable);
      const result = this.cartState.addBatchToCart(batch, qty);
      if (!result.added) {
        const refusal = result.refusal ?? 'exceedsStock';
        this.batchPickerForm.controls.quantity.setErrors({ [refusal]: true });
        this.showQuantityRefusal(refusal, sellable);
        return;
      }

      if (result.addedLineKey && !this.isOfflineMode()) {
        this.applyProductDefaultsForLine(result.addedLineKey, batch.itemName, batch.barcode);
      }
    } else {
      // A service line is whole units: the quantity is checked before the line exists, so a refused one adds nothing.
      if (!Number.isInteger(qty) || qty <= 0) {
        const refusal = qty > 0 ? 'tooPrecise' : 'notPositive';
        this.batchPickerForm.controls.quantity.setErrors({ [refusal]: true });
        this.showQuantityRefusal(refusal, { serviceName: sellable.name, quantityScale: 0 });
        return;
      }
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
    if (value === null || value === undefined) {
      return;
    }
    this.reportQuantityChange(
      this.cartState.setServiceQuantity(clientLineKey, value),
      this.serviceCart().find((item) => item.clientLineKey === clientLineKey),
    );
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
    if (this.isPreparingCheckout) return;
    if (this.cart().length === 0 && this.serviceCart().length === 0) return;

    // This cart already left as a checkout whose outcome is unknown. It must be reconciled or
    // retried as that same checkout — never resent under a new key, nor billed offline.
    if (this.activeCheckoutIntentId()) {
      this.paymentSplitError.set('sales.newSale.pendingCheckout.blocksSubmit');
      return;
    }
    if (this.customerForm.invalid || this.paymentForm.invalid) {
      this.customerForm.markAllAsTouched();
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

    this.isPreparingCheckout = true;
    try {
      const intent = await this.checkoutIntents.prepare(this.checkoutScope(), request);
      this.activeCheckoutIntentId.set(intent.intentId);
      this.pendingCheckouts.update((intents) => [...intents, intent]);
      this.salesFacade.recordSale(intent.request, intent.intentId);
    } catch {
      // Not durably stored, so not sent: the cart stays editable and nothing reached the server.
      this.paymentSplitError.set('sales.newSale.pendingCheckout.persistFailed');
    } finally {
      this.isPreparingCheckout = false;
    }
  }

  async refreshPendingCheckouts(): Promise<void> {
    const scope = this.checkoutScope();
    let unresolved: readonly CheckoutIntent[];
    try {
      unresolved = await this.checkoutIntents.loadUnresolved(scope);
    } catch {
      return;
    }
    const current = this.checkoutScope();
    if (current.userId !== scope.userId || current.shopId !== scope.shopId) {
      return;
    }

    this.pendingCheckouts.set(unresolved);
    const activeId = this.activeCheckoutIntentId();
    if (activeId && !unresolved.some((intent) => intent.intentId === activeId)) {
      await this.settleDetachedActiveCheckout(activeId);
    }
    if (!this.activeCheckoutIntentId()) {
      const restored = this.findIntentForCurrentCart(unresolved);
      if (restored) {
        this.activeCheckoutIntentId.set(restored.intentId);
      }
    }
  }

  onRetryCheckout(intentId: string): void {
    const intent = this.pendingCheckouts().find((candidate) => candidate.intentId === intentId);
    if (!intent || this.isSubmitting()) return;
    this.paymentSplitError.set('');
    this.salesFacade.recordSale(intent.request, intent.intentId);
  }

  onReconcileCheckout(intentId: string): void {
    if (this.isReconcilingCheckout()) return;
    this.isReconcilingCheckout.set(true);
    this.paymentSplitError.set('');
    this.checkoutIntents.recover(this.checkoutScope(), intentId).subscribe({
      next: (recovery) => {
        this.isReconcilingCheckout.set(false);
        if (recovery.kind === 'replayed') {
          // The success effect refreshes the list and clears the cart once the sale is shown.
          this.salesFacade.checkoutRecovered(recovery.sale, intentId);
          return;
        }
        if (recovery.kind === 'pending') {
          this.paymentSplitError.set('sales.newSale.pendingCheckout.stillPending');
        }
        void this.refreshPendingCheckouts();
      },
      error: () => {
        this.isReconcilingCheckout.set(false);
        this.paymentSplitError.set('sales.newSale.pendingCheckout.stillPending');
      },
    });
  }

  /** Detaches the cart from its unresolved checkout and starts a fresh draft; the checkout stays recoverable. */
  onStartNewDraft(): void {
    this.activeCheckoutIntentId.set(null);
    this.resetTransientState();
  }

  getPendingCheckoutAmount(intent: CheckoutIntent): number {
    const request = intent.request;
    return this.roundAmount(request.paidAmount + request.dueAmount + (request.creditNoteAppliedAmount ?? 0));
  }

  /**
   * The cart's checkout left the unresolved set without this tab seeing its answer. A rejection
   * frees the cart for editing; a confirmation (e.g. sent from another tab) is fetched and shown
   * like any success, so the cart is cleared rather than submitted again.
   */
  private async settleDetachedActiveCheckout(activeId: string): Promise<void> {
    if (this.lastRecordedIntentId() === activeId || this.isReconcilingCheckout()) {
      return;
    }
    const intent = await this.checkoutIntents.get(activeId).catch(() => null);
    if (this.activeCheckoutIntentId() !== activeId) {
      return;
    }
    const scope = this.checkoutScope();
    if (!intent || intent.status === 'rejected' || intent.userId !== scope.userId || intent.shopId !== scope.shopId) {
      this.activeCheckoutIntentId.set(null);
    } else if (intent.status === 'confirmed') {
      this.onReconcileCheckout(activeId);
    }
  }

  /** After a reload the cart is restored on its own; rebind it to the checkout its lines were frozen into. */
  private findIntentForCurrentCart(unresolved: readonly CheckoutIntent[]): CheckoutIntent | null {
    const cartKeys = [...this.cart(), ...this.serviceCart()].map((line) => line.clientLineKey).sort();
    if (cartKeys.length === 0) {
      return null;
    }
    const signature = cartKeys.join('|');
    for (let index = unresolved.length - 1; index >= 0; index--) {
      const intent = unresolved[index];
      const intentKeys = intent.request.items.map((item) => item.clientLineKey ?? '').sort();
      if (intentKeys.join('|') === signature) {
        return intent;
      }
    }
    return null;
  }

}
