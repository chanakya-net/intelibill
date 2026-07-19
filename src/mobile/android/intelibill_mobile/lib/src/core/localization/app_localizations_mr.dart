// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Marathi (`mr`).
class AppLocalizationsMr extends AppLocalizations {
  AppLocalizationsMr([String locale = 'mr']) : super(locale);

  @override
  String get commonLanguage => 'भाषा';

  @override
  String get commonCancel => 'रद्द करा';

  @override
  String get commonClose => 'बंद करा';

  @override
  String get commonClear => 'साफ करा';

  @override
  String get purchaseOrdersFilterDateFrom => 'From';

  @override
  String get purchaseOrdersFilterDateTo => 'To';

  @override
  String get commonActions => 'क्रिया';

  @override
  String get commonEdit => 'संपादित करा';

  @override
  String get commonSave => 'जतन करा';

  @override
  String get commonDone => 'झाले';

  @override
  String get commonSearch => 'शोधा...';

  @override
  String get commonLoading => 'Loading...';

  @override
  String get languageEnIn => 'इंग्रजी';

  @override
  String get languageHiIn => 'हिंदी';

  @override
  String get languageTaIn => 'तमिळ';

  @override
  String get languageTeIn => 'तेलुगू';

  @override
  String get languageBnIn => 'बंगाली';

  @override
  String get languageMlIn => 'मल्याळम';

  @override
  String get languageKnIn => 'कन्नड';

  @override
  String get languageMrIn => 'मराठी';

  @override
  String get languageGuIn => 'गुजराती';

  @override
  String get shellAppName => 'Intelibill';

  @override
  String get shellLogout => 'लॉगआउट';

  @override
  String get shellProfile => 'प्रोफाइल';

  @override
  String get shellLanguage => 'भाषा';

  @override
  String get shellDashboard => 'डॅशबोर्ड';

  @override
  String get shellManageInventory => 'इन्व्हेंटरी';

  @override
  String get shellManageSales => 'विक्री';

  @override
  String get shellNewSale => 'नवीन विक्री';

  @override
  String get shellManageCustomers => 'ग्राहक';

  @override
  String get shellManageCreditNotes => 'Credit Notes';

  @override
  String get shellManageSuppliers => 'पुरवठादार';

  @override
  String get shellManageExpenses => 'खर्च';

  @override
  String get expensesNoExpensesFound => 'कोणतेही खर्च आढळले नाहीत';

  @override
  String get expensesSummaryTotal => 'Total expenses';

  @override
  String get expensesSummaryLoadedAmount => 'Loaded amount';

  @override
  String get expensesSummaryLoadedActive => 'Loaded active';

  @override
  String get expensesSummaryLoadedVoided => 'Loaded voided';

  @override
  String get expensesFilterAll => 'All';

  @override
  String get expensesFilterActive => 'Active';

  @override
  String get expensesFilterVoided => 'Voided';

  @override
  String get expensesNoFilteredResults =>
      'No loaded expenses match this filter';

  @override
  String get expensesUnableToLoad => 'खर्च लोड करता आले नाहीत';

  @override
  String get expensesRetry => 'पुन्हा प्रयत्न करा';

  @override
  String get expensesRefreshFailed => 'खर्च रिफ्रेश करता आले नाहीत';

  @override
  String get expensesErrorNetwork =>
      'कनेक्ट करता आले नाही. कृपया तुमचे नेटवर्क तपासा.';

  @override
  String get expensesErrorTimeout =>
      'विनंतीची वेळ संपली. कृपया पुन्हा प्रयत्न करा.';

  @override
  String get expensesErrorUnauthorized =>
      'सत्र कालबाह्य झाले. कृपया पुन्हा लॉग इन करा.';

  @override
  String get expensesErrorForbidden => 'तुम्हाला खर्च पाहण्याची परवानगी नाही.';

  @override
  String get expensesErrorGeneric =>
      'खर्च लोड करता आले नाहीत. कृपया पुन्हा प्रयत्न करा.';

  @override
  String get expensesDetailTitle => 'Expense details';

  @override
  String get expensesDetailUnableToLoad => 'Unable to load expense details';

  @override
  String get expensesDetailNoSelection => 'No expense selected';

  @override
  String get expensesDetailExpenseId => 'Expense ID';

  @override
  String get expensesDetailShopId => 'Shop ID';

  @override
  String get expensesDetailCategoryId => 'Category ID';

  @override
  String get expensesDetailAmount => 'Amount';

  @override
  String get expensesDetailStatus => 'Status';

  @override
  String get expensesDetailCategory => 'Category';

  @override
  String get expensesDetailPaidTo => 'Paid to';

  @override
  String get expensesDetailDescription => 'Description';

  @override
  String get expensesDetailExpenseDate => 'Expense date';

  @override
  String get expensesDetailActor => 'Recorded by';

  @override
  String get expensesDetailCreatedAt => 'Created at';

  @override
  String get expensesDetailSource => 'Source';

  @override
  String get expensesDetailOriginalExpense => 'Original expense';

  @override
  String get expensesDetailSupplierLedgerEntry => 'Supplier ledger entry';

  @override
  String get expensesDetailActive => 'Active';

  @override
  String get expensesDetailVoided => 'Voided';

  @override
  String get expensesDetailNotProvided => 'Not provided';

  @override
  String get expensesDetailNotLinked => 'Not linked';

  @override
  String get expensesDetailSourceManual => 'Manual expense';

  @override
  String get expensesDetailSourceCorrection => 'Expense correction';

  @override
  String get expensesDetailSourceSupplierPayment => 'Supplier payment';

  @override
  String get expensesRecordExpense => 'Record expense';

  @override
  String get expensesRecordSuccess => 'Expense recorded successfully.';

  @override
  String get expensesCategoryLabel => 'Category';

  @override
  String get expensesCategoryRequired => 'Category is required.';

  @override
  String get expensesCategoryMax => 'Category must be 100 characters or fewer.';

  @override
  String get expensesCategoryRetry => 'Retry categories';

  @override
  String get expensesCategoryLoadError => 'Unable to load expense categories.';

  @override
  String get expensesAmountLabel => 'Amount';

  @override
  String get expensesAmountRequired => 'Amount is required.';

  @override
  String get expensesAmountInvalid => 'Enter an amount greater than 0.';

  @override
  String get expensesPaidToLabel => 'Paid to';

  @override
  String get expensesPaidToRequired => 'Paid to is required.';

  @override
  String get expensesPaidToMax => 'Paid to must be 255 characters or fewer.';

  @override
  String get expensesDateLabel => 'Expense date';

  @override
  String get expensesDescriptionLabel => 'Description';

  @override
  String get expensesDescriptionMax =>
      'Description must be 500 characters or fewer.';

  @override
  String get expensesMutationErrorNetwork =>
      'Unable to connect. Please try again.';

  @override
  String get expensesMutationErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get expensesMutationErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get expensesMutationErrorForbidden =>
      'You do not have permission to record expenses.';

  @override
  String get expensesMutationErrorGeneric =>
      'Unable to record expense. Please try again.';

  @override
  String get shellManageBankAccounts => 'बँक खाती';

  @override
  String get shellManageUsers => 'वापरकर्ते व्यवस्थापित करा';

  @override
  String get shellManageServices => 'Services';

