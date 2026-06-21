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

  /// No description provided for @shellManageCreditNotes.
  ///
  /// In en_IN, this message translates to:
  /// **'Credit Notes'**
  String get shellManageCreditNotes;

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

  /// No description provided for @shellManageServices.
  ///
  /// In en_IN, this message translates to:
  /// **'Services'**
  String get shellManageServices;

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

  /// No description provided for @shellSectionManagement.
  ///
  /// In en_IN, this message translates to:
  /// **'Management'**
  String get shellSectionManagement;

  /// No description provided for @shellSectionProfile.
  ///
  /// In en_IN, this message translates to:
  /// **'Account'**
  String get shellSectionProfile;

  /// No description provided for @shellSectionShop.
  ///
  /// In en_IN, this message translates to:
  /// **'Shop'**
  String get shellSectionShop;

  /// No description provided for @shellSectionSettings.
  ///
  /// In en_IN, this message translates to:
  /// **'Settings'**
  String get shellSectionSettings;

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

  /// No description provided for @servicesTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Services'**
  String get servicesTitle;

  /// No description provided for @servicesFilterAll.
  ///
  /// In en_IN, this message translates to:
  /// **'All'**
  String get servicesFilterAll;

  /// No description provided for @servicesFilterActive.
  ///
  /// In en_IN, this message translates to:
  /// **'Active'**
  String get servicesFilterActive;

  /// No description provided for @servicesFilterInactive.
  ///
  /// In en_IN, this message translates to:
  /// **'Inactive'**
  String get servicesFilterInactive;

  /// No description provided for @servicesNoServicesFound.
  ///
  /// In en_IN, this message translates to:
  /// **'No services found'**
  String get servicesNoServicesFound;

  /// No description provided for @servicesUnableToLoad.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load services'**
  String get servicesUnableToLoad;

  /// No description provided for @servicesRetry.
  ///
  /// In en_IN, this message translates to:
  /// **'Retry'**
  String get servicesRetry;

  /// No description provided for @servicesActive.
  ///
  /// In en_IN, this message translates to:
  /// **'Active'**
  String get servicesActive;

  /// No description provided for @servicesInactive.
  ///
  /// In en_IN, this message translates to:
  /// **'Inactive'**
  String get servicesInactive;

  /// No description provided for @servicesErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get servicesErrorNetwork;

  /// No description provided for @servicesErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get servicesErrorTimeout;

  /// No description provided for @servicesErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get servicesErrorUnauthorized;

  /// No description provided for @servicesErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to view services.'**
  String get servicesErrorForbidden;

  /// No description provided for @servicesErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load services. Please try again.'**
  String get servicesErrorGeneric;

  /// No description provided for @servicesAddService.
  ///
  /// In en_IN, this message translates to:
  /// **'Add Service'**
  String get servicesAddService;

  /// No description provided for @servicesEditService.
  ///
  /// In en_IN, this message translates to:
  /// **'Edit Service'**
  String get servicesEditService;

  /// No description provided for @servicesNameLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Name'**
  String get servicesNameLabel;

  /// No description provided for @servicesNameRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Service name is required.'**
  String get servicesNameRequired;

  /// No description provided for @servicesNameMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Name must be 180 characters or fewer.'**
  String get servicesNameMax;

  /// No description provided for @servicesDescriptionLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Description'**
  String get servicesDescriptionLabel;

  /// No description provided for @servicesDescriptionMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Description must be 1000 characters or fewer.'**
  String get servicesDescriptionMax;

  /// No description provided for @servicesPriceLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Price'**
  String get servicesPriceLabel;

  /// No description provided for @servicesPriceRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Price is required.'**
  String get servicesPriceRequired;

  /// No description provided for @servicesPriceInvalid.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter a valid price greater than 0.'**
  String get servicesPriceInvalid;

  /// No description provided for @servicesHsnCodeLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'HSN Code'**
  String get servicesHsnCodeLabel;

  /// No description provided for @servicesHsnCodeInvalid.
  ///
  /// In en_IN, this message translates to:
  /// **'HSN code must be 4 to 8 digits.'**
  String get servicesHsnCodeInvalid;

  /// No description provided for @servicesTaxRateLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Tax Rate (%)'**
  String get servicesTaxRateLabel;

  /// No description provided for @servicesTaxRateRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Tax rate is required.'**
  String get servicesTaxRateRequired;

  /// No description provided for @servicesTaxRateInvalid.
  ///
  /// In en_IN, this message translates to:
  /// **'Tax rate must be between 0 and 100.'**
  String get servicesTaxRateInvalid;

  /// No description provided for @servicesTaxIncludedLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Tax included'**
  String get servicesTaxIncludedLabel;

  /// No description provided for @servicesActiveOnCreateLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Active on create'**
  String get servicesActiveOnCreateLabel;

  /// No description provided for @servicesTaxIncluded.
  ///
  /// In en_IN, this message translates to:
  /// **'Tax included'**
  String get servicesTaxIncluded;

  /// No description provided for @servicesTaxExcluded.
  ///
  /// In en_IN, this message translates to:
  /// **'Tax excluded'**
  String get servicesTaxExcluded;

  /// No description provided for @servicesActivate.
  ///
  /// In en_IN, this message translates to:
  /// **'Activate'**
  String get servicesActivate;

  /// No description provided for @servicesDeactivate.
  ///
  /// In en_IN, this message translates to:
  /// **'Deactivate'**
  String get servicesDeactivate;

  /// No description provided for @servicesCreateSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Service created successfully.'**
  String get servicesCreateSuccess;

  /// No description provided for @servicesUpdateSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Service updated successfully.'**
  String get servicesUpdateSuccess;

  /// No description provided for @servicesActivatedSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Service activated successfully.'**
  String get servicesActivatedSuccess;

  /// No description provided for @servicesDeactivatedSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Service deactivated successfully.'**
  String get servicesDeactivatedSuccess;

  /// No description provided for @servicesMutationErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get servicesMutationErrorNetwork;

  /// No description provided for @servicesMutationErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get servicesMutationErrorTimeout;

  /// No description provided for @servicesMutationErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get servicesMutationErrorUnauthorized;

  /// No description provided for @servicesMutationErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to manage services.'**
  String get servicesMutationErrorForbidden;

  /// No description provided for @servicesMutationErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to save service. Please try again.'**
  String get servicesMutationErrorGeneric;

  /// No description provided for @usersTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Shop Users'**
  String get usersTitle;

  /// No description provided for @usersSubtitle.
  ///
  /// In en_IN, this message translates to:
  /// **'View all members in the active shop and manage role assignments.'**
  String get usersSubtitle;

  /// No description provided for @usersAddUser.
  ///
  /// In en_IN, this message translates to:
  /// **'Add User'**
  String get usersAddUser;

  /// No description provided for @usersAddUserDescription.
  ///
  /// In en_IN, this message translates to:
  /// **'Create a manager or sales person for the active shop.'**
  String get usersAddUserDescription;

  /// No description provided for @usersEditUser.
  ///
  /// In en_IN, this message translates to:
  /// **'Edit User'**
  String get usersEditUser;

  /// No description provided for @usersEditUserDescription.
  ///
  /// In en_IN, this message translates to:
  /// **'Update role and login access for this user.'**
  String get usersEditUserDescription;

  /// No description provided for @usersSearchPlaceholder.
  ///
  /// In en_IN, this message translates to:
  /// **'Search users...'**
  String get usersSearchPlaceholder;

  /// No description provided for @usersNoUsersFound.
  ///
  /// In en_IN, this message translates to:
  /// **'No users found'**
  String get usersNoUsersFound;

  /// No description provided for @usersNoUsersDescription.
  ///
  /// In en_IN, this message translates to:
  /// **'Start by adding a manager or sales person for this shop.'**
  String get usersNoUsersDescription;

  /// No description provided for @usersUnableToLoad.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load users'**
  String get usersUnableToLoad;

  /// No description provided for @usersRetry.
  ///
  /// In en_IN, this message translates to:
  /// **'Retry'**
  String get usersRetry;

  /// No description provided for @usersShopsLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Shops'**
  String get usersShopsLabel;

  /// No description provided for @usersSelectShopsDescription.
  ///
  /// In en_IN, this message translates to:
  /// **'Select one or more shops for this user.'**
  String get usersSelectShopsDescription;

  /// No description provided for @usersSelectShopsForAccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Select the shops this user should have access to.'**
  String get usersSelectShopsForAccess;

  /// No description provided for @usersDefaultShop.
  ///
  /// In en_IN, this message translates to:
  /// **'Default'**
  String get usersDefaultShop;

  /// No description provided for @usersSelectAtLeastOneShop.
  ///
  /// In en_IN, this message translates to:
  /// **'Select at least one shop.'**
  String get usersSelectAtLeastOneShop;

  /// No description provided for @usersFirstNameLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'First Name'**
  String get usersFirstNameLabel;

  /// No description provided for @usersFirstNameRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'First name is required.'**
  String get usersFirstNameRequired;

  /// No description provided for @usersFirstNameMax.
  ///
  /// In en_IN, this message translates to:
  /// **'First name must be 100 characters or fewer.'**
  String get usersFirstNameMax;

  /// No description provided for @usersLastNameLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Last Name'**
  String get usersLastNameLabel;

  /// No description provided for @usersLastNameRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Last name is required.'**
  String get usersLastNameRequired;

  /// No description provided for @usersLastNameMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Last name must be 100 characters or fewer.'**
  String get usersLastNameMax;

  /// No description provided for @usersEmailLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Email'**
  String get usersEmailLabel;

  /// No description provided for @usersEmailRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Email is required.'**
  String get usersEmailRequired;

  /// No description provided for @usersEmailMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Email must be 256 characters or fewer.'**
  String get usersEmailMax;

  /// No description provided for @usersPhoneLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Mobile Number'**
  String get usersPhoneLabel;

  /// No description provided for @usersPhoneRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Mobile number is required.'**
  String get usersPhoneRequired;

  /// No description provided for @usersPhoneMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Mobile number must be 32 characters or fewer.'**
  String get usersPhoneMax;

  /// No description provided for @usersPhoneInvalid.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter a valid phone number.'**
  String get usersPhoneInvalid;

  /// No description provided for @usersConfirmPasswordLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Confirm Password'**
  String get usersConfirmPasswordLabel;

  /// No description provided for @usersConfirmPasswordRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Confirm password is required.'**
  String get usersConfirmPasswordRequired;

  /// No description provided for @usersPasswordMismatch.
  ///
  /// In en_IN, this message translates to:
  /// **'Password and confirm password must match.'**
  String get usersPasswordMismatch;

  /// No description provided for @usersPasswordMin.
  ///
  /// In en_IN, this message translates to:
  /// **'Password must be at least 8 characters.'**
  String get usersPasswordMin;

  /// No description provided for @usersPasswordMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Password must be 100 characters or fewer.'**
  String get usersPasswordMax;

  /// No description provided for @usersRoleLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Role'**
  String get usersRoleLabel;

  /// No description provided for @usersRoleOwner.
  ///
  /// In en_IN, this message translates to:
  /// **'Owner'**
  String get usersRoleOwner;

  /// No description provided for @usersRoleManager.
  ///
  /// In en_IN, this message translates to:
  /// **'Manager'**
  String get usersRoleManager;

  /// No description provided for @usersRoleStaff.
  ///
  /// In en_IN, this message translates to:
  /// **'Staff'**
  String get usersRoleStaff;

  /// No description provided for @usersAllowLoginLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Allow this user to login'**
  String get usersAllowLoginLabel;

  /// No description provided for @usersLoginEnabled.
  ///
  /// In en_IN, this message translates to:
  /// **'Enabled'**
  String get usersLoginEnabled;

  /// No description provided for @usersLoginDisabled.
  ///
  /// In en_IN, this message translates to:
  /// **'Disabled'**
  String get usersLoginDisabled;

  /// No description provided for @usersAddSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'User added successfully.'**
  String get usersAddSuccess;

  /// No description provided for @usersEditSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'User updated successfully.'**
  String get usersEditSuccess;

  /// No description provided for @usersErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get usersErrorNetwork;

  /// No description provided for @usersErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get usersErrorTimeout;

  /// No description provided for @usersErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get usersErrorUnauthorized;

  /// No description provided for @usersErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to manage users.'**
  String get usersErrorForbidden;

  /// No description provided for @usersErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load users. Please try again.'**
  String get usersErrorGeneric;

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

  /// No description provided for @suppliersEmptyDescription.
  ///
  /// In en_IN, this message translates to:
  /// **'Suppliers you add will appear here with contact details and payables.'**
  String get suppliersEmptyDescription;

  /// No description provided for @suppliersSummaryCount.
  ///
  /// In en_IN, this message translates to:
  /// **'{count} suppliers'**
  String suppliersSummaryCount(int count);

  /// No description provided for @suppliersSummaryActive.
  ///
  /// In en_IN, this message translates to:
  /// **'{count} active'**
  String suppliersSummaryActive(int count);

  /// No description provided for @suppliersSummaryPreferred.
  ///
  /// In en_IN, this message translates to:
  /// **'{count} preferred'**
  String suppliersSummaryPreferred(int count);

  /// No description provided for @suppliersSummaryPayable.
  ///
  /// In en_IN, this message translates to:
  /// **'{amount} payable'**
  String suppliersSummaryPayable(String amount);

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

  /// No description provided for @inventoryMenuTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Inventory actions'**
  String get inventoryMenuTitle;

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

  /// No description provided for @shopsManageSelectShopTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Manage Shop'**
  String get shopsManageSelectShopTitle;

  /// No description provided for @shopsManageSelectShopLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Select Shop'**
  String get shopsManageSelectShopLabel;

  /// No description provided for @shopsManageSelectShopRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Please select a shop.'**
  String get shopsManageSelectShopRequired;

  /// No description provided for @shopsManageSelectShopHint.
  ///
  /// In en_IN, this message translates to:
  /// **'Choose a shop to update its details.'**
  String get shopsManageSelectShopHint;

  /// No description provided for @shopsManageEditDetailsTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Edit Shop Details'**
  String get shopsManageEditDetailsTitle;

  /// No description provided for @shopsManageSuccessTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Shop Updated'**
  String get shopsManageSuccessTitle;

  /// No description provided for @shopsManageSuccessDefaultShopName.
  ///
  /// In en_IN, this message translates to:
  /// **'Shop'**
  String get shopsManageSuccessDefaultShopName;

  /// No description provided for @shopsManageSuccessMessage.
  ///
  /// In en_IN, this message translates to:
  /// **'Your shop \"{shopName}\" has been updated.'**
  String shopsManageSuccessMessage(String shopName);

  /// No description provided for @shopsManageErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Something went wrong. Please try again.'**
  String get shopsManageErrorGeneric;

  /// No description provided for @shopsManageErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'You are not authorized.'**
  String get shopsManageErrorUnauthorized;

  /// No description provided for @shopsManageErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission.'**
  String get shopsManageErrorForbidden;

  /// No description provided for @shopsManageErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Network error. Please try again.'**
  String get shopsManageErrorNetwork;

  /// No description provided for @shopsManageErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get shopsManageErrorTimeout;

  /// No description provided for @dashboardSubtitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Operational snapshot for your active shop.'**
  String get dashboardSubtitle;

  /// No description provided for @dashboardGreetingMorning.
  ///
  /// In en_IN, this message translates to:
  /// **'Good morning, {shopName}'**
  String dashboardGreetingMorning(String shopName);

  /// No description provided for @dashboardGreetingAfternoon.
  ///
  /// In en_IN, this message translates to:
  /// **'Good afternoon, {shopName}'**
  String dashboardGreetingAfternoon(String shopName);

  /// No description provided for @dashboardGreetingEvening.
  ///
  /// In en_IN, this message translates to:
  /// **'Good evening, {shopName}'**
  String dashboardGreetingEvening(String shopName);

  /// No description provided for @dashboardRangeLast7Days.
  ///
  /// In en_IN, this message translates to:
  /// **'Last 7 days'**
  String get dashboardRangeLast7Days;

  /// No description provided for @dashboardRangeLast30Days.
  ///
  /// In en_IN, this message translates to:
  /// **'Last 30 days'**
  String get dashboardRangeLast30Days;

  /// No description provided for @dashboardRangeCustom.
  ///
  /// In en_IN, this message translates to:
  /// **'Custom'**
  String get dashboardRangeCustom;

  /// No description provided for @dashboardRangeFrom.
  ///
  /// In en_IN, this message translates to:
  /// **'From'**
  String get dashboardRangeFrom;

  /// No description provided for @dashboardRangeTo.
  ///
  /// In en_IN, this message translates to:
  /// **'To'**
  String get dashboardRangeTo;

  /// No description provided for @dashboardKpiSalesRevenue.
  ///
  /// In en_IN, this message translates to:
  /// **'Sales Revenue'**
  String get dashboardKpiSalesRevenue;

  /// No description provided for @dashboardKpiNetProfit.
  ///
  /// In en_IN, this message translates to:
  /// **'Net Profit'**
  String get dashboardKpiNetProfit;

  /// No description provided for @dashboardKpiInvoiceCount.
  ///
  /// In en_IN, this message translates to:
  /// **'Invoices'**
  String get dashboardKpiInvoiceCount;

  /// No description provided for @dashboardKpiLowStockItems.
  ///
  /// In en_IN, this message translates to:
  /// **'Low Stock Items'**
  String get dashboardKpiLowStockItems;

  /// No description provided for @dashboardKpiStockValue.
  ///
  /// In en_IN, this message translates to:
  /// **'Stock Value'**
  String get dashboardKpiStockValue;

  /// No description provided for @dashboardKpiCustomerCreditDue.
  ///
  /// In en_IN, this message translates to:
  /// **'Customer Credit Due'**
  String get dashboardKpiCustomerCreditDue;

  /// No description provided for @dashboardKpiSupplierPayables.
  ///
  /// In en_IN, this message translates to:
  /// **'Supplier Payables'**
  String get dashboardKpiSupplierPayables;

  /// No description provided for @dashboardKpiExpenses.
  ///
  /// In en_IN, this message translates to:
  /// **'Expenses'**
  String get dashboardKpiExpenses;

  /// No description provided for @dashboardKpiVsPreviousPeriod.
  ///
  /// In en_IN, this message translates to:
  /// **'vs previous period'**
  String get dashboardKpiVsPreviousPeriod;

  /// No description provided for @dashboardChartSalesTrend.
  ///
  /// In en_IN, this message translates to:
  /// **'Sales Trend'**
  String get dashboardChartSalesTrend;

  /// No description provided for @dashboardChartSalesTrendSubtitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Total sales amount per day for the selected period.'**
  String get dashboardChartSalesTrendSubtitle;

  /// No description provided for @dashboardChartRevenueVsExpenses.
  ///
  /// In en_IN, this message translates to:
  /// **'Revenue vs Expenses'**
  String get dashboardChartRevenueVsExpenses;

  /// No description provided for @dashboardChartRevenueVsExpensesSubtitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Daily cash movement.'**
  String get dashboardChartRevenueVsExpensesSubtitle;

  /// No description provided for @dashboardChartRevenue.
  ///
  /// In en_IN, this message translates to:
  /// **'Revenue'**
  String get dashboardChartRevenue;

  /// No description provided for @dashboardChartExpenses.
  ///
  /// In en_IN, this message translates to:
  /// **'Expenses'**
  String get dashboardChartExpenses;

  /// No description provided for @dashboardNoChartData.
  ///
  /// In en_IN, this message translates to:
  /// **'No chart data for the selected period.'**
  String get dashboardNoChartData;

  /// No description provided for @dashboardLatestSalesEyebrow.
  ///
  /// In en_IN, this message translates to:
  /// **'Recent Activity'**
  String get dashboardLatestSalesEyebrow;

  /// No description provided for @dashboardLatestSalesTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Latest Sales'**
  String get dashboardLatestSalesTitle;

  /// No description provided for @dashboardLatestSalesEmptyTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'No recent sales'**
  String get dashboardLatestSalesEmptyTitle;

  /// No description provided for @dashboardLatestSalesEmptyDescription.
  ///
  /// In en_IN, this message translates to:
  /// **'The latest active-shop sales will appear here.'**
  String get dashboardLatestSalesEmptyDescription;

  /// No description provided for @dashboardLatestSalesViewSales.
  ///
  /// In en_IN, this message translates to:
  /// **'View all sales'**
  String get dashboardLatestSalesViewSales;

  /// No description provided for @dashboardAlertsEyebrow.
  ///
  /// In en_IN, this message translates to:
  /// **'Alerts'**
  String get dashboardAlertsEyebrow;

  /// No description provided for @dashboardAlertsTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Needs Attention'**
  String get dashboardAlertsTitle;

  /// No description provided for @dashboardAlertsEmpty.
  ///
  /// In en_IN, this message translates to:
  /// **'Nothing needs attention right now.'**
  String get dashboardAlertsEmpty;

  /// No description provided for @dashboardAlertsView.
  ///
  /// In en_IN, this message translates to:
  /// **'View'**
  String get dashboardAlertsView;

  /// No description provided for @dashboardQuickActionsTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Quick actions'**
  String get dashboardQuickActionsTitle;

  /// No description provided for @dashboardQuickActionNewSale.
  ///
  /// In en_IN, this message translates to:
  /// **'New sale'**
  String get dashboardQuickActionNewSale;

  /// No description provided for @dashboardQuickActionAddInventory.
  ///
  /// In en_IN, this message translates to:
  /// **'Add inventory'**
  String get dashboardQuickActionAddInventory;

  /// No description provided for @dashboardQuickActionExpenses.
  ///
  /// In en_IN, this message translates to:
  /// **'Expenses'**
  String get dashboardQuickActionExpenses;

  /// No description provided for @dashboardQuickActionProfitLoss.
  ///
  /// In en_IN, this message translates to:
  /// **'Profit & Loss'**
  String get dashboardQuickActionProfitLoss;

  /// No description provided for @dashboardUnableToLoad.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load dashboard'**
  String get dashboardUnableToLoad;

  /// No description provided for @dashboardRetry.
  ///
  /// In en_IN, this message translates to:
  /// **'Retry'**
  String get dashboardRetry;

  /// No description provided for @dashboardNoData.
  ///
  /// In en_IN, this message translates to:
  /// **'No dashboard data available.'**
  String get dashboardNoData;

  /// No description provided for @dashboardAccessRestrictedTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Dashboard is for owners and managers'**
  String get dashboardAccessRestrictedTitle;

  /// No description provided for @dashboardAccessRestrictedBody.
  ///
  /// In en_IN, this message translates to:
  /// **'Your current shop role does not include dashboard access. Open Sales to continue working.'**
  String get dashboardAccessRestrictedBody;

  /// No description provided for @dashboardAccessRestrictedAction.
  ///
  /// In en_IN, this message translates to:
  /// **'Open Sales'**
  String get dashboardAccessRestrictedAction;

  /// No description provided for @dashboardErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get dashboardErrorNetwork;

  /// No description provided for @dashboardErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get dashboardErrorTimeout;

  /// No description provided for @dashboardErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get dashboardErrorUnauthorized;

  /// No description provided for @dashboardErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to view the dashboard.'**
  String get dashboardErrorForbidden;

  /// No description provided for @dashboardErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load dashboard. Please try again.'**
  String get dashboardErrorGeneric;

  /// No description provided for @salesHistoryEyebrow.
  ///
  /// In en_IN, this message translates to:
  /// **'Sales ledger'**
  String get salesHistoryEyebrow;

  /// No description provided for @salesHistoryTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Sales history'**
  String get salesHistoryTitle;

  /// No description provided for @salesHistorySubtitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Review invoices, payment status, and refunds for the selected period.'**
  String get salesHistorySubtitle;

  /// No description provided for @salesHistoryKpiPeriodSales.
  ///
  /// In en_IN, this message translates to:
  /// **'Period sales'**
  String get salesHistoryKpiPeriodSales;

  /// No description provided for @salesHistoryKpiInvoices.
  ///
  /// In en_IN, this message translates to:
  /// **'Invoices'**
  String get salesHistoryKpiInvoices;

  /// No description provided for @salesHistoryKpiRefunds.
  ///
  /// In en_IN, this message translates to:
  /// **'Refunds'**
  String get salesHistoryKpiRefunds;

  /// No description provided for @salesHistoryControlsStatus.
  ///
  /// In en_IN, this message translates to:
  /// **'Status'**
  String get salesHistoryControlsStatus;

  /// No description provided for @salesHistoryControlsSearchPlaceholder.
  ///
  /// In en_IN, this message translates to:
  /// **'Search invoice, customer, or phone...'**
  String get salesHistoryControlsSearchPlaceholder;

  /// No description provided for @salesHistoryControlsClearFilters.
  ///
  /// In en_IN, this message translates to:
  /// **'Clear'**
  String get salesHistoryControlsClearFilters;

  /// No description provided for @salesHistoryStatusAll.
  ///
  /// In en_IN, this message translates to:
  /// **'All'**
  String get salesHistoryStatusAll;

  /// No description provided for @salesHistoryStatusPaid.
  ///
  /// In en_IN, this message translates to:
  /// **'Paid'**
  String get salesHistoryStatusPaid;

  /// No description provided for @salesHistoryStatusPartiallyPaid.
  ///
  /// In en_IN, this message translates to:
  /// **'Partially paid'**
  String get salesHistoryStatusPartiallyPaid;

  /// No description provided for @salesHistoryStatusRefunded.
  ///
  /// In en_IN, this message translates to:
  /// **'Refunded'**
  String get salesHistoryStatusRefunded;

  /// No description provided for @salesHistoryStatusReturned.
  ///
  /// In en_IN, this message translates to:
  /// **'Returned'**
  String get salesHistoryStatusReturned;

  /// No description provided for @salesHistoryStatusUnknown.
  ///
  /// In en_IN, this message translates to:
  /// **'Unknown'**
  String get salesHistoryStatusUnknown;

  /// No description provided for @salesHistoryPaymentCash.
  ///
  /// In en_IN, this message translates to:
  /// **'Cash'**
  String get salesHistoryPaymentCash;

  /// No description provided for @salesHistoryPaymentUpi.
  ///
  /// In en_IN, this message translates to:
  /// **'UPI'**
  String get salesHistoryPaymentUpi;

  /// No description provided for @salesHistoryPaymentCard.
  ///
  /// In en_IN, this message translates to:
  /// **'Card'**
  String get salesHistoryPaymentCard;

  /// No description provided for @salesHistoryPaymentCredit.
  ///
  /// In en_IN, this message translates to:
  /// **'Credit'**
  String get salesHistoryPaymentCredit;

  /// No description provided for @salesHistoryPaymentUnknown.
  ///
  /// In en_IN, this message translates to:
  /// **'Unknown'**
  String get salesHistoryPaymentUnknown;

  /// No description provided for @salesHistoryWalkInCustomer.
  ///
  /// In en_IN, this message translates to:
  /// **'Walk-in customer'**
  String get salesHistoryWalkInCustomer;

  /// No description provided for @salesHistoryNoSales.
  ///
  /// In en_IN, this message translates to:
  /// **'No sales found'**
  String get salesHistoryNoSales;

  /// No description provided for @salesHistoryNoSalesDescription.
  ///
  /// In en_IN, this message translates to:
  /// **'Try adjusting the date range, status filter, or search terms.'**
  String get salesHistoryNoSalesDescription;

  /// No description provided for @salesHistoryUnableToLoad.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load sales history'**
  String get salesHistoryUnableToLoad;

  /// No description provided for @salesDetailUnableToLoad.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load sale details'**
  String get salesDetailUnableToLoad;

  /// No description provided for @salesHistoryRetry.
  ///
  /// In en_IN, this message translates to:
  /// **'Retry'**
  String get salesHistoryRetry;

  /// No description provided for @salesHistoryInvoiceNumber.
  ///
  /// In en_IN, this message translates to:
  /// **'Invoice'**
  String get salesHistoryInvoiceNumber;

  /// No description provided for @salesHistoryCustomer.
  ///
  /// In en_IN, this message translates to:
  /// **'Customer'**
  String get salesHistoryCustomer;

  /// No description provided for @salesHistoryPhone.
  ///
  /// In en_IN, this message translates to:
  /// **'Phone'**
  String get salesHistoryPhone;

  /// No description provided for @salesHistoryDate.
  ///
  /// In en_IN, this message translates to:
  /// **'Date'**
  String get salesHistoryDate;

  /// No description provided for @salesDetailLineItems.
  ///
  /// In en_IN, this message translates to:
  /// **'Line items'**
  String get salesDetailLineItems;

  /// No description provided for @salesDetailTotals.
  ///
  /// In en_IN, this message translates to:
  /// **'Totals'**
  String get salesDetailTotals;

  /// No description provided for @salesDetailDiscounts.
  ///
  /// In en_IN, this message translates to:
  /// **'Discounts'**
  String get salesDetailDiscounts;

  /// No description provided for @salesDetailPaymentSplit.
  ///
  /// In en_IN, this message translates to:
  /// **'Payment split'**
  String get salesDetailPaymentSplit;

  /// No description provided for @salesDetailReturns.
  ///
  /// In en_IN, this message translates to:
  /// **'Returns'**
  String get salesDetailReturns;

  /// No description provided for @salesDetailRedemptions.
  ///
  /// In en_IN, this message translates to:
  /// **'Redemptions'**
  String get salesDetailRedemptions;

  /// No description provided for @salesDetailWarnings.
  ///
  /// In en_IN, this message translates to:
  /// **'Warnings'**
  String get salesDetailWarnings;

  /// No description provided for @salesHistoryPaymentMethod.
  ///
  /// In en_IN, this message translates to:
  /// **'Payment'**
  String get salesHistoryPaymentMethod;

  /// No description provided for @salesDetailNoLineItems.
  ///
  /// In en_IN, this message translates to:
  /// **'No line items'**
  String get salesDetailNoLineItems;

  /// No description provided for @salesDetailNoDiscounts.
  ///
  /// In en_IN, this message translates to:
  /// **'No discounts'**
  String get salesDetailNoDiscounts;

  /// No description provided for @salesDetailNoSettlementRecords.
  ///
  /// In en_IN, this message translates to:
  /// **'No settlement records'**
  String get salesDetailNoSettlementRecords;

  /// No description provided for @salesDetailNoReturns.
  ///
  /// In en_IN, this message translates to:
  /// **'No returns'**
  String get salesDetailNoReturns;

  /// No description provided for @salesDetailNoRedemptions.
  ///
  /// In en_IN, this message translates to:
  /// **'No redemptions'**
  String get salesDetailNoRedemptions;

  /// No description provided for @salesDetailNoWarnings.
  ///
  /// In en_IN, this message translates to:
  /// **'No warnings'**
  String get salesDetailNoWarnings;

  /// No description provided for @salesDetailReceipt.
  ///
  /// In en_IN, this message translates to:
  /// **'Receipt'**
  String get salesDetailReceipt;

  /// No description provided for @salesReceiptCreditNoteApplied.
  ///
  /// In en_IN, this message translates to:
  /// **'Credit note applied'**
  String get salesReceiptCreditNoteApplied;

  /// No description provided for @salesDetailBeforeDiscount.
  ///
  /// In en_IN, this message translates to:
  /// **'Before discount'**
  String get salesDetailBeforeDiscount;

  /// No description provided for @salesDetailDiscountLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Discount'**
  String get salesDetailDiscountLabel;

  /// No description provided for @salesDetailTaxLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Tax'**
  String get salesDetailTaxLabel;

  /// No description provided for @salesDetailPaidLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Paid'**
  String get salesDetailPaidLabel;

  /// No description provided for @salesDetailDueReduction.
  ///
  /// In en_IN, this message translates to:
  /// **'Due reduction'**
  String get salesDetailDueReduction;

  /// No description provided for @salesDetailVoidReturnAction.
  ///
  /// In en_IN, this message translates to:
  /// **'Void return'**
  String get salesDetailVoidReturnAction;

  /// No description provided for @salesDetailVoidReturnReason.
  ///
  /// In en_IN, this message translates to:
  /// **'Reason'**
  String get salesDetailVoidReturnReason;

  /// No description provided for @salesDetailVoidReturnReasonRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Reason is required.'**
  String get salesDetailVoidReturnReasonRequired;

  /// No description provided for @salesDetailVoidReturnFailed.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to void return.'**
  String get salesDetailVoidReturnFailed;

  /// No description provided for @salesDetailVoidReturnUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get salesDetailVoidReturnUnauthorized;

  /// No description provided for @salesDetailVoidReturnForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to void returns.'**
  String get salesDetailVoidReturnForbidden;

  /// No description provided for @salesDetailVoidReturnNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get salesDetailVoidReturnNetwork;

  /// No description provided for @salesDetailVoidReturnTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get salesDetailVoidReturnTimeout;

  /// No description provided for @salesDetailVoidReturnDate.
  ///
  /// In en_IN, this message translates to:
  /// **'Voided on'**
  String get salesDetailVoidReturnDate;

  /// No description provided for @salesDetailReturnVoided.
  ///
  /// In en_IN, this message translates to:
  /// **'Voided'**
  String get salesDetailReturnVoided;

  /// No description provided for @salesDetailReturnItem.
  ///
  /// In en_IN, this message translates to:
  /// **'{quantity} returned of {itemName}'**
  String salesDetailReturnItem(double quantity, String itemName);

  /// No description provided for @salesHistoryItemCountLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Items'**
  String get salesHistoryItemCountLabel;

  /// No description provided for @salesHistoryTotal.
  ///
  /// In en_IN, this message translates to:
  /// **'Total'**
  String get salesHistoryTotal;

  /// No description provided for @salesHistoryDueAmount.
  ///
  /// In en_IN, this message translates to:
  /// **'Due'**
  String get salesHistoryDueAmount;

  /// No description provided for @salesHistoryRefundAmount.
  ///
  /// In en_IN, this message translates to:
  /// **'Refund'**
  String get salesHistoryRefundAmount;

  /// No description provided for @salesHistoryDetailTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Sale details'**
  String get salesHistoryDetailTitle;

  /// No description provided for @salesHistoryErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load sales history. Please try again.'**
  String get salesHistoryErrorGeneric;

  /// No description provided for @salesHistoryErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get salesHistoryErrorNetwork;

  /// No description provided for @salesHistoryErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get salesHistoryErrorTimeout;

  /// No description provided for @salesHistoryErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get salesHistoryErrorUnauthorized;

  /// No description provided for @salesHistoryErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to view sales history.'**
  String get salesHistoryErrorForbidden;

  /// No description provided for @salesHistoryItemCount.
  ///
  /// In en_IN, this message translates to:
  /// **'{count} items'**
  String salesHistoryItemCount(int count);

  /// No description provided for @salesHistoryDateRangeLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'{from} – {to}'**
  String salesHistoryDateRangeLabel(String from, String to);

  /// No description provided for @salesHistoryShowingCount.
  ///
  /// In en_IN, this message translates to:
  /// **'Showing {shown} of {total}'**
  String salesHistoryShowingCount(int shown, int total);

  /// No description provided for @discountsTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Discount rules'**
  String get discountsTitle;

  /// No description provided for @discountsSubtitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Manage active promotion rules for products and sales.'**
  String get discountsSubtitle;

  /// No description provided for @discountsSearchPlaceholder.
  ///
  /// In en_IN, this message translates to:
  /// **'Search rule name...'**
  String get discountsSearchPlaceholder;

  /// No description provided for @discountsClearSearch.
  ///
  /// In en_IN, this message translates to:
  /// **'Clear search'**
  String get discountsClearSearch;

  /// No description provided for @discountsStatusAll.
  ///
  /// In en_IN, this message translates to:
  /// **'All'**
  String get discountsStatusAll;

  /// No description provided for @discountsStatusActive.
  ///
  /// In en_IN, this message translates to:
  /// **'Active'**
  String get discountsStatusActive;

  /// No description provided for @discountsStatusUpcoming.
  ///
  /// In en_IN, this message translates to:
  /// **'Upcoming'**
  String get discountsStatusUpcoming;

  /// No description provided for @discountsStatusExpired.
  ///
  /// In en_IN, this message translates to:
  /// **'Expired'**
  String get discountsStatusExpired;

  /// No description provided for @discountsStatusDisabled.
  ///
  /// In en_IN, this message translates to:
  /// **'Disabled'**
  String get discountsStatusDisabled;

  /// No description provided for @discountsTypeAll.
  ///
  /// In en_IN, this message translates to:
  /// **'All types'**
  String get discountsTypeAll;

  /// No description provided for @discountsTypeBatch.
  ///
  /// In en_IN, this message translates to:
  /// **'Batch percentage'**
  String get discountsTypeBatch;

  /// No description provided for @discountsTypeSalePercent.
  ///
  /// In en_IN, this message translates to:
  /// **'Sales percentage'**
  String get discountsTypeSalePercent;

  /// No description provided for @discountsTypeSaleThresholdPercent.
  ///
  /// In en_IN, this message translates to:
  /// **'Sales threshold percentage'**
  String get discountsTypeSaleThresholdPercent;

  /// No description provided for @discountsSortLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Sort'**
  String get discountsSortLabel;

  /// No description provided for @discountsSortCreatedDesc.
  ///
  /// In en_IN, this message translates to:
  /// **'Created (new first)'**
  String get discountsSortCreatedDesc;

  /// No description provided for @discountsSortCreatedAsc.
  ///
  /// In en_IN, this message translates to:
  /// **'Created (old first)'**
  String get discountsSortCreatedAsc;

  /// No description provided for @discountsSortNameAsc.
  ///
  /// In en_IN, this message translates to:
  /// **'Name (A-Z)'**
  String get discountsSortNameAsc;

  /// No description provided for @discountsSortNameDesc.
  ///
  /// In en_IN, this message translates to:
  /// **'Name (Z-A)'**
  String get discountsSortNameDesc;

  /// No description provided for @discountsSortStartsAtAsc.
  ///
  /// In en_IN, this message translates to:
  /// **'Starts at (soonest)'**
  String get discountsSortStartsAtAsc;

  /// No description provided for @discountsSortStartsAtDesc.
  ///
  /// In en_IN, this message translates to:
  /// **'Starts at (latest)'**
  String get discountsSortStartsAtDesc;

  /// No description provided for @discountsSortStatus.
  ///
  /// In en_IN, this message translates to:
  /// **'Status'**
  String get discountsSortStatus;

  /// No description provided for @discountsNoRules.
  ///
  /// In en_IN, this message translates to:
  /// **'No discount rules found'**
  String get discountsNoRules;

  /// No description provided for @discountsUnableToLoad.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load discount rules'**
  String get discountsUnableToLoad;

  /// No description provided for @discountsRetry.
  ///
  /// In en_IN, this message translates to:
  /// **'Retry'**
  String get discountsRetry;

  /// No description provided for @discountsShowingCount.
  ///
  /// In en_IN, this message translates to:
  /// **'Showing {shown} of {total}'**
  String discountsShowingCount(int shown, int total);

  /// No description provided for @discountsRuleTypeLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Type:'**
  String get discountsRuleTypeLabel;

  /// No description provided for @discountsPercentageLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Discount:'**
  String get discountsPercentageLabel;

  /// No description provided for @discountsThresholdLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Threshold:'**
  String get discountsThresholdLabel;

  /// No description provided for @discountsStatusLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Status:'**
  String get discountsStatusLabel;

  /// No description provided for @discountsInventoryBatchIdLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Inventory batch:'**
  String get discountsInventoryBatchIdLabel;

  /// No description provided for @discountsNotSet.
  ///
  /// In en_IN, this message translates to:
  /// **'Not set'**
  String get discountsNotSet;

  /// No description provided for @discountsErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get discountsErrorNetwork;

  /// No description provided for @discountsErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get discountsErrorTimeout;

  /// No description provided for @discountsErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get discountsErrorUnauthorized;

  /// No description provided for @discountsErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to view discounts.'**
  String get discountsErrorForbidden;

  /// No description provided for @discountsErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load discount rules. Please try again.'**
  String get discountsErrorGeneric;

  /// No description provided for @discountsUnableToLoadDetail.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to load discount details'**
  String get discountsUnableToLoadDetail;

  /// No description provided for @discountsDetailTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Discount rule details'**
  String get discountsDetailTitle;

  /// No description provided for @discountsDetailRuleLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Rule'**
  String get discountsDetailRuleLabel;

  /// No description provided for @discountsDescriptionLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Description:'**
  String get discountsDescriptionLabel;

  /// No description provided for @discountsBelowCostReasonLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Below-cost confirmation:'**
  String get discountsBelowCostReasonLabel;

  /// No description provided for @discountsStartsAtLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Starts at:'**
  String get discountsStartsAtLabel;

  /// No description provided for @discountsEndsAtLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Ends at:'**
  String get discountsEndsAtLabel;

  /// No description provided for @discountsCreatedAtLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Created at:'**
  String get discountsCreatedAtLabel;

  /// No description provided for @discountsUpdatedAtLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Updated at:'**
  String get discountsUpdatedAtLabel;

  /// No description provided for @discountsDisabledAtLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Disabled at:'**
  String get discountsDisabledAtLabel;

  /// No description provided for @discountsDisabledReasonLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Disable reason:'**
  String get discountsDisabledReasonLabel;

  /// No description provided for @discountsReplacesRuleLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Replaces rule:'**
  String get discountsReplacesRuleLabel;

  /// No description provided for @discountsReplacedByRuleLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Replaced by:'**
  String get discountsReplacedByRuleLabel;

  /// No description provided for @creditNotesVerifyCode.
  ///
  /// In en_IN, this message translates to:
  /// **'Verify code'**
  String get creditNotesVerifyCode;

  /// No description provided for @creditNotesFilterAll.
  ///
  /// In en_IN, this message translates to:
  /// **'All'**
  String get creditNotesFilterAll;

  /// No description provided for @creditNotesLoadMore.
  ///
  /// In en_IN, this message translates to:
  /// **'Load more'**
  String get creditNotesLoadMore;

  /// No description provided for @creditNotesEmpty.
  ///
  /// In en_IN, this message translates to:
  /// **'No credit notes found'**
  String get creditNotesEmpty;

  /// No description provided for @creditNotesNoExpiry.
  ///
  /// In en_IN, this message translates to:
  /// **'No expiry'**
  String get creditNotesNoExpiry;

  /// No description provided for @creditNotesClose.
  ///
  /// In en_IN, this message translates to:
  /// **'Close'**
  String get creditNotesClose;

  /// No description provided for @creditNotesVoidReason.
  ///
  /// In en_IN, this message translates to:
  /// **'Reason'**
  String get creditNotesVoidReason;

  /// No description provided for @creditNotesOpenReceipt.
  ///
  /// In en_IN, this message translates to:
  /// **'Open receipt'**
  String get creditNotesOpenReceipt;

  /// No description provided for @creditNotesReceiptTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Credit note receipt'**
  String get creditNotesReceiptTitle;

  /// No description provided for @creditNotesStatusLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Status:'**
  String get creditNotesStatusLabel;

  /// No description provided for @creditNotesReceiptCodeLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Code:'**
  String get creditNotesReceiptCodeLabel;

  /// No description provided for @creditNotesReceiptIssueDateLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Issued at:'**
  String get creditNotesReceiptIssueDateLabel;

  /// No description provided for @creditNotesReceiptExpiryDateLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Expires at:'**
  String get creditNotesReceiptExpiryDateLabel;

  /// No description provided for @creditNotesReceiptCustomerLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Customer:'**
  String get creditNotesReceiptCustomerLabel;

  /// No description provided for @creditNotesReceiptReasonLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Reason:'**
  String get creditNotesReceiptReasonLabel;

  /// No description provided for @creditNotesReceiptVoidReasonLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Void reason:'**
  String get creditNotesReceiptVoidReasonLabel;

  /// No description provided for @creditNotesReceiptStatusActive.
  ///
  /// In en_IN, this message translates to:
  /// **'Active'**
  String get creditNotesReceiptStatusActive;

  /// No description provided for @creditNotesReceiptStatusExpired.
  ///
  /// In en_IN, this message translates to:
  /// **'Expired'**
  String get creditNotesReceiptStatusExpired;

  /// No description provided for @creditNotesReceiptStatusVoided.
  ///
  /// In en_IN, this message translates to:
  /// **'Voided'**
  String get creditNotesReceiptStatusVoided;

  /// No description provided for @creditNotesOriginalAmountLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Original amount:'**
  String get creditNotesOriginalAmountLabel;

  /// No description provided for @creditNotesVoid.
  ///
  /// In en_IN, this message translates to:
  /// **'Void'**
  String get creditNotesVoid;

  /// No description provided for @creditNotesInvoiceLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Invoice:'**
  String get creditNotesInvoiceLabel;

  /// No description provided for @creditNotesReturnLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Return:'**
  String get creditNotesReturnLabel;

  /// No description provided for @creditNotesBalanceLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Balance:'**
  String get creditNotesBalanceLabel;

  /// No description provided for @creditNotesRetry.
  ///
  /// In en_IN, this message translates to:
  /// **'Retry'**
  String get creditNotesRetry;

  /// No description provided for @salesReturnTitle.
  ///
  /// In en_IN, this message translates to:
  /// **'Record Return'**
  String get salesReturnTitle;

  /// No description provided for @salesReturnNoReturnableLines.
  ///
  /// In en_IN, this message translates to:
  /// **'No returnable lines'**
  String get salesReturnNoReturnableLines;

  /// No description provided for @salesReturnPreview.
  ///
  /// In en_IN, this message translates to:
  /// **'Preview'**
  String get salesReturnPreview;

  /// No description provided for @salesReturnSubmit.
  ///
  /// In en_IN, this message translates to:
  /// **'Submit'**
  String get salesReturnSubmit;

  /// No description provided for @salesReturnSubmitRoleNotice.
  ///
  /// In en_IN, this message translates to:
  /// **'Only owner or manager can record this return.'**
  String get salesReturnSubmitRoleNotice;

  /// No description provided for @salesReturnDestinationRefund.
  ///
  /// In en_IN, this message translates to:
  /// **'Refund payout'**
  String get salesReturnDestinationRefund;

  /// No description provided for @salesReturnDestinationCreditNote.
  ///
  /// In en_IN, this message translates to:
  /// **'Credit note'**
  String get salesReturnDestinationCreditNote;

  /// No description provided for @salesReturnCreditNoteReasonLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Credit note reason'**
  String get salesReturnCreditNoteReasonLabel;

  /// No description provided for @salesReturnCreditNoteExpiryLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Credit note expiry (YYYY-MM-DD)'**
  String get salesReturnCreditNoteExpiryLabel;

  /// No description provided for @salesReturnDueOverrideLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Due reduction override'**
  String get salesReturnDueOverrideLabel;

  /// No description provided for @salesReturnDueOverrideReasonLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Due reduction reason'**
  String get salesReturnDueOverrideReasonLabel;

  /// No description provided for @salesReturnApprovedRefundLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Approved refund'**
  String get salesReturnApprovedRefundLabel;

  /// No description provided for @salesReturnLineQuantityLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Return quantity'**
  String get salesReturnLineQuantityLabel;

  /// No description provided for @salesReturnLineConditionLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Condition'**
  String get salesReturnLineConditionLabel;

  /// No description provided for @salesReturnLineConditionRestockable.
  ///
  /// In en_IN, this message translates to:
  /// **'Restockable'**
  String get salesReturnLineConditionRestockable;

  /// No description provided for @salesReturnLineConditionWastage.
  ///
  /// In en_IN, this message translates to:
  /// **'Wastage'**
  String get salesReturnLineConditionWastage;

  /// No description provided for @salesReturnLineNoteLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Line note'**
  String get salesReturnLineNoteLabel;

  /// No description provided for @salesReturnNotesLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Notes'**
  String get salesReturnNotesLabel;

  /// No description provided for @salesReturnPayoutDestinationLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Payout destination'**
  String get salesReturnPayoutDestinationLabel;

  /// No description provided for @salesReturnPreviewRefundLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Refund'**
  String get salesReturnPreviewRefundLabel;

  /// No description provided for @salesReturnPreviewDueReductionLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Due reduction'**
  String get salesReturnPreviewDueReductionLabel;

  /// No description provided for @salesReturnPreviewPayoutLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Payout'**
  String get salesReturnPreviewPayoutLabel;

  /// No description provided for @salesReturnDueOverrideConfirmLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Confirm due reduction override'**
  String get salesReturnDueOverrideConfirmLabel;
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
