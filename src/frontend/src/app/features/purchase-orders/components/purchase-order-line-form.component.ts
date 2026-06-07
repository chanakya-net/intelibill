import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { firstValueFrom } from 'rxjs';

import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryService } from '../../inventory/services/inventory.service';
import type { AddItemRequest } from '../../inventory/services/inventory.models';
import type { CreatePurchaseOrderLineRequest } from '../services/purchase-order.service';

@Component({
  selector: 'app-purchase-order-line-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslocoPipe],
  template: `
    <form class="po-line-form" [formGroup]="form" (ngSubmit)="submitLine()">
      <label>
        <span>{{ 'purchaseOrders.builder.item' | transloco }}</span>
        <input type="text" formControlName="description" list="po-item-suggestions" />
        <datalist id="po-item-suggestions">
          @for (entry of itemSuggestions(); track entry.itemId) {
            <option [value]="entry.name"></option>
          }
        </datalist>
      </label>
      <label>
        <span>{{ 'purchaseOrders.builder.qty' | transloco }}</span>
        <input type="number" min="1" step="1" formControlName="expectedQuantity" />
      </label>
      <label>
        <span>{{ 'purchaseOrders.builder.unitCost' | transloco }}</span>
        <input type="number" min="0" step="0.01" formControlName="unitCost" />
      </label>
      <button type="submit" [disabled]="form.invalid">{{ 'purchaseOrders.builder.addLine' | transloco }}</button>
      <button type="button" (click)="quickCreateProduct()" [disabled]="form.controls.description.invalid">
        {{ 'purchaseOrders.builder.quickCreateProduct' | transloco }}
      </button>
      @if (quickCreateError()) {
        <p class="po-line-form__error" aria-live="polite">{{ quickCreateError() | transloco }}</p>
      }
    </form>
  `,
  styles: [`
    .po-line-form { display: grid; gap: .75rem; grid-template-columns: minmax(14rem, 1fr) 7rem 9rem auto auto; align-items: end; }
    label { display: grid; gap: .25rem; font-size: .875rem; }
    input { min-height: 2.25rem; padding: .35rem .5rem; }
    button { min-height: 2.25rem; }
    .po-line-form__error { grid-column: 1 / -1; margin: 0; color: #b42318; font-size: .875rem; }
    @media (max-width: 760px) { .po-line-form { grid-template-columns: 1fr; } }
  `],
})
export class PurchaseOrderLineFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly inventoryService = inject(InventoryService);
  private readonly catalogSync = inject(ProductCatalogSyncService);

  @Output() readonly lineSubmitted = new EventEmitter<CreatePurchaseOrderLineRequest>();
  readonly quickCreateError = signal('');

  readonly form = this.fb.nonNullable.group({
    description: ['', [Validators.required, Validators.maxLength(500)]],
    expectedQuantity: [1, [Validators.required, Validators.min(1)]],
    unitCost: [0, [Validators.required, Validators.min(0)]],
  });
  itemSuggestions() {
    return this.catalogSync.filterByName(this.form.controls.description.value).slice(0, 20);
  }

  submitLine(): void {
    if (this.form.invalid) return;
    const line = this.buildLineFromSelectedItem();
    if (!line) return;
    this.lineSubmitted.emit(line);
    this.form.reset({ description: '', expectedQuantity: 1, unitCost: 0 });
  }

  async quickCreateProduct(): Promise<void> {
    if (this.form.controls.description.invalid) return;

    this.quickCreateError.set('');
    const line = this.buildLineValues();
    if (!line.description) {
      this.quickCreateError.set('purchaseOrders.builder.quickCreateFailed');
      return;
    }

    try {
      const barcode = (await firstValueFrom(this.inventoryService.generateItemBarcode())).barcode.trim();
      if (!barcode) {
        this.quickCreateError.set('purchaseOrders.builder.quickCreateFailed');
        return;
      }

      const payload: AddItemRequest = {
        name: line.description,
        barcode,
        description: null,
        uom: 'pcs',
        isActive: true,
        hsnCode: null,
        defaultTaxRatePercent: 0,
      };
      const item = await firstValueFrom(this.inventoryService.addItem(payload));
      this.catalogSync.upsertEntry({ itemId: item.id, name: item.name, barcode: item.barcode });
      this.lineSubmitted.emit({
        itemId: item.id,
        description: item.name,
        expectedQuantity: line.expectedQuantity,
        unitCost: line.unitCost,
      });
      this.form.reset({ description: '', expectedQuantity: 1, unitCost: 0 });
    } catch {
      this.quickCreateError.set('purchaseOrders.builder.quickCreateFailed');
    }
  }

  private buildLineFromSelectedItem(): CreatePurchaseOrderLineRequest | null {
    const value = this.buildLineValues();
    const entry = this.catalogSync.findByName(value.description);
    if (!entry) return null;
    return {
      itemId: entry.itemId,
      description: entry.name,
      expectedQuantity: value.expectedQuantity,
      unitCost: value.unitCost,
    };
  }

  private buildLineValues(): Omit<CreatePurchaseOrderLineRequest, 'itemId'> {
    const value = this.form.getRawValue();
    return {
      description: value.description.trim(),
      expectedQuantity: Number(value.expectedQuantity),
      unitCost: Number(value.unitCost),
    };
  }
}