  @override
  String get shellManagePurchaseOrders => 'Purchase Orders';

  @override
  String get shellAddShop => 'दुकान जोडा';

  @override
  String get shellManageShop => 'दुकान व्यवस्थापित करा';

  @override
  String get shellMore => 'More';

  @override
  String get shellSectionManagement => 'Management';

  @override
  String get shellSectionProfile => 'Account';

  @override
  String get shellSectionShop => 'Shop';

  @override
  String get shellSectionSettings => 'Settings';

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
  String get authLoginNow => 'आता लॉगिन करा';

  @override
  String get authPassword => 'पासवर्ड';

  @override
  String get authLoginCta => 'लॉगिन';

  @override
  String get authEmailAddress => 'ईमेल पत्ता';

  @override
  String get authRememberMe => 'मला लक्षात ठेवा';

  @override
  String get authForgotPassword => 'पासवर्ड विसरलात?';

  @override
  String get authRegister => 'Register';

  @override
  String get authValidationEmailInvalid => 'वैध ईमेल पत्ता प्रविष्ट करा.';

  @override
  String get authValidationPasswordRequired => 'पासवर्ड आवश्यक आहे.';

  @override
  String get authValidationLoginIdentifierRequired =>
      'तुमचा ईमेल किंवा मोबाईल नंबर टाका.';

  @override
  String get authValidationEmailRequired => 'ईमेल आवश्यक आहे.';

  @override
  String get customersTitle => 'ग्राहक';

  @override
  String get customersAddCustomer => 'ग्राहक जोडा';

  @override
  String get customersNoCustomersFound => 'कोणतेही ग्राहक आढळले नाहीत';

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
  String get servicesTitle => 'Services';

  @override
  String get servicesFilterAll => 'All';

  @override
  String get servicesFilterActive => 'Active';

  @override
  String get servicesFilterInactive => 'Inactive';

  @override
  String get servicesNoServicesFound => 'No services found';

  @override
  String get servicesUnableToLoad => 'Unable to load services';

  @override
  String get servicesRetry => 'Retry';

  @override
  String get servicesActive => 'Active';

  @override
  String get servicesInactive => 'Inactive';

  @override
  String get servicesErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get servicesErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get servicesErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get servicesErrorForbidden =>
      'You do not have permission to view services.';

  @override
  String get servicesErrorGeneric =>
      'Unable to load services. Please try again.';

  @override
  String get bankAccountsTitle => 'Bank Accounts';

  @override
  String get bankAccountsEmpty => 'No bank accounts found';

  @override
  String get bankAccountsUnableToLoad => 'Unable to load bank accounts';

  @override
  String get bankAccountsRetry => 'Retry';

  @override
  String get bankAccountsType => 'Type';

  @override
  String get bankAccountsIfsc => 'IFSC';

  @override
  String get bankAccountsHolder => 'Account holder';

  @override
  String get bankAccountsErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get bankAccountsErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get bankAccountsErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get bankAccountsErrorForbidden =>
      'You do not have permission to view bank accounts.';

  @override
  String get servicesAddService => 'Add Service';

  @override
  String get servicesEditService => 'Edit Service';

  @override
  String get servicesNameLabel => 'Name';

  @override
  String get servicesNameRequired => 'Service name is required.';

  @override
  String get servicesNameMax => 'Name must be 180 characters or fewer.';

  @override
  String get servicesDescriptionLabel => 'Description';

  @override
  String get servicesDescriptionMax =>
      'Description must be 1000 characters or fewer.';

  @override
  String get servicesPriceLabel => 'Price';

  @override
  String get servicesPriceRequired => 'Price is required.';

  @override
  String get servicesPriceInvalid => 'Enter a valid price greater than 0.';

  @override
  String get servicesHsnCodeLabel => 'HSN Code';

  @override
  String get servicesHsnCodeInvalid => 'HSN code must be 4 to 8 digits.';

  @override
  String get servicesTaxRateLabel => 'Tax Rate (%)';

  @override
  String get servicesTaxRateRequired => 'Tax rate is required.';

  @override
  String get servicesTaxRateInvalid => 'Tax rate must be between 0 and 100.';

  @override
  String get servicesTaxIncludedLabel => 'Tax included';

  @override
  String get servicesActiveOnCreateLabel => 'Active on create';

  @override
  String get servicesTaxIncluded => 'Tax included';

  @override
  String get servicesTaxExcluded => 'Tax excluded';

  @override
  String get servicesActivate => 'Activate';

  @override
  String get servicesDeactivate => 'Deactivate';

  @override
  String get servicesCreateSuccess => 'Service created successfully.';

  @override
  String get servicesUpdateSuccess => 'Service updated successfully.';

  @override
  String get servicesActivatedSuccess => 'Service activated successfully.';

  @override
  String get servicesDeactivatedSuccess => 'Service deactivated successfully.';

  @override
  String get servicesMutationErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get servicesMutationErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get servicesMutationErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get servicesMutationErrorForbidden =>
      'You do not have permission to manage services.';

  @override
  String get servicesMutationErrorGeneric =>
      'Unable to save service. Please try again.';

  @override
  String get usersTitle => 'Shop Users';

  @override
  String get usersSubtitle =>
      'View all members in the active shop and manage role assignments.';

  @override
  String get usersAddUser => 'Add User';

  @override
  String get usersAddUserDescription =>
      'Create a manager or sales person for the active shop.';

  @override
  String get usersEditUser => 'Edit User';

  @override
  String get usersEditUserDescription =>
      'Update role and login access for this user.';

  @override
  String get usersSearchPlaceholder => 'Search users...';

  @override
  String get usersNoUsersFound => 'No users found';

  @override
  String get usersNoUsersDescription =>
      'Start by adding a manager or sales person for this shop.';

  @override
  String get usersUnableToLoad => 'Unable to load users';

  @override
  String get usersRetry => 'Retry';

  @override
  String get usersShopsLabel => 'Shops';

  @override
  String get usersSelectShopsDescription =>
      'Select one or more shops for this user.';

  @override
  String get usersSelectShopsForAccess =>
      'Select the shops this user should have access to.';

  @override
  String get usersDefaultShop => 'Default';

  @override
  String get usersSelectAtLeastOneShop => 'Select at least one shop.';

  @override
  String get usersFirstNameLabel => 'First Name';

  @override
  String get usersFirstNameRequired => 'First name is required.';

  @override
  String get usersFirstNameMax => 'First name must be 100 characters or fewer.';

  @override
  String get usersLastNameLabel => 'Last Name';

  @override
  String get usersLastNameRequired => 'Last name is required.';

  @override
  String get usersLastNameMax => 'Last name must be 100 characters or fewer.';

  @override
  String get usersEmailLabel => 'Email';

  @override
  String get usersEmailRequired => 'Email is required.';

  @override
  String get usersEmailMax => 'Email must be 256 characters or fewer.';

  @override
  String get usersPhoneLabel => 'Mobile Number';

  @override
  String get usersPhoneRequired => 'Mobile number is required.';

  @override
  String get usersPhoneMax => 'Mobile number must be 32 characters or fewer.';

  @override
  String get usersPhoneInvalid => 'Enter a valid phone number.';

  @override
  String get usersConfirmPasswordLabel => 'Confirm Password';

