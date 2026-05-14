// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Gujarati (`gu`).
class AppLocalizationsGu extends AppLocalizations {
  AppLocalizationsGu([String locale = 'gu']) : super(locale);

  @override
  String get commonLanguage => 'ભાષા';

  @override
  String get commonCancel => 'રદ કરો';

  @override
  String get commonClose => 'બંધ કરો';

  @override
  String get commonClear => 'સાફ કરો';

  @override
  String get commonActions => 'ક્રિયાઓ';

  @override
  String get commonEdit => 'ફેરફાર કરો';

  @override
  String get commonSave => 'સાચવો';

  @override
  String get commonDone => 'થયું';

  @override
  String get commonSearch => 'શોધો...';

  @override
  String get commonLoading => 'Loading...';

  @override
  String get languageEnIn => 'અંગ્રેજી';

  @override
  String get languageHiIn => 'હિન્દી';

  @override
  String get languageTaIn => 'તમિલ';

  @override
  String get languageTeIn => 'તેલુગુ';

  @override
  String get languageBnIn => 'બંગાળી';

  @override
  String get languageMlIn => 'મલયાળમ';

  @override
  String get languageKnIn => 'કન્નડ';

  @override
  String get languageMrIn => 'મરાઠી';

  @override
  String get languageGuIn => 'ગુજરાતી';

  @override
  String get shellAppName => 'Intelibill';

  @override
  String get shellLogout => 'લૉગઆઉટ';

  @override
  String get shellProfile => 'પ્રોફાઇલ';

  @override
  String get shellLanguage => 'ભાષા';

  @override
  String get shellDashboard => 'ડેશબોર્ડ';

  @override
  String get shellManageInventory => 'ઈન્વેન્ટરી';

  @override
  String get shellManageSales => 'વેચાણ';

  @override
  String get shellNewSale => 'નવી વેચાણ';

  @override
  String get shellManageCustomers => 'ગ્રાહકો';

  @override
  String get shellManageSuppliers => 'પુરવઠાકર્તાઓ સંભાળો';

  @override
  String get shellManageExpenses => 'ખર્ચા';

  @override
  String get shellManageBankAccounts => 'બેંક ખાતા';

  @override
  String get shellManageUsers => 'વપરાશકર્તાઓ સંભાળો';

  @override
  String get shellAddShop => 'દુકાન ઉમેરો';

  @override
  String get shellManageShop => 'દુકાન સંભાળો';

  @override
  String get authLoginNow => 'હવે લૉગિન કરો';

  @override
  String get authPassword => 'પાસવર્ડ';

  @override
  String get authLoginCta => 'લૉગિન';

  @override
  String get authEmailAddress => 'ઈમેલ સરનામું';

  @override
  String get authRememberMe => 'મને યાદ રાખો';

  @override
  String get authForgotPassword => 'પાસવર્ડ ભૂલી ગયા?';

  @override
  String get authValidationEmailInvalid => 'માન્ય ઈમેલ સરનામું દાખલ કરો.';

  @override
  String get authValidationPasswordRequired => 'પાસવર્ડ આવશ્યક છે.';

  @override
  String get authValidationLoginIdentifierRequired =>
      'તમારો ઈમેલ અથવા મોબાઇલ નંબર દાખલ કરો.';

  @override
  String get authValidationEmailRequired => 'ઈમેલની જરૂર છે.';

  @override
  String get customersTitle => 'ગ્રાહકો';

  @override
  String get customersAddCustomer => 'ગ્રાહક ઉમેરો';

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
  String get customersNoCustomersFound => 'કોઈ ગ્રાહકો મળ્યા નથી';

  @override
  String get suppliersTitle => 'પુરવઠાકર્તાઓ';

  @override
  String get suppliersAddSupplier => 'પુરવઠાકર્તા ઉમેરો';

  @override
  String get suppliersNoSuppliersFound => 'કોઈ પુરવઠાકર્તાઓ મળ્યા નથી';

  @override
  String get inventoryTitle => 'ઈન્વેન્ટરી';

  @override
  String get inventoryAddNewProductDescription =>
      'તમારી વર્તમાન સક્રિય દુકાન સાથે જોડાયેલો ઉત્પાદન બનાવો.';

  @override
  String get notFoundTitle => 'પૃષ્ઠ મળ્યું નથી';

  @override
  String get notFoundGoBack => 'પાછા ફરો';
}

