import { NewSalePageDiscountValidationService } from './new-sale-page.discount-validation.service';
import { SalePreviewDto, SalePreviewLineDto } from '../services/sale.models';

export abstract class NewSalePagePaymentFlowService extends NewSalePageDiscountValidationService {
  syncPaymentSplitFromPaid(rawPaid: number | null, total = this.totalAmount()): void {
    const paidControl = this.paymentForm.controls.paidAmount;
    const dueControl = this.paymentForm.controls.dueAmount;
    const normalizedPaid = this.normalizeAmount(rawPaid, total);
    const normalizedDue = this.roundAmount(total - normalizedPaid);

    this.isSyncingPaymentControls = true;
    try {
      if (!this.areAmountsEqual(Number(paidControl.value ?? 0), normalizedPaid)) {
        paidControl.setValue(normalizedPaid, { emitEvent: false });
      }

      if (!this.areAmountsEqual(Number(dueControl.value ?? 0), normalizedDue)) {
        dueControl.setValue(normalizedDue, { emitEvent: false });
      }
    } finally {
      this.isSyncingPaymentControls = false;
    }
  }

  syncPaymentSplitFromDue(rawDue: number | null, total = this.totalAmount()): void {
    const paidControl = this.paymentForm.controls.paidAmount;
    const dueControl = this.paymentForm.controls.dueAmount;
    const normalizedDue = this.normalizeAmount(rawDue, total);
    const normalizedPaid = this.roundAmount(total - normalizedDue);

    this.isSyncingPaymentControls = true;
    try {
      if (!this.areAmountsEqual(Number(dueControl.value ?? 0), normalizedDue)) {
        dueControl.setValue(normalizedDue, { emitEvent: false });
      }

      if (!this.areAmountsEqual(Number(paidControl.value ?? 0), normalizedPaid)) {
        paidControl.setValue(normalizedPaid, { emitEvent: false });
      }
    } finally {
      this.isSyncingPaymentControls = false;
    }
  }

  normalizeAmount(value: number | null | undefined, total: number): number {
    return this.roundAmount(Math.max(0, Math.min(Number(value ?? 0), total)));
  }

  toFiniteAmount(value: number | null | undefined): number {
    const amount = Number(value ?? 0);
    return Number.isFinite(amount) ? this.roundAmount(amount) : Number.NaN;
  }

  roundAmount(value: number): number {
    return Number(value.toFixed(2));
  }

  areAmountsEqual(left: number, right: number): boolean {
    return this.roundAmount(left) === this.roundAmount(right);
  }

  enforceNoCustomerCreditRestrictions(): void {
    const dueControl = this.paymentForm.controls.dueAmount;

    if (this.paymentForm.controls.paymentMethod.value === 4) {
      this.paymentForm.controls.paymentMethod.setValue(1);
    }

    if (Number(this.paymentForm.getRawValue().dueAmount ?? 0) !== 0) {
      this.lastEditedPaymentField.set('due');
      this.syncPaymentSplitFromDue(0);
    }

    if (dueControl.enabled) {
      dueControl.disable({ emitEvent: false });
    }
  }

  resetSearchAndPickerState(): void {
    this.showBatchPicker.set(false);
    this.selectedSellable.set(null);
    this.searchInput.set('');
    this.batchPickerForm.reset({ batchNumber: '', quantity: 1 });
    this.availableBatches.set([]);
    this.availableSellables.set([]);
    this.batchSearchError.set('');
  }

  applyProductDefaultsForLine(clientLineKey: string, itemName: string, barcode: string): void {
    const normalizedName = itemName?.trim();
    const normalizedBarcode = barcode?.trim();
    if (!normalizedName && !normalizedBarcode) {
      return;
    }

    this.inventoryService.getProductDetailsByNameOrBarcode(normalizedName, normalizedBarcode).subscribe({
      next: (details) => {
        const resolvedHsn = this.normalizeHsnCode(details.hsnCode);
        if (!resolvedHsn) {
          return;
        }
        this.cartState.applyResolvedHsnCodeIfMissing(clientLineKey, resolvedHsn);
      },
      error: () => undefined,
    });
  }

  getEventNotificationKey(eventType: string): string {
    const keyMap: Record<string, string> = {
      'PricingChanged': 'pricingUpdated',
      'ItemApplicabilityChanged': 'itemUpdated',
      'DiscountRuleChanged': 'discountUpdated',
      'InventoryBatchVoided': 'inventoryUpdated',
    };
    return keyMap[eventType] ?? 'updated';
  }

  detectAndHighlightChangedRows(
    oldPreview: SalePreviewDto | null,
    newPreview: SalePreviewDto
  ): void {
    const highlightedKeys = new Set<string>();

    if (!oldPreview) {
      // If no previous preview, highlight all rows
      newPreview.lines.forEach((line) => {
        if (line.clientLineKey) {
          highlightedKeys.add(line.clientLineKey);
        }
      });
    } else {
      // Compare each line with old preview and detect changes
      const oldLinesByKey = new Map<string, SalePreviewLineDto>();
      oldPreview.lines.forEach((line) => {
        if (line.clientLineKey) {
          oldLinesByKey.set(line.clientLineKey, line);
        }
      });

      newPreview.lines.forEach((newLine) => {
        if (!newLine.clientLineKey) return;

        const oldLine = oldLinesByKey.get(newLine.clientLineKey);
        if (!oldLine) {
          highlightedKeys.add(newLine.clientLineKey);
          return;
        }

        // Check if pricing or discount changed
        if (
          newLine.lineTotalAmount !== oldLine.lineTotalAmount ||
          newLine.itemDiscountAmount !== oldLine.itemDiscountAmount ||
          newLine.saleDiscountAmount !== oldLine.saleDiscountAmount ||
          newLine.salesPrice !== oldLine.salesPrice ||
          newLine.taxAmount !== oldLine.taxAmount
        ) {
          highlightedKeys.add(newLine.clientLineKey);
        }
      });
    }

    this.highlightedRowKeys.set(highlightedKeys);

    // Auto-clear highlights after 1.5 seconds (fade transition)
    setTimeout(() => {
      this.highlightedRowKeys.set(new Set());
    }, 1500);
  }

  resetTransientState(): void {
    this.cartState.onClearCart();
    this.searchInput.set('');
    this.customerNameSuggestions.set([]);
    this.customerForm.reset();
    this.selectedCustomerId.set(null);
    this.selectedCustomerName.set(null);
    this.paymentForm.reset({ paymentMethod: 1, paidAmount: 0, dueAmount: 0 });
    this.paymentSplitError.set('');
    this.salePreview.clearPreviewState();
  }

  onDone(): void {
    this.showConfirmation.set(false);
    this.salesFacade.clearLastRecordedSale();
    this.salesFacade.clearMutationStatus();
    this.router.navigate(['/sales']);
  }

  onOfflineDone(): void {
    this.showOfflineConfirmation.set(false);
    this.offlineConfirmation.set(null);
    this.router.navigate(['/sales']);
  }

}