  @override
  String get usersConfirmPasswordRequired => 'Confirm password is required.';

  @override
  String get usersPasswordMismatch =>
      'Password and confirm password must match.';

  @override
  String get usersPasswordMin => 'Password must be at least 8 characters.';

  @override
  String get usersPasswordMax => 'Password must be 100 characters or fewer.';

  @override
  String get usersRoleLabel => 'Role';

  @override
  String get usersRoleOwner => 'Owner';

  @override
  String get usersRoleManager => 'Manager';

  @override
  String get usersRoleStaff => 'Staff';

  @override
  String get usersAllowLoginLabel => 'Allow this user to login';

  @override
  String get usersLoginEnabled => 'Enabled';

  @override
  String get usersLoginDisabled => 'Disabled';

  @override
  String get usersAddSuccess => 'User added successfully.';

  @override
  String get usersEditSuccess => 'User updated successfully.';

  @override
  String get usersErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get usersErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get usersErrorUnauthorized => 'Session expired. Please log in again.';

  @override
  String get usersErrorForbidden =>
      'You do not have permission to manage users.';

  @override
  String get usersErrorGeneric => 'Unable to load users. Please try again.';

  @override
  String get suppliersTitle => 'पुरवठादार';

  @override
  String get suppliersAddSupplier => 'पुरवठादार जोडा';

  @override
  String get suppliersNoSuppliersFound => 'कोणतेही पुरवठादार आढळले नाहीत';

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
  String get inventoryTitle => 'इन्व्हेंटरी';

  @override
  String get inventoryAddNewProductDescription =>
      'तुमच्या सध्याच्या सक्रिय दुकानाशी जोडलेले उत्पादन तयार करा.';

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
  String get inventoryMenuTitle => 'Inventory actions';

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
  String get notFoundTitle => 'पृष्ठ आढळले नाही';

  @override
  String get notFoundGoBack => 'मागे जा';

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

  @override
  String get salesHistoryEyebrow => 'Sales ledger';

  @override
  String get salesHistoryTitle => 'Sales history';

  @override
  String get salesHistorySubtitle =>
      'Review invoices, payment status, and refunds for the selected period.';

  @override
  String get salesHistoryKpiPeriodSales => 'Period sales';

  @override
  String get salesHistoryKpiInvoices => 'Invoices';

  @override
  String get salesHistoryKpiRefunds => 'Refunds';

  @override
  String get salesHistoryControlsStatus => 'Status';

  @override
  String get salesHistoryControlsSearchPlaceholder =>
      'Search invoice, customer, or phone...';

  @override
  String get salesHistoryControlsClearFilters => 'Clear';

  @override
  String get salesHistoryStatusAll => 'All';

  @override
  String get salesHistoryStatusPaid => 'Paid';

  @override
  String get salesHistoryStatusPartiallyPaid => 'Partially paid';

  @override
  String get salesHistoryStatusRefunded => 'Refunded';

  @override
  String get salesHistoryStatusReturned => 'Returned';

  @override
  String get salesHistoryStatusUnknown => 'Unknown';

  @override
  String get salesHistoryPaymentCash => 'Cash';

  @override
  String get salesHistoryPaymentUpi => 'UPI';

  @override
  String get salesHistoryPaymentCard => 'Card';

  @override
  String get salesHistoryPaymentCredit => 'Credit';

  @override
  String get salesHistoryPaymentUnknown => 'Unknown';

  @override
  String get salesHistoryWalkInCustomer => 'Walk-in customer';

  @override
  String get salesHistoryNoSales => 'No sales found';

  @override
  String get salesHistoryNoSalesDescription =>
      'Try adjusting the date range, status filter, or search terms.';

  @override
  String get salesHistoryUnableToLoad => 'Unable to load sales history';

  @override
  String get salesDetailUnableToLoad => 'Unable to load sale details';

  @override
  String get salesHistoryRetry => 'Retry';

  @override
  String get salesHistoryInvoiceNumber => 'Invoice';

  @override
  String get salesHistoryCustomer => 'Customer';

  @override
  String get salesHistoryPhone => 'Phone';

  @override
  String get salesHistoryDate => 'Date';

  @override
  String get salesDetailLineItems => 'Line items';

  @override
  String get salesDetailTotals => 'Totals';

  @override
  String get salesDetailDiscounts => 'Discounts';

  @override
  String get salesDetailPaymentSplit => 'Payment split';

  @override
  String get salesDetailReturns => 'Returns';

  @override
  String get salesDetailRedemptions => 'Redemptions';

  @override
  String get salesDetailWarnings => 'Warnings';

  @override
  String get salesHistoryPaymentMethod => 'Payment';

  @override
  String get salesDetailNoLineItems => 'No line items';

  @override
  String get salesDetailNoDiscounts => 'No discounts';

  @override
  String get salesDetailNoSettlementRecords => 'No settlement records';

  @override
  String get salesDetailNoReturns => 'No returns';

  @override
  String get salesDetailNoRedemptions => 'No redemptions';

  @override
  String get salesDetailNoWarnings => 'No warnings';

  @override
  String get salesDetailReceipt => 'Receipt';

  @override
  String get salesReceiptCreditNoteApplied => 'Credit note applied';

  @override
  String get salesDetailBeforeDiscount => 'Before discount';

  @override
  String get salesDetailDiscountLabel => 'Discount';

  @override
  String get salesDetailTaxLabel => 'Tax';

  @override
  String get salesDetailPaidLabel => 'Paid';

  @override
  String get salesDetailDueReduction => 'Due reduction';

  @override
  String get salesDetailVoidReturnAction => 'Void return';

  @override
  String get salesDetailVoidReturnReason => 'Reason';

  @override
  String get salesDetailVoidReturnReasonRequired => 'Reason is required.';

  @override
  String get salesDetailVoidReturnFailed => 'Unable to void return.';

  @override
  String get salesDetailVoidReturnUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get salesDetailVoidReturnForbidden =>
      'You do not have permission to void returns.';

  @override
  String get salesDetailVoidReturnNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get salesDetailVoidReturnTimeout =>
      'Request timed out. Please try again.';

  @override
  String get salesDetailVoidReturnDate => 'Voided on';

  @override
  String get salesDetailReturnVoided => 'Voided';

  @override
  String salesDetailReturnItem(double quantity, String itemName) {
    return '$quantity returned of $itemName';
  }

  @override
  String get salesHistoryItemCountLabel => 'Items';

  @override
  String get salesHistoryTotal => 'Total';

  @override
  String get salesHistoryDueAmount => 'Due';

  @override
  String get salesHistoryRefundAmount => 'Refund';

  @override
  String get salesHistoryDetailTitle => 'Sale details';

  @override
  String get salesHistoryErrorGeneric =>
      'Unable to load sales history. Please try again.';

  @override
  String get salesHistoryErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get salesHistoryErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get salesHistoryErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get salesHistoryErrorForbidden =>
      'You do not have permission to view sales history.';

  @override
  String salesHistoryItemCount(int count) {
    return '$count items';
  }

  @override
  String salesHistoryDateRangeLabel(String from, String to) {
    return '$from – $to';
  }

  @override
  String salesHistoryShowingCount(int shown, int total) {
    return 'Showing $shown of $total';
  }

