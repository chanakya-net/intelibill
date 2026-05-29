import { effect, runInInjectionContext } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { NewSalePageStateService } from './new-sale-page.state.service';
import { SalePreviewDto } from '../services/sale.models';

export abstract class NewSalePageLifecycleService extends NewSalePageStateService {
  onInit(): void {
    if (this.initialized) {
      return;
    }
    this.initialized = true;

    const offlineClockId = setInterval(() => {
      this.nowTick.set(Date.now());
    }, this.offlineClockIntervalMs);
    this.destroyRef.onDestroy(() => {
      clearInterval(offlineClockId);
    });

    this.customersFacade.loadCustomers();
    this.salesFacade.clearError();
    this.salesFacade.clearMutationStatus();

    this.customerForm.controls.customerName.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => {
        const typedName = value.trim().toLowerCase();
        const selectedName = this.selectedCustomerName();

        if (!typedName || !selectedName || typedName !== selectedName) {
          this.selectedCustomerId.set(null);
          this.selectedCustomerName.set(null);
          this.selectedCustomer.set(null);
          if (!this.isOfflineMode()) {
            this.enforceNoCustomerCreditRestrictions();
          }
        }
      });

    this.paymentForm.controls.paidAmount.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => {
        if (this.isSyncingPaymentControls) {
          return;
        }

        this.lastEditedPaymentField.set('paid');
        this.syncPaymentSplitFromPaid(value);
      });

    this.paymentForm.controls.dueAmount.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => {
        if (this.isSyncingPaymentControls) {
          return;
        }

        this.lastEditedPaymentField.set('due');
        this.syncPaymentSplitFromDue(value);
      });

    const handlePreviewApplied = (preview: SalePreviewDto, oldPreview: SalePreviewDto | null): void => {
      this.detectAndHighlightChangedRows(oldPreview, preview);
      this.revalidateDiscountsAgainstPreview();
    };

    this.salePreview.refreshOnlinePreview({
      destroyRef: this.destroyRef,
      getCart: () => this.cart(),
      getServiceCart: () => this.serviceCart(),
      getSaleDiscount: () => ({ type: this.saleDiscountType(), value: this.saleDiscountValue() }),
      onPreviewApplied: handlePreviewApplied,
    });

    this.salePreview.refreshOnServerUpdate({
      destroyRef: this.destroyRef,
      getCart: () => this.cart(),
      getServiceCart: () => this.serviceCart(),
      getSaleDiscount: () => ({ type: this.saleDiscountType(), value: this.saleDiscountValue() }),
      onPreviewApplied: handlePreviewApplied,
    });

    // Subscribe to server updates and trigger immediate preview refresh (no debounce)
    this.shopUpdatesService.updates$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((update) => {
        const cart = this.cart();
        const serviceCart = this.serviceCart();
        if (cart.length === 0 && serviceCart.length === 0) {
          return;
        }

        // Show notification for incoming update
        this.updateNotificationText.set(`sales.newSale.shopUpdate.${this.getEventNotificationKey(update.eventType)}`);
        this.showUpdateNotification.set(true);
        setTimeout(() => this.showUpdateNotification.set(false), 2000);

        // Immediately trigger preview refresh for server updates
        this.salePreview.triggerServerUpdateRefresh();
      });

    // Connect to shop updates on initialization
    void this.shopUpdatesService.startConnection();

    runInInjectionContext(this.injector, () => {
      effect(() => {
        if (this.lastMutationSucceeded()) {
          const type = this.lastMutationType();
          if (type === 'record-sale') {
            this.showConfirmation.set(true);
            this.resetTransientState();
            return;
          } else {
            this.salesFacade.clearMutationStatus();
            this.router.navigate(['/sales']);
          }
        }
      });

      effect(() => {
        const total = this.totalAmount();
        if (this.lastEditedPaymentField() === 'paid') {
          this.syncPaymentSplitFromPaid(this.paymentForm.controls.paidAmount.value, total);
        } else {
          this.syncPaymentSplitFromDue(this.paymentForm.controls.dueAmount.value, total);
        }
      });

      effect(() => {
        const dueControl = this.paymentForm.controls.dueAmount;

        if (this.isOfflineMode() || this.canUseCredit()) {
          if (dueControl.disabled) {
            dueControl.enable({ emitEvent: false });
          }
          return;
        }

        this.enforceNoCustomerCreditRestrictions();
      });

      effect(() => {
        this.activeShopId();
        void this.cartState.loadPersistedCart(this.cartRetentionMs, (row) => {
          this.applyProductDefaultsForLine(row.clientLineKey, row.itemName, row.barcode);
        });
      });

      effect(() => {
        this.activeShopId();
        this.cart();
        this.serviceCart();
        if (!this.cartBootstrapped()) {
          return;
        }

        void this.cartState.persistCart();
      });

      // Load device settings and snapshot info reactively
      effect(() => {
        this.activeShopId();
        void this.offlineSaleState.refreshSnapshot();
      });

      // Schedule preview whenever cart changes (after bootstrap). Item-level discount
      // edits always go through cart mutations, so they reach this effect automatically.
      effect(() => {
        const cart = this.cart();
        const serviceCart = this.serviceCart();
        this.isOfflineMode();
        this.isOfflineEligible();
        this.snapshotCompletedAt();
        this.saleDiscountType();
        this.saleDiscountValue();
        if (!this.cartBootstrapped()) {
          return;
        }
        const hasOverrideErrors = this.revalidateLineOverrides(cart);
        if (cart.length === 0 && serviceCart.length === 0) {
          this.salePreview.clearPreviewState();
          return;
        }
        if (hasOverrideErrors) {
          this.salePreview.markPreviewInvalid('sales.newSale.overrides.invalid');
          return;
        }
        this.salePreview.clearPreviewError();
        if (this.isOfflineMode()) {
          if (this.isOfflineEligible()) {
            void this.refreshOfflinePreview(cart);
          } else {
            this.salePreview.clearPreviewState();
          }
          return;
        }
        this.salePreview.triggerPreview();
      });
    });
  }

  onDestroy(): void {
    void this.shopUpdatesService.stopConnection();
  }

  async onBarcodeSearch(): Promise<void> {
    const searchTerm = this.searchInput().trim();
    if (!searchTerm) return;

    if (this.isOfflineMode() && this.isOfflineEligible()) {
      await this.searchOfflineCatalog(searchTerm);
      return;
    }

    this.batchSearchError.set('');
    this.isSearchingBatches.set(true);
    this.availableBatches.set([]);
    this.availableSellables.set([]);

    this.saleService.getSellables(searchTerm).subscribe({
      next: (sellables) => {
        this.isSearchingBatches.set(false);
        if (sellables.length === 0) {
          this.batchSearchError.set('sales.newSale.noBatchesFound');
          return;
        }
        if (sellables.length === 1) {
          const sellable = sellables[0];
          if (sellable.kind === 'Goods') {
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
            const result = this.cartState.addBatchToCart(batch, 1);
            if (!result.added) {
              this.batchSearchError.set('sales.newSale.exceedsStock');
              return;
            }
            if (result.addedLineKey) {
              this.applyProductDefaultsForLine(result.addedLineKey, batch.itemName, batch.barcode);
            }
          } else {
            this.cartState.addServiceToCart(sellable);
          }
          this.resetSearchAndPickerState();
        } else {
          const allGoods = sellables.every((s) => s.kind === 'Goods');
          if (allGoods) {
            this.availableBatches.set(
              sellables
                .filter((s): s is import('../services/sale.models').SellableGoodsDto => s.kind === 'Goods')
                .map((s) => ({
                  barcode: s.barcode,
                  itemName: s.itemName,
                  batchNumber: s.batchNumber,
                  inventoryBatchId: s.inventoryBatchId,
                  quantity: s.quantity,
                  salesPrice: s.salesPrice,
                  mrp: s.mrp,
                  taxRatePercent: s.taxRatePercent,
                  taxIncluded: s.taxIncluded,
                  expiryDate: s.expiryDate,
                  hsnCode: s.hsnCode,
                })),
            );
          } else {
            this.availableSellables.set(sellables);
          }
          this.showBatchPicker.set(true);
        }
      },
      error: (err) => {
        this.isSearchingBatches.set(false);
        this.batchSearchError.set((err as { error?: { detail?: string } }).error?.detail || 'sales.newSale.searchError');
      },
    });
  }

}
