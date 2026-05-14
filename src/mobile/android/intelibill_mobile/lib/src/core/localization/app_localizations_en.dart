// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get commonLanguage => 'Language';

  @override
  String get commonCancel => 'Cancel';

  @override
  String get commonClose => 'Close';

  @override
  String get commonClear => 'Clear';

  @override
  String get commonActions => 'Actions';

  @override
  String get commonEdit => 'Edit';

  @override
  String get commonSave => 'Save';

  @override
  String get commonDone => 'Done';

  @override
  String get commonSearch => 'Search...';

  @override
  String get commonLoading => 'Loading...';

  @override
  String get languageEnIn => 'English';

  @override
  String get languageHiIn => 'Hindi';

  @override
  String get languageTaIn => 'Tamil';

  @override
  String get languageTeIn => 'Telugu';

  @override
  String get languageBnIn => 'Bangla';

  @override
  String get languageMlIn => 'Malayalam';

  @override
  String get languageKnIn => 'Kannada';

  @override
  String get languageMrIn => 'Marathi';

  @override
  String get languageGuIn => 'Gujarati';

  @override
  String get shellAppName => 'Intelibill';

  @override
  String get shellLogout => 'Logout';

  @override
  String get shellProfile => 'Profile';

  @override
  String get shellLanguage => 'Language';

  @override
  String get shellDashboard => 'Dashboard';

  @override
  String get shellManageInventory => 'Inventory';

  @override
  String get shellManageSales => 'Sales';

  @override
  String get shellNewSale => 'New Sale';

  @override
  String get shellManageCustomers => 'Customers';

  @override
  String get shellManageSuppliers => 'Suppliers';

  @override
  String get shellManageExpenses => 'Expenses';

  @override
  String get shellManageBankAccounts => 'Bank Accounts';

  @override
  String get shellManageUsers => 'Manage Users';

  @override
  String get shellAddShop => 'Add Shop';

  @override
  String get shellManageShop => 'Manage Shop';

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
  String get authLoginNow => 'Login Now';

  @override
  String get authPassword => 'Password';

  @override
  String get authLoginCta => 'LOGIN';

  @override
  String get authEmailAddress => 'Email Address';

  @override
  String get authRememberMe => 'Remember me';

  @override
  String get authForgotPassword => 'Forgot password';

  @override
  String get authRegister => 'Register';

  @override
  String get authValidationEmailInvalid => 'Enter a valid email address.';

  @override
  String get authValidationPasswordRequired => 'Password is required.';

  @override
  String get authValidationLoginIdentifierRequired =>
      'Enter your email or mobile number.';

  @override
  String get authValidationEmailRequired => 'Email is required.';

  @override
  String get customersTitle => 'Customers';

  @override
  String get customersAddCustomer => 'Add Customer';

  @override
  String get customersCreateSuccess => 'Customer created successfully.';

  @override
  String get customersCreateNameLabel => 'Customer name';

  @override
  String get customersCreatePhoneLabel => 'Phone number';

  @override
  String get customersCreateAddressLabel => 'Address (optional)';

  @override
  String get customersCreateActiveLabel => 'Active customer';

  @override
  String get customersCreateNameRequired => 'Name is required.';

  @override
  String get customersCreateNameMax => 'Name must be 180 characters or fewer.';

  @override
  String get customersCreatePhoneRequired => 'Phone number is required.';

  @override
  String get customersCreatePhoneMax =>
      'Phone number must be 32 characters or fewer.';

  @override
  String get customersCreatePhoneInvalid => 'Enter a valid phone number.';

  @override
  String get customersCreateAddressMax =>
      'Address must be 320 characters or fewer.';

  @override
  String get customersCreateErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get customersCreateErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get customersCreateErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get customersCreateErrorForbidden =>
      'You do not have permission to create customers.';

  @override
  String get customersCreateErrorGeneric =>
      'Unable to create customer. Please try again.';

  @override
  String get customersNoCustomersFound => 'No customers found';

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
  String get suppliersTitle => 'Suppliers';

  @override
  String get suppliersAddSupplier => 'Add Supplier';

  @override
  String get suppliersNoSuppliersFound => 'No suppliers found';

  @override
  String get inventoryTitle => 'Inventory';

  @override
  String get inventoryAddNewProductDescription =>
      'Create a product linked to your current active shop.';

  @override
  String get placeholderBody => 'This feature is coming soon.';

  @override
  String get notFoundTitle => 'Page Not Found';

  @override
  String get notFoundGoBack => 'Go Back';
}

/// The translations for English, as used in India (`en_IN`).
class AppLocalizationsEnIn extends AppLocalizationsEn {
  AppLocalizationsEnIn() : super('en_IN');

