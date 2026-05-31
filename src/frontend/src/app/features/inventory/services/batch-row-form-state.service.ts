import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { firstValueFrom } from 'rxjs';

import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';
import {
  HsnLookupResult,
  ProductDetailsDto,
} from '../services/inventory.models';
import { InventoryService } from '../services/inventory.service';
import { InventoryInboundDraftRow } from '../../../core/storage/inventory-draft-indexeddb.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { Supplier } from '../../suppliers/services/supplier.service';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';

export type PrepareScannedRowResult =
  | { status: 'added'; row: InventoryInboundDraftRow }
  | { status: 'missingPricing' }
  | { status: 'review' };

@Injectable({ providedIn: 'root' })
export class BatchRowFormStateService {
  private readonly formBuilder = inject(FormBuilder);
  private readonly inventoryService = inject(InventoryService);
  private readonly catalogSync = inject(ProductCatalogSyncService);
  private readonly suppliersFacade = inject(SuppliersFacade);
  private readonly destroyRef = inject(DestroyRef);

  readonly loadingProduct = signal(false);
  readonly isLoadingHsn = signal(false);
  readonly barcodeGenerating = signal(false);
  readonly barcodeGenerateError = signal('');
  readonly selectedHsnCode = signal<string | null>(null);
  readonly hsnResult = signal<HsnLookupResult | null>(null);
  readonly pickerOpen = signal(false);
  pickerHsnCode: string | null = null;
  pickerTaxRate: string | null = null;
  readonly filteredPickerHsnOptions = signal<string[]>([]);
  readonly filteredPickerTaxOptions = signal<string[]>([]);
  readonly nameSuggestions = signal<string[]>([]);
  readonly barcodeSuggestions = signal<string[]>([]);
  readonly supplierSuggestions = signal<string[]>([]);
  readonly taxModeOptions = signal([
    { label: 'With Tax', value: true },
    { label: 'Without Tax', value: false },
  ]);

  readonly pickerHsnOptions = computed(() => [...(this.hsnResult()?.hsnCodes ?? [])]);
  readonly pickerTaxOptions = computed(() => (this.hsnResult()?.taxScenarios ?? []).map((s) => s.taxPercentage));
  readonly suppliers = this.suppliersFacade.suppliers;
  readonly form = this.formBuilder.nonNullable.group({
    itemName: ['', [Validators.required, Validators.maxLength(180)]],
    barcode: ['', [Validators.required, Validators.maxLength(120)]],
    itemDescription: ['', [Validators.maxLength(320)]],
    uom: ['PCS', [Validators.required, Validators.maxLength(40)]],
    batchNumber: ['', [Validators.required, Validators.maxLength(80)]],
    quantity: [1, [Validators.required, Validators.min(0.0001)]],
    totalPurchaseCost: [0, [Validators.required, Validators.min(0)]],
    mrp: [0, [Validators.required, Validators.min(0)]],
    salesPrice: [0, [Validators.required, Validators.min(0)]],
    taxRatePercent: [0, [Validators.required, Validators.min(0)]],
    taxIncluded: [true, [Validators.required]],
    expiryDate: [''],
    manufacturingDate: [''],
    supplierName: [''],
    referenceNumber: ['', [Validators.maxLength(120)]],
    notes: ['', [Validators.maxLength(320)]],
  });

  private clearHsnSelectionOnNextItemNameChange = false;

  constructor() {
    this.form.controls.itemName.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((value) => {
      if (this.clearHsnSelectionOnNextItemNameChange && value.trim().length > 0) {
        this.clearHsnSelection();
      }
    });

    this.resetForm();
  }

  onFilterName(event: AutoCompleteCompleteEvent): void {
    this.nameSuggestions.set(this.catalogSync.filterByName(event.query).map((entry) => entry.name));
  }

  onFilterBarcode(event: AutoCompleteCompleteEvent): void {
    this.barcodeSuggestions.set(this.catalogSync.filterByBarcode(event.query).map((entry) => entry.barcode));
  }

