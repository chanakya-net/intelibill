import { NewSalePageLifecycleService } from './new-sale-page.lifecycle.service';
import { AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import { AvailableBatchDto } from '../../inventory/services/inventory.models';
import { CustomerDto } from '../components/new-sale/sale-customer-section.component';

export abstract class NewSalePageSearchCustomerService extends NewSalePageLifecycleService {
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
    if (this.availableBatches().length > 0) {
      this.showBatchPicker.set(true);
    }
  }

  onBatchQuantityChanged(quantity: number | null): void {
    const normalized = Number(quantity ?? 1);
    this.batchPickerForm.patchValue({ quantity: Number.isFinite(normalized) ? Math.max(1, normalized) : 1 });
    this.batchPickerQuantity.set(this.batchPickerForm.controls.quantity.value);
  }

  onBatchPickerBatchSelected(batch: AvailableBatchDto): void {
    const normalizedQuantity = Number.isFinite(Number(this.batchPickerQuantity())) ? Math.max(1, Math.trunc(Number(this.batchPickerQuantity()))) : 1;
    this.selectedBatch.set(batch);
    this.batchPickerForm.patchValue({
      batchNumber: batch.batchNumber,
      quantity: normalizedQuantity,
    });
    this.batchPickerQuantity.set(this.batchPickerForm.controls.quantity.value);
    this.onAddToCart();
  }

  onBatchPickerClosed(): void {
    this.showBatchPicker.set(false);
    this.batchPickerForm.reset({ batchNumber: '', quantity: 1 });
    this.batchPickerQuantity.set(1);
    this.batchSearchError.set('');
  }

  onFilterSearch(event: AutoCompleteCompleteEvent): void {
    const query = event.query.trim();
    if (!query) {
      this.searchSuggestions.set([]);
      return;
    }

    const names = this.catalogSync.filterByName(query).map((e) => e.name);
    const barcodes = this.catalogSync.filterByBarcode(query).map((e) => e.barcode);
    const merged = [...new Set([...names, ...barcodes])];
    this.searchSuggestions.set(merged.slice(0, 20));
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

}
