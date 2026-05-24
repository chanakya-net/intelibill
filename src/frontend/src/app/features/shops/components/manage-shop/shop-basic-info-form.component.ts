import { CommonModule } from '@angular/common';
import { Component, DestroyRef, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { InputTextModule } from 'primeng/inputtext';

import { ShopDetailsDto, UpdateShopRequest } from '../../services/shop.service';
import { INDIA_GST_REGEX, toOptionalTrimmed } from './manage-shop-form.helper';

@Component({
  selector: 'app-shop-basic-info-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule],
  templateUrl: './shop-basic-info-form.component.html',
})
export class ShopBasicInfoFormComponent implements OnInit, OnChanges {
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  @Input() initialValues: ShopDetailsDto | null = null;
  @Input() disabled = false;
  @Output() readonly formChange = new EventEmitter<Partial<UpdateShopRequest>>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    address: ['', [Validators.required, Validators.maxLength(320)]],
    city: ['', [Validators.required, Validators.maxLength(120)]],
    state: ['', [Validators.required, Validators.maxLength(120)]],
    pincode: ['', [Validators.required, Validators.maxLength(16)]],
    contactPerson: ['', [Validators.maxLength(120)]],
    mobileNumber: ['', [Validators.maxLength(32)]],
    gstNumber: ['', [Validators.maxLength(20), Validators.pattern(INDIA_GST_REGEX)]],
  });

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.emitFormChange());
  }

  ngOnInit(): void {
    this.patchFromInitialValues();
    if (this.disabled) this.form.disable({ emitEvent: false });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['initialValues']) this.patchFromInitialValues();
    if (changes['disabled']) {
      if (this.disabled) this.form.disable({ emitEvent: false });
      else this.form.enable({ emitEvent: false });
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
      { emitEvent: false }
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
