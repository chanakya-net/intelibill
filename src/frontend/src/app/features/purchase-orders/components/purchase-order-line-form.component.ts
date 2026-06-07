import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Output, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

import { InventoryService } from '../../inventory/services/inventory.service';
import type { AddItemRequest } from '../../inventory/services/inventory.models';
import type { CreatePurchaseOrderLineRequest } from '../services/purchase-order.service';

@Component({
  selector: 'app-purchase-order-line-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <form class="po-line-form" [formGroup]="form" (ngSubmit)="submitLine()">
      <label>
        <span>Item</span>
        <input type="text" formControlName="description" list="po-item-suggestions" />
      </label>
      <label>
        <span>Qty</span>
        <input type="number" min="1" step="1" formControlName="expectedQuantity" />
      </label>
      <label>
        <span>Unit cost</span>
        <input type="number" min="0" step="0.01" formControlName="unitCost" />
      </label>
      <button type="submit" [disabled]="form.invalid">Add line</button>
      <button type="button" (click)="quickCreateProduct()" [disabled]="form.controls.description.invalid">
        Quick-create product
      </button>
    </form>
  `,
  styles: [`
    .po-line-form { display: grid; gap: .75rem; grid-template-columns: minmax(14rem, 1fr) 7rem 9rem auto auto; align-items: end; }
    label { display: grid; gap: .25rem; font-size: .875rem; }
    input { min-height: 2.25rem; padding: .35rem .5rem; }
    button { min-height: 2.25rem; }
    @media (max-width: 760px) { .po-line-form { grid-template-columns: 1fr; } }
  `],
})
export class PurchaseOrderLineFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly inventoryService = inject(InventoryService);

  @Output() readonly lineSubmitted = new EventEmitter<CreatePurchaseOrderLineRequest>();

  readonly form = this.fb.nonNullable.group({
    description: ['', [Validators.required, Validators.maxLength(500)]],
    expectedQuantity: [1, [Validators.required, Validators.min(1)]],
    unitCost: [0, [Validators.required, Validators.min(0)]],
  });

  submitLine(): void {
    if (this.form.invalid) return;
    this.lineSubmitted.emit(this.buildLine());
    this.form.reset({ description: '', expectedQuantity: 1, unitCost: 0 });
  }

  async quickCreateProduct(): Promise<void> {
    if (this.form.controls.description.invalid) return;

    const line = this.buildLine();
    const payload: AddItemRequest = {
      name: line.description,
      barcode: '',
      description: null,
      uom: 'pcs',
      isActive: true,
      hsnCode: null,
      defaultTaxRatePercent: 0,
    };
    const item = await firstValueFrom(this.inventoryService.addItem(payload));
    this.lineSubmitted.emit({
      description: item.name,
      expectedQuantity: line.expectedQuantity,
      unitCost: line.unitCost,
    });
    this.form.reset({ description: '', expectedQuantity: 1, unitCost: 0 });
  }

  private buildLine(): CreatePurchaseOrderLineRequest {
    const value = this.form.getRawValue();
    return {
      description: value.description.trim(),
      expectedQuantity: Number(value.expectedQuantity),
      unitCost: Number(value.unitCost),
    };
  }
}
