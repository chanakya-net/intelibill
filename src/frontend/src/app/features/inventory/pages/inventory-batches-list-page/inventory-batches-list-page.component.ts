import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { AvatarModule } from 'primeng/avatar';
import { DialogModule } from 'primeng/dialog';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { Table, TableModule } from 'primeng/table';
import { ToastModule } from 'primeng/toast';
import { TagModule } from 'primeng/tag';
import { AutoCompleteModule, AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { SelectModule } from 'primeng/select';

import { finalize } from 'rxjs';

import {
  InventoryBatchDto,
  InventoryService,
  UpdateInventoryBatchRequest,
} from '../../services/inventory.service';
import { SuppliersFacade } from '../../../suppliers/state/suppliers.facade';
import { Supplier } from '../../../suppliers/services/supplier.service';

@Component({
  selector: 'app-inventory-batches-list-page',
  standalone: true,
  imports: [
    FormsModule,
    ReactiveFormsModule,
    TranslocoPipe,
    DecimalPipe,
    ButtonModule,
    CardModule,
    AvatarModule,
    DialogModule,
    IconFieldModule,
    InputIconModule,
    InputGroupAddonModule,
    InputGroupModule,
    InputNumberModule,
    InputTextModule,
    TableModule,
    ToastModule,
    TagModule,
    AutoCompleteModule,
    SelectModule,
  ],
  providers: [MessageService],
  templateUrl: './inventory-batches-list-page.component.html',
  styleUrl: './inventory-batches-list-page.component.scss',
})
export class InventoryBatchesListPageComponent {
  private readonly inventoryService = inject(InventoryService);
  private readonly suppliersFacade = inject(SuppliersFacade);
  private readonly formBuilder = inject(FormBuilder);
  private readonly messageService = inject(MessageService);
  private readonly translocoService = inject(TranslocoService);

  readonly batches = signal<InventoryBatchDto[]>([]);
  readonly tableBatches = computed(() => [...this.batches()]);
  readonly filteredBatches = computed(() => {
    const q = this.searchValue().toLowerCase();
    if (!q) return [...this.batches()];
    return this.batches().filter(
      (b) =>
        b.itemName.toLowerCase().includes(q) ||
        b.barcode.toLowerCase().includes(q) ||
        b.batchNumber.toLowerCase().includes(q),
    );
  });
  readonly loading = signal(false);
  readonly isEditDialogOpen = signal(false);
  readonly isSaving = signal(false);
  readonly selectedBatch = signal<InventoryBatchDto | null>(null);
  readonly supplierSuggestions = signal<string[]>([]);
  readonly searchValue = signal('');
  readonly suppliers = this.suppliersFacade.suppliers;
  readonly taxModeOptions = signal([
    { label: 'With Tax', value: true },
    { label: 'Without Tax', value: false },
  ]);

  readonly editForm = this.formBuilder.nonNullable.group({
    newBatchNumber: ['', [Validators.maxLength(80)]],
    quantity: [0, [Validators.required, Validators.min(0)]],
    costPrice: [0, [Validators.required, Validators.min(0)]],
    mrp: [0, [Validators.required, Validators.min(0)]],
    salesPrice: [0, [Validators.required, Validators.min(0)]],
    taxRatePercent: [0, [Validators.required, Validators.min(0)]],
    taxIncluded: [false, [Validators.required]],
    expiryDate: [''],
    manufacturingDate: [''],
    supplierName: [''],
    notes: [''],
    entryDate: [''],
  });

  constructor() {
    this.suppliersFacade.load();
    this.loadBatches();
  }

  loadBatches(): void {
    this.loading.set(true);
    this.inventoryService
      .getInventoryBatches()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (data) => this.batches.set([...data]),
        error: () => this.showError('inventory.loadBatchesError'),
      });
  }

  onEditBatch(batch: InventoryBatchDto): void {
    this.selectedBatch.set(batch);
    this.editForm.patchValue({
      newBatchNumber: '', // Start empty for user to provide or system to generate
      quantity: batch.quantity,
      costPrice: batch.costPrice,
      mrp: batch.mrp,
      salesPrice: batch.salesPrice,
      taxRatePercent: batch.taxRatePercent,
      taxIncluded: batch.taxIncluded,
      expiryDate: batch.expiryDate ?? '',
      manufacturingDate: batch.manufacturingDate ?? '',
      supplierName: batch.supplierName ?? this.getSupplierDisplayName(batch.supplierId),
      notes: '',
      entryDate: new Date().toISOString().split('T')[0],
    });
    this.isEditDialogOpen.set(true);
  }

  onSaveEdit(): void {
    const batch = this.selectedBatch();
    if (!batch || this.editForm.invalid) return;

    this.isSaving.set(true);
    const formValue = this.editForm.getRawValue();
    const payload: UpdateInventoryBatchRequest = {
      newBatchNumber: this.nullable(formValue.newBatchNumber),
      quantity: formValue.quantity,
      costPrice: formValue.costPrice,
      mrp: formValue.mrp,
      salesPrice: formValue.salesPrice,
      taxRatePercent: formValue.taxRatePercent,
      taxIncluded: formValue.taxIncluded,
      expiryDate: this.nullable(formValue.expiryDate),
      manufacturingDate: this.nullable(formValue.manufacturingDate),
      supplierId: this.resolveSupplierId(formValue.supplierName),
      notes: this.nullable(formValue.notes) ?? 'Batch Correction',
      entryDate: this.nullable(formValue.entryDate),
    };

    this.inventoryService
      .updateInventoryBatch(batch.id, payload)
      .pipe(finalize(() => this.isSaving.set(false)))
      .subscribe({
        next: () => {
          this.showSuccess('inventory.batchCorrected');
          this.isEditDialogOpen.set(false);
          this.loadBatches();
        },
        error: (err) => {
          const detail = err.error?.detail || this.translate('inventory.updateBatchError');
          this.messageService.add({ severity: 'error', summary: 'Error', detail });
        },
      });
  }

  onVoidBatch(batchId: string): void {
    this.inventoryService.voidInventoryBatch(batchId).subscribe({
      next: () => {
        this.showSuccess('inventory.batchVoided');
        this.loadBatches();
      },
      error: () => this.showError('inventory.voidBatchError'),
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

  getSupplierDisplayName(supplierId: string | null): string {
    if (!supplierId) return '-';
    return (
      this.suppliers().find((supplier) => supplier.supplierId === supplierId)?.name ?? supplierId
    );
  }

  private findSupplierByName(name: string): Supplier | undefined {
    const normalized = name.trim().toLowerCase();
    return this.suppliers().find((supplier) => supplier.name.toLowerCase() === normalized);
  }

  private resolveSupplierId(value: string): string | null {
    const normalized = value.trim();
    if (!normalized) return null;
    return this.findSupplierByName(normalized)?.supplierId ?? null;
  }

  private nullable(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }

  private showSuccess(messageKey: string): void {
    this.messageService.add({
      severity: 'success',
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

  private translate(key: string): string {
    return this.translocoService.translate(key);
  }

  productInitials(name: string): string {
    const words = name.trim().split(/\s+/);
    if (words.length === 1) return words[0].substring(0, 2).toUpperCase();
    return (words[0][0] + words[1][0]).toUpperCase();
  }

  productAvatarColor(name: string): string {
    const colors = [
      '#b45309', '#0369a1', '#15803d', '#7c3aed',
      '#be185d', '#c2410c', '#0f766e', '#1d4ed8',
    ];
    let hash = 0;
    for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash);
    return colors[Math.abs(hash) % colors.length];
  }

  clearFilters(table: Table): void {
    table.clear();
    this.searchValue.set('');
  }
}
