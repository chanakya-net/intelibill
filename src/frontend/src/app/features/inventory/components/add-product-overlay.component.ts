import { Component, EventEmitter, OnInit, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';
import { firstValueFrom } from 'rxjs';

import { AutoCompleteCompleteEvent, AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';

import { InventoryBarcodeFieldComponent } from './barcode-field.component';

import { RootState } from '../../../core/state/app.state';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryActions } from '../state/inventory.actions';
import { InventoryService } from '../services/inventory.service';
import {
  selectInventoryErrorMessage,
  selectInventorySubmitting,
} from '../state/inventory.selectors';

@Component({
  selector: 'app-add-product-overlay',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AutoCompleteModule,
    DialogModule,
    InputNumberModule,
    InputTextModule,
    TextareaModule,
    CheckboxModule,
    ButtonModule,
    InventoryBarcodeFieldComponent,
    TranslocoPipe,
  ],
  templateUrl: './add-product-overlay.component.html',
  styleUrl: './add-product-overlay.component.scss',
})
export class AddProductOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);
  private readonly catalogSync = inject(ProductCatalogSyncService);
  private readonly inventoryService = inject(InventoryService);

  readonly isSubmitting = this.store.selectSignal(selectInventorySubmitting);
  readonly serverError = this.store.selectSignal(selectInventoryErrorMessage);
  readonly nameSuggestions = signal<string[]>([]);
  readonly barcodeSuggestions = signal<string[]>([]);
  readonly suggestedHsnCodes = signal<string[]>([]);
  readonly suggestedTaxSlabs = signal<string[]>([]);
  readonly barcodeGenerating = signal(false);
  readonly barcodeGenerateError = signal('');
  readonly barcodeReplaceConfirmVisible = signal(false);
  private pendingGeneratedBarcode: string | null = null;

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(180)]],
    barcode: ['', [Validators.required, Validators.maxLength(120)]],
    description: ['', [Validators.maxLength(320)]],
    uom: ['', [Validators.required, Validators.maxLength(40)]],
    hsnCode: ['', [Validators.pattern(/^\s*\d{4,8}\s*$/)]],
    defaultTaxRatePercent: [0, [Validators.required, Validators.min(0), Validators.max(100)]],
    isActive: [true],
  });

  ngOnInit(): void {
    this.store.dispatch(InventoryActions.clearError());
    this.store.dispatch(InventoryActions.clearMutationStatus());
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

    void this.lookupHsnAndTax();
  }

  onBarcodeSelected(barcode: string): void {
    const entry = this.catalogSync.findByBarcode(barcode);
    if (entry) {
      this.form.controls.name.setValue(entry.name);
      void this.lookupHsnAndTax();
    }
  }

  onNameBlur(): void {
    void this.lookupHsnAndTax();
  }

  selectSuggestedHsnCode(hsnCode: string): void {
    this.form.controls.hsnCode.setValue(hsnCode);
  }

  selectSuggestedTaxSlab(taxPercentage: string): void {
    const parsedTaxRate = this.parseTaxPercentage(taxPercentage);
    if (parsedTaxRate !== null) {
      this.form.controls.defaultTaxRatePercent.setValue(parsedTaxRate);
    }
  }

  async onGenerateBarcode(): Promise<void> {
    if (this.barcodeGenerating()) {
      return;
    }

    const currentBarcode = this.form.controls.barcode.value.trim();
    if (currentBarcode) {
      this.barcodeGenerating.set(true);
      this.barcodeGenerateError.set('');
      try {
        const result = await firstValueFrom(this.inventoryService.generateItemBarcode());
        this.pendingGeneratedBarcode = result.barcode;
        this.barcodeReplaceConfirmVisible.set(true);
      } catch {
        this.barcodeGenerateError.set('inventory.generateBarcodeError');
      } finally {
        this.barcodeGenerating.set(false);
      }
      return;
    }

    this.barcodeGenerating.set(true);
    this.barcodeGenerateError.set('');
    try {
      const result = await firstValueFrom(this.inventoryService.generateItemBarcode());
      this.form.controls.barcode.setValue(result.barcode);
    } catch {
      this.barcodeGenerateError.set('inventory.generateBarcodeError');
    } finally {
      this.barcodeGenerating.set(false);
    }
  }

  confirmBarcodeReplace(): void {
    if (this.pendingGeneratedBarcode) {
      this.form.controls.barcode.setValue(this.pendingGeneratedBarcode);
    }
    this.pendingGeneratedBarcode = null;
    this.barcodeReplaceConfirmVisible.set(false);
  }

  cancelBarcodeReplace(): void {
    this.pendingGeneratedBarcode = null;
    this.barcodeReplaceConfirmVisible.set(false);
  }

  onClose(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.closeRequested.emit();
  }

  onSubmit(): void {
    if (this.isSubmitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.store.dispatch(InventoryActions.clearError());
    this.store.dispatch(InventoryActions.clearMutationStatus());
    this.store.dispatch(
      InventoryActions.addItemRequested({
        payload: {
          name: this.form.controls.name.value.trim(),
          barcode: this.form.controls.barcode.value.trim(),
          description: this.nullableTrimmed(this.form.controls.description.value),
          uom: this.form.controls.uom.value.trim(),
          hsnCode: this.nullableTrimmed(this.form.controls.hsnCode.value),
          defaultTaxRatePercent: Number(this.form.controls.defaultTaxRatePercent.value),
          isActive: this.form.controls.isActive.value,
        },
      }),
    );
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }

  private async lookupHsnAndTax(): Promise<void> {
    const productName = this.form.controls.name.value.trim();
    if (productName.length < 3) {
      this.suggestedHsnCodes.set([]);
      this.suggestedTaxSlabs.set([]);
      return;
    }

    try {
      const result = await firstValueFrom(this.inventoryService.lookupHsn(productName));
      this.suggestedHsnCodes.set([...result.hsnCodes]);
      this.suggestedTaxSlabs.set(result.taxScenarios.map((scenario) => scenario.taxPercentage));

      const suggestedHsn = result.hsnCodes.length === 1 ? result.hsnCodes[0] : null;
      const suggestedTaxRate =
        result.taxScenarios.length === 1 ? this.parseTaxPercentage(result.taxScenarios[0].taxPercentage) : null;

      if (!this.form.controls.hsnCode.dirty && suggestedHsn) {
        this.form.controls.hsnCode.setValue(suggestedHsn);
      }

      if (!this.form.controls.defaultTaxRatePercent.dirty && suggestedTaxRate) {
        this.form.controls.defaultTaxRatePercent.setValue(suggestedTaxRate);
      }
    } catch {
      this.suggestedHsnCodes.set([]);
      this.suggestedTaxSlabs.set([]);
      // Keep form editable if lookup fails.
    }
  }

  private parseTaxPercentage(value: string): number | null {
    const parsed = Number.parseFloat(value.replace('%', '').trim());
    return Number.isFinite(parsed) ? parsed : null;
  }
}
