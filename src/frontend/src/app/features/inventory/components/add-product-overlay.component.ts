import { CommonModule } from '@angular/common';
import { Component, EventEmitter, OnInit, Output, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { RootState } from '../../../core/state/app.state';
import { InventoryActions } from '../state/inventory.actions';
import { selectInventoryErrorMessage, selectInventorySubmitting } from '../state/inventory.selectors';

@Component({
  selector: 'app-add-product-overlay',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
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

  readonly isSubmitting = this.store.selectSignal(selectInventorySubmitting);
  readonly serverError = this.store.selectSignal(selectInventoryErrorMessage);

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(180)]],
    barcode: ['', [Validators.required, Validators.maxLength(120)]],
    description: ['', [Validators.maxLength(320)]],
    uom: ['', [Validators.required, Validators.maxLength(40)]],
    isActive: [true],
    preferredSupplierId: ['', [Validators.pattern(/^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}$/)]],
  });

  ngOnInit(): void {
    this.store.dispatch(InventoryActions.clearError());
    this.store.dispatch(InventoryActions.clearMutationStatus());
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
          preferredSupplierId: this.nullableTrimmed(this.form.controls.preferredSupplierId.value),
        },
      })
    );
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}
