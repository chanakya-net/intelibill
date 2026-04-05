import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { AutoCompleteModule, AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';
import { ToastModule } from 'primeng/toast';

import { AuthService } from '../../../core/auth/auth.service';
import {
  AddInventoryBatchResponse,
  AddInventoryBatchRowRequest,
  InventoryService,
} from '../services/inventory.service';
import {
  InventoryDraftIndexedDbService,
  InventoryInboundDraftRow,
} from '../../../core/storage/inventory-draft-indexeddb.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { Supplier } from '../../suppliers/services/supplier.service';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';

@Component({
  selector: 'app-inventory-batch-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslocoPipe,
    AutoCompleteModule,
    ButtonModule,
    InputTextModule,
    InputNumberModule,
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
  private readonly inventoryService = inject(InventoryService);
  private readonly draftStorage = inject(InventoryDraftIndexedDbService);
  private readonly messageService = inject(MessageService);
  private readonly translocoService = inject(TranslocoService);
  private readonly catalogSync = inject(ProductCatalogSyncService);
  private readonly suppliersFacade = inject(SuppliersFacade);

  readonly isSaving = signal(false);
  readonly pendingRows = signal<readonly InventoryInboundDraftRow[]>([]);
  readonly saveSummary = signal<AddInventoryBatchResponse | null>(null);
  readonly loadingDraft = signal(false);
  readonly nameSuggestions = signal<string[]>([]);
  readonly barcodeSuggestions = signal<string[]>([]);
  readonly supplierSuggestions = signal<string[]>([]);
  readonly suppliers = this.suppliersFacade.suppliers;

  readonly activeShopId = computed(() => this.authService.session()?.activeShopId ?? '');
  readonly tableRows = computed(() => [...this.pendingRows()]);
  readonly failedClientRowIds = computed(() => {
    const summary = this.saveSummary();
    if (!summary) {
      return new Set<string>();
    }

    return new Set(summary.failed.map((row) => row.clientRowId));
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
    minSalePrice: [0, [Validators.required, Validators.min(0)]],
    taxRatePercent: [0, [Validators.required, Validators.min(0)]],
    expiryDate: [''],
    manufacturingDate: [''],
    supplierName: [''],
    referenceNumber: ['', [Validators.maxLength(120)]],
    notes: ['', [Validators.maxLength(320)]],
  });

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
    this.barcodeSuggestions.set(this.catalogSync.filterByBarcode(event.query).map((e) => e.barcode));
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
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    if (this.pendingRows().length >= 100) {
      this.showWarn('inventory.batchLimitReached');
      return;
    }

    const supplierId = this.resolveSupplierId(this.form.controls.supplierName.value);

    const row: InventoryInboundDraftRow = {
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
      minSalePrice: Number(this.form.controls.minSalePrice.value),
      taxRatePercent: Number(this.form.controls.taxRatePercent.value),
      expiryDate: this.nullable(this.form.controls.expiryDate.value),
      manufacturingDate: this.nullable(this.form.controls.manufacturingDate.value),
      supplierId,
      referenceNumber: this.nullable(this.form.controls.referenceNumber.value),
      notes: this.nullable(this.form.controls.notes.value),
      performedAt: new Date().toISOString(),
    };

    const updatedRows = [...this.pendingRows(), row];
    this.saveSummary.set(null);
    this.pendingRows.set(updatedRows);
    void this.persistRows(this.activeShopId(), updatedRows);

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
      minSalePrice: 0,
      taxRatePercent: 0,
      expiryDate: '',
      manufacturingDate: '',
      supplierName: '',
      referenceNumber: '',
      notes: '',
    });

    this.form.controls.batchNumber.setValue(this.generateBatchNumber());
  }

  onRemoveRow(clientRowId: string): void {
    const updatedRows = this.pendingRows().filter((row) => row.clientRowId !== clientRowId);
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

    const payload: readonly AddInventoryBatchRowRequest[] = this.pendingRows().map((row) => ({
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
      minSalePrice: row.minSalePrice,
      taxRatePercent: row.taxRatePercent,
      expiryDate: row.expiryDate,
      manufacturingDate: row.manufacturingDate,
      supplierId: row.supplierId,
      referenceNumber: row.referenceNumber,
      notes: row.notes,
      performedAt: new Date().toISOString(),
    }));

    this.inventoryService.addInventoryBatch({ items: payload }).subscribe({
      next: (response) => {
        this.saveSummary.set(response);
        if (response.failedCount === 0) {
          this.pendingRows.set([]);
          void this.draftStorage.clearRows(this.activeShopId());
          this.showSuccess('inventory.savedSuccess', response.successCount, response.requestedCount);
        } else {
          const failedIds = new Set(response.failed.map((row) => row.clientRowId));
          const remainingRows = this.pendingRows().filter((row) => failedIds.has(row.clientRowId));
          this.pendingRows.set(remainingRows);
          void this.persistRows(this.activeShopId(), remainingRows);
          this.showWarn('inventory.savedPartial');
        }
      },
      error: () => {
        this.showError('inventory.saveFailed');
        this.isSaving.set(false);
      },
      complete: () => {
        this.isSaving.set(false);
      },
    });
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

  private async persistRows(shopId: string, rows: readonly InventoryInboundDraftRow[]): Promise<void> {
    if (!shopId) {
      return;
    }

    if (rows.length === 0) {
      await this.draftStorage.clearRows(shopId);
      return;
    }

    await this.draftStorage.saveRows(shopId, rows);
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

    return this.suppliers().find((supplier) => supplier.supplierId === supplierId)?.name ?? supplierId;
  }

  private findSupplierByName(name: string): Supplier | undefined {
    const normalized = name.trim().toLowerCase();
    return this.suppliers().find((supplier) => supplier.name.toLowerCase() === normalized);
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

  private showInfo(messageKey: string): void {
    this.messageService.add({
      severity: 'info',
      summary: this.translate(messageKey),
      life: 2500,
    });
  }

  private translate(key: string): string {
    return this.translocoService.translate(key);
  }
}
