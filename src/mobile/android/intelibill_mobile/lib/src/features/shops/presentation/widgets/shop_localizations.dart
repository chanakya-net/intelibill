import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';

extension ShopLocalizationFallback on AppLocalizations {
  String get shopsCreateTitle => 'Create Shop';

  String get shopsCreateShopInfoStepTitle => 'Shop Information';

  String get shopsCreateBankDetailsStepTitle => 'Bank Details';

  String get shopsCreateSuccessTitle => 'Shop Created';

  String shopsCreateSuccessMessage(String shopName) =>
      'Your shop "$shopName" is ready.';

  String get shopsCreateNextButton => 'Next';

  String get shopsCreateSkipButton => 'Skip';

  String get shopsCreateShopNameLabel => 'Shop Name';

  String get shopsCreateShopNameHint => 'Enter shop name';

  String get shopsCreateShopNameRequired => 'Shop Name is required.';

  String get shopsCreateAddressLabel => 'Address';

  String get shopsCreateAddressHint => 'Enter shop address';

  String get shopsCreateAddressRequired => 'Address is required.';

  String get shopsCreateCityLabel => 'City';

  String get shopsCreateCityHint => 'Enter city';

  String get shopsCreateCityRequired => 'City is required.';

  String get shopsCreateStateLabel => 'State';

  String get shopsCreateStateHint => 'Enter state';

  String get shopsCreateStateRequired => 'State is required.';

  String get shopsCreatePincodeLabel => 'Pincode';

  String get shopsCreatePincodeHint => 'Enter 6-digit pincode';

  String get shopsCreatePincodeRequired => 'Pincode is required.';

  String get shopsCreatePincodeInvalid => 'Pincode must be exactly 6 digits.';

  String get shopsCreateContactPersonLabel => 'Contact Person';

  String get shopsCreateContactPersonHint => 'Enter contact person';

  String get shopsCreateMobileNumberLabel => 'Mobile Number';

  String get shopsCreateMobileNumberHint => 'Enter 10-digit mobile number';

  String get shopsCreateMobileNumberInvalid => 'Mobile number must be 10 digits.';

  String get shopsCreateGstNumberLabel => 'GST Number';

  String get shopsCreateGstNumberHint => 'Enter GST number';

  String get shopsCreateGstNumberInvalid => 'Enter a valid GST number.';

  String get shopsCreateBankNameLabel => 'Bank Name';

  String get shopsCreateBankNameHint => 'Enter bank name';

  String get shopsCreateBankNameRequired => 'Bank Name is required.';

  String get shopsCreateAccountNumberLabel => 'Account Number';

  String get shopsCreateAccountNumberHint => 'Enter account number';

  String get shopsCreateAccountNumberRequired => 'Account Number is required.';

  String get shopsCreateAccountTypeLabel => 'Account Type';

  String get shopsCreateAccountTypeRequired => 'Account type is required.';

  String get shopsCreateIfscCodeLabel => 'IFSC Code';

  String get shopsCreateIfscCodeHint => 'Enter IFSC code';

  String get shopsCreateIfscCodeInvalid => 'Enter a valid IFSC code.';

  String get shopsCreateAccountHolderNameLabel => 'Account Holder Name';

  String get shopsCreateAccountHolderNameHint => 'Enter account holder name';

  String get shopsCreateAccountHolderNameRequired =>
      'Account Holder Name is required.';


  String get shopsCreateErrorGeneric => 'Something went wrong. Please try again.';

  String get shopsCreateErrorUnauthorized => 'You are not authorized.';

  String get shopsCreateErrorForbidden => 'You do not have permission.';

  String get shopsCreateErrorNetwork => 'Network error. Please try again.';

  String get shopsCreateErrorTimeout => 'Request timed out. Please try again.';

}
