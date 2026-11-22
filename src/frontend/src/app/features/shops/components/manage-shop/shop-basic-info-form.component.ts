import { CommonModule } from '@angular/common';
import {
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  inject,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@ngneat/transloco';

import { InputTextModule } from 'primeng/inputtext';

import { ShopDetailsDto, UpdateShopRequest } from '../../services/shop.service';
import type { ShopProfileField } from '../../services/shop-setup-access';
import { isAnotherShop, toOptionalTrimmed } from './manage-shop-form.helper';
import { InputValidators } from '../../../../shared/forms/input-validation';

@Component({
  selector: 'app-shop-basic-info-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, TranslocoPipe],
  templateUrl: './shop-basic-info-form.component.html',
})
export class ShopBasicInfoFormComponent implements OnInit, OnChanges {
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  @Input() initialValues: ShopDetailsDto | null = null;
  @Input() disabled = false;
  @Input() allowedFields: readonly ShopProfileField[] | null = null;
  /** Off where the GSTIN is edited with the tax profile, which checks it against the registration state. */
  @Input() showGstNumber = true;
  @Output() readonly formChange = new EventEmitter<Partial<UpdateShopRequest>>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', InputValidators.requiredText(120)],
    address: ['', InputValidators.requiredText(320)],
    city: ['', InputValidators.requiredText(120)],
    state: ['', InputValidators.requiredText(120)],
    pincode: ['', InputValidators.requiredText(16)],
    contactPerson: ['', InputValidators.optionalText(120)],
    mobileNumber: ['', InputValidators.optionalText(32)],
    gstNumber: ['', InputValidators.gstNumber()],
  });

  constructor() {
    this.form.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.emitFormChange());
  }

  ngOnInit(): void {
    this.patchFromInitialValues();
    this.updateDisabledFields();
  }

  ngOnChanges(changes: SimpleChanges): void {
    // Details refreshed by a save elsewhere (the tax profile) keep edits the owner has not saved yet.
    if (changes['initialValues'] && (this.form.pristine || isAnotherShop(changes['initialValues']))) this.patchFromInitialValues();
    if (changes['disabled'] || changes['allowedFields']) this.updateDisabledFields();
  }

  isVisible(field: ShopProfileField): boolean {
    return this.allowedFields === null || this.allowedFields.includes(field);
  }

  private updateDisabledFields(): void {
    for (const name of Object.keys(this.form.controls) as ShopProfileField[]) {
      const control = this.form.controls[name];
      if (this.disabled || !this.isVisible(name)) control.disable({ emitEvent: false });
      else control.enable({ emitEvent: false });
    }
  }

  private patchFromInitialValues(): void {
    const values = this.initialValues;
    this.form.patchValue(
      {
        name: values?.name ?? '',
        address: values?.address ?? '',
        city: values?.city ?? '',
        state: values?.state ?? '',
        pincode: values?.pincode ?? '',
        contactPerson: values?.contactPerson ?? '',
        mobileNumber: values?.mobileNumber ?? '',
        gstNumber: values?.gstNumber ?? '',
      },
      { emitEvent: false },
    );
  }

  private emitFormChange(): void {
    const values = this.form.getRawValue();
    this.formChange.emit({
      name: values.name.trim(),
      address: values.address.trim(),
      city: values.city.trim(),
      state: values.state.trim(),
      pincode: values.pincode.trim(),
      contactPerson: toOptionalTrimmed(values.contactPerson),
      mobileNumber: toOptionalTrimmed(values.mobileNumber),
      gstNumber: toOptionalTrimmed(values.gstNumber),
    });
  }
}
