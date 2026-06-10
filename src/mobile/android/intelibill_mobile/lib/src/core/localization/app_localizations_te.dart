// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Telugu (`te`).
class AppLocalizationsTe extends AppLocalizations {
  AppLocalizationsTe([String locale = 'te']) : super(locale);

  @override
  String get commonLanguage => 'భాష';

  @override
  String get commonCancel => 'రద్దు';

  @override
  String get commonClose => 'మూసివేయి';

  @override
  String get commonClear => 'క్లియర్';

  @override
  String get commonActions => 'చర్యలు';

  @override
  String get commonEdit => 'సవరించు';

  @override
  String get commonSave => 'సేవ్ చేయి';

  @override
  String get commonDone => 'పూర్తయింది';

  @override
  String get commonSearch => 'వెతుకు...';

  @override
  String get commonLoading => 'Loading...';

  @override
  String get languageEnIn => 'ఇంగ్లీష్';

  @override
  String get languageHiIn => 'హిందీ';

  @override
  String get languageTaIn => 'తమిళం';

  @override
  String get languageTeIn => 'తెలుగు';

  @override
  String get languageBnIn => 'బంగ్లా';

  @override
  String get languageMlIn => 'మలయాళం';

  @override
  String get languageKnIn => 'కన్నడ';

  @override
  String get languageMrIn => 'మరాఠీ';

  @override
  String get languageGuIn => 'గుజరాతీ';

  @override
  String get shellAppName => 'Intelibill';

  @override
  String get shellLogout => 'లాగౌట్';

  @override
  String get shellProfile => 'ప్రొఫైల్';

  @override
  String get shellLanguage => 'భాష';

  @override
  String get shellDashboard => 'డాష్‌బోర్డ్';

  @override
  String get shellManageInventory => 'ఇన్వెంటరీ';

  @override
  String get shellManageSales => 'అమ్మకాలు';

  @override
  String get shellNewSale => 'కొత్త అమ్మకం';

  @override
  String get shellManageCustomers => 'కస్టమర్లు';

  @override
  String get shellManageSuppliers => 'సరఫరాదారులను నిర్వహించండి';

  @override
  String get shellManageExpenses => 'ఖర్చులు';

  @override
  String get shellManageBankAccounts => 'బ్యాంకు ఖాతాలు';

  @override
  String get shellManageUsers => 'వినియోగదారులను నిర్వహించండి';

  @override
  String get shellAddShop => 'దుకాణం జోడించండి';

  @override
  String get shellManageShop => 'దుకాణం నిర్వహించండి';

  @override
  String get shellMore => 'More';

  @override
  String get shellManageDiscounts => 'Discounts';

  @override
  String get shellChangePassword => 'Change Password';

  @override
  String get shellSalesHistory => 'Sales History';

  @override
  String get shellProfitLossReport => 'Profit & Loss';

  @override
  String get shellInventoryAdjustments => 'Inventory Adjustments';

  @override
  String get shellInventoryBatchesOverview => 'Inventory Batches';

  @override
  String get shellBatchInventoryInbound => 'Batch Inventory Inbound';

  @override
  String get shellAddNewProduct => 'Add New Product';

  @override
  String get authLoginNow => 'ఇప్పుడే లాగిన్ అవ్వండి';

  @override
  String get authPassword => 'పాస్‌వర్డ్';

  @override
  String get authLoginCta => 'లాగిన్';

  @override
  String get authEmailAddress => 'ఇమెయిల్ చిరునామా';

  @override
  String get authRememberMe => 'నన్ను గుర్తుంచుకో';

  @override
  String get authForgotPassword => 'పాస్‌వర్డ్ మర్చిపోయారా?';

  @override
  String get authRegister => 'Register';

  @override
  String get authValidationEmailInvalid => 'చెల్లుబాటు అయ్యే ఇమెయిల్ ఇవ్వండి.';

  @override
  String get authValidationPasswordRequired => 'పాస్‌వర్డ్ అవసరం.';

  @override
  String get authValidationLoginIdentifierRequired =>
      'మీ ఇమెయిల్ లేదా మొబైల్ నంబర్‌ను నమోదు చేయండి.';

  @override
  String get authValidationEmailRequired => 'ఇమెయిల్ అవసరం.';

  @override
  String get customersTitle => 'కస్టమర్లు';

  @override
  String get customersAddCustomer => 'కస్టమర్‌ని జోడించండి';

  @override
  String get customersNoCustomersFound => 'కస్టమర్లు కనుగొనబడలేదు';

  @override
  String get customersUnableToLoad => 'Unable to load customers';

  @override
  String get customersRetry => 'Retry';

  @override
  String get customersInactive => 'Inactive';

  @override
  String get customersOutstandingLabel => 'Outstanding:';

  @override
  String get customersErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get customersErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get customersErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get customersErrorForbidden =>
      'You do not have permission to view customers.';

  @override
  String get customersErrorGeneric =>
      'Unable to load customers. Please try again.';

  @override
  String get suppliersTitle => 'సరఫరాదారులు';

  @override
  String get suppliersAddSupplier => 'సరఫరాదారిని జోడించండి';

  @override
  String get suppliersNoSuppliersFound => 'సరఫరాదారులు లేరు';

  @override
  String get suppliersEmptyDescription =>
      'Suppliers you add will appear here with contact details and payables.';

  @override
  String suppliersSummaryCount(int count) {
    return '$count suppliers';
  }

  @override
  String suppliersSummaryActive(int count) {
    return '$count active';
  }

  @override
  String suppliersSummaryPreferred(int count) {
    return '$count preferred';
  }

  @override
  String suppliersSummaryPayable(String amount) {
    return '$amount payable';
  }

  @override
  String get suppliersUnableToLoad => 'Unable to load suppliers';

  @override
  String get suppliersRetry => 'Retry';

  @override
  String get suppliersInactive => 'Inactive';

  @override
  String get suppliersPreferred => 'Preferred';

  @override
  String get suppliersBalanceDueLabel => 'Balance Due:';

  @override
  String get suppliersErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get suppliersErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get suppliersErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get suppliersErrorForbidden =>
      'You do not have permission to view suppliers.';

  @override
  String get suppliersErrorGeneric =>
      'Unable to load suppliers. Please try again.';

  @override
  String get suppliersCreateNameLabel => 'Name';