  @override
  String get discountsTitle => 'Discount rules';

  @override
  String get discountsSubtitle =>
      'Manage active promotion rules for products and sales.';

  @override
  String get discountsSearchPlaceholder => 'Search rule name...';

  @override
  String get discountsClearSearch => 'Clear search';

  @override
  String get discountsStatusAll => 'All';

  @override
  String get discountsStatusActive => 'Active';

  @override
  String get discountsStatusUpcoming => 'Upcoming';

  @override
  String get discountsStatusExpired => 'Expired';

  @override
  String get discountsStatusDisabled => 'Disabled';

  @override
  String get discountsTypeAll => 'All types';

  @override
  String get discountsTypeBatch => 'Batch percentage';

  @override
  String get discountsTypeSalePercent => 'Sales percentage';

  @override
  String get discountsTypeSaleThresholdPercent => 'Sales threshold percentage';

  @override
  String get discountsSortLabel => 'Sort';

  @override
  String get discountsSortCreatedDesc => 'Created (new first)';

  @override
  String get discountsSortCreatedAsc => 'Created (old first)';

  @override
  String get discountsSortNameAsc => 'Name (A-Z)';

  @override
  String get discountsSortNameDesc => 'Name (Z-A)';

  @override
  String get discountsSortStartsAtAsc => 'Starts at (soonest)';

  @override
  String get discountsSortStartsAtDesc => 'Starts at (latest)';

  @override
  String get discountsSortStatus => 'Status';

  @override
  String get discountsNoRules => 'No discount rules found';

  @override
  String get discountsUnableToLoad => 'Unable to load discount rules';

  @override
  String get discountsRetry => 'Retry';

  @override
  String discountsShowingCount(int shown, int total) {
    return 'Showing $shown of $total';
  }

  @override
  String get discountsRuleTypeLabel => 'Type:';

  @override
  String get discountsPercentageLabel => 'Discount:';

  @override
  String get discountsThresholdLabel => 'Threshold:';

  @override
  String get discountsStatusLabel => 'Status:';

  @override
  String get discountsInventoryBatchIdLabel => 'Inventory batch:';

  @override
  String get discountsNotSet => 'Not set';

  @override
  String get discountsErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get discountsErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get discountsErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get discountsErrorForbidden =>
      'You do not have permission to view discounts.';

  @override
  String get discountsErrorGeneric =>
      'Unable to load discount rules. Please try again.';

  @override
  String get discountsUnableToLoadDetail => 'Unable to load discount details';

  @override
  String get discountsDetailTitle => 'Discount rule details';

  @override
  String get discountsDetailRuleLabel => 'Rule';

  @override
  String get discountsDescriptionLabel => 'Description:';

  @override
  String get discountsBelowCostReasonLabel => 'Below-cost confirmation:';

  @override
  String get discountsStartsAtLabel => 'Starts at:';

  @override
  String get discountsEndsAtLabel => 'Ends at:';

  @override
  String get discountsCreatedAtLabel => 'Created at:';

  @override
  String get discountsUpdatedAtLabel => 'Updated at:';

  @override
  String get discountsDisabledAtLabel => 'Disabled at:';

  @override
  String get discountsDisabledReasonLabel => 'Disable reason:';

  @override
  String get discountsReplacesRuleLabel => 'Replaces rule:';

  @override
  String get discountsReplacedByRuleLabel => 'Replaced by:';

  @override
  String get creditNotesVerifyCode => 'Verify code';

  @override
  String get creditNotesFilterAll => 'All';

  @override
  String get creditNotesLoadMore => 'Load more';

  @override
  String get creditNotesEmpty => 'No credit notes found';

  @override
  String get creditNotesNoExpiry => 'No expiry';

  @override
  String get creditNotesClose => 'Close';

  @override
  String get creditNotesVoidReason => 'Reason';

  @override
  String get creditNotesOpenReceipt => 'Open receipt';

  @override
  String get creditNotesReceiptTitle => 'Credit note receipt';

  @override
  String get creditNotesStatusLabel => 'Status:';

  @override
  String get creditNotesReceiptCodeLabel => 'Code:';

  @override
  String get creditNotesReceiptIssueDateLabel => 'Issued at:';

  @override
  String get creditNotesReceiptExpiryDateLabel => 'Expires at:';

  @override
  String get creditNotesReceiptCustomerLabel => 'Customer:';

  @override
  String get creditNotesReceiptReasonLabel => 'Reason:';

  @override
  String get creditNotesReceiptVoidReasonLabel => 'Void reason:';

  @override
  String get creditNotesReceiptStatusActive => 'Active';

  @override
  String get creditNotesReceiptStatusExpired => 'Expired';

  @override
  String get creditNotesReceiptStatusVoided => 'Voided';

  @override
  String get creditNotesOriginalAmountLabel => 'Original amount:';

  @override
  String get creditNotesVoid => 'Void';

  @override
  String get creditNotesInvoiceLabel => 'Invoice:';

  @override
  String get creditNotesReturnLabel => 'Return:';

  @override
  String get creditNotesBalanceLabel => 'Balance:';

  @override
  String get creditNotesRetry => 'Retry';

  @override
  String get salesReturnTitle => 'Record Return';

  @override
  String get salesReturnNoReturnableLines => 'No returnable lines';

  @override
  String get salesReturnPreview => 'Preview';

  @override
  String get salesReturnSubmit => 'Submit';

  @override
  String get salesReturnSubmitRoleNotice =>
      'Only owner or manager can record this return.';

  @override
  String get salesReturnDestinationRefund => 'Refund payout';

  @override
  String get salesReturnDestinationCreditNote => 'Credit note';

  @override
  String get salesReturnCreditNoteReasonLabel => 'Credit note reason';

  @override
  String get salesReturnCreditNoteExpiryLabel =>
      'Credit note expiry (YYYY-MM-DD)';

  @override
  String get salesReturnDueOverrideLabel => 'Due reduction override';

  @override
  String get salesReturnDueOverrideReasonLabel => 'Due reduction reason';

  @override
  String get salesReturnApprovedRefundLabel => 'Approved refund';

  @override
  String get salesReturnLineQuantityLabel => 'Return quantity';

  @override
  String get salesReturnLineConditionLabel => 'Condition';

  @override
  String get salesReturnLineConditionRestockable => 'Restockable';

  @override
  String get salesReturnLineConditionWastage => 'Wastage';

  @override
  String get salesReturnLineNoteLabel => 'Line note';

  @override
  String get salesReturnNotesLabel => 'Notes';

  @override
  String get salesReturnPayoutDestinationLabel => 'Payout destination';

  @override
  String get salesReturnPreviewRefundLabel => 'Refund';

  @override
  String get salesReturnPreviewDueReductionLabel => 'Due reduction';

  @override
  String get salesReturnPreviewPayoutLabel => 'Payout';

  @override
  String get salesReturnDueOverrideConfirmLabel =>
      'Confirm due reduction override';

  @override
  String get expensesCorrectExpense => 'Correct expense';

  @override
  String get expensesCorrectExpenseConfirmTitle => 'Confirm correction';

  @override
  String get expensesCorrectExpenseConfirmMessage =>
      'The original expense will be permanently voided and a replacement created. This cannot be undone.';

