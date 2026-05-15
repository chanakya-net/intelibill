import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_bn.dart';
import 'app_localizations_en.dart';
import 'app_localizations_gu.dart';
import 'app_localizations_hi.dart';
import 'app_localizations_kn.dart';
import 'app_localizations_ml.dart';
import 'app_localizations_mr.dart';
import 'app_localizations_ta.dart';
import 'app_localizations_te.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'localization/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale)
    : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations? of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations);
  }

  static const LocalizationsDelegate<AppLocalizations> delegate =
      _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates =
      <LocalizationsDelegate<dynamic>>[
        delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
      ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[
    Locale('bn'),
    Locale('bn', 'IN'),
    Locale('en'),
    Locale('en', 'IN'),
    Locale('gu'),
    Locale('gu', 'IN'),
    Locale('hi'),
    Locale('hi', 'IN'),
    Locale('kn'),
    Locale('kn', 'IN'),
    Locale('ml'),
    Locale('ml', 'IN'),
    Locale('mr'),
    Locale('mr', 'IN'),
    Locale('ta'),
    Locale('ta', 'IN'),
    Locale('te'),
    Locale('te', 'IN'),
  ];

  /// No description provided for @commonLanguage.
  ///
  /// In en_IN, this message translates to:
  /// **'Language'**
  String get commonLanguage;

  /// No description provided for @commonCancel.
  ///
  /// In en_IN, this message translates to:
  /// **'Cancel'**
  String get commonCancel;

  /// No description provided for @commonClose.
  ///
  /// In en_IN, this message translates to:
  /// **'Close'**
  String get commonClose;

  /// No description provided for @commonClear.
  ///
  /// In en_IN, this message translates to:
  /// **'Clear'**
  String get commonClear;

  /// No description provided for @commonActions.
  ///
  /// In en_IN, this message translates to:
  /// **'Actions'**
  String get commonActions;

  /// No description provided for @commonEdit.
  ///
  /// In en_IN, this message translates to:
  /// **'Edit'**
  String get commonEdit;

  /// No description provided for @commonSave.
  ///
  /// In en_IN, this message translates to:
  /// **'Save'**
  String get commonSave;

  /// No description provided for @commonDone.
  ///
  /// In en_IN, this message translates to:
  /// **'Done'**
  String get commonDone;

  /// No description provided for @commonSearch.
  ///
  /// In en_IN, this message translates to:
  /// **'Search...'**
  String get commonSearch;

  /// No description provided for @commonLoading.
  ///
  /// In en_IN, this message translates to:
  /// **'Loading...'**
  String get commonLoading;

  /// No description provided for @languageEnIn.
  ///
  /// In en_IN, this message translates to:
  /// **'English'**
  String get languageEnIn;

  /// No description provided for @languageHiIn.
  ///
  /// In en_IN, this message translates to:
  /// **'Hindi'**
  String get languageHiIn;

  /// No description provided for @languageTaIn.
  ///
  /// In en_IN, this message translates to:
  /// **'Tamil'**
  String get languageTaIn;

  /// No description provided for @languageTeIn.
  ///
  /// In en_IN, this message translates to:
  /// **'Telugu'**
  String get languageTeIn;

  /// No description provided for @languageBnIn.
  ///
  /// In en_IN, this message translates to:
  /// **'Bangla'**
  String get languageBnIn;

  /// No description provided for @languageMlIn.
  ///
  /// In en_IN, this message translates to:
  /// **'Malayalam'**
  String get languageMlIn;

  /// No description provided for @languageKnIn.
  ///
  /// In en_IN, this message translates to:
  /// **'Kannada'**
  String get languageKnIn;

  /// No description provided for @languageMrIn.
  ///
  /// In en_IN, this message translates to:
  /// **'Marathi'**
  String get languageMrIn;

  /// No description provided for @languageGuIn.
  ///
  /// In en_IN, this message translates to:
  /// **'Gujarati'**
  String get languageGuIn;

  /// No description provided for @shellAppName.
  ///
  /// In en_IN, this message translates to:
  /// **'Intelibill'**
  String get shellAppName;

  /// No description provided for @shellLogout.
  ///
  /// In en_IN, this message translates to:
  /// **'Logout'**
  String get shellLogout;

  /// No description provided for @shellProfile.
  ///
  /// In en_IN, this message translates to:
  /// **'Profile'**
  String get shellProfile;

  /// No description provided for @shellLanguage.
  ///
  /// In en_IN, this message translates to:
  /// **'Language'**
  String get shellLanguage;

  /// No description provided for @shellDashboard.
  ///
  /// In en_IN, this message translates to:
  /// **'Dashboard'**
  String get shellDashboard;

  /// No description provided for @shellManageInventory.
  ///
  /// In en_IN, this message translates to:
  /// **'Inventory'**
  String get shellManageInventory;

  /// No description provided for @shellManageSales.
  ///
  /// In en_IN, this message translates to:
  /// **'Sales'**
  String get shellManageSales;

  /// No description provided for @shellNewSale.
  ///
  /// In en_IN, this message translates to:
  /// **'New Sale'**
  String get shellNewSale;

  /// No description provided for @shellManageCustomers.
  ///
  /// In en_IN, this message translates to:
  /// **'Customers'**
  String get shellManageCustomers;

  /// No description provided for @shellManageSuppliers.
  ///
  /// In en_IN, this message translates to:
  /// **'Suppliers'**
  String get shellManageSuppliers;

  /// No description provided for @shellManageExpenses.
  ///
  /// In en_IN, this message translates to:
  /// **'Expenses'**
  String get shellManageExpenses;

  /// No description provided for @shellManageBankAccounts.
  ///
  /// In en_IN, this message translates to:
  /// **'Bank Accounts'**
  String get shellManageBankAccounts;

  /// No description provided for @shellManageUsers.
  ///
  /// In en_IN, this message translates to:
  /// **'Manage Users'**
  String get shellManageUsers;

  /// No description provided for @shellAddShop.
  ///
  /// In en_IN, this message translates to:
  /// **'Add Shop'**
  String get shellAddShop;

  /// No description provided for @shellManageShop.
  ///
  /// In en_IN, this message translates to:
  /// **'Manage Shop'**
  String get shellManageShop;

  /// No description provided for @shellMore.
  ///
  /// In en_IN, this message translates to:
  /// **'More'**
  String get shellMore;

  /// No description provided for @shellManageDiscounts.
  ///
  /// In en_IN, this message translates to:
  /// **'Discounts'**
  String get shellManageDiscounts;

  /// No description provided for @shellChangePassword.
  ///
  /// In en_IN, this message translates to:
  /// **'Change Password'**
  String get shellChangePassword;

  /// No description provided for @shellSalesHistory.
  ///
  /// In en_IN, this message translates to:
  /// **'Sales History'**
  String get shellSalesHistory;

  /// No description provided for @shellProfitLossReport.
  ///
  /// In en_IN, this message translates to:
  /// **'Profit & Loss'**
  String get shellProfitLossReport;

  /// No description provided for @shellInventoryAdjustments.
  ///
  /// In en_IN, this message translates to:
  /// **'Inventory Adjustments'**
  String get shellInventoryAdjustments;

  /// No description provided for @shellInventoryBatchesOverview.
  ///
  /// In en_IN, this message translates to:
  /// **'Inventory Batches'**
  String get shellInventoryBatchesOverview;

  /// No description provided for @shellBatchInventoryInbound.
  ///
  /// In en_IN, this message translates to:
  /// **'Batch Inventory Inbound'**
  String get shellBatchInventoryInbound;

  /// No description provided for @shellAddNewProduct.
  ///
  /// In en_IN, this message translates to:
  /// **'Add New Product'**
  String get shellAddNewProduct;

  /// No description provided for @authLoginNow.
  ///
  /// In en_IN, this message translates to:
  /// **'Login Now'**
  String get authLoginNow;

  /// No description provided for @authPassword.
  ///
  /// In en_IN, this message translates to:
  /// **'Password'**
  String get authPassword;

  /// No description provided for @authLoginCta.
  ///
  /// In en_IN, this message translates to:
  /// **'LOGIN'**
  String get authLoginCta;

  /// No description provided for @authEmailAddress.
  ///
  /// In en_IN, this message translates to:
  /// **'Email Address'**
  String get authEmailAddress;

  /// No description provided for @authRememberMe.
  ///
  /// In en_IN, this message translates to:
  /// **'Remember me'**
  String get authRememberMe;

  /// No description provided for @authForgotPassword.
  ///
  /// In en_IN, this message translates to:
  /// **'Forgot password'**
  String get authForgotPassword;

  /// No description provided for @authRegister.
  ///
  /// In en_IN, this message translates to:
  /// **'Register'**
  String get authRegister;

  /// No description provided for @authValidationEmailInvalid.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter a valid email address.'**
  String get authValidationEmailInvalid;

  /// No description provided for @authValidationPasswordRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Password is required.'**
  String get authValidationPasswordRequired;

  /// No description provided for @authValidationLoginIdentifierRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter your email or mobile number.'**
  String get authValidationLoginIdentifierRequired;

  /// No description provided for @authValidationEmailRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Email is required.'**
  String get authValidationEmailRequired;

  /// No description provided for @customersTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Customers'**
  String get customersTitle;

  /// No description provided for @customersAddCustomer.
  ///
  /// In en_IN, this message translates to:
  /// **'Add Customer'**
  String get customersAddCustomer;

  /// No description provided for @customersNoCustomersFound.
  ///
  /// In en_IN, this message translates to:
  /// **'No customers found'**
  String get customersNoCustomersFound;

  /// No description provided for @customersUnableToLoad.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load customers'**
  String get customersUnableToLoad;

  /// No description provided for @customersRetry.
  ///
  /// In en_IN, this message translates to:
  /// **'Retry'**
  String get customersRetry;

  /// No description provided for @customersInactive.
  ///
  /// In en_IN, this message translates to:
  /// **'Inactive'**
  String get customersInactive;

  /// No description provided for @customersOutstandingLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Outstanding:'**
  String get customersOutstandingLabel;

  /// No description provided for @customersErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get customersErrorNetwork;

  /// No description provided for @customersErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get customersErrorTimeout;

  /// No description provided for @customersErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get customersErrorUnauthorized;

  /// No description provided for @customersErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to view customers.'**
  String get customersErrorForbidden;

  /// No description provided for @customersErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load customers. Please try again.'**
  String get customersErrorGeneric;

  /// No description provided for @suppliersTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Suppliers'**
  String get suppliersTitle;

  /// No description provided for @suppliersAddSupplier.
  ///
  /// In en_IN, this message translates to:
  /// **'Add Supplier'**
  String get suppliersAddSupplier;

  /// No description provided for @suppliersNoSuppliersFound.
  ///
  /// In en_IN, this message translates to:
  /// **'No suppliers found'**
  String get suppliersNoSuppliersFound;

  /// No description provided for @suppliersUnableToLoad.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load suppliers'**
  String get suppliersUnableToLoad;

  /// No description provided for @suppliersRetry.
  ///
  /// In en_IN, this message translates to:
  /// **'Retry'**
  String get suppliersRetry;

  /// No description provided for @suppliersInactive.
  ///
  /// In en_IN, this message translates to:
  /// **'Inactive'**
  String get suppliersInactive;

  /// No description provided for @suppliersPreferred.
  ///
  /// In en_IN, this message translates to:
  /// **'Preferred'**
  String get suppliersPreferred;

  /// No description provided for @suppliersBalanceDueLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Balance Due:'**
  String get suppliersBalanceDueLabel;

  /// No description provided for @suppliersErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get suppliersErrorNetwork;

  /// No description provided for @suppliersErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get suppliersErrorTimeout;

  /// No description provided for @suppliersErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get suppliersErrorUnauthorized;

  /// No description provided for @suppliersErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to view suppliers.'**
  String get suppliersErrorForbidden;

  /// No description provided for @suppliersErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load suppliers. Please try again.'**
  String get suppliersErrorGeneric;

  /// No description provided for @suppliersCreateNameLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Name'**
  String get suppliersCreateNameLabel;

  /// No description provided for @suppliersCreateNameRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Name is required.'**
  String get suppliersCreateNameRequired;

  /// No description provided for @suppliersCreateNameMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Name must be 180 characters or fewer.'**
  String get suppliersCreateNameMax;

  /// No description provided for @suppliersCreateContactPersonLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Contact Person'**
  String get suppliersCreateContactPersonLabel;

  /// No description provided for @suppliersCreateContactPersonMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Contact person must be 120 characters or fewer.'**
  String get suppliersCreateContactPersonMax;

  /// No description provided for @suppliersCreateContactPhoneLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Contact Phone'**
  String get suppliersCreateContactPhoneLabel;

  /// No description provided for @suppliersCreateContactPhoneMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Contact phone must be 32 characters or fewer.'**
  String get suppliersCreateContactPhoneMax;

  /// No description provided for @suppliersCreateContactPhoneInvalid.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter a valid phone number.'**
  String get suppliersCreateContactPhoneInvalid;

  /// No description provided for @suppliersCreateAddressLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Address'**
  String get suppliersCreateAddressLabel;

  /// No description provided for @suppliersCreateAddressRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Address is required.'**
  String get suppliersCreateAddressRequired;

  /// No description provided for @suppliersCreateAddressMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Address must be 320 characters or fewer.'**
  String get suppliersCreateAddressMax;

  /// No description provided for @suppliersCreateCityLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'City'**
  String get suppliersCreateCityLabel;

  /// No description provided for @suppliersCreateCityRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'City is required.'**
  String get suppliersCreateCityRequired;

  /// No description provided for @suppliersCreateCityMax.
  ///
  /// In en_IN, this message translates to:
  /// **'City must be 120 characters or fewer.'**
  String get suppliersCreateCityMax;

  /// No description provided for @suppliersCreateStateLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'State'**
  String get suppliersCreateStateLabel;

  /// No description provided for @suppliersCreateStateRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'State is required.'**
  String get suppliersCreateStateRequired;

  /// No description provided for @suppliersCreateStateMax.
  ///
  /// In en_IN, this message translates to:
  /// **'State must be 120 characters or fewer.'**
  String get suppliersCreateStateMax;

  /// No description provided for @suppliersCreatePinLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'PIN'**
  String get suppliersCreatePinLabel;

  /// No description provided for @suppliersCreatePinRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'PIN is required.'**
  String get suppliersCreatePinRequired;

  /// No description provided for @suppliersCreatePinMax.
  ///
  /// In en_IN, this message translates to:
  /// **'PIN must be 16 characters or fewer.'**
  String get suppliersCreatePinMax;

  /// No description provided for @suppliersCreateActiveLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Active'**
  String get suppliersCreateActiveLabel;

  /// No description provided for @suppliersCreatePreferredLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Preferred'**
  String get suppliersCreatePreferredLabel;

  /// No description provided for @suppliersCreateSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Supplier created successfully.'**
  String get suppliersCreateSuccess;

  /// No description provided for @suppliersCreateErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to create supplier. Please try again.'**
  String get suppliersCreateErrorGeneric;

  /// No description provided for @suppliersCreateErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get suppliersCreateErrorUnauthorized;

  /// No description provided for @suppliersCreateErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to create suppliers.'**
  String get suppliersCreateErrorForbidden;

  /// No description provided for @suppliersCreateErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get suppliersCreateErrorNetwork;

  /// No description provided for @suppliersCreateErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get suppliersCreateErrorTimeout;

  /// No description provided for @inventoryTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Inventory'**
  String get inventoryTitle;

  /// No description provided for @inventoryAddNewProductDescription.
  ///
  /// In en_IN, this message translates to:
  /// **'Create a product linked to your current active shop.'**
  String get inventoryAddNewProductDescription;

  /// No description provided for @inventoryProductsNoProductsFound.
  ///
  /// In en_IN, this message translates to:
  /// **'No products found'**
  String get inventoryProductsNoProductsFound;

  /// No description provided for @inventoryProductsUnableToLoad.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load products'**
  String get inventoryProductsUnableToLoad;

  /// No description provided for @inventoryProductsRetry.
  ///
  /// In en_IN, this message translates to:
  /// **'Retry'**
  String get inventoryProductsRetry;

  /// No description provided for @inventoryProductsInactive.
  ///
  /// In en_IN, this message translates to:
  /// **'Inactive'**
  String get inventoryProductsInactive;

  /// No description provided for @inventoryProductsAddProduct.
  ///
  /// In en_IN, this message translates to:
  /// **'Add Product'**
  String get inventoryProductsAddProduct;

  /// No description provided for @inventoryProductsCreateSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Product created successfully.'**
  String get inventoryProductsCreateSuccess;

  /// No description provided for @inventoryProductsUpdateSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Product updated successfully.'**
  String get inventoryProductsUpdateSuccess;

  /// No description provided for @inventoryCreateTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Add Product'**
  String get inventoryCreateTitle;

  /// No description provided for @inventoryCreateNameLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Name'**
  String get inventoryCreateNameLabel;

  /// No description provided for @inventoryCreateNameRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Name is required.'**
  String get inventoryCreateNameRequired;

  /// No description provided for @inventoryCreateNameMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Name must be 180 characters or fewer.'**
  String get inventoryCreateNameMax;

  /// No description provided for @inventoryCreateBarcodeLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Barcode'**
  String get inventoryCreateBarcodeLabel;

  /// No description provided for @inventoryCreateBarcodeRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Barcode is required.'**
  String get inventoryCreateBarcodeRequired;

  /// No description provided for @inventoryCreateBarcodeMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Barcode must be 120 characters or fewer.'**
  String get inventoryCreateBarcodeMax;

  /// No description provided for @inventoryCreateUomLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Unit of Measure'**
  String get inventoryCreateUomLabel;

  /// No description provided for @inventoryCreateUomRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Unit of measure is required.'**
  String get inventoryCreateUomRequired;

  /// No description provided for @inventoryCreateUomMax.
  ///
  /// In en_IN, this message translates to:
  /// **'UOM must be 40 characters or fewer.'**
  String get inventoryCreateUomMax;

  /// No description provided for @inventoryCreateDescriptionLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Description'**
  String get inventoryCreateDescriptionLabel;

  /// No description provided for @inventoryCreateDescriptionMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Description must be 320 characters or fewer.'**
  String get inventoryCreateDescriptionMax;

  /// No description provided for @inventoryCreateProductActiveLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Active'**
  String get inventoryCreateProductActiveLabel;

  /// No description provided for @inventoryCreateErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to save product. Please try again.'**
  String get inventoryCreateErrorGeneric;

  /// No description provided for @inventoryCreateErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get inventoryCreateErrorUnauthorized;

  /// No description provided for @inventoryCreateErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to manage products.'**
  String get inventoryCreateErrorForbidden;

  /// No description provided for @inventoryCreateErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get inventoryCreateErrorNetwork;

  /// No description provided for @inventoryCreateErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get inventoryCreateErrorTimeout;

  /// No description provided for @inventoryUpdateTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Edit Product'**
  String get inventoryUpdateTitle;

  /// No description provided for @inventoryProductErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load products. Please try again.'**
  String get inventoryProductErrorGeneric;

  /// No description provided for @inventoryProductErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get inventoryProductErrorNetwork;

  /// No description provided for @inventoryProductErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get inventoryProductErrorTimeout;

  /// No description provided for @inventoryProductErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get inventoryProductErrorUnauthorized;

  /// No description provided for @inventoryProductErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to view products.'**
  String get inventoryProductErrorForbidden;

  /// No description provided for @inventoryMenuAddInventory.
  ///
  /// In en_IN, this message translates to:
  /// **'Add Inventory'**
  String get inventoryMenuAddInventory;

  /// No description provided for @inventoryMenuBatchOverview.
  ///
  /// In en_IN, this message translates to:
  /// **'Batch Overview'**
  String get inventoryMenuBatchOverview;

  /// No description provided for @inventoryMenuAdjustmentHistory.
  ///
  /// In en_IN, this message translates to:
  /// **'Adjustment History'**
  String get inventoryMenuAdjustmentHistory;

  /// No description provided for @inventoryInboundSectionProduct.
  ///
  /// In en_IN, this message translates to:
  /// **'Product Details'**
  String get inventoryInboundSectionProduct;

  /// No description provided for @inventoryInboundSectionBatch.
  ///
  /// In en_IN, this message translates to:
  /// **'Batch Details'**
  String get inventoryInboundSectionBatch;

  /// No description provided for @inventoryInboundSectionAdditional.
  ///
  /// In en_IN, this message translates to:
  /// **'Additional Details'**
  String get inventoryInboundSectionAdditional;

  /// No description provided for @inventoryInboundItemNameLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Item Name'**
  String get inventoryInboundItemNameLabel;

  /// No description provided for @inventoryInboundItemNameRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Item name is required.'**
  String get inventoryInboundItemNameRequired;

  /// No description provided for @inventoryInboundItemNameMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Item name must be 200 characters or fewer.'**
  String get inventoryInboundItemNameMax;

  /// No description provided for @inventoryInboundBarcodeLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Barcode'**
  String get inventoryInboundBarcodeLabel;

  /// No description provided for @inventoryInboundBarcodeRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Barcode is required.'**
  String get inventoryInboundBarcodeRequired;

  /// No description provided for @inventoryInboundBarcodeMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Barcode must be 120 characters or fewer.'**
  String get inventoryInboundBarcodeMax;

  /// No description provided for @inventoryInboundUomLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Unit of Measure'**
  String get inventoryInboundUomLabel;

  /// No description provided for @inventoryInboundUomRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Unit of measure is required.'**
  String get inventoryInboundUomRequired;

  /// No description provided for @inventoryInboundUomMax.
  ///
  /// In en_IN, this message translates to:
  /// **'UOM must be 40 characters or fewer.'**
  String get inventoryInboundUomMax;

  /// No description provided for @inventoryInboundBatchNumberLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Batch Number'**
  String get inventoryInboundBatchNumberLabel;

  /// No description provided for @inventoryInboundBatchNumberHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Optional batch identifier'**
  String get inventoryInboundBatchNumberHint;

  /// No description provided for @inventoryInboundBatchNumberMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Batch number must be 80 characters or fewer.'**
  String get inventoryInboundBatchNumberMax;

  /// No description provided for @inventoryInboundQuantityLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Quantity'**
  String get inventoryInboundQuantityLabel;

  /// No description provided for @inventoryInboundQuantityRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Quantity is required.'**
  String get inventoryInboundQuantityRequired;

  /// No description provided for @inventoryInboundQuantityMin.
  ///
  /// In en_IN, this message translates to:
  /// **'Quantity must be greater than 0.'**
  String get inventoryInboundQuantityMin;

  /// No description provided for @inventoryInboundCostPriceLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Cost Price'**
  String get inventoryInboundCostPriceLabel;

  /// No description provided for @inventoryInboundCostPriceRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Cost price is required.'**
  String get inventoryInboundCostPriceRequired;

  /// No description provided for @inventoryInboundMrpLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'MRP'**
  String get inventoryInboundMrpLabel;

  /// No description provided for @inventoryInboundMrpRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'MRP is required.'**
  String get inventoryInboundMrpRequired;

  /// No description provided for @inventoryInboundSalesPriceLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Sales Price'**
  String get inventoryInboundSalesPriceLabel;

  /// No description provided for @inventoryInboundSalesPriceRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Sales price is required.'**
  String get inventoryInboundSalesPriceRequired;

  /// No description provided for @inventoryInboundTaxRateLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Tax Rate (%)'**
  String get inventoryInboundTaxRateLabel;

  /// No description provided for @inventoryInboundTaxRateRange.
  ///
  /// In en_IN, this message translates to:
  /// **'Tax rate must be between 0 and 100.'**
  String get inventoryInboundTaxRateRange;

  /// No description provided for @inventoryInboundTaxIncludedLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Tax Included'**
  String get inventoryInboundTaxIncludedLabel;

  /// No description provided for @inventoryInboundExpiryDateLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Expiry Date'**
  String get inventoryInboundExpiryDateLabel;

  /// No description provided for @inventoryInboundManufacturingDateLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Manufacturing Date'**
  String get inventoryInboundManufacturingDateLabel;

  /// No description provided for @inventoryInboundReferenceLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Reference Number'**
  String get inventoryInboundReferenceLabel;

  /// No description provided for @inventoryInboundReferenceMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Reference must be 80 characters or fewer.'**
  String get inventoryInboundReferenceMax;

  /// No description provided for @inventoryInboundNotesLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Notes'**
  String get inventoryInboundNotesLabel;

  /// No description provided for @inventoryInboundNotesMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Notes must be 500 characters or fewer.'**
  String get inventoryInboundNotesMax;

  /// No description provided for @inventoryInboundNumberInvalid.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter a valid number.'**
  String get inventoryInboundNumberInvalid;

  /// No description provided for @inventoryInboundSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Inventory added successfully.'**
  String get inventoryInboundSuccess;

  /// No description provided for @inventoryInboundErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to add inventory. Please try again.'**
  String get inventoryInboundErrorGeneric;

  /// No description provided for @inventoryInboundErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get inventoryInboundErrorNetwork;

  /// No description provided for @inventoryInboundErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get inventoryInboundErrorTimeout;

  /// No description provided for @placeholderBody.
  ///
  /// In en_IN, this message translates to:
  /// **'This feature is coming soon.'**
  String get placeholderBody;

  /// No description provided for @notFoundTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Page Not Found'**
  String get notFoundTitle;

  /// No description provided for @notFoundGoBack.
  ///
  /// In en_IN, this message translates to:
  /// **'Go Back'**
  String get notFoundGoBack;

  /// No description provided for @profileEditTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Edit Profile'**
  String get profileEditTitle;

  /// No description provided for @profileFirstNameLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'First Name'**
  String get profileFirstNameLabel;

  /// No description provided for @profileFirstNameHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter your first name'**
  String get profileFirstNameHint;

  /// No description provided for @profileFirstNameRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'First name is required'**
  String get profileFirstNameRequired;

  /// No description provided for @profileLastNameLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Last Name'**
  String get profileLastNameLabel;

  /// No description provided for @profileLastNameHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter your last name'**
  String get profileLastNameHint;

  /// No description provided for @profileLastNameRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Last name is required'**
  String get profileLastNameRequired;

  /// No description provided for @profileEmailLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Email'**
  String get profileEmailLabel;

  /// No description provided for @profileEmailHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter your email'**
  String get profileEmailHint;

  /// No description provided for @profileEmailRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Email is required'**
  String get profileEmailRequired;

  /// No description provided for @profileEmailInvalid.
  ///
  /// In en_IN, this message translates to:
  /// **'Please enter a valid email'**
  String get profileEmailInvalid;

  /// No description provided for @profilePhoneLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Phone Number (Optional)'**
  String get profilePhoneLabel;

  /// No description provided for @profilePhoneHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter your phone number'**
  String get profilePhoneHint;

  /// No description provided for @profilePhoneMin.
  ///
  /// In en_IN, this message translates to:
  /// **'Phone number must be at least 10 digits'**
  String get profilePhoneMin;

  /// No description provided for @profileUpdateButton.
  ///
  /// In en_IN, this message translates to:
  /// **'Update Profile'**
  String get profileUpdateButton;

  /// No description provided for @profileUpdatingButton.
  ///
  /// In en_IN, this message translates to:
  /// **'Updating...'**
  String get profileUpdatingButton;

  /// No description provided for @profileUpdateSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Profile updated successfully'**
  String get profileUpdateSuccess;

  /// No description provided for @profileUnableToLoad.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load profile'**
  String get profileUnableToLoad;

  /// No description provided for @passwordChangeTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Change Password'**
  String get passwordChangeTitle;

  /// No description provided for @passwordCurrentLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Current Password'**
  String get passwordCurrentLabel;

  /// No description provided for @passwordCurrentHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter your current password'**
  String get passwordCurrentHint;

  /// No description provided for @passwordCurrentRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Current password is required'**
  String get passwordCurrentRequired;

  /// No description provided for @passwordNewLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'New Password'**
  String get passwordNewLabel;

  /// No description provided for @passwordNewHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter your new password'**
  String get passwordNewHint;

  /// No description provided for @passwordNewRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'New password is required'**
  String get passwordNewRequired;

  /// No description provided for @passwordConfirmLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Confirm Password'**
  String get passwordConfirmLabel;

  /// No description provided for @passwordConfirmHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Confirm your new password'**
  String get passwordConfirmHint;

  /// No description provided for @passwordConfirmRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Please confirm your password'**
  String get passwordConfirmRequired;

  /// No description provided for @passwordConfirmMismatch.
  ///
  /// In en_IN, this message translates to:
  /// **'Passwords do not match'**
  String get passwordConfirmMismatch;

  /// No description provided for @passwordChangeButton.
  ///
  /// In en_IN, this message translates to:
  /// **'Change Password'**
  String get passwordChangeButton;

  /// No description provided for @passwordChangingButton.
  ///
  /// In en_IN, this message translates to:
  /// **'Changing password...'**
  String get passwordChangingButton;

  /// No description provided for @passwordChangeSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Password changed successfully'**
  String get passwordChangeSuccess;

  /// No description provided for @passwordUnableToInitialize.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to initialize password screen'**
  String get passwordUnableToInitialize;

  /// No description provided for @profileSettingsTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Profile Settings'**
  String get profileSettingsTitle;

  /// No description provided for @profileSettingsActiveShop.
  ///
  /// In en_IN, this message translates to:
  /// **'Active Shop'**
  String get profileSettingsActiveShop;

  /// No description provided for @profileSettingsEditProfile.
  ///
  /// In en_IN, this message translates to:
  /// **'Edit Profile'**
  String get profileSettingsEditProfile;

  /// No description provided for @profileSettingsChangePassword.
  ///
  /// In en_IN, this message translates to:
  /// **'Change Password'**
  String get profileSettingsChangePassword;

  /// No description provided for @inventoryBatchesNoBatchesFound.
  ///
  /// In en_IN, this message translates to:
  /// **'No batches found'**
  String get inventoryBatchesNoBatchesFound;

  /// No description provided for @inventoryBatchesUnableToLoad.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load batches'**
  String get inventoryBatchesUnableToLoad;

  /// No description provided for @inventoryBatchesRetry.
  ///
  /// In en_IN, this message translates to:
  /// **'Retry'**
  String get inventoryBatchesRetry;

  /// No description provided for @inventoryBatchesVoided.
  ///
  /// In en_IN, this message translates to:
  /// **'Voided'**
  String get inventoryBatchesVoided;

  /// No description provided for @inventoryBatchesAdjustAction.
  ///
  /// In en_IN, this message translates to:
  /// **'Adjust'**
  String get inventoryBatchesAdjustAction;

  /// No description provided for @inventoryBatchesAdjustTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Adjust Stock'**
  String get inventoryBatchesAdjustTitle;

  /// No description provided for @inventoryBatchesAdjustPreviewLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'New Quantity'**
  String get inventoryBatchesAdjustPreviewLabel;

  /// No description provided for @inventoryBatchesAdjustDirectionLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Direction'**
  String get inventoryBatchesAdjustDirectionLabel;

  /// No description provided for @inventoryBatchesAdjustDirectionIncrease.
  ///
  /// In en_IN, this message translates to:
  /// **'Increase'**
  String get inventoryBatchesAdjustDirectionIncrease;

  /// No description provided for @inventoryBatchesAdjustDirectionDecrease.
  ///
  /// In en_IN, this message translates to:
  /// **'Decrease'**
  String get inventoryBatchesAdjustDirectionDecrease;

  /// No description provided for @inventoryBatchesAdjustQuantityLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Quantity'**
  String get inventoryBatchesAdjustQuantityLabel;

  /// No description provided for @inventoryBatchesAdjustQuantityRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Quantity is required.'**
  String get inventoryBatchesAdjustQuantityRequired;

  /// No description provided for @inventoryBatchesAdjustQuantityMin.
  ///
  /// In en_IN, this message translates to:
  /// **'Quantity must be greater than 0.'**
  String get inventoryBatchesAdjustQuantityMin;

  /// No description provided for @inventoryBatchesAdjustQuantityMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Quantity cannot exceed current batch quantity.'**
  String get inventoryBatchesAdjustQuantityMax;

  /// No description provided for @inventoryBatchesAdjustReasonLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Reason'**
  String get inventoryBatchesAdjustReasonLabel;

  /// No description provided for @inventoryBatchesAdjustNotesLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Notes'**
  String get inventoryBatchesAdjustNotesLabel;

  /// No description provided for @inventoryBatchesAdjustNotesMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Notes must be 500 characters or fewer.'**
  String get inventoryBatchesAdjustNotesMax;

  /// No description provided for @inventoryBatchesAdjustNotesRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Notes are required for this reason.'**
  String get inventoryBatchesAdjustNotesRequired;

  /// No description provided for @inventoryBatchesAdjustPerformedAtLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Performed At'**
  String get inventoryBatchesAdjustPerformedAtLabel;

  /// No description provided for @inventoryBatchesAdjustSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Stock adjusted successfully.'**
  String get inventoryBatchesAdjustSuccess;

  /// No description provided for @inventoryBatchesErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load batches. Please try again.'**
  String get inventoryBatchesErrorGeneric;

  /// No description provided for @inventoryBatchesErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get inventoryBatchesErrorNetwork;

  /// No description provided for @inventoryBatchesErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get inventoryBatchesErrorTimeout;

  /// No description provided for @inventoryBatchesErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get inventoryBatchesErrorUnauthorized;

  /// No description provided for @inventoryBatchesErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to view batches.'**
  String get inventoryBatchesErrorForbidden;

  /// No description provided for @inventoryAdjustReasonDamaged.
  ///
  /// In en_IN, this message translates to:
  /// **'Damaged'**
  String get inventoryAdjustReasonDamaged;

  /// No description provided for @inventoryAdjustReasonExpired.
  ///
  /// In en_IN, this message translates to:
  /// **'Expired'**
  String get inventoryAdjustReasonExpired;

  /// No description provided for @inventoryAdjustReasonStolen.
  ///
  /// In en_IN, this message translates to:
  /// **'Stolen'**
  String get inventoryAdjustReasonStolen;

  /// No description provided for @inventoryAdjustReasonMissingLost.
  ///
  /// In en_IN, this message translates to:
  /// **'Missing / Lost'**
  String get inventoryAdjustReasonMissingLost;

  /// No description provided for @inventoryAdjustReasonStockCountCorrection.
  ///
  /// In en_IN, this message translates to:
  /// **'Stock Count Correction'**
  String get inventoryAdjustReasonStockCountCorrection;

  /// No description provided for @inventoryAdjustReasonOtherLoss.
  ///
  /// In en_IN, this message translates to:
  /// **'Other Loss'**
  String get inventoryAdjustReasonOtherLoss;

  /// No description provided for @inventoryAdjustReasonFoundStock.
  ///
  /// In en_IN, this message translates to:
  /// **'Found Stock'**
  String get inventoryAdjustReasonFoundStock;

  /// No description provided for @inventoryAdjustReasonReturnRestockCorrection.
  ///
  /// In en_IN, this message translates to:
  /// **'Return / Restock Correction'**
  String get inventoryAdjustReasonReturnRestockCorrection;

  /// No description provided for @inventoryAdjustReasonOtherGain.
  ///
  /// In en_IN, this message translates to:
  /// **'Other Gain'**
  String get inventoryAdjustReasonOtherGain;

  /// No description provided for @inventoryAdjustmentsTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Adjustment History'**
  String get inventoryAdjustmentsTitle;

  /// No description provided for @inventoryAdjustmentsNoAdjustmentsFound.
  ///
  /// In en_IN, this message translates to:
  /// **'No adjustments found'**
  String get inventoryAdjustmentsNoAdjustmentsFound;

  /// No description provided for @inventoryAdjustmentsUnableToLoad.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load adjustments'**
  String get inventoryAdjustmentsUnableToLoad;

  /// No description provided for @inventoryAdjustmentsRetry.
  ///
  /// In en_IN, this message translates to:
  /// **'Retry'**
  String get inventoryAdjustmentsRetry;

  /// No description provided for @inventoryAdjustmentsVoided.
  ///
  /// In en_IN, this message translates to:
  /// **'Voided'**
  String get inventoryAdjustmentsVoided;

  /// No description provided for @inventoryAdjustmentsIncrease.
  ///
  /// In en_IN, this message translates to:
  /// **'Increase'**
  String get inventoryAdjustmentsIncrease;

  /// No description provided for @inventoryAdjustmentsDecrease.
  ///
  /// In en_IN, this message translates to:
  /// **'Decrease'**
  String get inventoryAdjustmentsDecrease;

  /// No description provided for @inventoryAdjustmentsPerformedByLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Performed by'**
  String get inventoryAdjustmentsPerformedByLabel;

  /// No description provided for @inventoryAdjustmentsCostImpactLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Cost impact'**
  String get inventoryAdjustmentsCostImpactLabel;

  /// No description provided for @barcodeScannerTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Scan Barcode'**
  String get barcodeScannerTitle;

  /// No description provided for @barcodeScannerHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Point the camera at a barcode'**
  String get barcodeScannerHint;

  /// No description provided for @barcodeScannerSearching.
  ///
  /// In en_IN, this message translates to:
  /// **'Starting camera...'**
  String get barcodeScannerSearching;

  /// No description provided for @barcodeScannerSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Barcode detected'**
  String get barcodeScannerSuccess;

  /// No description provided for @barcodeScannerPermissionDenied.
  ///
  /// In en_IN, this message translates to:
  /// **'Camera permission denied'**
  String get barcodeScannerPermissionDenied;

  /// No description provided for @barcodeScannerUnavailable.
  ///
  /// In en_IN, this message translates to:
  /// **'Scanner unavailable'**
  String get barcodeScannerUnavailable;

  /// No description provided for @shopsCreateTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Create Shop'**
  String get shopsCreateTitle;

  /// No description provided for @shopsCreateShopInfoStepTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Shop Information'**
  String get shopsCreateShopInfoStepTitle;

  /// No description provided for @shopsCreateBankDetailsStepTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Bank Details'**
  String get shopsCreateBankDetailsStepTitle;

  /// No description provided for @shopsCreateSuccessTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Shop Created'**
  String get shopsCreateSuccessTitle;

  /// No description provided for @shopsCreateSuccessDefaultShopName.
  ///
  /// In en_IN, this message translates to:
  /// **'New Shop'**
  String get shopsCreateSuccessDefaultShopName;

  /// No description provided for @shopsCreateSuccessMessage.
  ///
  /// In en_IN, this message translates to:
  /// **'Your shop \"{shopName}\" is ready.'**
  String shopsCreateSuccessMessage(String shopName);

  /// No description provided for @shopsCreateNextButton.
  ///
  /// In en_IN, this message translates to:
  /// **'Next'**
  String get shopsCreateNextButton;

  /// No description provided for @shopsCreateSkipButton.
  ///
  /// In en_IN, this message translates to:
  /// **'Skip'**
  String get shopsCreateSkipButton;

  /// No description provided for @shopsCreateShopNameLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Shop Name'**
  String get shopsCreateShopNameLabel;

  /// No description provided for @shopsCreateShopNameHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter shop name'**
  String get shopsCreateShopNameHint;

  /// No description provided for @shopsCreateShopNameRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Shop Name is required.'**
  String get shopsCreateShopNameRequired;

  /// No description provided for @shopsCreateAddressLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Address'**
  String get shopsCreateAddressLabel;

  /// No description provided for @shopsCreateAddressHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter shop address'**
  String get shopsCreateAddressHint;

  /// No description provided for @shopsCreateAddressRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Address is required.'**
  String get shopsCreateAddressRequired;

  /// No description provided for @shopsCreateCityLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'City'**
  String get shopsCreateCityLabel;

  /// No description provided for @shopsCreateCityHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter city'**
  String get shopsCreateCityHint;

  /// No description provided for @shopsCreateCityRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'City is required.'**
  String get shopsCreateCityRequired;

  /// No description provided for @shopsCreateStateLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'State'**
  String get shopsCreateStateLabel;

  /// No description provided for @shopsCreateStateHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter state'**
  String get shopsCreateStateHint;

  /// No description provided for @shopsCreateStateRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'State is required.'**
  String get shopsCreateStateRequired;

  /// No description provided for @shopsCreatePincodeLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Pincode'**
  String get shopsCreatePincodeLabel;

  /// No description provided for @shopsCreatePincodeHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter 6-digit pincode'**
  String get shopsCreatePincodeHint;

  /// No description provided for @shopsCreatePincodeRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Pincode is required.'**
  String get shopsCreatePincodeRequired;

  /// No description provided for @shopsCreatePincodeInvalid.
  ///
  /// In en_IN, this message translates to:
  /// **'Pincode must be exactly 6 digits.'**
  String get shopsCreatePincodeInvalid;

  /// No description provided for @shopsCreateContactPersonLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Contact Person'**
  String get shopsCreateContactPersonLabel;

  /// No description provided for @shopsCreateContactPersonHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter contact person'**
  String get shopsCreateContactPersonHint;

  /// No description provided for @shopsCreateMobileNumberLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Mobile Number'**
  String get shopsCreateMobileNumberLabel;

  /// No description provided for @shopsCreateMobileNumberHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter 10-digit mobile number'**
  String get shopsCreateMobileNumberHint;

  /// No description provided for @shopsCreateMobileNumberInvalid.
  ///
  /// In en_IN, this message translates to:
  /// **'Mobile number must be 10 digits.'**
  String get shopsCreateMobileNumberInvalid;

  /// No description provided for @shopsCreateGstNumberLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'GST Number'**
  String get shopsCreateGstNumberLabel;

  /// No description provided for @shopsCreateGstNumberHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter GST number'**
  String get shopsCreateGstNumberHint;

  /// No description provided for @shopsCreateGstNumberInvalid.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter a valid GST number.'**
  String get shopsCreateGstNumberInvalid;

  /// No description provided for @shopsCreateBankNameLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Bank Name'**
  String get shopsCreateBankNameLabel;

  /// No description provided for @shopsCreateBankNameHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter bank name'**
  String get shopsCreateBankNameHint;

  /// No description provided for @shopsCreateBankNameRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Bank Name is required.'**
  String get shopsCreateBankNameRequired;

  /// No description provided for @shopsCreateAccountNumberLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Account Number'**
  String get shopsCreateAccountNumberLabel;

  /// No description provided for @shopsCreateAccountNumberHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter account number'**
  String get shopsCreateAccountNumberHint;

  /// No description provided for @shopsCreateAccountNumberRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Account Number is required.'**
  String get shopsCreateAccountNumberRequired;

  /// No description provided for @shopsCreateAccountTypeLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Account Type'**
  String get shopsCreateAccountTypeLabel;

  /// No description provided for @shopsCreateAccountTypeRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Account type is required.'**
  String get shopsCreateAccountTypeRequired;

  /// No description provided for @shopsCreateAccountTypeSavings.
  ///
  /// In en_IN, this message translates to:
  /// **'Savings'**
  String get shopsCreateAccountTypeSavings;

  /// No description provided for @shopsCreateAccountTypeCurrent.
  ///
  /// In en_IN, this message translates to:
  /// **'Current'**
  String get shopsCreateAccountTypeCurrent;

  /// No description provided for @shopsCreateAccountTypeOverdraft.
  ///
  /// In en_IN, this message translates to:
  /// **'Overdraft'**
  String get shopsCreateAccountTypeOverdraft;

  /// No description provided for @shopsCreateIfscCodeLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'IFSC Code'**
  String get shopsCreateIfscCodeLabel;

  /// No description provided for @shopsCreateIfscCodeHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter IFSC code'**
  String get shopsCreateIfscCodeHint;

  /// No description provided for @shopsCreateIfscCodeInvalid.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter a valid IFSC code.'**
  String get shopsCreateIfscCodeInvalid;

  /// No description provided for @shopsCreateAccountHolderNameLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Account Holder Name'**
  String get shopsCreateAccountHolderNameLabel;

  /// No description provided for @shopsCreateAccountHolderNameHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter account holder name'**
  String get shopsCreateAccountHolderNameHint;

  /// No description provided for @shopsCreateAccountHolderNameRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Account Holder Name is required.'**
  String get shopsCreateAccountHolderNameRequired;

  /// No description provided for @shopsCreateErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Something went wrong. Please try again.'**
  String get shopsCreateErrorGeneric;

  /// No description provided for @shopsCreateErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'You are not authorized.'**
  String get shopsCreateErrorUnauthorized;

  /// No description provided for @shopsCreateErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission.'**
  String get shopsCreateErrorForbidden;

  /// No description provided for @shopsCreateErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Network error. Please try again.'**
  String get shopsCreateErrorNetwork;

  /// No description provided for @shopsCreateErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get shopsCreateErrorTimeout;
}

