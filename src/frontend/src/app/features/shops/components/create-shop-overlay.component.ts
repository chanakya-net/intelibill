import { Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';

import { RootState } from '../../../core/state/app.state';
import { ShopsActions } from '../state/shops.actions';
import {
  selectShopsErrorMessage,
  selectShopsSubmitting,
} from '../state/shops.selectors';
import { InputValidators } from '../../../shared/forms/input-validation';
import { GST_STATE_OPTIONS } from '../../../shared/gst/gst-state-codes';
import { CreateShopRequest } from '../services/shop.service';
import {
  E_INVOICE_STATUS_OPTIONS,
  TAX_REGIME_OPTIONS,
  translatedOptions,
  createTaxProfileControls,
  taxRegistrationValidator,
  toCreateShopTaxProfileRequest,
  toOptionalGstin,
} from './manage-shop/shop-tax-profile-form.helper';

@Component({
  selector: 'app-create-shop-overlay',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    InputTextModule,
    ButtonModule,
    ProgressSpinnerModule,
    SelectModule,
    TranslocoPipe,
  ],
  templateUrl: './create-shop-overlay.component.html',
  styleUrl: './create-shop-overlay.component.scss',
})
export class CreateShopOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);

  readonly isSubmitting = this.store.selectSignal(selectShopsSubmitting);
  readonly serverError = this.store.selectSignal(selectShopsErrorMessage);

  @Input() firstShop = true;
  @Output() readonly closeRequested = new EventEmitter<void>();
  @Output() readonly accountSecurityRequested = new EventEmitter<void>();

  readonly stateOptions = [...GST_STATE_OPTIONS];
  readonly regimeOptions = translatedOptions(inject(TranslocoService), TAX_REGIME_OPTIONS);
  readonly eInvoiceOptions = translatedOptions(inject(TranslocoService), E_INVOICE_STATUS_OPTIONS);

  // The GST registration is declared with the shop: a shop cannot bill until it has one.
  readonly shopForm = this.formBuilder.nonNullable.group(
    {
      name: ['', InputValidators.requiredText(120)],
      address: ['', InputValidators.requiredText(320)],
      city: ['', InputValidators.requiredText(120)],
      state: ['', InputValidators.requiredText(120)],
      pincode: ['', InputValidators.requiredText(16)],
      contactPerson: ['', InputValidators.optionalText(120)],
      mobileNumber: ['', InputValidators.optionalText(32)],
      gstNumber: ['', InputValidators.gstin()],
      ...createTaxProfileControls(),
    },
    { validators: taxRegistrationValidator() },
  );

  readonly progressSpinnerPt = {
    root: { class: 'create-shop-spinner-root' },
  };

  ngOnInit(): void {
    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
  }

  onClose(): void {
    if (this.isSubmitting()) {
      return;
    }
    this.closeRequested.emit();
  }

  submit(): void {
    if (this.isSubmitting()) return;

    if (this.shopForm.invalid) {
      this.shopForm.markAllAsTouched();
      return;
    }

    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());

    const values = this.shopForm.getRawValue();
    const payload: CreateShopRequest = {
      name: values.name.trim(),
      address: values.address.trim(),
      city: values.city.trim(),
      state: values.state.trim(),
      pincode: values.pincode.trim(),
      contactPerson: this.toOptionalValue(values.contactPerson),
      mobileNumber: this.toOptionalValue(values.mobileNumber),
      gstNumber: toOptionalGstin(values.gstNumber) ?? undefined,
      taxProfile: toCreateShopTaxProfileRequest(values),
    };

    this.store.dispatch(ShopsActions.createShopRequested({ payload }));
  }

  isRegularRegime(): boolean {
    return this.shopForm.controls.taxRegime.value === 'Regular';
  }

  private toOptionalValue(value: string): string | undefined {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : undefined;
  }
}