  @override
  String get expensesCorrectSuccess => 'Expense corrected successfully.';

  @override
  String get expensesLoadingMore => 'Loading more expenses';

  @override
  String get expensesUnableToLoadMore => 'Unable to load more expenses';

  @override
  String get expensesSubmitting => 'Saving expense';

  @override
  String expensesMetricSemantics(String label, String value) {
    return '$label: $value';
  }

  @override
  String expensesStatusSemantics(String status) {
    return 'Status: $status';
  }

  @override
  String get bankAccountsAdd => 'Add Bank Account';

  @override
  String get bankAccountsSearchHint => 'Search bank accounts';

  @override
  String get bankAccountsClearSearch => 'Clear bank account search';

  @override
  String get bankAccountsNoResults => 'No bank accounts match your search';

  @override
  String get bankAccountsRefresh => 'Refresh bank accounts';

  @override
  String get bankAccountsCreateSuccess => 'Bank account created successfully.';

  @override
  String get bankAccountsBankName => 'Bank name';

  @override
  String get bankAccountsBankNameRequired => 'Bank name is required.';

  @override
  String get bankAccountsAccountNumber => 'Account number';

  @override
  String get bankAccountsAccountNumberRequired => 'Account number is required.';

  @override
  String get bankAccountsAccountType => 'Account type';

  @override
  String get bankAccountsAccountTypeRequired => 'Select Savings or Current.';

  @override
  String get bankAccountsTypeSavings => 'Savings';

  @override
  String get bankAccountsTypeCurrent => 'Current';

  @override
  String get bankAccountsIfscInvalid => 'Enter a valid IFSC code.';

  @override
  String get bankAccountsHolderMax =>
      'Account holder name must be 120 characters or fewer.';

  @override
  String get bankAccountsSubmitError =>
      'Unable to save bank account. Please try again.';

  @override
  String get bankAccountsEdit => 'Edit Bank Account';

  @override
  String get bankAccountsUpdate => 'Update Bank Account';

  @override
  String get bankAccountsUpdateSuccess => 'Bank account updated successfully.';

  @override
  String get bankAccountsDelete => 'Delete bank account';

  @override
  String get bankAccountsDeleteTitle => 'Delete bank account';

  @override
  String bankAccountsDeleteConfirmation(Object bankName) {
    return 'Permanently delete $bankName? This cannot be undone.';
  }

  @override
  String get bankAccountsDeleteCancel => 'Cancel';

  @override
  String get bankAccountsDeleteConfirm => 'Delete permanently';

  @override
  String get bankAccountsDeleteSuccess => 'Bank account deleted successfully.';

  @override
  String get bankAccountsDeleteError =>
      'Unable to delete bank account. Please try again.';

  @override
  String get purchaseOrderDetailPageTitle => 'Purchase order';

  @override
  String get purchaseOrderDetailUnableToLoad =>
      'Could not load purchase order.';

  @override
  String get purchaseOrderDetailNotFound => 'Purchase order not found.';

  @override
  String get purchaseOrderDetailRetry => 'Retry';

  @override
  String get purchaseOrderDetailBack => 'Back to purchase orders';

  @override
  String get purchaseOrderDetailLinesHeader => 'Lines';

  @override
  String get purchaseOrderDetailNoLines => 'No lines on this order';

  @override
  String get purchaseOrderDetailSupplier => 'Supplier';

  @override
  String get purchaseOrderDetailNotProvided => 'Not provided';

  @override
  String get purchaseOrderDetailSupplierReferenceNumber =>
      'Supplier reference number:';

  @override
  String get purchaseOrderDetailSupplierReference => 'Supplier reference:';

  @override
  String get purchaseOrderDetailCreatedAt => 'Created at';

  @override
  String get purchaseOrderDetailOrderDate => 'Order date';

  @override
  String get purchaseOrderDetailExpectedDeliveryDate => 'Expected delivery';

  @override
  String get purchaseOrderDetailNotes => 'Notes';

  @override
  String get purchaseOrderDetailExpectedQuantity => 'Expected quantity';

  @override
  String get purchaseOrderDetailReceivedQuantity => 'Received quantity';

  @override
  String get purchaseOrderDetailRemainingQuantity => 'Remaining quantity';

  @override
  String get purchaseOrderDetailExpectedTotal => 'Expected total';

  @override
  String get purchaseOrderDetailCancellationReason => 'Cancellation reason';

  @override
  String get purchaseOrderDetailClosedAt => 'Closed at';

  @override
  String get purchaseOrderDetailClosedBy => 'Closed by';

  @override
  String get purchaseOrderDetailCloseReason => 'Close reason';

  @override
  String get purchaseOrderReceiptHistory => 'Receipt history';

  @override
  String get purchaseOrderNoReceipts => 'No receipts recorded';

  @override
  String get purchaseOrderReceiptReceivedAt => 'Received at';

  @override
  String get purchaseOrderReceiptReceivedBy => 'Received by';

  @override
  String get purchaseOrderReceiptReference => 'Reference';

  @override
  String get purchaseOrderReceiptNotes => 'Notes';

  @override
  String get purchaseOrderReceiptBatch => 'Batch';

  @override
  String get purchaseOrderReceiptBatchState => 'Batch state';

  @override
  String get purchaseOrderReceiptVoided => 'Voided';

  @override
  String get purchaseOrderReceiptActive => 'Active';

  @override
  String get purchaseOrderReceiptStockTransaction => 'Stock transaction';

  @override
  String get purchaseOrderReceiptQuantity => 'Quantity';

  @override
  String get purchaseOrderReceiptTotalPurchaseCost => 'Total purchase cost';

  @override
  String get purchaseOrderReceiptUnitCost => 'Unit cost';

  @override
  String get purchaseOrderReceiptMrp => 'MRP';

  @override
  String get purchaseOrderReceiptSalesPrice => 'Sales price';

  @override
  String get purchaseOrderReceiptTaxRate => 'Tax rate';

  @override
  String get purchaseOrderReceiptTaxIncluded => 'Tax included';

  @override
  String get purchaseOrderReceiptPurchaseTaxIncluded => 'Purchase tax included';

  @override
  String get purchaseOrderReceiptYes => 'Yes';

  @override
  String get purchaseOrderReceiptNo => 'No';

  @override
  String get purchaseOrderLineExpectedQuantity => 'Expected';

  @override
  String get purchaseOrderLineReceivedQuantity => 'Received';

  @override
  String get purchaseOrderLineRemainingQuantity => 'Remaining';

  @override
  String get purchaseOrderLineUnitCost => 'Unit cost';

  @override
  String get purchaseOrderLineTotal => 'Line total';

  @override
  String get purchaseOrderBuilderTitle => 'New purchase order';

  @override
  String get purchaseOrderBuilderSupplier => 'Supplier';

  @override
  String get purchaseOrderBuilderNoSupplier => 'No supplier';

  @override
  String get purchaseOrderBuilderOrderDate => 'Order date';

  @override
  String get purchaseOrderBuilderExpectedDeliveryDate =>
      'Expected delivery date';

  @override
  String get purchaseOrderBuilderReference => 'Supplier reference number';

  @override
  String get purchaseOrderBuilderNotes => 'Notes';

  @override
  String get purchaseOrderBuilderSave => 'Save draft';