  onNameSelected(name: string): void {
    const entry = this.catalogSync.findByName(name);
    if (!entry) {
      return;
    }

    this.form.controls.barcode.setValue(entry.barcode);
    void this.fetchProductDetails({ lookupHsnIfMissing: true });
  }

  onBarcodeSelected(barcode: string): void {
    const entry = this.catalogSync.findByBarcode(barcode);
    if (entry) {
      this.form.controls.itemName.setValue(entry.name);
    }

    void this.fetchProductDetails();
  }

  onBarcodeFocusOut(): void {
    void this.fetchProductDetails();
  }

  async onItemNameBlur(): Promise<void> {
    if (this.loadingProduct()) {
      return;
    }

    const itemName = this.form.controls.itemName.value.trim();
    if (itemName.length < 3) {
      return;
    }

    this.isLoadingHsn.set(true);
    try {
      const result = await firstValueFrom(this.inventoryService.lookupHsn(itemName));
      if (this.selectedHsnCode()) {
        return;
      }

      this.hsnResult.set(result);
      if (result.hsnCodes.length === 1 && result.taxScenarios.length === 1) {
        this.applyHsnSelection(result.hsnCodes[0], result.taxScenarios[0].taxPercentage);
        return;
      }

      if (result.hsnCodes.length > 0 || result.taxScenarios.length > 0) {
        this.pickerHsnCode = result.hsnCodes[0] ?? null;
        this.pickerTaxRate = result.taxScenarios[0]?.taxPercentage ?? null;
        this.filteredPickerHsnOptions.set([...result.hsnCodes]);
        this.filteredPickerTaxOptions.set(result.taxScenarios.map((scenario) => scenario.taxPercentage));
        this.pickerOpen.set(true);
      }
    } catch {
      // Leave the form usable when lookup fails.
    } finally {
      this.isLoadingHsn.set(false);
    }
  }

  applyHsnSelection(hsnCode: string, taxPercentage: string): void {
    const taxRatePercent = Number.parseFloat(taxPercentage.replace('%', '').trim());
    this.selectedHsnCode.set(hsnCode);
    this.pickerHsnCode = hsnCode;
    this.pickerTaxRate = taxPercentage;
    this.form.controls.taxRatePercent.setValue(taxRatePercent);
    this.pickerOpen.set(false);
    this.clearHsnSelectionOnNextItemNameChange = true;
  }

  dismissPicker(): void {
    this.pickerOpen.set(false);
  }

  clearHsnSelection(): void {
    this.hsnResult.set(null);
    this.isLoadingHsn.set(false);
    this.selectedHsnCode.set(null);
    this.pickerOpen.set(false);
    this.pickerHsnCode = null;
    this.pickerTaxRate = null;
    this.clearHsnSelectionOnNextItemNameChange = false;
  }

  async onChangeHsnClick(): Promise<void> {
    this.selectedHsnCode.set(null);
    this.pickerOpen.set(false);
    this.clearHsnSelectionOnNextItemNameChange = false;

    const itemName = this.form.controls.itemName.value.trim();
    if (itemName.length < 3) {
      return;
    }

    this.isLoadingHsn.set(true);
    try {
      const result = await firstValueFrom(this.inventoryService.lookupHsn(itemName));
      this.hsnResult.set(result);
      if (result.hsnCodes.length > 0 || result.taxScenarios.length > 0) {
        this.pickerHsnCode = result.hsnCodes[0] ?? null;
        this.pickerTaxRate = result.taxScenarios[0]?.taxPercentage ?? null;
        this.filteredPickerHsnOptions.set([...result.hsnCodes]);
        this.filteredPickerTaxOptions.set(result.taxScenarios.map((scenario) => scenario.taxPercentage));
        this.pickerOpen.set(true);
      }
    } finally {
      this.isLoadingHsn.set(false);
    }
  }