  @override
  String get suppliersCreateNameRequired => 'Name is required.';

  @override
  String get suppliersCreateNameMax => 'Name must be 180 characters or fewer.';

  @override
  String get suppliersCreateContactPersonLabel => 'Contact Person';

  @override
  String get suppliersCreateContactPersonMax =>
      'Contact person must be 120 characters or fewer.';

  @override
  String get suppliersCreateContactPhoneLabel => 'Contact Phone';

  @override
  String get suppliersCreateContactPhoneMax =>
      'Contact phone must be 32 characters or fewer.';

  @override
  String get suppliersCreateContactPhoneInvalid =>
      'Enter a valid phone number.';

  @override
  String get suppliersCreateAddressLabel => 'Address';

  @override
  String get suppliersCreateAddressRequired => 'Address is required.';

  @override
  String get suppliersCreateAddressMax =>
      'Address must be 320 characters or fewer.';

  @override
  String get suppliersCreateCityLabel => 'City';

  @override
  String get suppliersCreateCityRequired => 'City is required.';

  @override
  String get suppliersCreateCityMax => 'City must be 120 characters or fewer.';

  @override
  String get suppliersCreateStateLabel => 'State';

  @override
  String get suppliersCreateStateRequired => 'State is required.';

  @override
  String get suppliersCreateStateMax =>
      'State must be 120 characters or fewer.';

  @override
  String get suppliersCreatePinLabel => 'PIN';

  @override
  String get suppliersCreatePinRequired => 'PIN is required.';

  @override
  String get suppliersCreatePinMax => 'PIN must be 16 characters or fewer.';

  @override
  String get suppliersCreateActiveLabel => 'Active';

  @override
  String get suppliersCreatePreferredLabel => 'Preferred';

  @override
  String get suppliersCreateSuccess => 'Supplier created successfully.';

  @override
  String get suppliersCreateErrorGeneric =>
      'Unable to create supplier. Please try again.';

  @override
  String get suppliersCreateErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get suppliersCreateErrorForbidden =>
      'You do not have permission to create suppliers.';

  @override
  String get suppliersCreateErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get suppliersCreateErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get inventoryTitle => 'ఇన్వెంటరీ';

  @override
  String get inventoryAddNewProductDescription =>
      'ప్రస్తుతం యాక్టివ్ షాప్‌కు అనుసంధానించిన ఉత్పత్తిని సృష్టించండి.';

  @override
  String get inventoryProductsNoProductsFound => 'No products found';

  @override
  String get inventoryProductsUnableToLoad => 'Unable to load products';

  @override
  String get inventoryProductsRetry => 'Retry';

  @override
  String get inventoryProductsInactive => 'Inactive';

  @override
  String get inventoryProductsAddProduct => 'Add Product';

  @override
  String get inventoryProductsCreateSuccess => 'Product created successfully.';

  @override
  String get inventoryProductsUpdateSuccess => 'Product updated successfully.';

  @override
  String get inventoryCreateTitle => 'Add Product';

  @override
  String get inventoryCreateNameLabel => 'Name';

  @override
  String get inventoryCreateNameRequired => 'Name is required.';

  @override
  String get inventoryCreateNameMax => 'Name must be 180 characters or fewer.';

  @override
  String get inventoryCreateBarcodeLabel => 'Barcode';

  @override
  String get inventoryCreateBarcodeRequired => 'Barcode is required.';

  @override
  String get inventoryCreateBarcodeMax =>
      'Barcode must be 120 characters or fewer.';

  @override
  String get inventoryCreateUomLabel => 'Unit of Measure';

  @override
  String get inventoryCreateUomRequired => 'Unit of measure is required.';

  @override
  String get inventoryCreateUomMax => 'UOM must be 40 characters or fewer.';

  @override
  String get inventoryCreateDescriptionLabel => 'Description';

  @override
  String get inventoryCreateDescriptionMax =>
      'Description must be 320 characters or fewer.';

  @override
  String get inventoryCreateProductActiveLabel => 'Active';

  @override
  String get inventoryCreateErrorGeneric =>
      'Unable to save product. Please try again.';

  @override
  String get inventoryCreateErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get inventoryCreateErrorForbidden =>
      'You do not have permission to manage products.';

  @override
  String get inventoryCreateErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get inventoryCreateErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get inventoryUpdateTitle => 'Edit Product';

  @override
  String get inventoryProductErrorGeneric =>
      'Unable to load products. Please try again.';

  @override
  String get inventoryProductErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get inventoryProductErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get inventoryProductErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get inventoryProductErrorForbidden =>
      'You do not have permission to view products.';

  @override
  String get inventoryMenuAddInventory => 'Add Inventory';

  @override
  String get inventoryMenuBatchOverview => 'Batch Overview';

  @override
  String get inventoryMenuAdjustmentHistory => 'Adjustment History';

  @override
  String get inventoryInboundSectionProduct => 'Product Details';

  @override
  String get inventoryInboundSectionBatch => 'Batch Details';

  @override
  String get inventoryInboundSectionAdditional => 'Additional Details';

  @override
  String get inventoryInboundItemNameLabel => 'Item Name';

  @override
  String get inventoryInboundItemNameRequired => 'Item name is required.';

  @override
  String get inventoryInboundItemNameMax =>
      'Item name must be 200 characters or fewer.';

  @override
  String get inventoryInboundBarcodeLabel => 'Barcode';

  @override
  String get inventoryInboundBarcodeRequired => 'Barcode is required.';

  @override
  String get inventoryInboundBarcodeMax =>
      'Barcode must be 120 characters or fewer.';

  @override
  String get inventoryInboundUomLabel => 'Unit of Measure';

  @override
  String get inventoryInboundUomRequired => 'Unit of measure is required.';

  @override
  String get inventoryInboundUomMax => 'UOM must be 40 characters or fewer.';

  @override
  String get inventoryInboundBatchNumberLabel => 'Batch Number';

  @override
  String get inventoryInboundBatchNumberHint => 'Optional batch identifier';

  @override
  String get inventoryInboundBatchNumberMax =>
      'Batch number must be 80 characters or fewer.';

  @override
  String get inventoryInboundQuantityLabel => 'Quantity';

  @override
  String get inventoryInboundQuantityRequired => 'Quantity is required.';

  @override
  String get inventoryInboundQuantityMin => 'Quantity must be greater than 0.';

