import { FormBuilder, FormControl, FormGroup } from '@angular/forms';

import {
  ShopDetailsDto,
  UpdateBankDetailsRequest,
  UpdateShopRequest,
} from '../../services/shop.service';
import { InputValidators } from '../../../../shared/forms/input-validation';

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
    shopId: ['', InputValidators.requiredText()],
    name: ['', InputValidators.requiredText(120)],
    address: ['', InputValidators.requiredText(320)],
    city: ['', InputValidators.requiredText(120)],
    state: ['', InputValidators.requiredText(120)],
    pincode: ['', InputValidators.requiredText(16)],
    contactPerson: ['', InputValidators.optionalText(120)],
    mobileNumber: ['', InputValidators.optionalText(32)],
    gstNumber: ['', InputValidators.gstNumber()],
  });
}

export function createManageBankForm(formBuilder: FormBuilder): ManageShopBankFormGroup {
  return formBuilder.nonNullable.group({
    bankName: ['', InputValidators.optionalText(120)],
    accountNumber: ['', InputValidators.optionalText(50)],
    accountType: [''],
    ifscCode: ['', InputValidators.ifscCode()],
    accountHolderName: ['', InputValidators.optionalText(120)],
  });
}

export function patchManageShopFormsFromDetails(
  form: ManageShopFormGroup,
  bankForm: ManageShopBankFormGroup,
  details: ShopDetailsDto,
): void {
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

export function mapManageBankFormToUpdateBankDetailsRequest(
  bankForm: ManageShopBankFormGroup,
): UpdateBankDetailsRequest {
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