  filterPickerHsn(event: AutoCompleteCompleteEvent): void {
    const filter = (event.query ?? '').toLowerCase();
    this.filteredPickerHsnOptions.set(
      this.pickerHsnOptions().filter((hsn) => hsn.toLowerCase().includes(filter)),
    );
  }

  filterPickerTax(event: AutoCompleteCompleteEvent): void {
    const filter = (event.query ?? '').toLowerCase();
    this.filteredPickerTaxOptions.set(
      this.pickerTaxOptions().filter((taxPercentage) => taxPercentage.toLowerCase().includes(filter)),
    );
  }

  onFilterSupplier(event: AutoCompleteCompleteEvent): void {
    const normalized = event.query.trim().toLowerCase();
    const matches = this.suppliers()
      .filter((supplier) => supplier.name.toLowerCase().includes(normalized))
      .slice(0, 15)
      .map((supplier) => supplier.name);

    this.supplierSuggestions.set(matches);
  }

  onSupplierSelected(selection: string): void {
    const supplier = this.findSupplierByName(selection);
    if (!supplier) {
      return;
    }

    this.form.controls.supplierName.setValue(supplier.name);
  }

  async generateBarcode(): Promise<{ needsConfirm: boolean; barcode: string } | { error: true }> {
    if (this.barcodeGenerating()) {
      return { error: true };
    }

    this.barcodeGenerating.set(true);
    this.barcodeGenerateError.set('');
    try {
      const result = await firstValueFrom(this.inventoryService.generateItemBarcode());
      const currentBarcode = this.form.controls.barcode.value.trim();
      if (currentBarcode) {
        return { needsConfirm: true, barcode: result.barcode };
      }
      this.form.controls.barcode.setValue(result.barcode);
      return { needsConfirm: false, barcode: result.barcode };
    } catch {
      this.barcodeGenerateError.set('inventory.generateBarcodeError');
      return { error: true };
    } finally {
      this.barcodeGenerating.set(false);
    }
  }

  patchGeneratedBarcode(barcode: string): void {
    this.form.controls.barcode.setValue(barcode);
    this.barcodeGenerateError.set('');
  }

  async fetchProductDetails(
    options?: { lookupHsnIfMissing?: boolean },
  ): Promise<{ readonly patched: boolean; readonly hsnApplied: boolean; readonly error: boolean }> {
    const lookupHsnIfMissing = options?.lookupHsnIfMissing ?? false;
    const itemName = this.form.controls.itemName.value?.trim();
    const barcode = this.form.controls.barcode.value?.trim();

    if (!barcode) {
      return { patched: false, hsnApplied: false, error: false };
    }

    this.loadingProduct.set(true);

    let hsnApplied = false;
    let patched = false;
    try {
      const details = await firstValueFrom(
        this.inventoryService.getProductDetailsByNameOrBarcode(itemName || undefined, barcode),
      );

      const patch = this.buildProductDetailsPatch(details);
      if (Object.keys(patch).length > 0) {
        this.form.patchValue(patch);
        patched = true;
      }

      if (details.hsnCode) {
        this.hsnResult.set(null);
        this.selectedHsnCode.set(details.hsnCode);
        this.pickerOpen.set(false);
        this.clearHsnSelectionOnNextItemNameChange = true;
        hsnApplied = true;
      }
    } catch {
      return { patched: false, hsnApplied: false, error: true };
    } finally {
      this.loadingProduct.set(false);
    }

    if (lookupHsnIfMissing && !hsnApplied && !this.selectedHsnCode()) {
      await this.onItemNameBlur();
    }

    return { patched, hsnApplied, error: false };
  }

  canAutoAddScannedRow(): boolean {
    const itemName = this.form.controls.itemName.value.trim();
    const barcode = this.form.controls.barcode.value.trim();
    const uom = this.form.controls.uom.value.trim();
    const mrp = Number(this.form.controls.mrp.value);
    const salesPrice = Number(this.form.controls.salesPrice.value);

    return itemName.length > 0 && barcode.length > 0 && uom.length > 0 && mrp > 0 && salesPrice > 0;
  }

