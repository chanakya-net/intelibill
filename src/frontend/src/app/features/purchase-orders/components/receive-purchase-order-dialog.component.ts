import { CommonModule } from '@angular/common';
import { Component, DestroyRef, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
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
      header="Receive purchase order"
      [(visible)]="visible"
      [modal]="true"
      [style]="{ width: 'min(720px, 96vw)' }"
      (onHide)="closed.emit()"
    >
      <form class="receive-form" [formGroup]="form" (ngSubmit)="submit()">
        <label>
          Line
          <p-select
            formControlName="purchaseOrderLineId"
            [options]="receivableLines"
            optionLabel="description"
            optionValue="lineId"
            placeholder="Select line"
          />
        </label>
        <label>
          Batch number
          <input pInputText formControlName="batchNumber" />
        </label>
        <label>
          Quantity
          <p-inputNumber formControlName="quantity" [min]="1" [max]="selectedRemaining" />
        </label>
        <label>
          Total purchase cost
          <p-inputNumber formControlName="totalPurchaseCost" mode="decimal" [min]="0" [minFractionDigits]="2" />
        </label>
        <label>
          MRP
          <p-inputNumber formControlName="mrp" mode="decimal" [min]="0" [minFractionDigits]="2" />
        </label>
        <label>
          Sales price
          <p-inputNumber formControlName="salesPrice" mode="decimal" [min]="0" [minFractionDigits]="2" />
        </label>
        <label>
          Tax rate
          <p-inputNumber formControlName="taxRatePercent" mode="decimal" [min]="0" [max]="100" />
        </label>
        <label>
          Expiry date
          <input pInputText type="date" formControlName="expiryDate" />
        </label>
        <label>
          Manufacturing date
          <input pInputText type="date" formControlName="manufacturingDate" />
        </label>
        <label>
          Reference
          <input pInputText formControlName="referenceNumber" />
        </label>
        <label class="full-width">
          Notes
          <textarea pTextarea formControlName="notes"></textarea>
        </label>
        <label class="checkbox-row">
          <p-checkbox formControlName="taxIncluded" [binary]="true" />
          Tax included
        </label>
        <label class="checkbox-row">
          <p-checkbox formControlName="purchaseTaxIncluded" [binary]="true" />
          Purchase tax included
        </label>
        @if (remainingError) {
          <p class="error">Quantity cannot exceed remaining quantity.</p>
        }
        <div class="actions">
          <button pButton type="button" severity="secondary" label="Cancel" (click)="hide()"></button>
          <button pButton type="submit" label="Receive" [disabled]="form.invalid || remainingError || submitting"></button>
        </div>
      </form>
    </p-dialog>
  `,
  styles: [`
    .receive-form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: .75rem; }
    label { display: grid; gap: .25rem; font-size: .875rem; }
    .full-width, .actions, .error { grid-column: 1 / -1; }
    .checkbox-row { display: flex; align-items: center; gap: .5rem; }
    .actions { display: flex; justify-content: flex-end; gap: .5rem; margin-top: .5rem; }
    .error { color: var(--p-red-600); margin: 0; }
    @media (max-width: 640px) { .receive-form { grid-template-columns: 1fr; } }
  `],
})
export class ReceivePurchaseOrderDialogComponent implements OnChanges, OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  @Input({ required: true }) order!: PurchaseOrderDetail;
  @Input() visible = false;
  @Input() submitting = false;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() closed = new EventEmitter<void>();
  @Output() receive = new EventEmitter<ReceivePurchaseOrderRequest>();

  protected receivableLines: PurchaseOrderLine[] = [];
  protected selectedRemaining = 0;

  protected readonly form = this.fb.nonNullable.group({
    purchaseOrderLineId: ['', Validators.required],
    batchNumber: ['', Validators.required],
    quantity: [1, [Validators.required, Validators.min(1)]],
    totalPurchaseCost: [0, [Validators.required, Validators.min(0)]],
    mrp: [0, [Validators.required, Validators.min(0)]],
    salesPrice: [0, [Validators.required, Validators.min(0)]],
    taxRatePercent: [0, [Validators.required, Validators.min(0), Validators.max(100)]],
    taxIncluded: [false],
    purchaseTaxIncluded: [false],
    expiryDate: [''],
    manufacturingDate: [''],
    referenceNumber: [''],
    notes: [''],
  });

  get remainingError(): boolean {
    return this.selectedRemaining > 0 && this.form.controls.quantity.value > this.selectedRemaining;
  }

  ngOnInit(): void {
    this.form.controls.purchaseOrderLineId.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((lineId) => {
        this.updateSelectedRemaining(lineId);
      });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['order'] && this.order) {
      this.receivableLines = [...this.order.lines.filter((line) => (line.remainingQuantity ?? line.expectedQuantity) > 0)];
      const firstLine = this.receivableLines[0];
      if (firstLine) {
        this.form.patchValue({
          purchaseOrderLineId: firstLine.lineId,
          quantity: firstLine.remainingQuantity ?? firstLine.expectedQuantity,
          totalPurchaseCost: firstLine.unitCost * (firstLine.remainingQuantity ?? firstLine.expectedQuantity),
        });
        this.updateSelectedRemaining(firstLine.lineId);
      }
    }
  }

  private updateSelectedRemaining(lineId: string): void {
    const line = this.receivableLines.find((item) => item.lineId === lineId);
    this.selectedRemaining = line?.remainingQuantity ?? line?.expectedQuantity ?? 0;
  }

  protected hide(): void {
    this.visible = false;
    this.visibleChange.emit(false);
    this.closed.emit();
  }

  protected submit(): void {
    if (this.form.invalid || this.remainingError) return;

    const value = this.form.getRawValue();
    this.receive.emit({
      referenceNumber: value.referenceNumber.trim() || null,
      notes: value.notes.trim() || null,
      receivedAt: null,
      lines: [{
        purchaseOrderLineId: value.purchaseOrderLineId,
        batchNumber: value.batchNumber.trim(),
        quantity: value.quantity,
        totalPurchaseCost: value.totalPurchaseCost,
        mrp: value.mrp,
        salesPrice: value.salesPrice,
        taxRatePercent: value.taxRatePercent,
        taxIncluded: value.taxIncluded,
        purchaseTaxIncluded: value.purchaseTaxIncluded,
        expiryDate: value.expiryDate || null,
        manufacturingDate: value.manufacturingDate || null,
      }],
    });
  }
}
