import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { BadgeModule } from 'primeng/badge';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { RootState } from '../../../core/state/app.state';
import { InventoryActions } from '../state/inventory.actions';
import { selectInventoryErrorMessage, selectInventorySubmitting } from '../state/inventory.selectors';
import { Item } from '../services/inventory.service';

@Component({
  selector: 'app-edit-item-overlay',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    InputTextModule,
    TextareaModule,
    BadgeModule,
    ButtonModule,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './edit-item-overlay.component.html',
  styleUrl: './edit-item-overlay.component.scss',
})
export class EditItemOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);

  @Input({ required: true }) item!: Item;

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly isSubmitting = this.store.selectSignal(selectInventorySubmitting);
  readonly serverError = this.store.selectSignal(selectInventoryErrorMessage);

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(180)]],
    barcode: ['', [Validators.required, Validators.maxLength(128)]],
    description: ['', [Validators.maxLength(1000)]],
    uom: ['', [Validators.required, Validators.maxLength(32)]],
  });

  ngOnInit(): void {
    this.store.dispatch(InventoryActions.clearError());
    this.store.dispatch(InventoryActions.clearMutationStatus());

    this.form.controls.name.setValue(this.item.name);
    this.form.controls.barcode.setValue(this.item.barcode);
    this.form.controls.description.setValue(this.item.description ?? '');
    this.form.controls.uom.setValue(this.item.uom);
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
      InventoryActions.updateItemRequested({
        itemId: this.item.id,
        payload: {
          name: this.form.controls.name.value.trim(),
          barcode: this.form.controls.barcode.value.trim(),
          description: this.nullableTrimmed(this.form.controls.description.value),
          uom: this.form.controls.uom.value.trim(),
        },
      })
    );
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }

  getStockSeverity(): 'danger' | 'warn' | 'success' {
    const stock = this.item.currentStock;
    if (stock <= 5) return 'danger';
    if (stock < 50) return 'warn';
    return 'success';
  }
}