  hasScannedRowWithMissingPricing(): boolean {
    const itemName = this.form.controls.itemName.value.trim();
    const barcode = this.form.controls.barcode.value.trim();
    const uom = this.form.controls.uom.value.trim();
    const mrp = Number(this.form.controls.mrp.value);
    const salesPrice = Number(this.form.controls.salesPrice.value);

    if (itemName.length === 0 || barcode.length === 0 || uom.length === 0) return false;
    return !Number.isFinite(mrp) || !Number.isFinite(salesPrice) || mrp <= 0 || salesPrice <= 0;
  }

  async prepareScannedRow(barcode: string): Promise<PrepareScannedRowResult> {
    const normalizedBarcode = barcode.trim();
    if (!normalizedBarcode) {
      return { status: 'review' };
    }

    const catalogEntry = this.catalogSync.findByBarcode(normalizedBarcode);
    this.form.controls.barcode.setValue(normalizedBarcode);
    this.form.controls.barcode.markAsDirty();

    if (catalogEntry) {
      this.form.controls.itemName.setValue(catalogEntry.name);
    }

    await this.fetchProductDetails();
    if (this.canAutoAddScannedRow()) {
      const row = this.buildDraftRow();
      return row ? { status: 'added', row } : { status: 'review' };
    }

    if (this.hasScannedRowWithMissingPricing()) {
      return { status: 'missingPricing' };
    }

    return { status: 'review' };
  }

  buildDraftRow(): InventoryInboundDraftRow | null {
    if (this.form.invalid) {
      return null;
    }

    const supplierId = this.resolveSupplierId(this.form.controls.supplierName.value);
    return {
      clientRowId: this.createRowId(),
      itemName: this.form.controls.itemName.value.trim(),
      barcode: this.form.controls.barcode.value.trim(),
      itemDescription: this.nullable(this.form.controls.itemDescription.value),
      uom: this.form.controls.uom.value.trim(),
      batchNumber: this.form.controls.batchNumber.value.trim(),
      quantity: Number(this.form.controls.quantity.value),
      totalPurchaseCost: Number(this.form.controls.totalPurchaseCost.value),
      mrp: Number(this.form.controls.mrp.value),
      salesPrice: Number(this.form.controls.salesPrice.value),
      taxRatePercent: Number(this.form.controls.taxRatePercent.value),
      taxIncluded: this.form.controls.taxIncluded.value,
      purchaseTaxIncluded: this.form.controls.taxIncluded.value,
      hsnCode: this.selectedHsnCode(),
      expiryDate: this.nullable(this.form.controls.expiryDate.value),
      manufacturingDate: this.nullable(this.form.controls.manufacturingDate.value),
      supplierId,
      referenceNumber: this.nullable(this.form.controls.referenceNumber.value),
      notes: this.nullable(this.form.controls.notes.value),
      performedAt: new Date().toISOString(),
    };
  }

  loadDraftRow(row: InventoryInboundDraftRow): void {
    this.form.setValue({
      itemName: row.itemName,
      barcode: row.barcode,
      itemDescription: row.itemDescription ?? '',
      uom: row.uom,
      batchNumber: row.batchNumber,
      quantity: row.quantity,
      totalPurchaseCost: row.totalPurchaseCost,
      mrp: row.mrp,
      salesPrice: row.salesPrice,
      taxRatePercent: row.taxRatePercent,
      taxIncluded: row.taxIncluded,
      expiryDate: row.expiryDate ?? '',
      manufacturingDate: row.manufacturingDate ?? '',
      supplierName:
        this.getSupplierDisplayName(row.supplierId) === '-' ? '' : this.getSupplierDisplayName(row.supplierId),
      referenceNumber: row.referenceNumber ?? '',
      notes: row.notes ?? '',
    });

    this.selectedHsnCode.set(row.hsnCode ?? null);
    this.pickerHsnCode = row.hsnCode ?? null;
    this.pickerTaxRate = row.taxRatePercent.toString();
    this.clearHsnSelectionOnNextItemNameChange = row.hsnCode != null;
    this.pickerOpen.set(false);
  }