  @override
  String get inventoryInboundCostPriceLabel => 'Cost Price';

  @override
  String get inventoryInboundCostPriceRequired => 'Cost price is required.';

  @override
  String get inventoryInboundMrpLabel => 'MRP';

  @override
  String get inventoryInboundMrpRequired => 'MRP is required.';

  @override
  String get inventoryInboundSalesPriceLabel => 'Sales Price';

  @override
  String get inventoryInboundSalesPriceRequired => 'Sales price is required.';

  @override
  String get inventoryInboundTaxRateLabel => 'Tax Rate (%)';

  @override
  String get inventoryInboundTaxRateRange =>
      'Tax rate must be between 0 and 100.';

  @override
  String get inventoryInboundTaxIncludedLabel => 'Tax Included';

  @override
  String get inventoryInboundExpiryDateLabel => 'Expiry Date';

  @override
  String get inventoryInboundManufacturingDateLabel => 'Manufacturing Date';

  @override
  String get inventoryInboundReferenceLabel => 'Reference Number';

  @override
  String get inventoryInboundReferenceMax =>
      'Reference must be 80 characters or fewer.';

  @override
  String get inventoryInboundNotesLabel => 'Notes';

  @override
  String get inventoryInboundNotesMax =>
      'Notes must be 500 characters or fewer.';

  @override
  String get inventoryInboundNumberInvalid => 'Enter a valid number.';

  @override
  String get inventoryInboundSuccess => 'Inventory added successfully.';

  @override
  String get inventoryInboundErrorGeneric =>
      'Unable to add inventory. Please try again.';

  @override
  String get inventoryInboundErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get inventoryInboundErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get placeholderBody => 'This feature is coming soon.';

  @override
  String get notFoundTitle => 'పేజీ కనబడలేదు';

  @override
  String get notFoundGoBack => 'వెనక్కి వెళ్ళండి';

  @override
  String get profileEditTitle => 'Edit Profile';

  @override
  String get profileFirstNameLabel => 'First Name';

  @override
  String get profileFirstNameHint => 'Enter your first name';

  @override
  String get profileFirstNameRequired => 'First name is required';

  @override
  String get profileLastNameLabel => 'Last Name';

  @override
  String get profileLastNameHint => 'Enter your last name';

  @override
  String get profileLastNameRequired => 'Last name is required';

  @override
  String get profileEmailLabel => 'Email';

  @override
  String get profileEmailHint => 'Enter your email';

  @override
  String get profileEmailRequired => 'Email is required';

  @override
  String get profileEmailInvalid => 'Please enter a valid email';

  @override
  String get profilePhoneLabel => 'Phone Number (Optional)';

  @override
  String get profilePhoneHint => 'Enter your phone number';

  @override
  String get profilePhoneMin => 'Phone number must be at least 10 digits';

  @override
  String get profileUpdateButton => 'Update Profile';

  @override
  String get profileUpdatingButton => 'Updating...';

  @override
  String get profileUpdateSuccess => 'Profile updated successfully';

  @override
  String get profileUnableToLoad => 'Unable to load profile';

  @override
  String get passwordChangeTitle => 'Change Password';

  @override
  String get passwordCurrentLabel => 'Current Password';

  @override
  String get passwordCurrentHint => 'Enter your current password';

  @override
  String get passwordCurrentRequired => 'Current password is required';

  @override
  String get passwordNewLabel => 'New Password';

  @override
  String get passwordNewHint => 'Enter your new password';

  @override
  String get passwordNewRequired => 'New password is required';

  @override
  String get passwordConfirmLabel => 'Confirm Password';

  @override
  String get passwordConfirmHint => 'Confirm your new password';

  @override
  String get passwordConfirmRequired => 'Please confirm your password';

  @override
  String get passwordConfirmMismatch => 'Passwords do not match';

  @override
  String get passwordChangeButton => 'Change Password';

  @override
  String get passwordChangingButton => 'Changing password...';

  @override
  String get passwordChangeSuccess => 'Password changed successfully';

  @override
  String get passwordUnableToInitialize =>
      'Unable to initialize password screen';

  @override
  String get profileSettingsTitle => 'Profile Settings';

  @override
  String get profileSettingsActiveShop => 'Active Shop';

  @override
  String get profileSettingsEditProfile => 'Edit Profile';

  @override
  String get profileSettingsChangePassword => 'Change Password';

  @override
  String get inventoryBatchesNoBatchesFound => 'No batches found';

  @override
  String get inventoryBatchesUnableToLoad => 'Unable to load batches';

  @override
  String get inventoryBatchesRetry => 'Retry';

  @override
  String get inventoryBatchesVoided => 'Voided';

  @override
  String get inventoryBatchesAdjustAction => 'Adjust';

  @override
  String get inventoryBatchesAdjustTitle => 'Adjust Stock';

  @override
  String get inventoryBatchesAdjustPreviewLabel => 'New Quantity';

  @override
  String get inventoryBatchesAdjustDirectionLabel => 'Direction';

  @override
  String get inventoryBatchesAdjustDirectionIncrease => 'Increase';

  @override
  String get inventoryBatchesAdjustDirectionDecrease => 'Decrease';

  @override
  String get inventoryBatchesAdjustQuantityLabel => 'Quantity';

  @override
  String get inventoryBatchesAdjustQuantityRequired => 'Quantity is required.';

  @override
  String get inventoryBatchesAdjustQuantityMin =>
      'Quantity must be greater than 0.';

  @override
  String get inventoryBatchesAdjustQuantityMax =>
      'Quantity cannot exceed current batch quantity.';

  @override
  String get inventoryBatchesAdjustReasonLabel => 'Reason';

  @override
  String get inventoryBatchesAdjustNotesLabel => 'Notes';

  @override
  String get inventoryBatchesAdjustNotesMax =>
      'Notes must be 500 characters or fewer.';

  @override
  String get inventoryBatchesAdjustNotesRequired =>
      'Notes are required for this reason.';

  @override
  String get inventoryBatchesAdjustPerformedAtLabel => 'Performed At';

  @override
  String get inventoryBatchesAdjustSuccess => 'Stock adjusted successfully.';

  @override
  String get inventoryBatchesErrorGeneric =>
      'Unable to load batches. Please try again.';

