import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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
import { TableModule } from 'primeng/table';
import { ToastModule } from 'primeng/toast';

import { firstValueFrom } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import {
  AddInventoryBatchFailedRow,
  AddInventoryBatchResponse,
  AddInventoryBatchRowRequest,
  AddInventoryBatchSucceededRow,
  InventoryService,
} from '../services/inventory.service';
import {
  InventoryDraftIndexedDbService,
  InventoryInboundDraftRow,
} from '../../../core/storage/inventory-draft-indexeddb.service';
import { AudioService } from '../../../core/services/audio.service';
import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { Supplier } from '../../suppliers/services/supplier.service';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';
import { BarcodeScannerDialogComponent } from '../../../shared/components/barcode-scanner-dialog.component';
import {
  CURRENCY_ADDON_PT,
  CURRENCY_INPUT_GROUP_PT,
  CURRENCY_INPUT_NUMBER_PT,
  CURRENCY_SELECT_PT,
} from '../../../shared/primeng-pt.config';

@Component({
  selector: 'app-inventory-batch-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    BarcodeScannerDialogComponent,
    AutoCompleteModule,
    ButtonModule,
    InputGroupAddonModule,
    InputGroupModule,
    InputNumberModule,
    InputTextModule,
    SelectModule,
    TextareaModule,
    TableModule,
    ToastModule,
    ProgressSpinnerModule,
  ],
  providers: [MessageService],
  templateUrl: './inventory-batch-page.component.html',
  styleUrl: './inventory-batch-page.component.scss',
})
export class InventoryBatchPageComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly audioService = inject(AudioService);
  private readonly inventoryService = inject(InventoryService);
  private readonly draftStorage = inject(InventoryDraftIndexedDbService);
  private readonly messageService = inject(MessageService);
  private readonly translocoService = inject(TranslocoService);
  private readonly catalogSync = inject(ProductCatalogSyncService);
  private readonly suppliersFacade = inject(SuppliersFacade);

  readonly isSaving = signal(false);
  readonly isScannerOpen = signal(false);
  readonly pendingRows = signal<readonly InventoryInboundDraftRow[]>([]);
  readonly saveSummary = signal<AddInventoryBatchResponse | null>(null);
  readonly loadingDraft = signal(false);
  readonly loadingProduct = signal(false);
  readonly scannerLastAction = signal('');
  readonly scannerSessionCount = signal(0);
  readonly nameSuggestions = signal<string[]>([]);
  readonly barcodeSuggestions = signal<string[]>([]);
  readonly highlightedRowId = signal<string | null>(null);
  readonly supplierSuggestions = signal<string[]>([]);
  readonly taxModeOptions = signal([
    { label: 'With Tax', value: true },
    { label: 'Without Tax', value: false },
  ]);
  readonly suppliers = this.suppliersFacade.suppliers;

  // Shared PT configurations
  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;
  readonly currencySelectPt = CURRENCY_SELECT_PT;

  // PassThrough configurations for PrimeNG components
  readonly editButtonPt = {
    root: {
      class: 'edit-row-button',
    },
  };
  readonly removeButtonPt = {
    root: {
      class: 'remove-row-button',
    },
  };
  readonly cameraButtonPt = {
    root: {
      class: 'camera-button',
    },
  };
  readonly barcodeAutocomplePt = {
    root: {
      class: 'barcode-autocomplete',
    },
  };
  readonly barcodeInputGroupPt = {
    root: {
      class: 'barcode-input-group',
    },
  };
  readonly cameraddonPt = {
    root: {
      class: 'camera-addon',
    },
  };

  readonly activeShopId = computed(() => this.authService.session()?.activeShopId ?? '');
  readonly tableRows = computed(() => [...this.pendingRows()]);
  readonly failedClientRowIds = computed(() => {
    const summary = this.saveSummary();
    if (!summary) {
      return new Set<string>();
    }

    return new Set(summary.failed.map((row) => row.clientRowId));
  });
  readonly failedRowErrorTextById = computed(() => {
    const summary = this.saveSummary();
    if (!summary) {
      return new Map<string, string>();
    }

    const entries = summary.failed.map((row) => [
      row.clientRowId,
      row.errors.map((error) => error.description).join(', '),
    ] as const);

    return new Map<string, string>(entries);
  });

  readonly form = this.formBuilder.nonNullable.group({
    itemName: ['', [Validators.required, Validators.maxLength(180)]],
    barcode: ['', [Validators.required, Validators.maxLength(120)]],
    itemDescription: ['', [Validators.maxLength(320)]],
    uom: ['', [Validators.required, Validators.maxLength(40)]],
    batchNumber: ['', [Validators.required, Validators.maxLength(80)]],
    quantity: [1, [Validators.required, Validators.min(0.0001)]],
    costPrice: [0, [Validators.required, Validators.min(0)]],
    mrp: [0, [Validators.required, Validators.min(0)]],
    salesPrice: [0, [Validators.required, Validators.min(0)]],
    taxRatePercent: [0, [Validators.required, Validators.min(0)]],
    taxIncluded: [false, [Validators.required]],
    expiryDate: [''],
    manufacturingDate: [''],
    supplierName: [''],
    referenceNumber: ['', [Validators.maxLength(120)]],
    notes: ['', [Validators.maxLength(320)]],
  });

  private highlightTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    effect(() => {
      const shopId = this.activeShopId();
      if (!shopId) {
        this.pendingRows.set([]);
        return;
      }

      this.suppliersFacade.load();
      void this.loadDraftRows(shopId);
    });

    this.form.controls.batchNumber.setValue(this.generateBatchNumber());
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

  openScanner(): void {
    this.scannerLastAction.set('');
    this.scannerSessionCount.set(0);
    this.isScannerOpen.set(true);
    void this.audioService.prime();
  }

  onScannerVisibilityChange(visible: boolean): void {
    this.isScannerOpen.set(visible);
  }

  async handleScannedBarcode(detection: BarcodeDetection): Promise<void> {
    const barcode = detection.value.trim();
    if (!barcode) {
      return;
    }

    this.scannerSessionCount.update((count) => count + 1);

    const mergedRow = await this.incrementExistingRowQuantity(barcode);
    if (mergedRow) {
      this.scannerLastAction.set('inventory.scannerActionIncremented');
      this.showScannerToast('inventory.scannerMerged', barcode, mergedRow.quantity);
      void this.audioService.beep();
      return;
    }

    const catalogEntry = this.catalogSync.findByBarcode(barcode);
    this.form.controls.barcode.setValue(barcode);
    this.form.controls.barcode.markAsDirty();

    if (catalogEntry) {
      this.form.controls.itemName.setValue(catalogEntry.name);
    }

    await this.fetchProductDetails({ showInfoToast: false, showErrorToast: false });

    if (this.canAutoAddScannedRow() && this.tryAddScannedRow()) {
      this.scannerLastAction.set('inventory.scannerActionAdded');
      this.showScannerToast('inventory.scannerAdded', barcode, 1);
      void this.audioService.beep();
      return;
    }

    this.form.markAllAsTouched();
    this.scannerLastAction.set('inventory.scannerActionReview');
    this.showWarn('inventory.scannerNeedsReview');
    this.isScannerOpen.set(false);
  }

  private async fetchProductDetails(options?: { showInfoToast?: boolean; showErrorToast?: boolean }): Promise<void> {
    const showInfoToast = options?.showInfoToast ?? true;
    const showErrorToast = options?.showErrorToast ?? true;
    const itemName = this.form.controls.itemName.value?.trim();
    const barcode = this.form.controls.barcode.value?.trim();

    // Barcode-driven lookup should still call API even when local catalog misses itemName.
    if (!barcode) {
      return;
    }

    this.loadingProduct.set(true);

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
      }
    } catch (error) {
      if (showErrorToast) {
        this.showError('inventory.productDetailsLoadError');
      }
    } finally {
      this.loadingProduct.set(false);
    }
  }

  private canAutoAddScannedRow(): boolean {
    const itemName = this.form.controls.itemName.value.trim();
    const barcode = this.form.controls.barcode.value.trim();
    const uom = this.form.controls.uom.value.trim();
    const costPrice = Number(this.form.controls.costPrice.value);
    const mrp = Number(this.form.controls.mrp.value);
    const salesPrice = Number(this.form.controls.salesPrice.value);

    return (
      itemName.length > 0
      && barcode.length > 0
      && uom.length > 0
      && costPrice > 0
      && mrp > 0
      && salesPrice > 0
    );
  }

  private showInfo(messageKey: string): void {
    this.messageService.add({
      severity: 'info',
      summary: this.translate(messageKey),
      life: 2500,
    });
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
    if (supplier) {
      this.form.controls.supplierName.setValue(supplier.name);
    }
  }

  onAddRow(): void {
    if (!this.tryAddScannedRow()) {
      this.form.markAllAsTouched();
    }
  }

  pendingRowInitials(name: string): string {
    const words = name.trim().split(/\s+/);
    if (words.length === 1) return words[0].substring(0, 2).toUpperCase();
    return (words[0][0] + words[1][0]).toUpperCase();
  }

  pendingRowAvatarColor(name: string): string {
    const colors = [
      '#b45309', '#0369a1', '#15803d', '#7c3aed',
      '#be185d', '#c2410c', '#0f766e', '#1d4ed8',
    ];
    let hash = 0;
    for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash);
    return colors[Math.abs(hash) % colors.length];
  }

  onRemoveRow(clientRowId: string): void {
    const updatedRows = this.pendingRows().filter((row) => row.clientRowId !== clientRowId);
    this.saveSummary.set(null);
    this.pendingRows.set(updatedRows);
    void this.persistRows(this.activeShopId(), updatedRows);
  }

  onEditRow(clientRowId: string): void {
    const row = this.pendingRows().find((candidate) => candidate.clientRowId === clientRowId);
    if (!row) {
      return;
    }

    this.form.setValue({
      itemName: row.itemName,
      barcode: row.barcode,
      itemDescription: row.itemDescription ?? '',
      uom: row.uom,
      batchNumber: row.batchNumber,
      quantity: row.quantity,
      costPrice: row.costPrice,
      mrp: row.mrp,
      salesPrice: row.salesPrice,
      taxRatePercent: row.taxRatePercent,
      taxIncluded: row.taxIncluded,
      expiryDate: row.expiryDate ?? '',
      manufacturingDate: row.manufacturingDate ?? '',
      supplierName:
        this.getSupplierDisplayName(row.supplierId) === '-'
          ? ''
          : this.getSupplierDisplayName(row.supplierId),
      referenceNumber: row.referenceNumber ?? '',
      notes: row.notes ?? '',
    });

    const updatedRows = this.pendingRows().filter(
      (candidate) => candidate.clientRowId !== clientRowId,
    );
    this.saveSummary.set(null);
    this.pendingRows.set(updatedRows);
    void this.persistRows(this.activeShopId(), updatedRows);
  }

  onClearAll(): void {
    this.saveSummary.set(null);
    this.pendingRows.set([]);
    void this.draftStorage.clearRows(this.activeShopId());
  }

  onSaveAll(): void {
    if (this.isSaving() || this.pendingRows().length === 0) {
      return;
    }

    this.isSaving.set(true);
    void this.saveRowsInChunks();
  }

  private async saveRowsInChunks(): Promise<void> {
    const rows = [...this.pendingRows()];
    const chunkSize = 100;
    const succeeded: AddInventoryBatchSucceededRow[] = [];
    const failed: AddInventoryBatchFailedRow[] = [];

    try {
      for (let index = 0; index < rows.length; index += chunkSize) {
        const chunk = rows.slice(index, index + chunkSize);
        const payload = this.mapRowsToRequest(chunk);
        const response = await firstValueFrom(
          this.inventoryService.addInventoryBatch({ items: payload }),
        );

        succeeded.push(...response.succeeded);
        failed.push(...response.failed);
      }

      const summary: AddInventoryBatchResponse = {
        requestedCount: rows.length,
        successCount: succeeded.length,
        failedCount: failed.length,
        succeeded,
        failed,
      };

      this.saveSummary.set(summary);
      if (summary.failedCount === 0) {
        this.pendingRows.set([]);
        await this.draftStorage.clearRows(this.activeShopId());
        this.showSuccess('inventory.savedSuccess', summary.successCount, summary.requestedCount);
        return;
      }

      const failedIds = new Set(summary.failed.map((row) => row.clientRowId));
      const remainingRows = rows.filter((row) => failedIds.has(row.clientRowId));
      this.pendingRows.set(remainingRows);
      await this.persistRows(this.activeShopId(), remainingRows);
      this.showWarn('inventory.savedPartial');
    } catch (error) {
      const succeededIds = new Set(succeeded.map((row) => row.clientRowId));
      const failedIds = new Set(failed.map((row) => row.clientRowId));
      const remainingRows = rows.filter(
        (row) => !succeededIds.has(row.clientRowId) || failedIds.has(row.clientRowId),
      );

      this.pendingRows.set(remainingRows);
      await this.persistRows(this.activeShopId(), remainingRows);

      if (succeeded.length > 0 || failed.length > 0) {
        this.saveSummary.set({
          requestedCount: rows.length,
          successCount: succeeded.length,
          failedCount: failed.length,
          succeeded,
          failed,
        });
      }

      const apiErrors = this.extractApiErrors(error);
      const hasBatchLimitError = apiErrors.some(
        (apiError) => this.getErrorCode(apiError) === 'Inventory.BatchLimitExceeded',
      );

      if (hasBatchLimitError) {
        this.showWarn('inventory.batchLimitReached');
      } else {
        const firstDetail = apiErrors
          .map((apiError) => this.getErrorDescription(apiError))
          .find((detail) => detail.length > 0);

        if (firstDetail) {
          this.showErrorWithDetail('inventory.saveFailed', firstDetail);
        } else {
          this.showError('inventory.saveFailed');
        }
      }
    } finally {
      this.isSaving.set(false);
    }
  }

  private mapRowsToRequest(rows: readonly InventoryInboundDraftRow[]): readonly AddInventoryBatchRowRequest[] {
    return rows.map((row) => ({
      clientRowId: row.clientRowId,
      itemName: row.itemName,
      barcode: row.barcode,
      itemDescription: row.itemDescription,
      uom: row.uom,
      batchNumber: row.batchNumber,
      quantity: row.quantity,
      costPrice: row.costPrice,
      mrp: row.mrp,
      salesPrice: row.salesPrice,
      taxRatePercent: row.taxRatePercent,
      taxIncluded: row.taxIncluded,
      expiryDate: row.expiryDate,
      manufacturingDate: row.manufacturingDate,
      supplierId: row.supplierId,
      referenceNumber: row.referenceNumber,
      notes: row.notes,
      performedAt: new Date().toISOString(),
    }));
  }

  private async loadDraftRows(shopId: string): Promise<void> {
    this.loadingDraft.set(true);
    try {
      const rows = await this.draftStorage.loadRows(shopId);
      this.pendingRows.set(rows);
      if (rows.length > 0) {
        this.showInfo('inventory.draftLoaded');
      }
    } finally {
      this.loadingDraft.set(false);
    }
  }

  private async persistRows(
    shopId: string,
    rows: readonly InventoryInboundDraftRow[],
  ): Promise<void> {
    if (!shopId) {
      return;
    }

    if (rows.length === 0) {
      await this.draftStorage.clearRows(shopId);
      return;
    }

    await this.draftStorage.saveRows(shopId, rows);
  }

  private buildProductDetailsPatch(details: {
    name: string;
    description: string;
    uom: string;
    costPrice: number;
    mrp: number;
    salesPrice: number;
    supplierName: string | null;
    taxIncluded: boolean | null;
    taxRatePercent: number | null;
  }): Partial<{
    itemName: string;
    itemDescription: string;
    uom: string;
    costPrice: number;
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
      costPrice: number;
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

    if (!this.form.controls.costPrice.dirty) {
      patch.costPrice = details.costPrice;
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

  getSupplierDisplayName(supplierId: string | null): string {
    if (!supplierId) {
      return '-';
    }

    return (
      this.suppliers().find((supplier) => supplier.supplierId === supplierId)?.name ?? supplierId
    );
  }

  private findSupplierByName(name: string): Supplier | undefined {
    const normalized = name.trim().toLowerCase();
    return this.suppliers().find((supplier) => supplier.name.toLowerCase() === normalized);
  }

  private async incrementExistingRowQuantity(
    barcode: string,
  ): Promise<InventoryInboundDraftRow | null> {
    const matchingRow = this.pendingRows().find((row) => row.barcode === barcode);
    if (!matchingRow) {
      return null;
    }

    const updatedRow = {
      ...matchingRow,
      quantity: Number((matchingRow.quantity + 1).toFixed(3)),
    } satisfies InventoryInboundDraftRow;
    const updatedRows = this.pendingRows().map((row) =>
      row.clientRowId === matchingRow.clientRowId ? updatedRow : row,
    );

    this.saveSummary.set(null);
    this.pendingRows.set(updatedRows);
    this.flashRow(updatedRow.clientRowId);
    await this.persistRows(this.activeShopId(), updatedRows);
    return updatedRow;
  }

  private tryAddScannedRow(): boolean {
    const row = this.buildDraftRow();
    if (!row) {
      return false;
    }

    const updatedRows = [...this.pendingRows(), row];
    this.saveSummary.set(null);
    this.pendingRows.set(updatedRows);
    this.flashRow(row.clientRowId);
    void this.persistRows(this.activeShopId(), updatedRows);
    this.resetForm();
    return true;
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
      costPrice: Number(this.form.controls.costPrice.value),
      mrp: Number(this.form.controls.mrp.value),
      salesPrice: Number(this.form.controls.salesPrice.value),
      taxRatePercent: Number(this.form.controls.taxRatePercent.value),
      taxIncluded: this.form.controls.taxIncluded.value,
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
      costPrice: 0,
      mrp: 0,
      salesPrice: 0,
      taxRatePercent: 0,
      taxIncluded: false,
      expiryDate: '',
      manufacturingDate: '',
      supplierName: '',
      referenceNumber: '',
      notes: '',
    });

    this.form.controls.batchNumber.setValue(this.generateBatchNumber());
  }

  private flashRow(clientRowId: string): void {
    this.highlightedRowId.set(clientRowId);
    if (this.highlightTimer) {
      clearTimeout(this.highlightTimer);
    }

    this.highlightTimer = setTimeout(() => {
      this.highlightedRowId.set(null);
      this.highlightTimer = null;
    }, 1600);
  }

  private generateBatchNumber(): string {
    const date = new Date().toISOString().slice(0, 10).replace(/-/g, '');
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

  private showSuccess(messageKey: string, successCount: number, requestedCount: number): void {
    this.messageService.add({
      severity: 'success',
      summary: this.translate(messageKey),
      detail: `${successCount}/${requestedCount}`,
      life: 3000,
    });
  }

  private showWarn(messageKey: string): void {
    this.messageService.add({
      severity: 'warn',
      summary: this.translate(messageKey),
      life: 3000,
    });
  }

  private showError(messageKey: string): void {
    this.messageService.add({
      severity: 'error',
      summary: this.translate(messageKey),
      life: 3500,
    });
  }

  private showErrorWithDetail(messageKey: string, detail: string): void {
    this.messageService.add({
      severity: 'error',
      summary: this.translate(messageKey),
      detail,
      life: 3500,
    });
  }

  private extractApiErrors(error: unknown): readonly Record<string, unknown>[] {
    if (!error || typeof error !== 'object') {
      return [];
    }

    const candidate = (error as { error?: unknown }).error;
    if (!candidate || typeof candidate !== 'object') {
      return [];
    }

    const rawErrors = (candidate as { errors?: unknown }).errors;
    if (!Array.isArray(rawErrors)) {
      return [];
    }

    return rawErrors.filter(
      (entry): entry is Record<string, unknown> => !!entry && typeof entry === 'object',
    );
  }

  private getErrorCode(apiError: Record<string, unknown>): string {
    const code = apiError['code'] ?? apiError['Code'];
    return typeof code === 'string' ? code : '';
  }

  private getErrorDescription(apiError: Record<string, unknown>): string {
    const description = apiError['description'] ?? apiError['Description'];
    return typeof description === 'string' ? description : '';
  }

  private showScannerToast(summaryKey: string, barcode: string, quantity: number): void {
    this.messageService.add({
      severity: 'success',
      summary: this.translate(summaryKey),
      detail: `${barcode} • Qty ${quantity}`,
      life: 2200,
    });
  }

  private translate(key: string): string {
    return this.translocoService.translate(key);
  }
}