  @override
  String get purchaseOrderBuilderSelectDate => 'Select date';

  @override
  String get purchaseOrderBuilderNoSuppliers =>
      'No active suppliers available.';

  @override
  String get purchaseOrderBuilderLoadError => 'Could not load suppliers.';

  @override
  String get purchaseOrderBuilderSaveError =>
      'Could not save purchase order draft.';

  @override
  String get purchaseOrderBuilderRetry => 'Retry';

  @override
  String get purchaseOrderBuilderAddItemTitle => 'Add item';

  @override
  String get purchaseOrderBuilderLinesHeader => 'Items';

  @override
  String get purchaseOrderBuilderExpectedTotal => 'Expected total';

  @override
  String get purchaseOrderBuilderAddItemLabel => 'Item';

  @override
  String get purchaseOrderBuilderQuantityLabel => 'Quantity';

  @override
  String get purchaseOrderBuilderUnitCostLabel => 'Unit cost';

  @override
  String get purchaseOrderBuilderLineTotalLabel => 'Line total';

  @override
  String get purchaseOrderBuilderItemIdLabel => 'Item';

  @override
  String get purchaseOrderBuilderRemoveLineLabel => 'Remove';

  @override
  String get purchaseOrderReceiveAction => 'Receive';

  @override
  String get purchaseOrderReceiveTitle => 'Receive purchase order';

  @override
  String get purchaseOrderReceiveRetry => 'Retry';

  @override
  String get purchaseOrderReceiveReceivedAtLabel => 'Received at';

  @override
  String get purchaseOrderReceiveReferenceLabel => 'Reference';

  @override
  String get purchaseOrderReceiveNotesLabel => 'Notes';

  @override
  String get purchaseOrderReceiveLineQuantity => 'Quantity';

  @override
  String get purchaseOrderReceiveBarcodeLabel => 'Barcode';

  @override
  String get purchaseOrderReceiveBatchLabel => 'Batch number';

  @override
  String get purchaseOrderReceiveScanBarcode => 'Scan barcode';

  @override
  String get purchaseOrderReceiveGenerateBarcode => 'Generate barcode';

  @override
  String purchaseOrderReceiveBarcodeReplaceConfirm(
    String existingBarcode,
    String newBarcode,
  ) {
    return 'Replace existing barcode \"$existingBarcode\" with \"$newBarcode\"?';
  }

  @override
  String get purchaseOrderReceiveBarcodeReplaceConfirmLabel => 'Replace';

  @override
  String get purchaseOrderReceiveRemaining => 'Remaining';

  @override
  String get purchaseOrderReceiveInventoryDetails => 'Inventory details';

  @override
  String get purchaseOrderReceiveUnitCostLabel => 'Unit cost';

  @override
  String get purchaseOrderReceiveTotalPurchaseCostLabel =>
      'Total purchase cost';

  @override
  String get purchaseOrderReceiveMrpLabel => 'MRP';

  @override
  String get purchaseOrderReceiveSalesPriceLabel => 'Sales price';

  @override
  String get purchaseOrderReceiveTaxRateLabel => 'Tax rate %';

  @override
  String get purchaseOrderReceiveTaxIncludedLabel =>
      'Tax included in sales price';

  @override
  String get purchaseOrderReceivePurchaseTaxIncludedLabel =>
      'Tax included in purchase cost';

  @override
  String get purchaseOrderReceiveManufacturingDateLabel => 'Manufacturing date';

  @override
  String get purchaseOrderReceiveExpiryDateLabel => 'Expiry date';

  @override
  String get purchaseOrderReceiveSelectDate => 'Select date';

  @override
  String get purchaseOrderReceiveClearDate => 'Clear date';

  @override
  String get purchaseOrderReceiveSummary => 'Receipt summary';

  @override
  String get purchaseOrderReceiveLineCount => 'Line count';

  @override
  String get purchaseOrderReceiveQuantity => 'Total quantity';

  @override
  String get purchaseOrderReceiveTotalExpectedPurchaseCost =>
      'Total purchase cost';

  @override
  String get purchaseOrderReceiveSubmit => 'Record receipt';

  @override
  String get purchaseOrderReceiveSubmitFailure => 'Could not record receipt.';

  @override
  String get purchaseOrderReceiveNoLines =>
      'No remaining lines available to receive.';

  @override
  String get purchaseOrdersRefreshFailed => 'Failed to refresh';
}

/// The translations for Marathi, as used in India (`mr_IN`).
class AppLocalizationsMrIn extends AppLocalizationsMr {
  AppLocalizationsMrIn() : super('mr_IN');

  @override
  String get commonLanguage => 'भाषा';

  @override
  String get commonCancel => 'रद्द करा';

  @override
  String get commonClose => 'बंद करा';

  @override
  String get commonClear => 'साफ करा';

  @override
  String get purchaseOrdersFilterDateFrom => 'From';

  @override
  String get purchaseOrdersFilterDateTo => 'To';

  @override
  String get commonActions => 'क्रिया';

  @override
  String get commonEdit => 'संपादित करा';

  @override
  String get commonSave => 'जतन करा';

  @override
  String get commonDone => 'झाले';

  @override
  String get commonSearch => 'शोधा...';

  @override
  String get commonLoading => 'Loading...';

  @override
  String get languageEnIn => 'इंग्रजी';

  @override
  String get languageHiIn => 'हिंदी';

  @override
  String get languageTaIn => 'तमिळ';

  @override
  String get languageTeIn => 'तेलुगू';

  @override
  String get languageBnIn => 'बंगाली';

  @override
  String get languageMlIn => 'मल्याळम';

  @override
  String get languageKnIn => 'कन्नड';

  @override
  String get languageMrIn => 'मराठी';

  @override
  String get languageGuIn => 'गुजराती';

  @override
  String get shellAppName => 'Intelibill';

  @override
  String get shellLogout => 'लॉगआउट';

  @override
  String get shellProfile => 'प्रोफाइल';

  @override
  String get shellLanguage => 'भाषा';

  @override
  String get shellDashboard => 'डॅशबोर्ड';

  @override
  String get shellManageInventory => 'इन्व्हेंटरी';

  @override
  String get shellManageSales => 'विक्री';

  @override
  String get shellNewSale => 'नवीन विक्री';

  @override
  String get shellManageCustomers => 'ग्राहक';

  @override
  String get shellManageSuppliers => 'पुरवठादार';

  @override
  String get shellManageExpenses => 'खर्च';

  @override
  String get expensesNoExpensesFound => 'कोणतेही खर्च आढळले नाहीत';

  @override
  String get expensesSummaryTotal => 'एकूण खर्च';

  @override
  String get expensesSummaryLoadedAmount => 'पृष्ठ रक्कम';

  @override
  String get expensesSummaryLoadedActive => 'सक्रिय खर्च';

  @override
  String get expensesSummaryLoadedVoided => 'रद्द खर्च';

  @override
  String get expensesFilterAll => 'All';

  @override
  String get expensesFilterActive => 'Active';

  @override
  String get expensesFilterVoided => 'Voided';

  @override
  String get expensesNoFilteredResults =>
      'No loaded expenses match this filter';

  @override
  String get expensesUnableToLoad => 'खर्च लोड करता आले नाहीत';

  @override
  String get expensesRetry => 'पुन्हा प्रयत्न करा';

