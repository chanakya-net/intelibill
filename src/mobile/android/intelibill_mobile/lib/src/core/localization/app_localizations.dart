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

  /// No description provided for @customersCreateSuccess.
  ///
  /// In en_IN, this message translates to:
  /// **'Customer created successfully.'**
  String get customersCreateSuccess;

  /// No description provided for @customersCreateNameLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Customer name'**
  String get customersCreateNameLabel;

  /// No description provided for @customersCreatePhoneLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Phone number'**
  String get customersCreatePhoneLabel;

  /// No description provided for @customersCreateAddressLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Address (optional)'**
  String get customersCreateAddressLabel;

  /// No description provided for @customersCreateActiveLabel.
  ///
  /// In en_IN, this message translates to:
  /// **'Active customer'**
  String get customersCreateActiveLabel;

  /// No description provided for @customersCreateNameRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Name is required.'**
  String get customersCreateNameRequired;

  /// No description provided for @customersCreateNameMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Name must be 180 characters or fewer.'**
  String get customersCreateNameMax;

  /// No description provided for @customersCreatePhoneRequired.
  ///
  /// In en_IN, this message translates to:
  /// **'Phone number is required.'**
  String get customersCreatePhoneRequired;

  /// No description provided for @customersCreatePhoneMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Phone number must be 32 characters or fewer.'**
  String get customersCreatePhoneMax;

  /// No description provided for @customersCreatePhoneInvalid.
  ///
  /// In en_IN, this message translates to:
  /// **'Enter a valid phone number.'**
  String get customersCreatePhoneInvalid;

  /// No description provided for @customersCreateAddressMax.
  ///
  /// In en_IN, this message translates to:
  /// **'Address must be 320 characters or fewer.'**
  String get customersCreateAddressMax;

  /// No description provided for @customersCreateErrorNetwork.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to connect. Please check your network.'**
  String get customersCreateErrorNetwork;

  /// No description provided for @customersCreateErrorTimeout.
  ///
  /// In en_IN, this message translates to:
  /// **'Request timed out. Please try again.'**
  String get customersCreateErrorTimeout;

  /// No description provided for @customersCreateErrorUnauthorized.
  ///
  /// In en_IN, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get customersCreateErrorUnauthorized;

  /// No description provided for @customersCreateErrorForbidden.
  ///
  /// In en_IN, this message translates to:
  /// **'You do not have permission to create customers.'**
  String get customersCreateErrorForbidden;

  /// No description provided for @customersCreateErrorGeneric.
  ///
  /// In en_IN, this message translates to:
  /// **'Unable to create customer. Please try again.'**
  String get customersCreateErrorGeneric;

  /// No description provided for @customersNoCustomersFound.
  ///
  /// In en_IN, this message translates to:
  /// **'No customers found'**
  String get customersNoCustomersFound;

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