  @override
  String get inventoryBatchesErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get inventoryBatchesErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get inventoryBatchesErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get inventoryBatchesErrorForbidden =>
      'You do not have permission to view batches.';

  @override
  String get inventoryAdjustReasonDamaged => 'Damaged';

  @override
  String get inventoryAdjustReasonExpired => 'Expired';

  @override
  String get inventoryAdjustReasonStolen => 'Stolen';

  @override
  String get inventoryAdjustReasonMissingLost => 'Missing / Lost';

  @override
  String get inventoryAdjustReasonStockCountCorrection =>
      'Stock Count Correction';

  @override
  String get inventoryAdjustReasonOtherLoss => 'Other Loss';

  @override
  String get inventoryAdjustReasonFoundStock => 'Found Stock';

  @override
  String get inventoryAdjustReasonReturnRestockCorrection =>
      'Return / Restock Correction';

  @override
  String get inventoryAdjustReasonOtherGain => 'Other Gain';

  @override
  String get inventoryAdjustmentsTitle => 'Adjustment History';

  @override
  String get inventoryAdjustmentsNoAdjustmentsFound => 'No adjustments found';

  @override
  String get inventoryAdjustmentsUnableToLoad => 'Unable to load adjustments';

  @override
  String get inventoryAdjustmentsRetry => 'Retry';

  @override
  String get inventoryAdjustmentsVoided => 'Voided';

  @override
  String get inventoryAdjustmentsIncrease => 'Increase';

  @override
  String get inventoryAdjustmentsDecrease => 'Decrease';

  @override
  String get inventoryAdjustmentsPerformedByLabel => 'Performed by';

  @override
  String get inventoryAdjustmentsCostImpactLabel => 'Cost impact';

  @override
  String get barcodeScannerTitle => 'Scan Barcode';

  @override
  String get barcodeScannerHint => 'Point the camera at a barcode';

  @override
  String get barcodeScannerSearching => 'Starting camera...';

  @override
  String get barcodeScannerSuccess => 'Barcode detected';

  @override
  String get barcodeScannerPermissionDenied => 'Camera permission denied';

  @override
  String get barcodeScannerUnavailable => 'Scanner unavailable';

  @override
  String get shopsCreateTitle => 'Create Shop';

  @override
  String get shopsCreateShopInfoStepTitle => 'Shop Information';

  @override
  String get shopsCreateBankDetailsStepTitle => 'Bank Details';

  @override
  String get shopsCreateSuccessTitle => 'Shop Created';

  @override
  String get shopsCreateSuccessDefaultShopName => 'New Shop';

  @override
  String shopsCreateSuccessMessage(String shopName) {
    return 'Your shop \"$shopName\" is ready.';
  }

  @override
  String get shopsCreateNextButton => 'Next';

  @override
  String get shopsCreateSkipButton => 'Skip';

  @override
  String get shopsCreateShopNameLabel => 'Shop Name';

  @override
  String get shopsCreateShopNameHint => 'Enter shop name';

  @override
  String get shopsCreateShopNameRequired => 'Shop Name is required.';

  @override
  String get shopsCreateAddressLabel => 'Address';

  @override
  String get shopsCreateAddressHint => 'Enter shop address';

  @override
  String get shopsCreateAddressRequired => 'Address is required.';

  @override
  String get shopsCreateCityLabel => 'City';

  @override
  String get shopsCreateCityHint => 'Enter city';

  @override
  String get shopsCreateCityRequired => 'City is required.';

  @override
  String get shopsCreateStateLabel => 'State';

  @override
  String get shopsCreateStateHint => 'Enter state';

  @override
  String get shopsCreateStateRequired => 'State is required.';

  @override
  String get shopsCreatePincodeLabel => 'Pincode';

  @override
  String get shopsCreatePincodeHint => 'Enter 6-digit pincode';

  @override
  String get shopsCreatePincodeRequired => 'Pincode is required.';

  @override
  String get shopsCreatePincodeInvalid => 'Pincode must be exactly 6 digits.';

  @override
  String get shopsCreateContactPersonLabel => 'Contact Person';

  @override
  String get shopsCreateContactPersonHint => 'Enter contact person';

  @override
  String get shopsCreateMobileNumberLabel => 'Mobile Number';

  @override
  String get shopsCreateMobileNumberHint => 'Enter 10-digit mobile number';

  @override
  String get shopsCreateMobileNumberInvalid =>
      'Mobile number must be 10 digits.';

  @override
  String get shopsCreateGstNumberLabel => 'GST Number';

  @override
  String get shopsCreateGstNumberHint => 'Enter GST number';

  @override
  String get shopsCreateGstNumberInvalid => 'Enter a valid GST number.';

  @override
  String get shopsCreateBankNameLabel => 'Bank Name';

  @override
  String get shopsCreateBankNameHint => 'Enter bank name';

  @override
  String get shopsCreateBankNameRequired => 'Bank Name is required.';

  @override
  String get shopsCreateAccountNumberLabel => 'Account Number';

  @override
  String get shopsCreateAccountNumberHint => 'Enter account number';

  @override
  String get shopsCreateAccountNumberRequired => 'Account Number is required.';

  @override
  String get shopsCreateAccountTypeLabel => 'Account Type';

  @override
  String get shopsCreateAccountTypeRequired => 'Account type is required.';

  @override
  String get shopsCreateAccountTypeSavings => 'Savings';

  @override
  String get shopsCreateAccountTypeCurrent => 'Current';

  @override
  String get shopsCreateAccountTypeOverdraft => 'Overdraft';

  @override
  String get shopsCreateIfscCodeLabel => 'IFSC Code';

  @override
  String get shopsCreateIfscCodeHint => 'Enter IFSC code';

  @override
  String get shopsCreateIfscCodeInvalid => 'Enter a valid IFSC code.';

  @override
  String get shopsCreateAccountHolderNameLabel => 'Account Holder Name';

  @override
  String get shopsCreateAccountHolderNameHint => 'Enter account holder name';

  @override
  String get shopsCreateAccountHolderNameRequired =>
      'Account Holder Name is required.';

  @override
  String get shopsCreateErrorGeneric =>
      'Something went wrong. Please try again.';

  @override
  String get shopsCreateErrorUnauthorized => 'You are not authorized.';

  @override
  String get shopsCreateErrorForbidden => 'You do not have permission.';

