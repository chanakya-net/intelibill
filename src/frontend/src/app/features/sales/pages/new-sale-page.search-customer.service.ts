import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CustomerService } from '../../customers/services/customer.service';
import { customerOptionLabel } from '../../../shared/models/party-directory.models';
import { NewSalePageLifecycleService } from './new-sale-page.lifecycle.service';
import { effect, runInInjectionContext, untracked } from '@angular/core';
import { AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import { AvailableBatchDto } from '../../inventory/services/inventory.models';
import { CreditNoteRedemptionRequest, SellableDto } from '../services/sale.models';
import { CustomerDto } from '../components/new-sale/sale-customer-section.component';
import { batchToGoodsSellable } from '../utils/sellable-goods.mapper';
import type { Subscription } from 'rxjs';

export abstract class NewSalePageSearchCustomerService extends NewSalePageLifecycleService {
  protected getPayableAmount(total = this.totalAmount()): number {
    return this.roundAmount(Math.max(0, total));
  }

  protected getCreditNoteAppliedAmount(): number {
    return 0;
  }

  protected getCreditNoteRedemptions(): readonly CreditNoteRedemptionRequest[] {
    return [];
  }

  private searchSuggestionsRequestSeq = 0;
  private searchSuggestionsSubscription: Subscription | null = null;
  private routeCustomerPreselectionApplied = false;
  private routeCustomerSubscription: Subscription | undefined;

  override onInit(): void {
    super.onInit();
    this.destroyRef.onDestroy(() => {
      this.onlineCustomerSearch.destroy();
      this.routeCustomerSubscription?.unsubscribe();
    });
    this.partyContext.changes$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.onlineCustomerSearch.destroy();
      this.onlineCustomerSearch.items.set([]);
      this.customerNameSuggestions.set([]);
      this.routeCustomerSubscription?.unsubscribe();
      this.onCustomerSectionSelected(null);
    });

    runInInjectionContext(this.injector, () => {
      effect(() => {
        if (this.isOfflineMode())
          untracked(() => {
            this.onlineCustomerSearch.cancel();
            this.onlineCustomerSearch.items.set([]);
            this.customerNameSuggestions.set([]);
            this.routeCustomerSubscription?.unsubscribe();
          });
      });
      effect(() => {
        this.loadingCustomers();
        this.customers();
        this.preselectRouteCustomerIfPossible();
      });
    });
  }

  onBatchSearchTermChanged(value: string): void {
    this.searchInput.set((value ?? '').toString());
  }

  onBatchSearchSuggestionFilter(value: string): void {
    this.onFilterSearch({ query: value } as AutoCompleteCompleteEvent);
  }

  onBatchSearchRequested(value: string): void {
    this.searchSuggestionsSubscription?.unsubscribe();
    this.searchSuggestions.set([]);
    this.searchInput.set((value ?? '').toString());
    void this.onBarcodeSearch();
  }

  onOpenBatchPicker(): void {
    if (this.pickerSellables().length > 0) {
      this.showBatchPicker.set(true);
    }
  }

  /** The picker's quantity field committed a valid quantity; it is kept exactly as typed. */
  onBatchQuantityChanged(quantity: number): void {
    this.batchPickerForm.patchValue({ quantity });
    this.batchPickerQuantity.set(quantity);
  }

  onBatchPickerBatchSelected(sellable: SellableDto): void {
    this.selectedSellable.set(sellable);
    this.batchPickerForm.patchValue({
      batchNumber: sellable.kind === 'Goods' ? sellable.batchNumber : sellable.code,
      quantity: this.batchPickerQuantity(),
    });
    this.onAddToCart();
  }

  onQuickProductTileSelected(batch: AvailableBatchDto): void {
    const matchingBatches = this.availableBatches().filter(
      (candidate) => candidate.itemName === batch.itemName && candidate.barcode === batch.barcode,
    );

    if (matchingBatches.length > 1) {
      this.selectedSellable.set(null);
      this.batchPickerForm.reset({ batchNumber: '', quantity: 1 });
      this.batchPickerQuantity.set(1);
      this.availableBatches.set(matchingBatches);
      this.showBatchPicker.set(true);
      this.batchSearchError.set('');
      return;
    }

    this.onBatchPickerBatchSelected(batchToGoodsSellable(batch));
  }

  onBatchPickerClosed(): void {
    this.showBatchPicker.set(false);
    this.batchPickerForm.reset({ batchNumber: '', quantity: 1 });
    this.batchPickerQuantity.set(1);
    this.batchSearchError.set('');
    this.selectedSellable.set(null);
    this.availableBatches.set([]);
    this.availableSellables.set([]);
  }

  onFilterSearch(event: AutoCompleteCompleteEvent): void {
    const query = event.query.trim();
    if (!query) {
      this.searchSuggestionsSubscription?.unsubscribe();
      this.searchSuggestionsSubscription = null;
      this.searchSuggestions.set([]);
      return;
    }

    if (this.isOfflineMode() && this.isOfflineEligible()) {
      const names = this.catalogSync.filterByName(query).map((e) => e.name);
      const barcodes = this.catalogSync.filterByBarcode(query).map((e) => e.barcode);
      const merged = [...new Set([...names, ...barcodes])];
      this.searchSuggestions.set(merged.slice(0, 20));
      return;
    }

    this.searchSuggestionsSubscription?.unsubscribe();
    const requestSeq = ++this.searchSuggestionsRequestSeq;
    this.searchSuggestionsSubscription = this.saleService.getSellables(query).subscribe({
      next: (sellables) => {
        if (requestSeq !== this.searchSuggestionsRequestSeq) {
          return;
        }
        const terms = sellables.flatMap((sellable) =>
          sellable.kind === 'Goods'
            ? [sellable.itemName, sellable.barcode]
            : [sellable.name, sellable.code],
        );
        this.searchSuggestions.set(
          [...new Set(terms.filter((term) => term.trim().length > 0))].slice(0, 20),
        );
      },
      error: () => {
        if (requestSeq !== this.searchSuggestionsRequestSeq) {
          return;
        }
        this.searchSuggestions.set([]);
      },
    });
  }

  onBatchSearchSuggestions(query: string): void {
    this.onFilterSearch({ query } as AutoCompleteCompleteEvent);
  }

  onSearchSuggestionSelected(value: string): void {
    this.searchInput.set(value?.trim() ?? '');
  }

  onFilterCustomerName(event: AutoCompleteCompleteEvent): void {
    if (this.isOfflineMode()) {
      this.onlineCustomerSearch.cancel();
      const query = event.query.trim().toLowerCase();
      const filtered = this.offlineCustomers().filter(
        (customer) =>
          !query ||
          [customer.name, customer.phoneNumber, customer.customerId].some((value) =>
            value?.toLowerCase().includes(query),
          ),
      );
      this.customerNameSuggestions.set(filtered.slice(0, 20).map(customerOptionLabel));
      return;
    }
    this.onlineCustomerSearch.search(event.query);
  }

  onCustomerSuggestionSelected(label: string): void {
    const matches = this.customerSelectionPool().filter(
      (candidate) =>
        customerOptionLabel(candidate) === label ||
        candidate.name.trim().toLowerCase() === label.trim().toLowerCase(),
    );
    this.onCustomerSectionSelected(matches.length === 1 ? matches[0] : null);
  }

  onCustomerSectionNameChanged(value: string | null): void {
    const nextValue = (value ?? '').toString();
    if (this.customerForm.controls.customerName.value === nextValue) {
      return;
    }

    this.customerForm.controls.customerName.setValue(nextValue, { emitEvent: false });
  }

  onCustomerSectionPhoneChanged(value: string | null): void {
    const nextValue = (value ?? '').toString();
    if (this.customerForm.controls.customerPhone.value === nextValue) {
      return;
    }

    this.customerForm.controls.customerPhone.setValue(nextValue, { emitEvent: false });
  }

  onCustomerSectionSearchCustomers(query: string): void {
    this.onFilterCustomerName({ query } as AutoCompleteCompleteEvent);
  }

  onCustomerSectionSuggestionSelected(name: string): void {
    this.onCustomerSuggestionSelected(name);
  }

  onCustomerSectionSelected(customer: CustomerDto | null): void {
    this.selectedCustomer.set(customer);
    if (!customer) {
      this.selectedCustomerId.set(null);
      this.selectedCustomerName.set(null);
      this.selectedCustomer.set(null);
      this.refreshCreditNoteCustomerMismatchState();
      return;
    }

    this.customerForm.patchValue(
      {
        customerName: customer.name,
        customerPhone: customer.phoneNumber,
      },
      { emitEvent: false },
    );
    this.selectedCustomerId.set(customer.customerId);
    this.selectedCustomerName.set(customer.name.trim().toLowerCase());
    this.refreshCreditNoteCustomerMismatchState();
  }

  protected preselectRouteCustomerIfPossible(): void {
    if (this.routeCustomerPreselectionApplied) {
      return;
    }

    const customerId = this.routeCustomerId;
    if (!customerId) return;
    if (this.isOfflineMode()) {
      const customer = this.customerSelectionPool().find(
        (candidate) => candidate.customerId === customerId,
      );
      if (customer) {
        this.routeCustomerPreselectionApplied = true;
        this.onCustomerSectionSelected(customer);
      }
      return;
    }
    this.routeCustomerPreselectionApplied = true;
    const epoch = this.partyContext.epoch();
    this.routeCustomerSubscription = this.injector
      .get(CustomerService)
      .getCustomer(customerId)
      .subscribe({
        next: (customer) => {
          if (
            epoch !== this.partyContext.epoch() ||
            this.isOfflineMode() ||
            !customer.isActive ||
            this.customerForm.controls.customerName.dirty ||
            (this.selectedCustomerId() && this.selectedCustomerId() !== customerId)
          )
            return;
          this.onCustomerSectionSelected(customer);
        },
        error: () => {},
      });
  }

  openScanner(): void {
    this.isScannerOpen.set(true);
  }

  onScannerVisibilityChange(visible: boolean): void {
    this.isScannerOpen.set(visible);
  }

  onScannedBarcode(detection: BarcodeDetection): void {
    const barcode = detection.value.trim();
    if (!barcode) {
      return;
    }

    this.searchInput.set(barcode);
    this.isScannerOpen.set(false);
    void this.onBarcodeSearch();
  }

  override onDestroy(): void {
    this.onlineCustomerSearch.destroy();
    this.routeCustomerSubscription?.unsubscribe();
    this.searchSuggestionsSubscription?.unsubscribe();
    this.searchSuggestionsSubscription = null;
    super.onDestroy();
  }
}