  @override
  String get commonLanguage => 'Language';

  @override
  String get commonCancel => 'Cancel';

  @override
  String get commonClose => 'Close';

  @override
  String get commonClear => 'Clear';

  @override
  String get commonActions => 'Actions';

  @override
  String get commonEdit => 'Edit';

  @override
  String get commonSave => 'Save';

  @override
  String get commonDone => 'Done';

  @override
  String get commonSearch => 'Search...';

  @override
  String get commonLoading => 'Loading...';

  @override
  String get languageEnIn => 'English';

  @override
  String get languageHiIn => 'Hindi';

  @override
  String get languageTaIn => 'Tamil';

  @override
  String get languageTeIn => 'Telugu';

  @override
  String get languageBnIn => 'Bangla';

  @override
  String get languageMlIn => 'Malayalam';

  @override
  String get languageKnIn => 'Kannada';

  @override
  String get languageMrIn => 'Marathi';

  @override
  String get languageGuIn => 'Gujarati';

  @override
  String get shellAppName => 'Intelibill';

  @override
  String get shellLogout => 'Logout';

  @override
  String get shellProfile => 'Profile';

  @override
  String get shellLanguage => 'Language';

  @override
  String get shellDashboard => 'Dashboard';

  @override
  String get shellManageInventory => 'Inventory';

  @override
  String get shellManageSales => 'Sales';

  @override
  String get shellNewSale => 'New Sale';

  @override
  String get shellManageCustomers => 'Customers';

  @override
  String get shellManageSuppliers => 'Suppliers';

  @override
  String get shellManageExpenses => 'Expenses';

  @override
  String get shellManageBankAccounts => 'Bank Accounts';

  @override
  String get shellManageUsers => 'Manage Users';

  @override
  String get shellAddShop => 'Add Shop';

  @override
  String get shellManageShop => 'Manage Shop';

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
  String get authLoginNow => 'Login Now';

  @override
  String get authPassword => 'Password';

  @override
  String get authLoginCta => 'LOGIN';

  @override
  String get authEmailAddress => 'Email Address';

  @override
  String get authRememberMe => 'Remember me';

  @override
  String get authForgotPassword => 'Forgot password';

  @override
  String get authRegister => 'Register';

  @override
  String get authValidationEmailInvalid => 'Enter a valid email address.';

  @override
  String get authValidationPasswordRequired => 'Password is required.';

  @override
  String get authValidationLoginIdentifierRequired =>
      'Enter your email or mobile number.';

  @override
  String get authValidationEmailRequired => 'Email is required.';

  @override
  String get customersTitle => 'Customers';

  @override
  String get customersAddCustomer => 'Add Customer';

  @override
  String get customersCreateSuccess => 'Customer created successfully.';

  @override
  String get customersCreateNameLabel => 'Customer name';

  @override
  String get customersCreatePhoneLabel => 'Phone number';

  @override
  String get customersCreateAddressLabel => 'Address (optional)';

  @override
  String get customersCreateActiveLabel => 'Active customer';

  @override
  String get customersCreateNameRequired => 'Name is required.';

  @override
  String get customersCreateNameMax => 'Name must be 180 characters or fewer.';

  @override
  String get customersCreatePhoneRequired => 'Phone number is required.';

  @override
  String get customersCreatePhoneMax =>
      'Phone number must be 32 characters or fewer.';

  @override
  String get customersCreatePhoneInvalid => 'Enter a valid phone number.';

  @override
  String get customersCreateAddressMax =>
      'Address must be 320 characters or fewer.';

  @override
  String get customersCreateErrorNetwork =>
      'Unable to connect. Please check your network.';

  @override
  String get customersCreateErrorTimeout =>
      'Request timed out. Please try again.';

  @override
  String get customersCreateErrorUnauthorized =>
      'Session expired. Please log in again.';

  @override
  String get customersCreateErrorForbidden =>
      'You do not have permission to create customers.';

  @override
  String get customersCreateErrorGeneric =>
      'Unable to create customer. Please try again.';

  @override
  String get customersNoCustomersFound => 'No customers found';

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
  String get suppliersTitle => 'Suppliers';

  @override
  String get suppliersAddSupplier => 'Add Supplier';

  @override
  String get suppliersNoSuppliersFound => 'No suppliers found';

  @override
  String get inventoryTitle => 'Inventory';

  @override
  String get inventoryAddNewProductDescription =>
      'Create a product linked to your current active shop.';

  @override
  String get placeholderBody => 'This feature is coming soon.';

  @override
  String get notFoundTitle => 'Page Not Found';

  @override
  String get notFoundGoBack => 'Go Back';
}