  @override
  String get shopsCreateErrorNetwork => 'Network error. Please try again.';

  @override
  String get shopsCreateErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get shopsManageSelectShopTitle => 'Manage Shop';

  @override
  String get shopsManageSelectShopLabel => 'Select Shop';

  @override
  String get shopsManageSelectShopRequired => 'Please select a shop.';

  @override
  String get shopsManageSelectShopHint =>
      'Choose a shop to update its details.';

  @override
  String get shopsManageEditDetailsTitle => 'Edit Shop Details';

  @override
  String get shopsManageSuccessTitle => 'Shop Updated';

  @override
  String get shopsManageSuccessDefaultShopName => 'Shop';

  @override
  String shopsManageSuccessMessage(String shopName) {
    return 'Your shop \"$shopName\" has been updated.';
  }

  @override
  String get shopsManageErrorGeneric =>
      'Something went wrong. Please try again.';

  @override
  String get shopsManageErrorUnauthorized => 'You are not authorized.';

  @override
  String get shopsManageErrorForbidden => 'You do not have permission.';

  @override
  String get shopsManageErrorNetwork => 'Network error. Please try again.';

  @override
  String get shopsManageErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get dashboardSubtitle => 'Operational snapshot for your active shop.';

  @override
  String dashboardGreetingMorning(String shopName) {
    return 'Good morning, $shopName';
  }

  @override
  String dashboardGreetingAfternoon(String shopName) {
    return 'Good afternoon, $shopName';
  }

  @override
  String dashboardGreetingEvening(String shopName) {
    return 'Good evening, $shopName';
  }

  @override
  String get dashboardRangeLast7Days => 'Last 7 days';

  @override
  String get dashboardRangeLast30Days => 'Last 30 days';

  @override
  String get dashboardRangeCustom => 'Custom';

  @override
  String get dashboardRangeFrom => 'From';

  @override
  String get dashboardRangeTo => 'To';

  @override
  String get dashboardKpiSalesRevenue => 'Sales Revenue';

  @override
  String get dashboardKpiNetProfit => 'Net Profit';

  @override
  String get dashboardKpiInvoiceCount => 'Invoices';

  @override
  String get dashboardKpiLowStockItems => 'Low Stock Items';

  @override
  String get dashboardKpiStockValue => 'Stock Value';

  @override
  String get dashboardKpiCustomerCreditDue => 'Customer Credit Due';

  @override
  String get dashboardKpiSupplierPayables => 'Supplier Payables';

  @override
  String get dashboardKpiExpenses => 'Expenses';

  @override
  String get dashboardKpiVsPreviousPeriod => 'vs previous period';

  @override
  String get dashboardChartSalesTrend => 'Sales Trend';

  @override
  String get dashboardChartSalesTrendSubtitle =>
      'Total sales amount per day for the selected period.';

  @override
  String get dashboardChartRevenueVsExpenses => 'Revenue vs Expenses';

  @override
  String get dashboardChartRevenueVsExpensesSubtitle => 'Daily cash movement.';

  @override
  String get dashboardChartRevenue => 'Revenue';

  @override
  String get dashboardChartExpenses => 'Expenses';

  @override
  String get dashboardNoChartData => 'No chart data for the selected period.';

  @override
  String get dashboardLatestSalesEyebrow => 'Recent Activity';

  @override
  String get dashboardLatestSalesTitle => 'Latest Sales';

  @override
  String get dashboardLatestSalesEmptyTitle => 'No recent sales';

  @override
  String get dashboardLatestSalesEmptyDescription =>
      'The latest active-shop sales will appear here.';

  @override
  String get dashboardLatestSalesViewSales => 'View all sales';

  @override
  String get dashboardAlertsEyebrow => 'Alerts';

  @override
  String get dashboardAlertsTitle => 'Needs Attention';

  @override
  String get dashboardAlertsEmpty => 'Nothing needs attention right now.';

  @override
  String get dashboardAlertsView => 'View';

  @override
  String get dashboardQuickActionsTitle => 'Quick actions';

  @override
  String get dashboardQuickActionNewSale => 'New sale';

  @override
  String get dashboardQuickActionAddInventory => 'Add inventory';

  @override
  String get dashboardQuickActionExpenses => 'Expenses';

  @override
  String get dashboardQuickActionProfitLoss => 'Profit & Loss';

  @override
  String get dashboardUnableToLoad => 'Unable to load dashboard';

  @override
  String get dashboardRetry => 'Retry';

  @override
  String get dashboardNoData => 'No dashboard data available.';

  @override
  String get dashboardAccessRestrictedTitle =>
      'Dashboard is for owners and managers';

  @override
  String get dashboardAccessRestrictedBody =>
      'Your current shop role does not include dashboard access. Open Sales to continue working.';

  @override
  String get dashboardAccessRestrictedAction => 'Open Sales';

  @override
  String get dashboardErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get dashboardErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get dashboardErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get dashboardErrorForbidden =>
      'You do not have permission to view the dashboard.';

  @override
  String get dashboardErrorGeneric =>
      'Unable to load dashboard. Please try again.';
}

/// The translations for Telugu, as used in India (`te_IN`).
class AppLocalizationsTeIn extends AppLocalizationsTe {
  AppLocalizationsTeIn() : super('te_IN');

  @override
  String get commonLanguage => 'భాష';

  @override
  String get commonCancel => 'రద్దు';

  @override
  String get commonClose => 'మూసివేయి';

  @override
  String get commonClear => 'క్లియర్';

  @override
  String get commonActions => 'చర్యలు';

  @override
  String get commonEdit => 'సవరించు';

  @override
  String get commonSave => 'సేవ్ చేయి';

  @override
  String get commonDone => 'పూర్తయింది';

  @override
  String get commonSearch => 'వెతుకు...';

  @override
  String get commonLoading => 'Loading...';

  @override
  String get languageEnIn => 'ఇంగ్లీష్';

  @override
  String get languageHiIn => 'హిందీ';

  @override
  String get languageTaIn => 'తమిళం';

  @override
  String get languageTeIn => 'తెలుగు';

  @override
  String get languageBnIn => 'బంగ్లా';

  @override
  String get languageMlIn => 'మలయాళం';

  @override
  String get languageKnIn => 'కన్నడ';

  @override
  String get languageMrIn => 'మరాఠీ';

  @override
  String get languageGuIn => 'గుజరాతీ';

