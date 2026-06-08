import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { firstValueFrom } from 'rxjs';

import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryBarcodeFieldComponent } from '../../inventory/components/barcode-field.component';
import {
  PurchaseOrderDetail,
  PurchaseOrderLine,
  ReceivePurchaseOrderRequest,
} from '../services/purchase-order.service';
import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';
import { InventoryBatchDefaultsService } from '../../inventory/services/inventory-batch-defaults.service';
import { InventoryService } from '../../inventory/services/inventory.service';

@Component({
  selector: 'app-receive-purchase-order-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslocoPipe,
    ButtonModule,
    CheckboxModule,
    DatePickerModule,
    DialogModule,
    InputGroupAddonModule,
    InputGroupModule,
    InputNumberModule,
    InputTextModule,
    SelectModule,
    TextareaModule,
    InventoryBarcodeFieldComponent,
  ],
  template: `
    <p-dialog
      [header]="'purchaseOrders.receiveDialog.title' | transloco"
      [(visible)]="visible"
      [modal]="true"
      [style]="{ width: 'min(720px, 96vw)' }"
      (onHide)="closed.emit()"
    >
      <form class="receive-form" [formGroup]="form" (ngSubmit)="submit()">
        <label>
          {{ 'purchaseOrders.receiveDialog.reference' | transloco }}
          <input pInputText formControlName="referenceNumber" />
        </label>
        <label class="full-width">
          {{ 'purchaseOrders.receiveDialog.notes' | transloco }}
          <textarea pTextarea formControlName="notes"></textarea>
        </label>
        <label class="full-width">
          {{ 'purchaseOrders.receiveDialog.searchLines' | transloco }}
          <input
            pInputText
            data-testid="receive-line-search"
            [value]="lineSearch()"
            (input)="lineSearch.set($any($event.target).value)"
          />
        </label>
        <div class="receipt-lines" formArrayName="lines">
          @for (index of filteredLineIndexes(); track index) {
            @let row = lines.at(index);
            <div class="receipt-row" data-testid="receipt-row" [formGroupName]="index">
              <label>
                {{ 'purchaseOrders.receiveDialog.line' | transloco }}
                <p-select
                  formControlName="purchaseOrderLineId"
                  [options]="receivableLines"
                  optionLabel="description"
                  optionValue="lineId"
                  [filter]="true"
                  filterBy="description"
                  [placeholder]="'purchaseOrders.receiveDialog.selectLine' | transloco"
                  (onChange)="onLineSelectionChange(index)"
                />
              </label>
              <app-inventory-barcode-field
                [control]="row.controls.barcode"
                [suggestions]="barcodeSuggestionsFor(index)"
                [barcodeGenerating]="isBarcodeGenerating(index)"
                [barcodeGenerateError]="barcodeGenerateErrorFor(index)"
                [barcodeReplaceConfirmVisible]="isBarcodeReplaceConfirmVisible(index)"
                [showScanner]="false"
                (filterRequested)="onBarcodeFilter(index, $event)"
                (selected)="onBarcodeSelected(index, $event)"
                (generateRequested)="generateBarcode(index)"
                (confirmReplace)="confirmBarcodeReplace(index)"
                (cancelReplace)="cancelBarcodeReplace(index)"
              />
              <label>
                {{ 'purchaseOrders.receiveDialog.batchNumber' | transloco }}
                <p-inputgroup>
                  <input pInputText formControlName="batchNumber" />
                  <p-inputgroup-addon>
                    <button
                      pButton
                      type="button"
                      icon="pi pi-refresh"
                      severity="secondary"
                      text
                      (click)="generateBatchNumber(index)"
                      [attr.aria-label]="'purchaseOrders.receiveDialog.generateBatchNumber' | transloco"
                    ></button>
                  </p-inputgroup-addon>
                </p-inputgroup>
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.quantity' | transloco }}
                <p-inputNumber formControlName="quantity" [min]="1" [max]="remainingFor(row.controls.purchaseOrderLineId.value)" />
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.totalPurchaseCost' | transloco }}
                <p-inputNumber formControlName="totalPurchaseCost" mode="decimal" [min]="0" [minFractionDigits]="2" />
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.mrp' | transloco }}
                <p-inputNumber formControlName="mrp" mode="decimal" [min]="0" [minFractionDigits]="2" />
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.salesPrice' | transloco }}
                <p-inputNumber formControlName="salesPrice" mode="decimal" [min]="0" [minFractionDigits]="2" />
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.taxRate' | transloco }}
                <p-inputgroup>
                  <p-inputNumber formControlName="taxRatePercent" mode="decimal" [min]="0" [max]="100" />
                  <p-inputgroup-addon>
                    <button
                      pButton
                      type="button"
                      icon="pi pi-search"
                      severity="secondary"
                      text
                      [loading]="isTaxLookupLoading(index)"
                      (click)="lookupTaxForLine(index)"
                      [attr.aria-label]="'purchaseOrders.receiveDialog.lookupTaxRate' | transloco"
                    ></button>
                  </p-inputgroup-addon>
                </p-inputgroup>
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.expiryDate' | transloco }}
                <p-datepicker
                  ngSkipHydration
                  formControlName="expiryDate"
                  dateFormat="dd/mm/yy"
                  [showIcon]="true"
                  [showButtonBar]="true"
                  appendTo="body"
                />
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.manufacturingDate' | transloco }}
                <p-datepicker
                  ngSkipHydration
                  formControlName="manufacturingDate"
                  dateFormat="dd/mm/yy"
                  [showIcon]="true"
                  [showButtonBar]="true"
                  appendTo="body"
                />
              </label>
              <label class="checkbox-row">
                <p-checkbox formControlName="taxIncluded" [binary]="true" />
                {{ 'purchaseOrders.receiveDialog.taxIncluded' | transloco }}
              </label>
              <label class="checkbox-row">
                <p-checkbox formControlName="purchaseTaxIncluded" [binary]="true" />
                {{ 'purchaseOrders.receiveDialog.purchaseTaxIncluded' | transloco }}
              </label>
              <button pButton type="button" severity="secondary" [label]="'purchaseOrders.receiveDialog.removeLine' | transloco" (click)="removeLine(index)" [disabled]="lines.length === 1"></button>
            </div>
          }
        </div>
        <button pButton type="button" severity="secondary" [label]="'purchaseOrders.receiveDialog.addLine' | transloco" (click)="addLine()"></button>
        @if (remainingError()) {
          <p class="error">{{ 'purchaseOrders.receiveDialog.quantityOverRemaining' | transloco }}</p>
        }
        @if (duplicateLineError()) {
          <p class="error">{{ 'purchaseOrders.receiveDialog.duplicateLine' | transloco }}</p>
        }
        <div class="actions">
          <button pButton type="button" severity="secondary" [label]="'purchaseOrders.actions.cancel' | transloco" (click)="hide()"></button>
          <button pButton type="submit" [label]="'purchaseOrders.actions.receive' | transloco" [disabled]="form.invalid || remainingError() || duplicateLineError() || lines.length === 0 || submitting"></button>
        </div>
      </form>
    </p-dialog>
  `,
  styles: [`
    .receive-form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: .75rem; }
    label { display: grid; gap: .25rem; font-size: .875rem; }
    .full-width, .actions, .error, .receipt-lines { grid-column: 1 / -1; }
    .receipt-lines { display: grid; gap: .75rem; }
    .receipt-row { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: .75rem; padding: .75rem; border: 1px solid var(--p-content-border-color); border-radius: 6px; }
    .checkbox-row { display: flex; align-items: center; gap: .5rem; }
    .actions { display: flex; justify-content: flex-end; gap: .5rem; margin-top: .5rem; }
    .error { color: var(--p-red-600); margin: 0; }
    @media (max-width: 640px) { .receive-form { grid-template-columns: 1fr; } }
  `],
})
export class ReceivePurchaseOrderDialogComponent implements OnChanges, OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly batchDefaults = inject(InventoryBatchDefaultsService);
  private readonly inventoryService = inject(InventoryService);
  private readonly catalogSync = inject(ProductCatalogSyncService);

  @Input({ required: true }) order!: PurchaseOrderDetail;
  @Input() visible = false;
  @Input() submitting = false;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() closed = new EventEmitter<void>();
  @Output() receive = new EventEmitter<ReceivePurchaseOrderRequest>();

  protected receivableLines: PurchaseOrderLine[] = [];
  protected readonly taxLookupLoading = signal<Record<number, boolean>>({});
  protected readonly lineSearch = signal('');
  protected readonly barcodeSuggestions = signal<Record<number, readonly string[]>>({});
  protected readonly barcodeGenerating = signal<Record<number, boolean>>({});
  protected readonly barcodeGenerateErrors = signal<Record<number, string>>({});
  protected readonly barcodeReplaceConfirmVisible = signal<Record<number, boolean>>({});
  private readonly pendingGeneratedBarcode = new Map<number, string>();

  protected readonly form = this.fb.nonNullable.group({
    referenceNumber: [''],
    notes: [''],
    lines: this.fb.array([this.createLineGroup()]),
  });

  protected get lines(): FormArray<ReturnType<ReceivePurchaseOrderDialogComponent['createLineGroup']>> {
    return this.form.controls.lines;
  }

  ngOnInit(): void {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['order'] && this.order) {
      this.receivableLines = [...this.order.lines.filter((line) => (line.remainingQuantity ?? line.expectedQuantity) > 0)];
      this.lines.clear();
      this.receivableLines.forEach((line, index) => {
        this.lines.push(this.createLineGroup(line));
        void this.applyTaxDefault(index);
      });
    }
  }

  protected filteredLineIndexes(): number[] {
    const searchTerm = this.lineSearch().trim().toLocaleLowerCase();
    return this.lines.controls
      .map((row, index) => ({ index, line: this.findLine(row.controls.purchaseOrderLineId.value) }))
      .filter(({ line }) => !searchTerm || line?.description.toLocaleLowerCase().includes(searchTerm))
      .map(({ index }) => index);
  }

  protected remainingFor(lineId: string): number {
    const line = this.receivableLines.find((item) => item.lineId === lineId);
    return line?.remainingQuantity ?? line?.expectedQuantity ?? 0;
  }

  protected remainingError(): boolean {
    return this.lines.controls.some((row) => {
      const remaining = this.remainingFor(row.controls.purchaseOrderLineId.value);
      return remaining > 0 && row.controls.quantity.value > remaining;
    });
  }

  protected duplicateLineError(): boolean {
    const selected = this.lines.controls
      .map((row) => row.controls.purchaseOrderLineId.value)
      .filter((lineId) => lineId);
    return new Set(selected).size !== selected.length;
  }

  protected addLine(): void {
    const selected = new Set(this.lines.controls.map((row) => row.controls.purchaseOrderLineId.value));
    const nextLine = this.receivableLines.find((line) => !selected.has(line.lineId));
    if (nextLine) {
      this.lines.push(this.createLineGroup(nextLine));
      void this.applyTaxDefault(this.lines.length - 1);
    }
  }

  protected removeLine(index: number): void {
    if (this.lines.length > 1) this.lines.removeAt(index);
  }

  protected onLineSelectionChange(index: number): void {
    const row = this.lines.at(index);
    const line = this.findLine(row.controls.purchaseOrderLineId.value);
    if (!line) {
      return;
    }

    const remaining = line.remainingQuantity ?? line.expectedQuantity;
    if (!row.controls.quantity.dirty) {
      row.controls.quantity.setValue(remaining);
    }
    if (!row.controls.totalPurchaseCost.dirty) {
      row.controls.totalPurchaseCost.setValue((line.unitCost ?? 0) * remaining);
    }
    if (!row.controls.batchNumber.value.trim()) {
      row.controls.batchNumber.setValue(this.batchDefaults.generateBatchNumber());
    }
    if (!row.controls.barcode.dirty) {
      row.controls.barcode.setValue(this.barcodeForLine(line));
    }
    void this.applyTaxDefault(index);
  }

  protected onBarcodeFilter(index: number, event: AutoCompleteCompleteEvent): void {
    this.setRecord(this.barcodeSuggestions, index, this.catalogSync.filterByBarcode(event.query).map((entry) => entry.barcode));
  }

  protected onBarcodeSelected(index: number, barcode: string): void {
    const row = this.lines.at(index);
    row.controls.barcode.setValue(barcode);
    row.controls.barcode.markAsDirty();
  }

  protected barcodeSuggestionsFor(index: number): string[] {
    return [...(this.barcodeSuggestions()[index] ?? [])];
  }

  protected isBarcodeGenerating(index: number): boolean {
    return this.barcodeGenerating()[index] ?? false;
  }

  protected barcodeGenerateErrorFor(index: number): string {
    return this.barcodeGenerateErrors()[index] ?? '';
  }

  protected isBarcodeReplaceConfirmVisible(index: number): boolean {
    return this.barcodeReplaceConfirmVisible()[index] ?? false;
  }

  protected async generateBarcode(index: number): Promise<void> {
    const row = this.lines.at(index);
    if (!row || this.barcodeGenerating()[index]) {
      return;
    }

    this.setRecord(this.barcodeGenerating, index, true);
    this.setRecord(this.barcodeGenerateErrors, index, '');
    try {
      const barcode = (await firstValueFrom(this.inventoryService.generateItemBarcode())).barcode.trim();
      if (!barcode) {
        this.setRecord(this.barcodeGenerateErrors, index, 'inventory.generateBarcodeError');
        return;
      }

      if (row.controls.barcode.value.trim()) {
        this.pendingGeneratedBarcode.set(index, barcode);
        this.setRecord(this.barcodeReplaceConfirmVisible, index, true);
        return;
      }

      row.controls.barcode.setValue(barcode);
      row.controls.barcode.markAsDirty();
    } catch {
      this.setRecord(this.barcodeGenerateErrors, index, 'inventory.generateBarcodeError');
    } finally {
      this.setRecord(this.barcodeGenerating, index, false);
    }
  }

  protected confirmBarcodeReplace(index: number): void {
    const barcode = this.pendingGeneratedBarcode.get(index);
    const row = this.lines.at(index);
    if (barcode && row) {
      row.controls.barcode.setValue(barcode);
      row.controls.barcode.markAsDirty();
      this.setRecord(this.barcodeGenerateErrors, index, '');
    }
    this.pendingGeneratedBarcode.delete(index);
    this.setRecord(this.barcodeReplaceConfirmVisible, index, false);
  }

  protected cancelBarcodeReplace(index: number): void {
    this.pendingGeneratedBarcode.delete(index);
    this.setRecord(this.barcodeReplaceConfirmVisible, index, false);
  }

  protected generateBatchNumber(index: number): void {
    const row = this.lines.at(index);
    row.controls.batchNumber.setValue(this.batchDefaults.generateBatchNumber());
    row.controls.batchNumber.markAsDirty();
  }

  protected lookupTaxForLine(index: number): void {
    void this.applyTaxDefault(index, { respectDirty: false });
  }

  protected isTaxLookupLoading(index: number): boolean {
    return this.taxLookupLoading()[index] ?? false;
  }

  private createLineGroup(line?: PurchaseOrderLine) {
    const remaining = line ? line.remainingQuantity ?? line.expectedQuantity : 1;
    return this.fb.nonNullable.group({
      purchaseOrderLineId: [line?.lineId ?? '', Validators.required],
      barcode: [line ? this.barcodeForLine(line) : '', [Validators.required, Validators.maxLength(120)]],
      batchNumber: [this.batchDefaults.generateBatchNumber(), Validators.required],
      quantity: [remaining, [Validators.required, Validators.min(1)]],
      totalPurchaseCost: [(line?.unitCost ?? 0) * remaining, [Validators.required, Validators.min(0)]],
      mrp: [0, [Validators.required, Validators.min(0)]],
      salesPrice: [0, [Validators.required, Validators.min(0)]],
      taxRatePercent: [0, [Validators.required, Validators.min(0), Validators.max(100)]],
      taxIncluded: [false],
      purchaseTaxIncluded: [false],
      expiryDate: [null as Date | null],
      manufacturingDate: [null as Date | null],
    });
  }

  private async applyTaxDefault(index: number, options: { readonly respectDirty?: boolean } = {}): Promise<void> {
    const respectDirty = options.respectDirty ?? true;
    const row = this.lines.at(index);
    if (!row || (respectDirty && row.controls.taxRatePercent.dirty)) {
      return;
    }

    const line = this.findLine(row.controls.purchaseOrderLineId.value);
    const productName = line?.description.trim() ?? '';
    if (productName.length < 3) {
      return;
    }

    this.setTaxLookupLoading(index, true);
    try {
      const result = await this.batchDefaults.lookupHsn(productName);
      const taxRatePercent = this.batchDefaults.getAutoTaxRatePercent(result);
      if (taxRatePercent !== null) {
        row.controls.taxRatePercent.setValue(taxRatePercent);
        if (!respectDirty) {
          row.controls.taxRatePercent.markAsDirty();
        }
      }
    } catch {
      // Keep manual tax entry available when lookup fails.
    } finally {
      this.setTaxLookupLoading(index, false);
    }
  }

  private setTaxLookupLoading(index: number, loading: boolean): void {
    this.setRecord(this.taxLookupLoading, index, loading || undefined);
  }

  private findLine(lineId: string): PurchaseOrderLine | undefined {
    return this.receivableLines.find((line) => line.lineId === lineId);
  }

  private barcodeForLine(line: PurchaseOrderLine): string {
    return this.catalogSync.catalogEntries().find((entry) => entry.itemId === line.itemId)?.barcode ?? '';
  }

  private setRecord<T>(target: { (): Record<number, T>; set(value: Record<number, T>): void }, index: number, value: T | undefined): void {
    const current = { ...target() };
    if (value === undefined || value === '' || value === false) {
      delete current[index];
    } else {
      current[index] = value;
    }
    target.set(current);
  }

  protected hide(): void {
    this.visible = false;
    this.visibleChange.emit(false);
    this.closed.emit();
  }

  protected submit(): void {
    if (this.form.invalid || this.remainingError() || this.duplicateLineError() || this.lines.length === 0) return;

    const value = this.form.getRawValue();
    this.receive.emit({
      referenceNumber: value.referenceNumber.trim() || null,
      notes: value.notes.trim() || null,
      receivedAt: null,
      lines: value.lines.map((line) => ({
        purchaseOrderLineId: line.purchaseOrderLineId,
        barcode: line.barcode.trim(),
        batchNumber: line.batchNumber.trim(),
        quantity: line.quantity,
        totalPurchaseCost: line.totalPurchaseCost,
        mrp: line.mrp,
        salesPrice: line.salesPrice,
        taxRatePercent: line.taxRatePercent,
        taxIncluded: line.taxIncluded,
        purchaseTaxIncluded: line.purchaseTaxIncluded,
        expiryDate: line.expiryDate ? formatLocalIsoDate(line.expiryDate) : null,
        manufacturingDate: line.manufacturingDate ? formatLocalIsoDate(line.manufacturingDate) : null,
      })),
    });
  }
}