  @override
  String get expensesRefreshFailed => 'खर्च रिफ्रेश करता आले नाहीत';

  @override
  String get expensesErrorNetwork =>
      'कनेक्ट करता आले नाही. कृपया तुमचे नेटवर्क तपासा.';

  @override
  String get expensesErrorTimeout =>
      'विनंतीची वेळ संपली. कृपया पुन्हा प्रयत्न करा.';

  @override
  String get expensesErrorUnauthorized =>
      'सत्र कालबाह्य झाले. कृपया पुन्हा लॉग इन करा.';

  @override
  String get expensesErrorForbidden => 'तुम्हाला खर्च पाहण्याची परवानगी नाही.';

  @override
  String get expensesErrorGeneric =>
      'खर्च लोड करता आले नाहीत. कृपया पुन्हा प्रयत्न करा.';

  @override
  String get expensesDetailTitle => 'खर्च खाते';

  @override
  String get expensesDetailUnableToLoad => 'Unable to load expense details';

  @override
  String get expensesDetailNoSelection => 'No expense selected';

  @override
  String get expensesDetailExpenseId => 'Expense ID';

  @override
  String get expensesDetailShopId => 'Shop ID';

  @override
  String get expensesDetailCategoryId => 'Category ID';

  @override
  String get expensesDetailAmount => 'Amount';

  @override
  String get expensesDetailStatus => 'स्थिती';

  @override
  String get expensesDetailCategory => 'Category';

  @override
  String get expensesDetailPaidTo => 'Paid to';

  @override
  String get expensesDetailDescription => 'Description';

  @override
  String get expensesDetailExpenseDate => 'Expense date';

  @override
  String get expensesDetailActor => 'Recorded by';

  @override
  String get expensesDetailCreatedAt => 'Created at';

  @override
  String get expensesDetailSource => 'Source';

  @override
  String get expensesDetailOriginalExpense => 'Original expense';

  @override
  String get expensesDetailSupplierLedgerEntry => 'Supplier ledger entry';

  @override
  String get expensesDetailActive => 'सक्रिय';

  @override
  String get expensesDetailVoided => 'रद्द';

  @override
  String get expensesDetailNotProvided => 'Not provided';

  @override
  String get expensesDetailNotLinked => 'Not linked';

  @override
  String get expensesDetailSourceManual => 'Manual expense';

  @override
  String get expensesDetailSourceCorrection => 'Expense correction';

  @override
  String get expensesDetailSourceSupplierPayment => 'Supplier payment';

  @override
  String get expensesRecordExpense => 'खर्च नोंदवा';

  @override
  String get expensesRecordSuccess => 'Expense recorded successfully.';

  @override
  String get expensesCategoryLabel => 'श्रेणी';

  @override
  String get expensesCategoryRequired => 'Category is required.';

  @override
  String get expensesCategoryMax => 'Category must be 100 characters or fewer.';

  @override
  String get expensesCategoryRetry => 'Retry categories';

  @override
  String get expensesCategoryLoadError => 'Unable to load expense categories.';

  @override
  String get expensesAmountLabel => 'रक्कम';

  @override
  String get expensesAmountRequired => 'Amount is required.';

  @override
  String get expensesAmountInvalid => 'Enter an amount greater than 0.';

  @override
  String get expensesPaidToLabel => 'ज्यांना दिले';

  @override
  String get expensesPaidToRequired => 'Paid to is required.';

  @override
  String get expensesPaidToMax => 'Paid to must be 255 characters or fewer.';

  @override
  String get expensesDateLabel => 'तारीख';

  @override
  String get expensesDescriptionLabel => 'वर्णन';

  @override
  String get expensesDescriptionMax =>
      'Description must be 500 characters or fewer.';

  @override
  String get expensesMutationErrorNetwork =>
      'Unable to connect. Please try again.';

  @override
  String get expensesMutationErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get expensesMutationErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get expensesMutationErrorForbidden =>
      'You do not have permission to record expenses.';

  @override
  String get expensesMutationErrorGeneric =>
      'Unable to record expense. Please try again.';

  @override
  String get shellManageBankAccounts => 'बँक खाती';

  @override
  String get shellManageUsers => 'वापरकर्ते व्यवस्थापित करा';

  @override
  String get shellAddShop => 'दुकान जोडा';

  @override
  String get shellManageShop => 'दुकान व्यवस्थापित करा';

  @override
  String get shellMore => 'More';

  @override
  String get shellSectionManagement => 'Management';

  @override
  String get shellSectionProfile => 'Account';

  @override
  String get shellSectionShop => 'Shop';

  @override
  String get shellSectionSettings => 'Settings';

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
  String get authLoginNow => 'आता लॉगिन करा';

  @override
  String get authPassword => 'पासवर्ड';

  @override
  String get authLoginCta => 'लॉगिन';

  @override
  String get authEmailAddress => 'ईमेल पत्ता';

  @override
  String get authRememberMe => 'मला लक्षात ठेवा';

  @override
  String get authForgotPassword => 'पासवर्ड विसरलात?';

  @override
  String get authRegister => 'Register';

  @override
  String get authValidationEmailInvalid => 'वैध ईमेल पत्ता प्रविष्ट करा.';

  @override
  String get authValidationPasswordRequired => 'पासवर्ड आवश्यक आहे.';

  @override
  String get authValidationLoginIdentifierRequired =>
      'तुमचा ईमेल किंवा मोबाईल नंबर टाका.';

  @override
  String get authValidationEmailRequired => 'ईमेल आवश्यक आहे.';

  @override
  String get customersTitle => 'ग्राहक';

  @override
  String get customersAddCustomer => 'ग्राहक जोडा';

  @override
  String get customersNoCustomersFound => 'कोणतेही ग्राहक आढळले नाहीत';

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
  String get suppliersTitle => 'पुरवठादार';

  @override
  String get suppliersAddSupplier => 'पुरवठादार जोडा';

  @override
  String get suppliersNoSuppliersFound => 'कोणतेही पुरवठादार आढळले नाहीत';

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
  String get inventoryTitle => 'इन्व्हेंटरी';

  @override
  String get inventoryAddNewProductDescription =>
      'तुमच्या सध्याच्या सक्रिय दुकानाशी जोडलेले उत्पादन तयार करा.';

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
  String get inventoryMenuTitle => 'Inventory actions';

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
  String get notFoundTitle => 'पृष्ठ आढळले नाही';

  @override
  String get notFoundGoBack => 'मागे जा';

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

  @override
  String get salesHistoryEyebrow => 'Sales ledger';

  @override
  String get salesHistoryTitle => 'Sales history';

  @override
  String get salesHistorySubtitle =>
      'Review invoices, payment status, and refunds for the selected period.';

  @override
  String get salesHistoryKpiPeriodSales => 'Period sales';

  @override
  String get salesHistoryKpiInvoices => 'Invoices';

  @override
  String get salesHistoryKpiRefunds => 'Refunds';

  @override
  String get salesHistoryControlsStatus => 'Status';

  @override
  String get salesHistoryControlsSearchPlaceholder =>
      'Search invoice, customer, or phone...';

  @override
  String get salesHistoryControlsClearFilters => 'Clear';

  @override
  String get salesHistoryStatusAll => 'All';

