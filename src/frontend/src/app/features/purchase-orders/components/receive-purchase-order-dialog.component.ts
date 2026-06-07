import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges, inject } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';

import {
  PurchaseOrderDetail,
  PurchaseOrderLine,
  ReceivePurchaseOrderRequest,
} from '../services/purchase-order.service';

@Component({
  selector: 'app-receive-purchase-order-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslocoPipe,
    ButtonModule,
    CheckboxModule,
    DialogModule,
    InputNumberModule,
    InputTextModule,
    SelectModule,
    TextareaModule,
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
        <div class="receipt-lines" formArrayName="lines">
          @for (row of lines.controls; track $index; let index = $index) {
            <div class="receipt-row" [formGroupName]="index">
              <label>
                {{ 'purchaseOrders.receiveDialog.line' | transloco }}
                <p-select
                  formControlName="purchaseOrderLineId"
                  [options]="receivableLines"
                  optionLabel="description"
                  optionValue="lineId"
                  [placeholder]="'purchaseOrders.receiveDialog.selectLine' | transloco"
                />
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.batchNumber' | transloco }}
                <input pInputText formControlName="batchNumber" />
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
                <p-inputNumber formControlName="taxRatePercent" mode="decimal" [min]="0" [max]="100" />
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.expiryDate' | transloco }}
                <input pInputText type="date" formControlName="expiryDate" />
              </label>
              <label>
                {{ 'purchaseOrders.receiveDialog.manufacturingDate' | transloco }}
                <input pInputText type="date" formControlName="manufacturingDate" />
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

  @Input({ required: true }) order!: PurchaseOrderDetail;
  @Input() visible = false;
  @Input() submitting = false;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() closed = new EventEmitter<void>();
  @Output() receive = new EventEmitter<ReceivePurchaseOrderRequest>();

  protected receivableLines: PurchaseOrderLine[] = [];

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
      const firstLine = this.receivableLines[0];
      this.lines.clear();
      if (firstLine) this.lines.push(this.createLineGroup(firstLine));
    }
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
    const nextLine = this.receivableLines.find((line) => !selected.has(line.lineId)) ?? this.receivableLines[0];
    if (nextLine) this.lines.push(this.createLineGroup(nextLine));
  }

  protected removeLine(index: number): void {
    if (this.lines.length > 1) this.lines.removeAt(index);
  }

  private createLineGroup(line?: PurchaseOrderLine) {
    const remaining = line ? line.remainingQuantity ?? line.expectedQuantity : 1;
    return this.fb.nonNullable.group({
      purchaseOrderLineId: [line?.lineId ?? '', Validators.required],
      batchNumber: ['', Validators.required],
      quantity: [remaining, [Validators.required, Validators.min(1)]],
      totalPurchaseCost: [(line?.unitCost ?? 0) * remaining, [Validators.required, Validators.min(0)]],
      mrp: [0, [Validators.required, Validators.min(0)]],
      salesPrice: [0, [Validators.required, Validators.min(0)]],
      taxRatePercent: [0, [Validators.required, Validators.min(0), Validators.max(100)]],
      taxIncluded: [false],
      purchaseTaxIncluded: [false],
      expiryDate: [''],
      manufacturingDate: [''],
    });
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
        batchNumber: line.batchNumber.trim(),
        quantity: line.quantity,
        totalPurchaseCost: line.totalPurchaseCost,
        mrp: line.mrp,
        salesPrice: line.salesPrice,
        taxRatePercent: line.taxRatePercent,
        taxIncluded: line.taxIncluded,
        purchaseTaxIncluded: line.purchaseTaxIncluded,
        expiryDate: line.expiryDate || null,
        manufacturingDate: line.manufacturingDate || null,
      })),
    });
  }
}
