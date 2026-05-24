import { NewSalePagePaymentFlowService } from './new-sale-page.payment-flow.service';
import { CartItem } from '../services/sale-cart-state.service';

export abstract class NewSalePageOfflineFlowService extends NewSalePagePaymentFlowService {
  async searchOfflineCatalog(term: string): Promise<void> {
    this.isSearchingBatches.set(true);
    this.batchSearchError.set('');
    this.availableBatches.set([]);
    this.showBatchPicker.set(false);

    try {
      const batches = await this.offlineSaleState.searchOfflineCatalog(term);
      if (batches.length === 0) {
        this.batchSearchError.set('sales.newSale.noBatchesFound');
        return;
      }

      this.availableBatches.set(batches);
      this.showBatchPicker.set(true);
    } catch {
      this.batchSearchError.set('sales.newSale.searchError');
    } finally {
      this.isSearchingBatches.set(false);
    }
  }

  async refreshOfflinePreview(cart: readonly CartItem[]): Promise<void> {
    const shopId = this.activeShopId();
    if (!shopId) {
      this.salePreview.clearPreviewState();
      return;
    }

    const requestId = this.salePreview.beginPreviewRequest();

    try {
      const preview = await this.offlineSaleState.buildOfflinePreview(cart, {
        paymentMethod: this.paymentForm.controls.paymentMethod.value,
        paidAmount: this.toFiniteAmount(this.paymentForm.controls.paidAmount.value),
        customerId: this.selectedCustomerId(),
        customerName: this.customerForm.controls.customerName.value.trim() || null,
        customerPhone: this.customerForm.controls.customerPhone.value.trim() || null,
        saleDiscountType: this.saleDiscountType(),
        saleDiscountValue: this.saleDiscountValue(),
      });
      this.salePreview.finishPreviewRequest(requestId, preview, {
        onPreviewApplied: (nextPreview, oldPreview) => {
          this.detectAndHighlightChangedRows(oldPreview, nextPreview);
          this.revalidateDiscountsAgainstPreview();
        },
      });
    } catch {
      this.salePreview.finishPreviewRequest(requestId, null, {
        failed: true,
        errorMessage: 'sales.newSale.previewError',
      });
    }
  }

  async onOfflineSubmit(): Promise<void> {
    if (this.isOfflineSubmitting()) return;

    this.isOfflineSubmitting.set(true);
    this.paymentSplitError.set('');

    try {
      const result = await this.offlineSaleState.submitOfflineSale({
        paymentMethod: this.paymentForm.controls.paymentMethod.value,
        paidAmount: this.toFiniteAmount(this.paymentForm.controls.paidAmount.value),
        dueAmount: this.toFiniteAmount(this.paymentForm.controls.dueAmount.value),
        totalAmount: this.roundAmount(this.totalAmount()),
        customerId: this.selectedCustomerId(),
        customerName: this.customerForm.controls.customerName.value.trim() || null,
        customerPhone: this.customerForm.controls.customerPhone.value.trim() || null,
        selectedCustomerId: this.selectedCustomerId(),
        saleDiscountType: this.saleDiscountType(),
        saleDiscountValue: this.saleDiscountValue(),
        lines: this.cart().map((item) => ({
          clientLineId: item.clientLineKey,
          inventoryBatchId: item.inventoryBatchId,
          itemId: item.inventoryBatchId,
          barcode: item.barcode,
          itemName: item.itemName,
          batchNumber: item.batchNumber,
          quantity: item.quantity,
          salesPrice: item.salesPrice,
          mrp: item.mrp,
          costPrice: item.costPrice,
          taxRatePercent: item.taxRatePercent,
          taxIncluded: item.taxIncluded,
          itemDiscount: { type: item.itemDiscountType as 0 | 1 | 2, value: item.itemDiscountValue },
          hsnCode: item.hsnCode ?? null,
        })),
      });

      if (result.ok) {
        this.offlineConfirmation.set({
          invoiceNumber: result.confirmation.invoiceNumber,
          grandTotal: result.confirmation.grandTotal,
          clientSaleId: result.confirmation.clientSaleId,
        });
        this.showOfflineConfirmation.set(true);
        this.resetTransientState();
        await this.offlineSaleState.refreshSnapshot();
      } else {
        this.paymentSplitError.set(result.errorKey);
      }
    } finally {
      this.isOfflineSubmitting.set(false);
    }
  }

  printA4(saleId: string): void {
    window.open(`/sales/${saleId}/print?template=a4`, '_blank');
  }

  printThermal(saleId: string): void {
    window.open(`/sales/${saleId}/print?template=thermal`, '_blank');
  }

  printOfflineA4(): void {
    const confirmation = this.offlineConfirmation();
    if (!confirmation) {
      return;
    }

    window.open(`/sales/${confirmation.clientSaleId}/print?template=a4&offline=1`, '_blank');
  }

  printOfflineThermal(): void {
    const confirmation = this.offlineConfirmation();
    if (!confirmation) {
      return;
    }

    window.open(`/sales/${confirmation.clientSaleId}/print?template=thermal&offline=1`, '_blank');
  }
}
