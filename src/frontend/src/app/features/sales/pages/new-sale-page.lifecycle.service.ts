import { effect, runInInjectionContext, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { NewSalePageStateService } from './new-sale-page.state.service';
import { sameOwner } from '../../../core/context/shop-scope';
import type { CheckoutScope } from '../services/checkout-intent.service';
import { SalePreviewDto } from '../services/sale.models';
import { goodsSellableToBatch } from '../utils/sellable-goods.mapper';

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

    const handlePreviewApplied = (
      preview: SalePreviewDto,
      oldPreview: SalePreviewDto | null,
    ): void => {
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
        this.updateNotificationText.set(
          `sales.newSale.shopUpdate.${this.getEventNotificationKey(update.eventType)}`,
        );
        this.showUpdateNotification.set(true);
        setTimeout(() => this.showUpdateNotification.set(false), 2000);

        // Immediately trigger preview refresh for server updates
        this.salePreview.triggerServerUpdateRefresh();
      });

    // Intent changes from this tab's effects or other tabs keep the pending list current.
    this.checkoutIntents.changes$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => void this.refreshPendingCheckouts());

    // Connect to shop updates on initialization
    void this.shopUpdatesService.startConnection();

    runInInjectionContext(this.injector, () => {
      effect(() => {
        const total = this.totalAmount();
        if (untracked(() => this.appliedCreditNotes().length) > 0) {
          if (total <= 0) {
            this.clearAppliedCreditNotes();
          } else {
            this.reconcileAppliedCreditNotesAgainstCartTotal();
          }
        }
      });

      effect(() => {
        if (this.lastMutationSucceeded()) {
          const type = this.lastMutationType();
          if (type === 'record-sale') {
            const recordedIntentId = this.lastRecordedIntentId();
            const activeIntentId = untracked(() => this.activeCheckoutIntentId());
            void untracked(() => this.refreshPendingCheckouts());
            if (recordedIntentId && recordedIntentId !== activeIntentId) {
              // A late answer for a checkout this cart no longer owns (detached, or from another
              // user/shop scope): it is stored against its own intent, and must not clear this cart.
              this.salesFacade.clearMutationStatus();
              return;
            }
            this.activeCheckoutIntentId.set(null);
            this.showConfirmation.set(true);
            queueMicrotask(() => this.resetTransientState());
            return;
          } else {
            this.salesFacade.clearMutationStatus();
            this.router.navigate(['/sales']);
          }
        }
      });

      effect(() => {
        this.nowTick();
        const preview = this.checkoutPreview();
        this.expiryBusinessTimeZone();
        if (
          preview?.expiryDecisions?.length &&
          this.expiryBusinessTimeZone() &&
          !this.expiryPreviewIsCurrent()
        )
          untracked(() => {
            this.availableBatches.set([]);
            this.availableSellables.set([]);
            this.selectedSellable.set(null);
            this.salePreview.triggerServerUpdateRefresh();
          });
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

      // The cart, the customer, discounts, credit notes and the checkout on this page belong to
      // the user and shop they were started under. When that owner changes, the cart is saved
      // under its owner and cleared (a no-op when the shop context already detached it), the rest
      // of the page is reset, and the next owner's draft is loaded. A checkout keeps its stored
      // intent; it is only detached from this page.
      let loadedOwner: CheckoutScope | null = null;
      effect(() => {
        const owner = this.checkoutScope();
        untracked(() => {
          if (loadedOwner && !sameOwner(loadedOwner, owner)) {
            void this.cartState.detach();
            this.resetTransientState();
          }
          loadedOwner = owner;

          const active = this.activeCheckoutIntent();
          if (active && (active.userId !== owner.userId || active.shopId !== owner.shopId)) {
            this.activeCheckoutIntentId.set(null);
          }

          void this.cartState
            .loadPersistedCart(owner, this.cartRetentionMs, (row) => {
              this.applyProductDefaultsForLine(row.clientLineKey, row.itemName, row.barcode);
            })
            .then(() => this.refreshPendingCheckouts());
        });
      });

      // Saved under the owner the rows were loaded for, never under whichever shop is active now.
      effect(() => {
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
            if (sellable.sellability?.allowed === false || sellable.availableQuantity === 0) {
              this.availableSellables.set([sellable]);
              this.availableBatches.set([goodsSellableToBatch(sellable)]);
              this.showBatchPicker.set(true);
              return;
            }
            const batch = goodsSellableToBatch(sellable);
            const result = this.cartState.addBatchToCart(batch, 1);
            if (!result.added) {
              if (
                result.refusal === 'exceedsStock' &&
                (batch.quantityScale ?? 0) > 0 &&
                this.remainingStock(batch) > 0
              ) {
                // Less than one unit of a weighed item is left: let the cashier enter the part quantity.
                this.availableBatches.set([batch]);
                this.showBatchPicker.set(true);
                return;
              }
              this.showQuantityRefusal(result.refusal ?? 'exceedsStock', batch);
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
                .filter(
                  (s): s is import('../services/sale.models').SellableGoodsDto =>
                    s.kind === 'Goods',
                )
                .map(goodsSellableToBatch),
            );
          } else {
            this.availableSellables.set(sellables);
          }
          this.showBatchPicker.set(true);
        }
      },
      error: (err) => {
        this.isSearchingBatches.set(false);
        this.batchSearchError.set('sales.newSale.searchError');
      },
    });
  }
}
