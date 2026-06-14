import { NewSalePageLifecycleService } from './new-sale-page.lifecycle.service';
import { effect, runInInjectionContext } from '@angular/core';
import { AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import { AvailableBatchDto } from '../../inventory/services/inventory.models';
import { SellableDto } from '../services/sale.models';
import { CustomerDto } from '../components/new-sale/sale-customer-section.component';
import type { Subscription } from 'rxjs';

export abstract class NewSalePageSearchCustomerService extends NewSalePageLifecycleService {
  private searchSuggestionsRequestSeq = 0;
  private searchSuggestionsSubscription: Subscription | null = null;
  private routeCustomerPreselectionApplied = false;

  override onInit(): void {
    super.onInit();

    runInInjectionContext(this.injector, () => {
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
    this.searchInput.set((value ?? '').toString());
    void this.onBarcodeSearch();
  }

  onOpenBatchPicker(): void {
    if (this.pickerSellables().length > 0) {
      this.showBatchPicker.set(true);
    }
  }

  onBatchQuantityChanged(quantity: number | null): void {
    const normalized = Number(quantity ?? 1);
    this.batchPickerForm.patchValue({ quantity: Number.isFinite(normalized) ? Math.max(1, normalized) : 1 });
    this.batchPickerQuantity.set(this.batchPickerForm.controls.quantity.value);
  }

  onBatchPickerBatchSelected(sellable: SellableDto): void {
    const normalizedQuantity = Number.isFinite(Number(this.batchPickerQuantity())) ? Math.max(1, Math.trunc(Number(this.batchPickerQuantity()))) : 1;
    this.selectedSellable.set(sellable);
    this.batchPickerForm.patchValue({
      batchNumber: sellable.kind === 'Goods' ? sellable.batchNumber : sellable.code,
      quantity: normalizedQuantity,
    });
    this.batchPickerQuantity.set(this.batchPickerForm.controls.quantity.value);
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

    this.onBatchPickerBatchSelected({
      kind: 'Goods',
      inventoryBatchId: batch.inventoryBatchId,
      barcode: batch.barcode,
      itemName: batch.itemName,
      batchNumber: batch.batchNumber,
      quantity: batch.quantity,
      salesPrice: batch.salesPrice,
      mrp: batch.mrp,
      taxRatePercent: batch.taxRatePercent,
      taxIncluded: batch.taxIncluded,
      expiryDate: batch.expiryDate,
      hsnCode: batch.hsnCode,
    });
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
            : [sellable.name, sellable.code]
        );
        this.searchSuggestions.set([...new Set(terms.filter((term) => term.trim().length > 0))].slice(0, 20));
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
    const query = event.query.trim().toLowerCase();

    if (this.isOfflineMode()) {
      const filtered = this.offlineCustomers()
        .filter((c) => !query || c.name.toLowerCase().includes(query))
        .map((c) => c.name.trim())
        .filter((name) => name.length > 0);
      this.customerNameSuggestions.set([...new Set(filtered)].slice(0, 20));
      return;
    }

    const activeCustomers = this.customers().filter((customer) => customer.isActive);
    const filteredNames = activeCustomers
      .filter((customer) => !query || customer.name.toLowerCase().includes(query))
      .map((customer) => customer.name.trim())
      .filter((name) => name.length > 0);

    const uniqueNames = [...new Set(filteredNames)];
    this.customerNameSuggestions.set(uniqueNames.slice(0, 20));
  }

  onCustomerSuggestionSelected(name: string): void {
    const normalizedName = name.trim().toLowerCase();
    if (!normalizedName) {
      this.selectedCustomerId.set(null);
      this.selectedCustomerName.set(null);
      this.selectedCustomer.set(null);
      return;
    }

    const customer = this.customerSelectionPool().find((candidate) => candidate.name.trim().toLowerCase() === normalizedName);
    if (!customer) {
      this.onCustomerSectionSelected(null);
      return;
    }

    this.onCustomerSectionSelected(customer);
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
      { emitEvent: false }
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
    if (!customerId || this.loadingCustomers()) {
      return;
    }

    const customer = this.customerSelectionPool().find((candidate) => candidate.customerId === customerId) ?? null;
    if (customer) {
      this.routeCustomerPreselectionApplied = true;
      this.onCustomerSectionSelected(customer);
      return;
    }

    if (this.customers().length === 0) {
      return;
    }

    this.routeCustomerPreselectionApplied = true;
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
    this.searchSuggestionsSubscription?.unsubscribe();
    this.searchSuggestionsSubscription = null;
    super.onDestroy();
  }

}