  @override
  String get shellAppName => 'Intelibill';

  @override
  String get shellLogout => 'లాగౌట్';

  @override
  String get shellProfile => 'ప్రొఫైల్';

  @override
  String get shellLanguage => 'భాష';

  @override
  String get shellDashboard => 'డాష్‌బోర్డ్';

  @override
  String get shellManageInventory => 'ఇన్వెంటరీ';

  @override
  String get shellManageSales => 'అమ్మకాలు';

  @override
  String get shellNewSale => 'కొత్త అమ్మకం';

  @override
  String get shellManageCustomers => 'కస్టమర్లు';

  @override
  String get shellManageSuppliers => 'సరఫరాదారులను నిర్వహించండి';

  @override
  String get shellManageExpenses => 'ఖర్చులు';

  @override
  String get shellManageBankAccounts => 'బ్యాంకు ఖాతాలు';

  @override
  String get shellManageUsers => 'వినియోగదారులను నిర్వహించండి';

  @override
  String get shellAddShop => 'దుకాణం జోడించండి';

  @override
  String get shellManageShop => 'దుకాణం నిర్వహించండి';

  @override
  String get shellMore => 'More';

  @override
  String get shellManageDiscounts => 'Discounts';

  @override
  String get shellChangePassword => 'Change Password';

  @override
  String get shellSalesHistory => 'Sales History';

  @override
  String get shellProfitLossReport => 'Profit & Loss';

  @override
  String get shellInventoryAdjustments => 'Inventory Adjustments';

  @override
  String get shellInventoryBatchesOverview => 'Inventory Batches';

  @override
  String get shellBatchInventoryInbound => 'Batch Inventory Inbound';

  @override
  String get shellAddNewProduct => 'Add New Product';

  @override
  String get authLoginNow => 'ఇప్పుడే లాగిన్ అవ్వండి';

  @override
  String get authPassword => 'పాస్‌వర్డ్';

  @override
  String get authLoginCta => 'లాగిన్';

  @override
  String get authEmailAddress => 'ఇమెయిల్ చిరునామా';

  @override
  String get authRememberMe => 'నన్ను గుర్తుంచుకో';

  @override
  String get authForgotPassword => 'పాస్‌వర్డ్ మర్చిపోయారా?';

  @override
  String get authRegister => 'Register';

  @override
  String get authValidationEmailInvalid => 'చెల్లుబాటు అయ్యే ఇమెయిల్ ఇవ్వండి.';

  @override
  String get authValidationPasswordRequired => 'పాస్‌వర్డ్ అవసరం.';

  @override
  String get authValidationLoginIdentifierRequired =>
      'మీ ఇమెయిల్ లేదా మొబైల్ నంబర్‌ను నమోదు చేయండి.';

  @override
  String get authValidationEmailRequired => 'ఇమెయిల్ అవసరం.';

  @override
  String get customersTitle => 'కస్టమర్లు';

  @override
  String get customersAddCustomer => 'కస్టమర్‌ని జోడించండి';

  @override
  String get customersNoCustomersFound => 'కస్టమర్లు కనుగొనబడలేదు';

  @override
  String get customersUnableToLoad => 'Unable to load customers';

  @override
  String get customersRetry => 'Retry';

  @override
  String get customersInactive => 'Inactive';

  @override
  String get customersOutstandingLabel => 'Outstanding:';

  @override
  String get customersErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get customersErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get customersErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get customersErrorForbidden =>
      'You do not have permission to view customers.';

  @override
  String get customersErrorGeneric =>
      'Unable to load customers. Please try again.';

  @override
  String get suppliersTitle => 'సరఫరాదారులు';

  @override
  String get suppliersAddSupplier => 'సరఫరాదారిని జోడించండి';

  @override
  String get suppliersNoSuppliersFound => 'సరఫరాదారులు లేరు';

  @override
  String get suppliersEmptyDescription =>
      'Suppliers you add will appear here with contact details and payables.';

  @override
  String suppliersSummaryCount(int count) {
    return '$count suppliers';
  }

  @override
  String suppliersSummaryActive(int count) {
    return '$count active';
  }

  @override
  String suppliersSummaryPreferred(int count) {
    return '$count preferred';
  }

  @override
  String suppliersSummaryPayable(String amount) {
    return '$amount payable';
  }

  @override
  String get suppliersUnableToLoad => 'Unable to load suppliers';

  @override
  String get suppliersRetry => 'Retry';

  @override
  String get suppliersInactive => 'Inactive';

  @override
  String get suppliersPreferred => 'Preferred';

  @override
  String get suppliersBalanceDueLabel => 'Balance Due:';

  @override
  String get suppliersErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get suppliersErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get suppliersErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get suppliersErrorForbidden =>
      'You do not have permission to view suppliers.';

  @override
  String get suppliersErrorGeneric =>
      'Unable to load suppliers. Please try again.';

  @override
  String get inventoryTitle => 'ఇన్వెంటరీ';

  @override
  String get inventoryAddNewProductDescription =>
      'ప్రస్తుతం యాక్టివ్ షాప్‌కు అనుసంధానించిన ఉత్పత్తిని సృష్టించండి.';

  @override
  String get inventoryProductsNoProductsFound => 'No products found';

  @override
  String get inventoryProductsUnableToLoad => 'Unable to load products';

  @override
  String get inventoryProductsRetry => 'Retry';

  @override
  String get inventoryProductsInactive => 'Inactive';

  @override
  String get inventoryProductsAddProduct => 'Add Product';

  @override
  String get inventoryProductsCreateSuccess => 'Product created successfully.';

  @override
  String get inventoryProductsUpdateSuccess => 'Product updated successfully.';

  @override
  String get inventoryCreateTitle => 'Add Product';

  @override
  String get inventoryCreateNameLabel => 'Name';

  @override
  String get inventoryCreateNameRequired => 'Name is required.';

  @override
  String get inventoryCreateNameMax => 'Name must be 180 characters or fewer.';

  @override
  String get inventoryCreateBarcodeLabel => 'Barcode';

  @override
  String get inventoryCreateBarcodeRequired => 'Barcode is required.';

  @override
  String get inventoryCreateBarcodeMax =>
      'Barcode must be 120 characters or fewer.';

  @override
  String get inventoryCreateUomLabel => 'Unit of Measure';

