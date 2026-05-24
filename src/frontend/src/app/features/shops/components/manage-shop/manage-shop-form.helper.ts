import { FormBuilder, FormControl, FormGroup, Validators } from '@angular/forms';

import {
  ShopDetailsDto,
  UpdateBankDetailsRequest,
  UpdateShopRequest,
} from '../../services/shop.service';

export const INDIA_GST_REGEX = /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/i;
export const INDIA_IFSC_REGEX = /^[A-Z]{4}0[A-Z0-9]{6}$/i;

export type ManageShopFormGroup = FormGroup<{
  shopId: FormControl<string>;
  name: FormControl<string>;
  address: FormControl<string>;
  city: FormControl<string>;
  state: FormControl<string>;
  pincode: FormControl<string>;
  contactPerson: FormControl<string>;
  mobileNumber: FormControl<string>;
  gstNumber: FormControl<string>;
}>;

export type ManageShopBankFormGroup = FormGroup<{
  bankName: FormControl<string>;
  accountNumber: FormControl<string>;
  accountType: FormControl<string>;
  ifscCode: FormControl<string>;
  accountHolderName: FormControl<string>;
}>;

export function createManageShopForm(formBuilder: FormBuilder): ManageShopFormGroup {
  return formBuilder.nonNullable.group({
    shopId: ['', [Validators.required]],
    name: ['', [Validators.required, Validators.maxLength(120)]],
    address: ['', [Validators.required, Validators.maxLength(320)]],
    city: ['', [Validators.required, Validators.maxLength(120)]],
    state: ['', [Validators.required, Validators.maxLength(120)]],
    pincode: ['', [Validators.required, Validators.maxLength(16)]],
    contactPerson: ['', [Validators.maxLength(120)]],
    mobileNumber: ['', [Validators.maxLength(32)]],
    gstNumber: ['', [Validators.maxLength(20), Validators.pattern(INDIA_GST_REGEX)]],
  });
}

export function createManageBankForm(formBuilder: FormBuilder): ManageShopBankFormGroup {
  return formBuilder.nonNullable.group({
    bankName: ['', [Validators.maxLength(120)]],
    accountNumber: ['', [Validators.maxLength(50)]],
    accountType: [''],
    ifscCode: ['', [Validators.maxLength(20), Validators.pattern(INDIA_IFSC_REGEX)]],
    accountHolderName: ['', [Validators.maxLength(120)]],
  });
}

export function patchManageShopFormsFromDetails(form: ManageShopFormGroup, bankForm: ManageShopBankFormGroup, details: ShopDetailsDto): void {
  form.patchValue({
    name: details.name,
    address: details.address,
    city: details.city,
    state: details.state,
    pincode: details.pincode,
    contactPerson: details.contactPerson ?? '',
    mobileNumber: details.mobileNumber ?? '',
    gstNumber: details.gstNumber ?? '',
  });

  bankForm.patchValue({
    bankName: details.bankName ?? '',
    accountNumber: details.bankAccountNumber ?? '',
    accountType: details.bankAccountType ?? '',
    ifscCode: details.ifscCode ?? '',
    accountHolderName: details.accountHolderName ?? '',
  });
}

export function mapManageShopFormToUpdateShopRequest(form: ManageShopFormGroup): UpdateShopRequest {
  return {
    name: form.controls.name.value.trim(),
    address: form.controls.address.value.trim(),
    city: form.controls.city.value.trim(),
    state: form.controls.state.value.trim(),
    pincode: form.controls.pincode.value.trim(),
    contactPerson: toOptionalTrimmed(form.controls.contactPerson.value),
    mobileNumber: toOptionalTrimmed(form.controls.mobileNumber.value),
    gstNumber: toOptionalTrimmed(form.controls.gstNumber.value),
  };
}

export function mapManageBankFormToUpdateBankDetailsRequest(bankForm: ManageShopBankFormGroup): UpdateBankDetailsRequest {
  return {
    bankName: toOptionalTrimmed(bankForm.controls.bankName.value),
    accountNumber: toOptionalTrimmed(bankForm.controls.accountNumber.value),
    accountType: toOptionalTrimmed(bankForm.controls.accountType.value),
    ifscCode: toOptionalTrimmed(bankForm.controls.ifscCode.value),
    accountHolderName: toOptionalTrimmed(bankForm.controls.accountHolderName.value),
  };
}

export function toOptionalTrimmed(value: string): string | undefined {
  const normalized = value.trim();
  return normalized.length > 0 ? normalized : undefined;
}
