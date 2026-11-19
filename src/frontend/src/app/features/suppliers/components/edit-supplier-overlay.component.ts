import { toSignal } from '@angular/core/rxjs-interop';
import { DefinitionAccessNoticeComponent } from '../../billing/status/definition-access-notice.component';
import { OperatingAccessHintService } from '../../billing/status/operating-access-hint.service';
import {
  Component,
  computed,
  EventEmitter,
  input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  inject,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { CheckboxModule } from 'primeng/checkbox';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { Supplier } from '../services/supplier.service';
import { SuppliersFacade } from '../state/suppliers.facade';
import { InputValidators } from '../../../shared/forms/input-validation';

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
    DefinitionAccessNoticeComponent,
  ],
  templateUrl: './edit-supplier-overlay.component.html',
  styleUrl: './edit-supplier-overlay.component.scss',
})
export class EditSupplierOverlayComponent implements OnInit, OnChanges {
  private readonly formBuilder = inject(FormBuilder);
  private readonly suppliersFacade = inject(SuppliersFacade);

  readonly definitionAccess = inject(OperatingAccessHintService);
  readonly isSubmitting = this.suppliersFacade.isSubmitting;
  readonly serverError = this.suppliersFacade.errorMessage;

  readonly supplier = input.required<Supplier>();
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', InputValidators.requiredText(180)],
    contactPersonName: ['', InputValidators.optionalText(120)],
    contactPersonPhone: ['', InputValidators.phoneNumber({ required: false, maxLength: 32 })],
    address: ['', InputValidators.requiredText(320)],
    city: ['', InputValidators.requiredText(120)],
    state: ['', InputValidators.requiredText(120)],
    pin: ['', InputValidators.requiredText(16)],
    isActive: [true],
    isPreferred: [false],
  });

  private readonly requestedActive = toSignal(this.form.controls.isActive.valueChanges, { initialValue: true });
  readonly activationBlocked = computed(() =>
    !this.supplier().isActive && this.requestedActive() && !!this.definitionAccess.blockingMessage(),
  );

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
    if (this.isSubmitting() || this.activationBlocked()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
    this.suppliersFacade.editSupplier(this.supplier().supplierId, {
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
    const supplier = this.supplier();
    this.form.patchValue({
      name: supplier.name ?? '',
      contactPersonName: supplier.contactPersonName ?? '',
      contactPersonPhone: supplier.contactPersonPhone ?? '',
      address: supplier.address ?? '',
      city: supplier.city ?? '',
      state: supplier.state ?? '',
      pin: supplier.pin ?? '',
      isActive: supplier.isActive,
      isPreferred: supplier.isPreferred,
    });
  }

  private nullableTrimmed(value: string | null): string | null {
    return value && value.trim() !== '' ? value.trim() : null;
  }
}