class _AppLocalizationsDelegate
    extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) => <String>[
    'bn',
    'en',
    'gu',
    'hi',
    'kn',
    'ml',
    'mr',
    'ta',
    'te',
  ].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {
  // Lookup logic when language+country codes are specified.
  switch (locale.languageCode) {
    case 'bn':
      {
        switch (locale.countryCode) {
          case 'IN':
            return AppLocalizationsBnIn();
        }
        break;
      }
    case 'en':
      {
        switch (locale.countryCode) {
          case 'IN':
            return AppLocalizationsEnIn();
        }
        break;
      }
    case 'gu':
      {
        switch (locale.countryCode) {
          case 'IN':
            return AppLocalizationsGuIn();
        }
        break;
      }
    case 'hi':
      {
        switch (locale.countryCode) {
          case 'IN':
            return AppLocalizationsHiIn();
        }
        break;
      }
    case 'kn':
      {
        switch (locale.countryCode) {
          case 'IN':
            return AppLocalizationsKnIn();
        }
        break;
      }
    case 'ml':
      {
        switch (locale.countryCode) {
          case 'IN':
            return AppLocalizationsMlIn();
        }
        break;
      }
    case 'mr':
      {
        switch (locale.countryCode) {
          case 'IN':
            return AppLocalizationsMrIn();
        }
        break;
      }
    case 'ta':
      {
        switch (locale.countryCode) {
          case 'IN':
            return AppLocalizationsTaIn();
        }
        break;
      }
    case 'te':
      {
        switch (locale.countryCode) {
          case 'IN':
            return AppLocalizationsTeIn();
        }
        break;
      }
  }

  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'bn':
      return AppLocalizationsBn();
    case 'en':
      return AppLocalizationsEn();
    case 'gu':
      return AppLocalizationsGu();
    case 'hi':
      return AppLocalizationsHi();
    case 'kn':
      return AppLocalizationsKn();
    case 'ml':
      return AppLocalizationsMl();
    case 'mr':
      return AppLocalizationsMr();
    case 'ta':
      return AppLocalizationsTa();
    case 'te':
      return AppLocalizationsTe();
  }

  throw FlutterError(
    'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
    'an issue with the localizations generation tool. Please file an issue '
    'on GitHub with a reproducible sample app and the gen-l10n configuration '
    'that was used.',
  );
}
