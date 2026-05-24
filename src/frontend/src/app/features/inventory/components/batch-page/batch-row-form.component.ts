import {
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, FormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { AutoCompleteModule, AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { firstValueFrom } from 'rxjs';

import { HsnLookupResult, InventoryService } from '../../services/inventory.service';
import { InventoryInboundDraftRow } from '../../../../core/storage/inventory-draft-indexeddb.service';
import { ProductCatalogSyncService } from '../../../../core/services/product-catalog-sync.service';
import { Supplier } from '../../../suppliers/services/supplier.service';
import {
  CURRENCY_ADDON_PT,
  CURRENCY_INPUT_GROUP_PT,
  CURRENCY_INPUT_NUMBER_PT,
  CURRENCY_SELECT_PT,
} from '../../../../shared/primeng-pt.config';
import { formatLocalIsoDate } from '../../../../shared/utils/date-time.util';
import { BatchHsnPickerDialogComponent } from './batch-hsn-picker-dialog.component';

@Component({
  selector: 'app-batch-row-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    FormsModule,
    TranslocoPipe,
    BatchHsnPickerDialogComponent,
    AutoCompleteModule,
    ButtonModule,
    InputGroupAddonModule,
    InputGroupModule,
    InputNumberModule,
    InputTextModule,
    SelectModule,
    TextareaModule,
    ProgressSpinnerModule,
  ],
  templateUrl: './batch-row-form.component.html',
})
export class BatchRowFormComponent implements OnInit, OnChanges {
  @Input() shopId = '';
  @Input() suppliers: readonly Supplier[] = [];
  @Input() catalogSync!: ProductCatalogSyncService;

  @Output() readonly rowAdded = new EventEmitter<InventoryInboundDraftRow>();
  @Output() readonly scanRequested = new EventEmitter<void>();

  private readonly formBuilder = inject(FormBuilder);
  private readonly inventoryService = inject(InventoryService);
  private readonly messageService = inject(MessageService);
  private readonly translocoService = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  readonly hsnResult = signal<HsnLookupResult | null>(null);
  readonly isLoadingHsn = signal(false);
  readonly selectedHsnCode = signal<string | null>(null);
  readonly pickerOpen = signal(false);
  readonly loadingProduct = signal(false);
  readonly nameSuggestions = signal<string[]>([]);
  readonly barcodeSuggestions = signal<string[]>([]);
  readonly supplierSuggestions = signal<string[]>([]);
  readonly taxModeOptions = signal([
    { label: 'With Tax', value: true },
    { label: 'Without Tax', value: false },
  ]);

  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;
  readonly currencySelectPt = CURRENCY_SELECT_PT;

  readonly barcodeAutocomplePt = { root: { class: 'barcode-autocomplete' } };
  readonly barcodeInputGroupPt = { root: { class: 'barcode-input-group' } };
  readonly cameraddonPt = { root: { class: 'camera-addon' } };

  readonly form = this.formBuilder.nonNullable.group({
    itemName: ['', [Validators.required, Validators.maxLength(180)]],
    barcode: ['', [Validators.required, Validators.maxLength(120)]],
    itemDescription: ['', [Validators.maxLength(320)]],
    uom: ['', [Validators.required, Validators.maxLength(40)]],
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

  ngOnInit(): void {
    this.form.controls.batchNumber.setValue(this.generateBatchNumber());
    this.form.controls.itemName.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => {
        if (this.clearHsnSelectionOnNextItemNameChange && value.trim().length > 0) {
          this.clearHsnSelection();
        }
      });
  }

  ngOnChanges(_changes: SimpleChanges): void {
    // Inputs are consumed reactively; no action needed here currently.
  }

  onFilterName(event: AutoCompleteCompleteEvent): void {
    this.nameSuggestions.set(this.catalogSync.filterByName(event.query).map((e) => e.name));
  }

  onFilterBarcode(event: AutoCompleteCompleteEvent): void {
    this.barcodeSuggestions.set(
      this.catalogSync.filterByBarcode(event.query).map((e) => e.barcode),
    );
  }

  onNameSelected(name: string): void {
    const entry = this.catalogSync.findByName(name);
    if (entry) {
      this.form.controls.barcode.setValue(entry.barcode);
      void this.fetchProductDetails({ lookupHsnIfMissing: true });
    }
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
        this.pickerOpen.set(true);
      }
    } catch {
      // Silent lookup failure.
    } finally {
      this.isLoadingHsn.set(false);
    }
  }

  applyHsnSelection(hsnCode: string, taxPercentage: string): void {
    const taxRatePercent = Number.parseFloat(taxPercentage.replace('%', '').trim());
    this.selectedHsnCode.set(hsnCode);
    this.form.controls.taxRatePercent.setValue(taxRatePercent);
    this.pickerOpen.set(false);
    this.clearHsnSelectionOnNextItemNameChange = true;
  }

  onHsnSelected(event: { hsnCode: string; taxRate: string }): void {
    this.applyHsnSelection(event.hsnCode, event.taxRate);
  }

  dismissPicker(): void {
    this.pickerOpen.set(false);
  }

  clearHsnSelection(): void {
    this.hsnResult.set(null);
    this.isLoadingHsn.set(false);
    this.selectedHsnCode.set(null);
    this.pickerOpen.set(false);
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
        this.pickerOpen.set(true);
      }
    } finally {
      this.isLoadingHsn.set(false);
    }
  }

  onFilterSupplier(event: AutoCompleteCompleteEvent): void {
    const normalized = event.query.trim().toLowerCase();
    const matches = this.suppliers
      .filter((supplier) => supplier.name.toLowerCase().includes(normalized))
      .slice(0, 15)
      .map((supplier) => supplier.name);

    this.supplierSuggestions.set(matches);
  }

  onSupplierSelected(selection: string): void {
    const supplier = this.findSupplierByName(selection);
    if (supplier) {
      this.form.controls.supplierName.setValue(supplier.name);
    }
  }

  onAddRow(): void {
    if (!this.tryAddRow()) {
      this.form.markAllAsTouched();
    }
  }

  /** Builds a draft row, emits `rowAdded`, resets the form. Returns true if emitted. */
  tryAddRow(): boolean {
    const row = this.buildDraftRow();
    if (!row) {
      return false;
    }
    this.rowAdded.emit(row);
    this.resetForm();
    return true;
  }

  /** Fill the form from an existing draft row (e.g. when editing). */
  populateFromRow(row: InventoryInboundDraftRow, supplierName: string): void {
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
      supplierName: supplierName === '-' ? '' : supplierName,
      referenceNumber: row.referenceNumber ?? '',
      notes: row.notes ?? '',
    });
    this.selectedHsnCode.set(row.hsnCode ?? null);
    this.clearHsnSelectionOnNextItemNameChange = row.hsnCode != null;
  }

  /**
   * Handle a barcode scanned from the scanner session.
   * Returns 'added' if a row was auto-added, 'review' if the user should review the form.
   */
  async handleBarcode(barcode: string): Promise<'added' | 'review'> {
    const catalogEntry = this.catalogSync.findByBarcode(barcode);
    this.form.controls.barcode.setValue(barcode);
    this.form.controls.barcode.markAsDirty();

    if (catalogEntry) {
      this.form.controls.itemName.setValue(catalogEntry.name);
    }

    await this.fetchProductDetails({ showInfoToast: false, showErrorToast: false });

    if (this.canAutoAddScannedRow() && this.tryAddRow()) {
      return 'added';
    }

    this.form.markAllAsTouched();
    return 'review';
  }

  private async fetchProductDetails(options?: {
    showInfoToast?: boolean;
    showErrorToast?: boolean;
    lookupHsnIfMissing?: boolean;
  }): Promise<void> {
    const showInfoToast = options?.showInfoToast ?? true;
    const showErrorToast = options?.showErrorToast ?? true;
    const lookupHsnIfMissing = options?.lookupHsnIfMissing ?? false;
    const itemName = this.form.controls.itemName.value?.trim();
    const barcode = this.form.controls.barcode.value?.trim();

    if (!barcode) {
      return;
    }

    this.loadingProduct.set(true);

    let hsnApplied = false;
    try {
      const details = await this.inventoryService
        .getProductDetailsByNameOrBarcode(itemName || undefined, barcode)
        .toPromise();

      if (details) {
        const patch = this.buildProductDetailsPatch(details);

        if (Object.keys(patch).length > 0) {
          this.form.patchValue(patch);
          if (showInfoToast) {
            this.showInfo('inventory.productDetailsLoaded');
          }
        }

        if (details.hsnCode) {
          this.hsnResult.set(null);
          this.selectedHsnCode.set(details.hsnCode);
          this.pickerOpen.set(false);
          this.clearHsnSelectionOnNextItemNameChange = true;
          hsnApplied = true;
        }
      }
    } catch {
      if (showErrorToast) {
        this.showError('inventory.productDetailsLoadError');
      }
    } finally {
      this.loadingProduct.set(false);
    }

    if (lookupHsnIfMissing && !hsnApplied && !this.selectedHsnCode()) {
      await this.onItemNameBlur();
    }
  }

  private canAutoAddScannedRow(): boolean {
    const itemName = this.form.controls.itemName.value.trim();
    const barcode = this.form.controls.barcode.value.trim();
    const uom = this.form.controls.uom.value.trim();
    const totalPurchaseCost = Number(this.form.controls.totalPurchaseCost.value);
    const mrp = Number(this.form.controls.mrp.value);
    const salesPrice = Number(this.form.controls.salesPrice.value);

    return (
      itemName.length > 0
      && barcode.length > 0
      && uom.length > 0
      && totalPurchaseCost > 0
      && mrp > 0
      && salesPrice > 0
    );
  }

  private buildDraftRow(): InventoryInboundDraftRow | null {
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

  private resetForm(): void {
    this.form.reset({
      itemName: '',
      barcode: '',
      itemDescription: '',
      uom: '',
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
  }

  private buildProductDetailsPatch(details: {
    name: string;
    description: string;
    uom: string;
    costPrice: number;
    mrp: number;
    salesPrice: number;
    supplierName: string | null;
    hsnCode: string | null;
    taxIncluded: boolean | null;
    taxRatePercent: number | null;
  }): Partial<{
    itemName: string;
    itemDescription: string;
    uom: string;
    totalPurchaseCost: number;
    mrp: number;
    salesPrice: number;
    supplierName: string;
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

    if (!this.form.controls.uom.dirty) {
      patch.uom = details.uom;
    }

    if (!this.form.controls.totalPurchaseCost.dirty) {
      patch.totalPurchaseCost = Number(
        (details.costPrice * Number(this.form.controls.quantity.value)).toFixed(2),
      );
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
    return this.suppliers.find((supplier) => supplier.name.toLowerCase() === normalized);
  }

  private generateBatchNumber(): string {
    const date = formatLocalIsoDate(new Date()).replace(/-/g, '');
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    let suffix = '';
    for (let i = 0; i < 5; i++) {
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

  private showInfo(messageKey: string): void {
    this.messageService.add({
      severity: 'info',
      summary: this.translate(messageKey),
      life: 2500,
    });
  }

  private showError(messageKey: string): void {
    this.messageService.add({
      severity: 'error',
      summary: this.translate(messageKey),
      life: 3500,
    });
  }

  private translate(key: string): string {
    return this.translocoService.translate(key);
  }
}