  @override
  String get salesHistoryStatusPaid => 'Paid';

  @override
  String get salesHistoryStatusPartiallyPaid => 'Partially paid';

  @override
  String get salesHistoryStatusRefunded => 'Refunded';

  @override
  String get salesHistoryStatusReturned => 'Returned';

  @override
  String get salesHistoryStatusUnknown => 'Unknown';

  @override
  String get salesHistoryPaymentCash => 'Cash';

  @override
  String get salesHistoryPaymentUpi => 'UPI';

  @override
  String get salesHistoryPaymentCard => 'Card';

  @override
  String get salesHistoryPaymentCredit => 'Credit';

  @override
  String get salesHistoryPaymentUnknown => 'Unknown';

  @override
  String get salesHistoryWalkInCustomer => 'Walk-in customer';

  @override
  String get salesHistoryNoSales => 'No sales found';

  @override
  String get salesHistoryNoSalesDescription =>
      'Try adjusting the date range, status filter, or search terms.';

  @override
  String get salesHistoryUnableToLoad => 'Unable to load sales history';

  @override
  String get salesHistoryRetry => 'Retry';

  @override
  String get salesHistoryInvoiceNumber => 'Invoice';

  @override
  String get salesHistoryCustomer => 'Customer';

  @override
  String get salesHistoryPhone => 'Phone';

  @override
  String get salesHistoryDate => 'Date';

  @override
  String get salesHistoryPaymentMethod => 'Payment';

  @override
  String get salesHistoryItemCountLabel => 'Items';

  @override
  String get salesHistoryTotal => 'Total';

  @override
  String get salesHistoryDueAmount => 'Due';

  @override
  String get salesHistoryRefundAmount => 'Refund';

  @override
  String get salesHistoryDetailTitle => 'Sale details';

  @override
  String get salesHistoryErrorGeneric =>
      'Unable to load sales history. Please try again.';

  @override
  String get salesHistoryErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get salesHistoryErrorTimeout => 'Request timed out. Please try again.';

  @override
  String get salesHistoryErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get salesHistoryErrorForbidden =>
      'You do not have permission to view sales history.';

  @override
  String salesHistoryItemCount(int count) {
    return '$count items';
  }

  @override
  String salesHistoryDateRangeLabel(String from, String to) {
    return '$from – $to';
  }

  @override
  String salesHistoryShowingCount(int shown, int total) {
    return 'Showing $shown of $total';
  }

  @override
  String get creditNotesVerifyCode => 'Verify code';

  @override
  String get creditNotesFilterAll => 'All';

  @override
  String get creditNotesLoadMore => 'Load more';

  @override
  String get creditNotesEmpty => 'No credit notes found';

  @override
  String get creditNotesNoExpiry => 'No expiry';

  @override
  String get creditNotesClose => 'Close';

  @override
  String get creditNotesVoidReason => 'Reason';

  @override
  String get creditNotesOpenReceipt => 'Open receipt';

  @override
  String get creditNotesReceiptTitle => 'Credit note receipt';

  @override
  String get creditNotesStatusLabel => 'Status:';

  @override
  String get creditNotesReceiptCodeLabel => 'Code:';

  @override
  String get creditNotesReceiptIssueDateLabel => 'Issued at:';

  @override
  String get creditNotesReceiptExpiryDateLabel => 'Expires at:';

  @override
  String get creditNotesReceiptCustomerLabel => 'Customer:';

  @override
  String get creditNotesReceiptReasonLabel => 'Reason:';

  @override
  String get creditNotesReceiptVoidReasonLabel => 'Void reason:';

  @override
  String get creditNotesReceiptStatusActive => 'Active';

  @override
  String get creditNotesReceiptStatusExpired => 'Expired';

  @override
  String get creditNotesReceiptStatusVoided => 'Voided';

  @override
  String get creditNotesOriginalAmountLabel => 'Original amount:';

  @override
  String get creditNotesVoid => 'Void';

  @override
  String get creditNotesInvoiceLabel => 'Invoice:';

  @override
  String get creditNotesReturnLabel => 'Return:';

  @override
  String get creditNotesBalanceLabel => 'Balance:';

  @override
  String get creditNotesRetry => 'Retry';

  @override
  String get expensesCorrectExpense => 'खर्च दुरुस्त करा';

  @override
  String get expensesCorrectExpenseConfirmTitle => 'Confirm correction';

  @override
  String get expensesCorrectExpenseConfirmMessage =>
      'The original expense will be permanently voided and a replacement created. This cannot be undone.';

  @override
  String get expensesCorrectSuccess => 'Expense corrected successfully.';

  @override
  String get expensesLoadingMore => 'Loading more expenses';

  @override
  String get expensesUnableToLoadMore => 'Unable to load more expenses';

  @override
  String get expensesSubmitting => 'Saving expense';

  @override
  String expensesMetricSemantics(String label, String value) {
    return '$label: $value';
  }

  @override
  String expensesStatusSemantics(String status) {
    return 'Status: $status';
  }

  @override
  String get bankAccountsAdd => 'Add Bank Account';

  @override
  String get bankAccountsSearchHint => 'Search bank accounts';

  @override
  String get bankAccountsClearSearch => 'Clear bank account search';

  @override
  String get bankAccountsNoResults => 'No bank accounts match your search';

  @override
  String get bankAccountsRefresh => 'Refresh bank accounts';

  @override
  String get bankAccountsCreateSuccess => 'Bank account created successfully.';

  @override
  String get bankAccountsBankName => 'Bank name';

  @override
  String get bankAccountsBankNameRequired => 'Bank name is required.';

  @override
  String get bankAccountsAccountNumber => 'Account number';

  @override
  String get bankAccountsAccountNumberRequired => 'Account number is required.';

  @override
  String get bankAccountsAccountType => 'Account type';

  @override
  String get bankAccountsAccountTypeRequired => 'Select Savings or Current.';

  @override
  String get bankAccountsTypeSavings => 'Savings';

  @override
  String get bankAccountsTypeCurrent => 'Current';

  @override
  String get bankAccountsIfscInvalid => 'Enter a valid IFSC code.';

  @override
  String get bankAccountsHolderMax =>
      'Account holder name must be 120 characters or fewer.';

  @override
  String get bankAccountsSubmitError =>
      'Unable to save bank account. Please try again.';

  @override
  String get bankAccountsEdit => 'Edit Bank Account';

  @override
  String get bankAccountsUpdate => 'Update Bank Account';

  @override
  String get bankAccountsUpdateSuccess => 'Bank account updated successfully.';

  @override
  String get bankAccountsDelete => 'Delete bank account';

  @override
  String get bankAccountsDeleteTitle => 'Delete bank account';

  @override
  String bankAccountsDeleteConfirmation(Object bankName) {
    return 'Permanently delete $bankName? This cannot be undone.';
  }

  @override
  String get bankAccountsDeleteCancel => 'Cancel';

  @override
  String get bankAccountsDeleteConfirm => 'Delete permanently';

  @override
  String get bankAccountsDeleteSuccess => 'Bank account deleted successfully.';

  @override
  String get bankAccountsDeleteError =>
      'Unable to delete bank account. Please try again.';

  @override
  String get purchaseOrdersRefreshFailed => 'Failed to refresh';
}