/// The translations for Gujarati, as used in India (`gu_IN`).
class AppLocalizationsGuIn extends AppLocalizationsGu {
  AppLocalizationsGuIn() : super('gu_IN');

  @override
  String get commonLanguage => 'ભાષા';

  @override
  String get commonCancel => 'રદ કરો';

  @override
  String get commonClose => 'બંધ કરો';

  @override
  String get commonClear => 'સાફ કરો';

  @override
  String get commonActions => 'ક્રિયાઓ';

  @override
  String get commonEdit => 'ફેરફાર કરો';

  @override
  String get commonSave => 'સાચવો';

  @override
  String get commonDone => 'થયું';

  @override
  String get commonSearch => 'શોધો...';

  @override
  String get commonLoading => 'Loading...';

  @override
  String get languageEnIn => 'અંગ્રેજી';

  @override
  String get languageHiIn => 'હિન્દી';

  @override
  String get languageTaIn => 'તમિલ';

  @override
  String get languageTeIn => 'તેલુગુ';

  @override
  String get languageBnIn => 'બંગાળી';

  @override
  String get languageMlIn => 'મલયાળમ';

  @override
  String get languageKnIn => 'કન્નડ';

  @override
  String get languageMrIn => 'મરાઠી';

  @override
  String get languageGuIn => 'ગુજરાતી';

  @override
  String get shellAppName => 'Intelibill';

  @override
  String get shellLogout => 'લૉગઆઉટ';

  @override
  String get shellProfile => 'પ્રોફાઇલ';

  @override
  String get shellLanguage => 'ભાષા';

  @override
  String get shellDashboard => 'ડેશબોર્ડ';

  @override
  String get shellManageInventory => 'ઈન્વેન્ટરી';

  @override
  String get shellManageSales => 'વેચાણ';

  @override
  String get shellNewSale => 'નવી વેચાણ';

  @override
  String get shellManageCustomers => 'ગ્રાહકો';

  @override
  String get shellManageSuppliers => 'પુરવઠાકર્તાઓ સંભાળો';

  @override
  String get shellManageExpenses => 'ખર્ચા';

  @override
  String get shellManageBankAccounts => 'બેંક ખાતા';

  @override
  String get shellManageUsers => 'વપરાશકર્તાઓ સંભાળો';

  @override
  String get shellAddShop => 'દુકાન ઉમેરો';

  @override
  String get shellManageShop => 'દુકાન સંભાળો';

  @override
  String get authLoginNow => 'હવે લૉગિન કરો';

  @override
  String get authPassword => 'પાસવર્ડ';

  @override
  String get authLoginCta => 'લૉગિન';

  @override
  String get authEmailAddress => 'ઈમેલ સરનામું';

  @override
  String get authRememberMe => 'મને યાદ રાખો';

  @override
  String get authForgotPassword => 'પાસવર્ડ ભૂલી ગયા?';

  @override
  String get authValidationEmailInvalid => 'માન્ય ઈમેલ સરનામું દાખલ કરો.';

  @override
  String get authValidationPasswordRequired => 'પાસવર્ડ આવશ્યક છે.';

  @override
  String get authValidationLoginIdentifierRequired =>
      'તમારો ઈમેલ અથવા મોબાઇલ નંબર દાખલ કરો.';

  @override
  String get authValidationEmailRequired => 'ઈમેલની જરૂર છે.';

  @override
  String get customersTitle => 'ગ્રાહકો';

  @override
  String get customersAddCustomer => 'ગ્રાહક ઉમેરો';

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
  String get customersNoCustomersFound => 'કોઈ ગ્રાહકો મળ્યા નથી';

  @override
  String get suppliersTitle => 'પુરવઠાકર્તાઓ';

  @override
  String get suppliersAddSupplier => 'પુરવઠાકર્તા ઉમેરો';

  @override
  String get suppliersNoSuppliersFound => 'કોઈ પુરવઠાકર્તાઓ મળ્યા નથી';

  @override
  String get inventoryTitle => 'ઈન્વેન્ટરી';

  @override
  String get inventoryAddNewProductDescription =>
      'તમારી વર્તમાન સક્રિય દુકાન સાથે જોડાયેલો ઉત્પાદન બનાવો.';

  @override
  String get notFoundTitle => 'પૃષ્ઠ મળ્યું નથી';

  @override
  String get notFoundGoBack => 'પાછા ફરો';
}
