import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';

import { InputValidators } from '../../../../shared/forms/input-validation';
import { GST_STATE_OPTIONS } from '../../../../shared/gst/gst-state-codes';
import { ShopDetailsDto, UpdateShopTaxProfileRequest } from '../../services/shop.service';
import { isAnotherShop } from './manage-shop-form.helper';
import {
  E_INVOICE_STATUS_OPTIONS,
  TAX_REGIME_OPTIONS,
  translatedOptions,
  createTaxProfileControls,
  taxProfileFormValue,
  taxRegistrationValidator,
  toCreateShopTaxProfileRequest,
  toOptionalGstin,
} from './shop-tax-profile-form.helper';

/**
 * The shop's GST registration, saved on its own (PUT shops/{id}/tax-profile). Only the owner may change it; everyone
 * else sees it read-only, together with the warning that billing is blocked until it is declared.
 */
@Component({
  selector: 'app-shop-tax-profile-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, ButtonModule, InputTextModule, SelectModule, TranslocoPipe],
  templateUrl: './shop-tax-profile-form.component.html',
  styleUrl: './shop-tax-profile-form.component.scss',
})
export class ShopTaxProfileFormComponent implements OnInit, OnChanges {
  private readonly formBuilder = inject(FormBuilder);

  @Input() details: ShopDetailsDto | null = null;
  @Input() canEdit = false;
  @Input() disabled = false;
  @Input() submitting = false;
  @Input() errorMessage = '';
  @Input() saved = false;
  @Output() readonly saveRequested = new EventEmitter<UpdateShopTaxProfileRequest>();

  readonly stateOptions = [...GST_STATE_OPTIONS];
  readonly regimeOptions = translatedOptions(inject(TranslocoService), TAX_REGIME_OPTIONS);
  readonly eInvoiceOptions = translatedOptions(inject(TranslocoService), E_INVOICE_STATUS_OPTIONS);

  readonly form = this.formBuilder.nonNullable.group(
    { ...createTaxProfileControls(), gstNumber: ['', InputValidators.gstin()] },
    { validators: taxRegistrationValidator() },
  );

  ngOnInit(): void {
    this.syncEditability();
  }

  ngOnChanges(changes: SimpleChanges): void {
    // Refreshed details (after a shop details save) keep a profile the owner is still editing.
    if (changes['details'] && (this.form.pristine || isAnotherShop(changes['details']))) this.patchFromDetails();
    this.syncEditability();
  }

  isNotDeclared(): boolean {
    return this.details?.taxProfile?.taxRegime === 'NotDeclared';
  }

  isRegular(): boolean {
    return this.form.controls.taxRegime.value === 'Regular';
  }

  eInvoiceRequired(): boolean {
    return this.isRegular() && this.form.controls.eInvoiceStatus.value === 'Required';
  }

  onSave(): void {
    if (!this.canEdit || this.disabled || this.submitting) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.saveRequested.emit({
      ...toCreateShopTaxProfileRequest(value),
      gstNumber: value.taxRegime === 'Unregistered' ? null : toOptionalGstin(value.gstNumber),
    });
  }

  private syncEditability(): void {
    const editable = this.canEdit && !this.disabled;
    if (editable && this.form.disabled) this.form.enable({ emitEvent: false });
    if (!editable && this.form.enabled) this.form.disable({ emitEvent: false });
  }

  private patchFromDetails(): void {
    this.form.reset(
      { ...taxProfileFormValue(this.details?.taxProfile), gstNumber: this.details?.gstNumber ?? '' },
      { emitEvent: false },
    );
  }
}