  resetForm(): void {
    this.form.reset({
      itemName: '',
      barcode: '',
      itemDescription: '',
      uom: 'PCS',
      batchNumber: '',
      quantity: 1,
      totalPurchaseCost: 0,
      mrp: 0,
      salesPrice: 0,
      taxRatePercent: 0,
      taxIncluded: true,
      expiryDate: '',
      manufacturingDate: '',
      supplierName: '',
      referenceNumber: '',
      notes: '',
    });

    this.form.controls.batchNumber.setValue(this.generateBatchNumber());
    this.clearHsnSelection();
    this.pickerHsnCode = null;
    this.pickerTaxRate = null;
  }

  getSupplierDisplayName(supplierId: string | null): string {
    if (!supplierId) {
      return '-';
    }

    return this.suppliers().find((supplier) => supplier.supplierId === supplierId)?.name ?? supplierId;
  }

  private buildProductDetailsPatch(details: ProductDetailsDto): Partial<{
    itemName: string;
    itemDescription: string;
    uom: string;
    totalPurchaseCost: number;
    mrp: number;
    salesPrice: number;
    supplierName: string;
    hsnCode: string;
    taxIncluded: boolean;
    taxRatePercent: number;
  }> {
    const patch: Partial<{
      itemName: string;
      itemDescription: string;
      uom: string;
      totalPurchaseCost: number;
      mrp: number;
      salesPrice: number;
      supplierName: string;
      hsnCode: string;
      taxIncluded: boolean;
      taxRatePercent: number;
    }> = {};

    const currentItemName = this.form.controls.itemName.value.trim();
    if (!currentItemName && !this.form.controls.itemName.dirty && details.name) {
      patch.itemName = details.name;
    }

    if (!this.form.controls.itemDescription.dirty) {
      patch.itemDescription = details.description || '';
    }

    if (!this.form.controls.uom.dirty && details.uom?.trim().length) {
      patch.uom = details.uom;
    }

    if (!this.form.controls.totalPurchaseCost.dirty) {
      patch.totalPurchaseCost = Number((details.costPrice * Number(this.form.controls.quantity.value)).toFixed(2));
    }

    if (!this.form.controls.mrp.dirty) {
      patch.mrp = details.mrp;
    }

    if (!this.form.controls.salesPrice.dirty) {
      patch.salesPrice = details.salesPrice;
    }

    if (!this.form.controls.supplierName.dirty && details.supplierName) {
      patch.supplierName = details.supplierName;
    }

    if (!this.form.controls.taxIncluded.dirty && details.taxIncluded !== null) {
      patch.taxIncluded = details.taxIncluded;
    }

    if (!this.form.controls.taxRatePercent.dirty && details.taxRatePercent !== null) {
      patch.taxRatePercent = details.taxRatePercent;
    }

    return patch;
  }

  private nullable(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }

  private resolveSupplierId(value: string): string | null {
    const normalized = value.trim();
    if (!normalized) {
      return null;
    }

    return this.findSupplierByName(normalized)?.supplierId ?? null;
  }

  private findSupplierByName(name: string): Supplier | undefined {
    const normalized = name.trim().toLowerCase();
    return this.suppliers().find((supplier) => supplier.name.toLowerCase() === normalized);
  }

  private generateBatchNumber(): string {
    const date = formatLocalIsoDate(new Date()).replace(/-/g, '');
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    let suffix = '';
    for (let index = 0; index < 5; index += 1) {
      suffix += chars[Math.floor(Math.random() * chars.length)];
    }

    return `BN-${date}-${suffix}`;
  }

  private createRowId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }

    return `${Date.now()}-${Math.floor(Math.random() * 1000000)}`;
  }

}
