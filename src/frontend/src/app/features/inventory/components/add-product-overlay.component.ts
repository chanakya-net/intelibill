import { Component, EventEmitter, OnInit, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { AutoCompleteModule, AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { RootState } from '../../../core/state/app.state';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryActions } from '../state/inventory.actions';
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
    InputTextModule,
    CheckboxModule,
    ButtonModule,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './add-product-overlay.component.html',
  styleUrl: './add-product-overlay.component.scss',
})
export class AddProductOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);
  private readonly catalogSync = inject(ProductCatalogSyncService);

  readonly isSubmitting = this.store.selectSignal(selectInventorySubmitting);
  readonly serverError = this.store.selectSignal(selectInventoryErrorMessage);
  readonly nameSuggestions = signal<string[]>([]);
  readonly barcodeSuggestions = signal<string[]>([]);

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(180)]],
    barcode: ['', [Validators.required, Validators.maxLength(120)]],
    description: ['', [Validators.maxLength(320)]],
    uom: ['', [Validators.required, Validators.maxLength(40)]],
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
  }

  onBarcodeSelected(barcode: string): void {
    const entry = this.catalogSync.findByBarcode(barcode);
    if (entry) {
      this.form.controls.name.setValue(entry.name);
    }
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
          isActive: this.form.controls.isActive.value,
        },
      }),
    );
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}