  @override
  String get inventoryCreateUomRequired => 'Unit of measure is required.';

  @override
  String get inventoryCreateUomMax => 'UOM must be 40 characters or fewer.';

  @override
  String get inventoryCreateDescriptionLabel => 'Description';

  @override
  String get inventoryCreateDescriptionMax =>
      'Description must be 320 characters or fewer.';

  @override
  String get inventoryCreateProductActiveLabel => 'Active';

  @override
  String get inventoryCreateErrorGeneric =>
      'Unable to save product. Please try again.';

  @override
  String get inventoryCreateErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get inventoryCreateErrorForbidden =>
      'You do not have permission to manage products.';

  @override
  String get inventoryCreateErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get inventoryCreateErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get inventoryUpdateTitle => 'Edit Product';

  @override
  String get inventoryProductErrorGeneric =>
      'Unable to load products. Please try again.';

  @override
  String get inventoryProductErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get inventoryProductErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get inventoryProductErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get inventoryProductErrorForbidden =>
      'You do not have permission to view products.';

  @override
  String get inventoryMenuAddInventory => 'Add Inventory';

  @override
  String get inventoryMenuBatchOverview => 'Batch Overview';

  @override
  String get inventoryMenuAdjustmentHistory => 'Adjustment History';

  @override
  String get inventoryInboundSectionProduct => 'Product Details';

  @override
  String get inventoryInboundSectionBatch => 'Batch Details';

  @override
  String get inventoryInboundSectionAdditional => 'Additional Details';

  @override
  String get inventoryInboundItemNameLabel => 'Item Name';

  @override
  String get inventoryInboundItemNameRequired => 'Item name is required.';

  @override
  String get inventoryInboundItemNameMax =>
      'Item name must be 200 characters or fewer.';

  @override
  String get inventoryInboundBarcodeLabel => 'Barcode';

  @override
  String get inventoryInboundBarcodeRequired => 'Barcode is required.';

  @override
  String get inventoryInboundBarcodeMax =>
      'Barcode must be 120 characters or fewer.';

  @override
  String get inventoryInboundUomLabel => 'Unit of Measure';

  @override
  String get inventoryInboundUomRequired => 'Unit of measure is required.';

  @override
  String get inventoryInboundUomMax => 'UOM must be 40 characters or fewer.';

  @override
  String get inventoryInboundBatchNumberLabel => 'Batch Number';

  @override
  String get inventoryInboundBatchNumberHint => 'Optional batch identifier';

  @override
  String get inventoryInboundBatchNumberMax =>
      'Batch number must be 80 characters or fewer.';

  @override
  String get inventoryInboundQuantityLabel => 'Quantity';

  @override
  String get inventoryInboundQuantityRequired => 'Quantity is required.';

  @override
  String get inventoryInboundQuantityMin => 'Quantity must be greater than 0.';

  @override
  String get inventoryInboundCostPriceLabel => 'Cost Price';

  @override
  String get inventoryInboundCostPriceRequired => 'Cost price is required.';

  @override
  String get inventoryInboundMrpLabel => 'MRP';

  @override
  String get inventoryInboundMrpRequired => 'MRP is required.';

  @override
  String get inventoryInboundSalesPriceLabel => 'Sales Price';

  @override
  String get inventoryInboundSalesPriceRequired => 'Sales price is required.';

  @override
  String get inventoryInboundTaxRateLabel => 'Tax Rate (%)';

  @override
  String get inventoryInboundTaxRateRange =>
      'Tax rate must be between 0 and 100.';

  @override
  String get inventoryInboundTaxIncludedLabel => 'Tax Included';

  @override
  String get inventoryInboundExpiryDateLabel => 'Expiry Date';

  @override
  String get inventoryInboundManufacturingDateLabel => 'Manufacturing Date';

  @override
  String get inventoryInboundReferenceLabel => 'Reference Number';

  @override
  String get inventoryInboundReferenceMax =>
      'Reference must be 80 characters or fewer.';

  @override
  String get inventoryInboundNotesLabel => 'Notes';

  @override
  String get inventoryInboundNotesMax =>
      'Notes must be 500 characters or fewer.';

  @override
  String get inventoryInboundNumberInvalid => 'Enter a valid number.';

  @override
  String get inventoryInboundSuccess => 'Inventory added successfully.';

  @override
  String get inventoryInboundErrorGeneric =>
      'Unable to add inventory. Please try again.';

  @override
  String get inventoryInboundErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get inventoryInboundErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get placeholderBody => 'This feature is coming soon.';

  @override
  String get notFoundTitle => 'పేజీ కనబడలేదు';

  @override
  String get notFoundGoBack => 'వెనక్కి వెళ్ళండి';

  @override
  String get inventoryBatchesNoBatchesFound => 'No batches found';

  @override
  String get inventoryBatchesUnableToLoad => 'Unable to load batches';

  @override
  String get inventoryBatchesRetry => 'Retry';

  @override
  String get inventoryBatchesVoided => 'Voided';

  @override
  String get inventoryBatchesAdjustAction => 'Adjust';

  @override
  String get inventoryBatchesAdjustTitle => 'Adjust Stock';

  @override
  String get inventoryBatchesAdjustPreviewLabel => 'New Quantity';

  @override
  String get inventoryBatchesAdjustDirectionLabel => 'Direction';

  @override
  String get inventoryBatchesAdjustDirectionIncrease => 'Increase';

  @override
  String get inventoryBatchesAdjustDirectionDecrease => 'Decrease';

  @override
  String get inventoryBatchesAdjustQuantityLabel => 'Quantity';

  @override
  String get inventoryBatchesAdjustQuantityRequired => 'Quantity is required.';

  @override
  String get inventoryBatchesAdjustQuantityMin =>
      'Quantity must be greater than 0.';

  @override
  String get inventoryBatchesAdjustQuantityMax =>
      'Quantity cannot exceed current batch quantity.';

  @override
  String get inventoryBatchesAdjustReasonLabel => 'Reason';

  @override
  String get inventoryBatchesAdjustNotesLabel => 'Notes';

  @override
  String get inventoryBatchesAdjustNotesMax =>
      'Notes must be 500 characters or fewer.';

  @override
  String get inventoryBatchesAdjustNotesRequired =>
      'Notes are required for this reason.';

  @override
  String get inventoryBatchesAdjustPerformedAtLabel => 'Performed At';

