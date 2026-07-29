import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  inject,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { CheckboxModule } from 'primeng/checkbox';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { Supplier } from '../services/supplier.service';
import { SuppliersFacade } from '../state/suppliers.facade';

@Component({
  selector: 'app-edit-supplier-overlay',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    InputTextModule,
    CheckboxModule,
    ButtonModule,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './edit-supplier-overlay.component.html',
  styleUrl: './edit-supplier-overlay.component.scss',
})
export class EditSupplierOverlayComponent implements OnInit, OnChanges {
  private readonly formBuilder = inject(FormBuilder);
  private readonly suppliersFacade = inject(SuppliersFacade);

  readonly isSubmitting = this.suppliersFacade.isSubmitting;
  readonly serverError = this.suppliersFacade.errorMessage;

  @Input({ required: true }) supplier!: Supplier;
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(180)]],
    contactPersonName: ['', [Validators.maxLength(120)]],
    contactPersonPhone: ['', [Validators.required, Validators.maxLength(32), Validators.pattern(/^[+]?\d{7,15}$/)]],
    address: ['', [Validators.required, Validators.maxLength(320)]],
    city: ['', [Validators.required, Validators.maxLength(120)]],
    state: ['', [Validators.required, Validators.maxLength(120)]],
    pin: ['', [Validators.required, Validators.maxLength(16)]],
    isActive: [true],
    isPreferred: [false],
  });

  constructor() {}

  ngOnInit(): void {
    this.patchForm();
    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['supplier']) {
      return;
    }
    this.patchForm();
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
    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
    this.suppliersFacade.editSupplier(this.supplier.supplierId, {
      name: this.form.controls.name.value.trim(),
      contactPersonName: this.nullableTrimmed(this.form.controls.contactPersonName.value),
      contactPersonPhone: this.nullableTrimmed(this.form.controls.contactPersonPhone.value),
      address: this.form.controls.address.value.trim(),
      city: this.form.controls.city.value.trim(),
      state: this.form.controls.state.value.trim(),
      pin: this.form.controls.pin.value.trim(),
      isActive: this.form.controls.isActive.value,
      isPreferred: this.form.controls.isPreferred.value,
    });
  }

  private patchForm(): void {
    if (!this.supplier) {
      return;
    }
    this.form.patchValue(
      {
        name: this.supplier.name ?? '',
        contactPersonName: this.supplier.contactPersonName ?? '',
        contactPersonPhone: this.supplier.contactPersonPhone ?? '',
        address: this.supplier.address ?? '',
        city: this.supplier.city ?? '',
        state: this.supplier.state ?? '',
        pin: this.supplier.pin ?? '',
        isActive: this.supplier.isActive,
        isPreferred: this.supplier.isPreferred,
      },
      { emitEvent: false },
    );
  }

  private nullableTrimmed(value: string | null): string | null {
    return value && value.trim() !== '' ? value.trim() : null;
  }
}
