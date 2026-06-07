import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Output, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { AutoCompleteCompleteEvent, AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { InputNumberModule } from 'primeng/inputnumber';
import { firstValueFrom } from 'rxjs';

import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryService } from '../../inventory/services/inventory.service';
import type { AddItemRequest } from '../../inventory/services/inventory.models';
import type { CreatePurchaseOrderLineRequest } from '../services/purchase-order.service';

@Component({
  selector: 'app-purchase-order-line-form',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslocoPipe,
    AutoCompleteModule,
    ButtonModule,
    InputNumberModule,
  ],
  template: `
    <form class="po-line-form" [formGroup]="form" (ngSubmit)="submitLine()">
      <label>
        <span>{{ 'purchaseOrders.builder.item' | transloco }}</span>
        <p-autocomplete
          formControlName="description"
          [suggestions]="itemSuggestions()"
          (completeMethod)="onItemSearch($event)"
          appendTo="body"
          [fluid]="true"
        ></p-autocomplete>
      </label>
      <label>
        <span>{{ 'purchaseOrders.builder.qty' | transloco }}</span>
        <p-inputNumber
          formControlName="expectedQuantity"
          [min]="1"
          [showButtons]="true"
          buttonLayout="horizontal"
          decrementButtonIcon="pi pi-minus"
          incrementButtonIcon="pi pi-plus"
          [fluid]="true"
        ></p-inputNumber>
      </label>
      <label>
        <span>{{ 'purchaseOrders.builder.unitCost' | transloco }}</span>
        <p-inputNumber
          formControlName="unitCost"
          mode="decimal"
          [min]="0"
          [minFractionDigits]="0"
          [maxFractionDigits]="2"
          [fluid]="true"
        ></p-inputNumber>
      </label>
      <button
        pButton
        type="submit"
        icon="pi pi-plus"
        [label]="'purchaseOrders.builder.addLine' | transloco"
        [disabled]="form.invalid"
      ></button>
      <button
        pButton
        type="button"
        severity="secondary"
        icon="pi pi-bolt"
        [label]="'purchaseOrders.builder.quickCreateProduct' | transloco"
        (click)="quickCreateProduct()"
        [disabled]="form.controls.description.invalid"
      ></button>
      @if (quickCreateError()) {
        <p class="po-line-form__error" aria-live="polite">{{ quickCreateError() | transloco }}</p>
      }
    </form>
  `,
  styles: [`
    .po-line-form { display: grid; gap: .75rem; grid-template-columns: minmax(14rem, 1fr) 7rem 9rem auto auto; align-items: end; }
    label { display: grid; gap: .35rem; font-size: .875rem; font-weight: 700; color: #1f2937; }
    .po-line-form__error { grid-column: 1 / -1; margin: 0; color: #b42318; font-size: .875rem; }
    .po-line-form .p-autocomplete,
    .po-line-form .p-inputnumber {
      width: 100%;
    }
    .po-line-form .p-inputtext,
    .po-line-form .p-autocomplete-input,
    .po-line-form .p-inputnumber-input {
      width: 100%;
      min-height: 2.75rem;
      border: 1px solid #cbd5e1;
      border-radius: 0.75rem;
      background: #ffffff;
      color: #111827;
      padding: 0.65rem 0.85rem;
      box-shadow: 0 1px 2px rgba(15, 23, 42, 0.06);
      transition: border-color 160ms ease, box-shadow 160ms ease;
    }
    .po-line-form .p-inputtext:enabled:focus,
    .po-line-form .p-autocomplete-input:enabled:focus,
    .po-line-form .p-inputnumber-input:enabled:focus {
      border-color: #ea580c;
      box-shadow: 0 0 0 3px rgba(234, 88, 12, 0.16);
      outline: 0;
    }
    .po-line-form .p-inputnumber-button {
      border-color: #cbd5e1;
      background: #fff7ed;
      color: #c2410c;
    }
    @media (max-width: 760px) { .po-line-form { grid-template-columns: 1fr; } }
  `],
})
export class PurchaseOrderLineFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly inventoryService = inject(InventoryService);
  private readonly catalogSync = inject(ProductCatalogSyncService);

  @Output() readonly lineSubmitted = new EventEmitter<CreatePurchaseOrderLineRequest>();
  readonly quickCreateError = signal('');
  readonly itemSuggestions = signal<string[]>([]);

  readonly form = this.fb.nonNullable.group({
    description: ['', [Validators.required, Validators.maxLength(500)]],
    expectedQuantity: [1, [Validators.required, Validators.min(1)]],
    unitCost: [0, [Validators.required, Validators.min(0)]],
  });

  onItemSearch(event: AutoCompleteCompleteEvent): void {
    this.itemSuggestions.set(this.filterItemNames(event.query));
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

  private filterItemNames(query: string): string[] {
    return this.catalogSync.filterByName(query)
      .map((entry) => entry.name)
      .slice(0, 20);
  }
}