  @override
  String get inventoryBatchesAdjustSuccess => 'Stock adjusted successfully.';

  @override
  String get inventoryBatchesErrorGeneric =>
      'Unable to load batches. Please try again.';

  @override
  String get inventoryBatchesErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get inventoryBatchesErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get inventoryBatchesErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get inventoryBatchesErrorForbidden =>
      'You do not have permission to view batches.';

  @override
  String get inventoryAdjustReasonDamaged => 'Damaged';

  @override
  String get inventoryAdjustReasonExpired => 'Expired';

  @override
  String get inventoryAdjustReasonStolen => 'Stolen';

  @override
  String get inventoryAdjustReasonMissingLost => 'Missing / Lost';

  @override
  String get inventoryAdjustReasonStockCountCorrection =>
      'Stock Count Correction';

  @override
  String get inventoryAdjustReasonOtherLoss => 'Other Loss';

  @override
  String get inventoryAdjustReasonFoundStock => 'Found Stock';

  @override
  String get inventoryAdjustReasonReturnRestockCorrection =>
      'Return / Restock Correction';

  @override
  String get inventoryAdjustReasonOtherGain => 'Other Gain';

  @override
  String get inventoryAdjustmentsTitle => 'Adjustment History';

  @override
  String get inventoryAdjustmentsNoAdjustmentsFound => 'No adjustments found';

  @override
  String get inventoryAdjustmentsUnableToLoad => 'Unable to load adjustments';

  @override
  String get inventoryAdjustmentsRetry => 'Retry';

  @override
  String get inventoryAdjustmentsVoided => 'Voided';

  @override
  String get inventoryAdjustmentsIncrease => 'Increase';

  @override
  String get inventoryAdjustmentsDecrease => 'Decrease';

  @override
  String get inventoryAdjustmentsPerformedByLabel => 'Performed by';

  @override
  String get inventoryAdjustmentsCostImpactLabel => 'Cost impact';

  @override
  String get barcodeScannerTitle => 'Scan Barcode';

  @override
  String get barcodeScannerHint => 'Point the camera at a barcode';

  @override
  String get barcodeScannerSearching => 'Starting camera...';

  @override
  String get barcodeScannerSuccess => 'Barcode detected';

  @override
  String get barcodeScannerPermissionDenied => 'Camera permission denied';

  @override
  String get barcodeScannerUnavailable => 'Scanner unavailable';

  @override
  String get dashboardSubtitle => 'Operational snapshot for your active shop.';

  @override
  String dashboardGreetingMorning(String shopName) {
    return 'Good morning, $shopName';
  }

  @override
  String dashboardGreetingAfternoon(String shopName) {
    return 'Good afternoon, $shopName';
  }

  @override
  String dashboardGreetingEvening(String shopName) {
    return 'Good evening, $shopName';
  }

  @override
  String get dashboardRangeLast7Days => 'Last 7 days';

  @override
  String get dashboardRangeLast30Days => 'Last 30 days';

  @override
  String get dashboardRangeCustom => 'Custom';

  @override
  String get dashboardRangeFrom => 'From';

  @override
  String get dashboardRangeTo => 'To';

  @override
  String get dashboardKpiSalesRevenue => 'Sales Revenue';

  @override
  String get dashboardKpiNetProfit => 'Net Profit';

  @override
  String get dashboardKpiInvoiceCount => 'Invoices';

  @override
  String get dashboardKpiLowStockItems => 'Low Stock Items';

  @override
  String get dashboardKpiStockValue => 'Stock Value';

  @override
  String get dashboardKpiCustomerCreditDue => 'Customer Credit Due';

  @override
  String get dashboardKpiSupplierPayables => 'Supplier Payables';

  @override
  String get dashboardKpiExpenses => 'Expenses';

  @override
  String get dashboardKpiVsPreviousPeriod => 'vs previous period';

  @override
  String get dashboardChartSalesTrend => 'Sales Trend';

  @override
  String get dashboardChartSalesTrendSubtitle =>
      'Total sales amount per day for the selected period.';

  @override
  String get dashboardChartRevenueVsExpenses => 'Revenue vs Expenses';

  @override
  String get dashboardChartRevenueVsExpensesSubtitle => 'Daily cash movement.';

  @override
  String get dashboardChartRevenue => 'Revenue';

  @override
  String get dashboardChartExpenses => 'Expenses';

  @override
  String get dashboardNoChartData => 'No chart data for the selected period.';

  @override
  String get dashboardLatestSalesEyebrow => 'Recent Activity';

  @override
  String get dashboardLatestSalesTitle => 'Latest Sales';

  @override
  String get dashboardLatestSalesEmptyTitle => 'No recent sales';

  @override
  String get dashboardLatestSalesEmptyDescription =>
      'The latest active-shop sales will appear here.';

  @override
  String get dashboardLatestSalesViewSales => 'View all sales';

  @override
  String get dashboardAlertsEyebrow => 'Alerts';

  @override
  String get dashboardAlertsTitle => 'Needs Attention';

  @override
  String get dashboardAlertsEmpty => 'Nothing needs attention right now.';

  @override
  String get dashboardAlertsView => 'View';

  @override
  String get dashboardQuickActionsTitle => 'Quick actions';

  @override
  String get dashboardQuickActionNewSale => 'New sale';

  @override
  String get dashboardQuickActionAddInventory => 'Add inventory';

  @override
  String get dashboardQuickActionExpenses => 'Expenses';

  @override
  String get dashboardQuickActionProfitLoss => 'Profit & Loss';

  @override
  String get dashboardUnableToLoad => 'Unable to load dashboard';

  @override
  String get dashboardRetry => 'Retry';

  @override
  String get dashboardNoData => 'No dashboard data available.';

  @override
  String get dashboardAccessRestrictedTitle =>
      'Dashboard is for owners and managers';

  @override
  String get dashboardAccessRestrictedBody =>
      'Your current shop role does not include dashboard access. Open Sales to continue working.';

  @override
  String get dashboardAccessRestrictedAction => 'Open Sales';

  @override
  String get dashboardErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get dashboardErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get dashboardErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get dashboardErrorForbidden =>
      'You do not have permission to view the dashboard.';

  @override
  String get dashboardErrorGeneric =>
      'Unable to load dashboard. Please try again.';
}
