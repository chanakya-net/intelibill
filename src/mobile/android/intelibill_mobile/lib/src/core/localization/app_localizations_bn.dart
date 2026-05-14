// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Bengali Bangla (`bn`).
class AppLocalizationsBn extends AppLocalizations {
  AppLocalizationsBn([String locale = 'bn']) : super(locale);

  @override
  String get commonLanguage => 'ভাষা';

  @override
  String get commonCancel => 'বাতিল';

  @override
  String get commonClose => 'বন্ধ করুন';

  @override
  String get commonClear => 'পরিষ্কার করুন';

  @override
  String get commonActions => 'কার্যক্রম';

  @override
  String get commonEdit => 'সম্পাদনা';

  @override
  String get commonSave => 'সংরক্ষণ করুন';

  @override
  String get commonDone => 'সম্পন্ন';

  @override
  String get commonSearch => 'অনুসন্ধান করুন...';

  @override
  String get commonLoading => 'Loading...';

  @override
  String get languageEnIn => 'ইংরেজি';

  @override
  String get languageHiIn => 'হিন্দি';

  @override
  String get languageTaIn => 'তামিল';

  @override
  String get languageTeIn => 'তেলুগু';

  @override
  String get languageBnIn => 'বাংলা';

  @override
  String get languageMlIn => 'মালয়ালম';

  @override
  String get languageKnIn => 'কন্নড়';

  @override
  String get languageMrIn => 'মারাঠি';

  @override
  String get languageGuIn => 'গুজরাটি';

  @override
  String get shellAppName => 'Intelibill';

  @override
  String get shellLogout => 'লগআউট';

  @override
  String get shellProfile => 'প্রোফাইল';

  @override
  String get shellLanguage => 'ভাষা';

  @override
  String get shellDashboard => 'ড্যাশবোর্ড';

  @override
  String get shellManageInventory => 'ইনভেন্টরি';

  @override
  String get shellManageSales => 'বিক্রয়';

  @override
  String get shellNewSale => 'নতুন বিক্রয়';

  @override
  String get shellManageCustomers => 'গ্রাহকরা';

  @override
  String get shellManageSuppliers => 'সরবরাহকারী ব্যবস্থাপনা';

  @override
  String get shellManageExpenses => 'খরচ';

  @override
  String get shellManageBankAccounts => 'ব্যাঙ্ক অ্যাকাউন্ট';

  @override
  String get shellManageUsers => 'ব্যবহারকারী ব্যবস্থাপনা';

  @override
  String get shellAddShop => 'দোকান যোগ করুন';

  @override
  String get shellManageShop => 'দোকান ব্যবস্থাপনা';

  @override
  String get authLoginNow => 'এখনই লগইন করুন';

  @override
  String get authPassword => 'পাসওয়ার্ড';

  @override
  String get authLoginCta => 'লগইন';

  @override
  String get authEmailAddress => 'ইমেল ঠিকানা';

  @override
  String get authRememberMe => 'আমাকে মনে রাখুন';

  @override
  String get authForgotPassword => 'পাসওয়ার্ড ভুলে গেছেন?';

  @override
  String get authValidationEmailInvalid => 'সঠিক ইমেল ঠিকানা লিখুন।';

  @override
  String get authValidationPasswordRequired => 'পাসওয়ার্ড আবশ্যক।';

  @override
  String get authValidationLoginIdentifierRequired =>
      'আপনার ইমেল বা মোবাইল নম্বর লিখুন.';

  @override
  String get authValidationEmailRequired => 'ইমেইল প্রয়োজন।';

  @override
  String get customersTitle => 'গ্রাহক';

  @override
  String get customersAddCustomer => 'গ্রাহক যোগ করুন';

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
  String get customersNoCustomersFound => 'কোনো গ্রাহক পাওয়া যায়নি';

  @override
  String get suppliersTitle => 'সরবরাহকারী';

  @override
  String get suppliersAddSupplier => 'সরবরাহকারী যোগ করুন';

  @override
  String get suppliersNoSuppliersFound => 'কোনো সরবরাহকারী পাওয়া যায়নি';

  @override
  String get inventoryTitle => 'ইনভেন্টরি';

  @override
  String get inventoryAddNewProductDescription =>
      'বর্তমান সক্রিয় শপের সাথে সংযুক্ত একটি পণ্য তৈরি করুন।';

  @override
  String get notFoundTitle => 'পৃষ্ঠা পাওয়া যায়নি';

  @override
  String get notFoundGoBack => 'ফিরে যান';
}

/// The translations for Bengali Bangla, as used in India (`bn_IN`).
class AppLocalizationsBnIn extends AppLocalizationsBn {
  AppLocalizationsBnIn() : super('bn_IN');

  @override
  String get commonLanguage => 'ভাষা';

  @override
  String get commonCancel => 'বাতিল';

  @override
  String get commonClose => 'বন্ধ করুন';

  @override
  String get commonClear => 'পরিষ্কার করুন';

  @override
  String get commonActions => 'কার্যক্রম';

  @override
  String get commonEdit => 'সম্পাদনা';

  @override
  String get commonSave => 'সংরক্ষণ করুন';

  @override
  String get commonDone => 'সম্পন্ন';

  @override
  String get commonSearch => 'অনুসন্ধান করুন...';

  @override
  String get commonLoading => 'Loading...';

  @override
  String get languageEnIn => 'ইংরেজি';

  @override
  String get languageHiIn => 'হিন্দি';

  @override
  String get languageTaIn => 'তামিল';

  @override
  String get languageTeIn => 'তেলুগু';

  @override
  String get languageBnIn => 'বাংলা';

  @override
  String get languageMlIn => 'মালয়ালম';

  @override
  String get languageKnIn => 'কন্নড়';

  @override
  String get languageMrIn => 'মারাঠি';

  @override
  String get languageGuIn => 'গুজরাটি';

  @override
  String get shellAppName => 'Intelibill';

  @override
  String get shellLogout => 'লগআউট';

  @override
  String get shellProfile => 'প্রোফাইল';

  @override
  String get shellLanguage => 'ভাষা';

  @override
  String get shellDashboard => 'ড্যাশবোর্ড';

  @override
  String get shellManageInventory => 'ইনভেন্টরি';

  @override
  String get shellManageSales => 'বিক্রয়';

  @override
  String get shellNewSale => 'নতুন বিক্রয়';

  @override
  String get shellManageCustomers => 'গ্রাহকরা';

  @override
  String get shellManageSuppliers => 'সরবরাহকারী ব্যবস্থাপনা';

  @override
  String get shellManageExpenses => 'খরচ';

  @override
  String get shellManageBankAccounts => 'ব্যাঙ্ক অ্যাকাউন্ট';

  @override
  String get shellManageUsers => 'ব্যবহারকারী ব্যবস্থাপনা';

  @override
  String get shellAddShop => 'দোকান যোগ করুন';

  @override
  String get shellManageShop => 'দোকান ব্যবস্থাপনা';

  @override
  String get authLoginNow => 'এখনই লগইন করুন';

  @override
  String get authPassword => 'পাসওয়ার্ড';

  @override
  String get authLoginCta => 'লগইন';

  @override
  String get authEmailAddress => 'ইমেল ঠিকানা';

  @override
  String get authRememberMe => 'আমাকে মনে রাখুন';

  @override
  String get authForgotPassword => 'পাসওয়ার্ড ভুলে গেছেন?';

  @override
  String get authValidationEmailInvalid => 'সঠিক ইমেল ঠিকানা লিখুন।';

  @override
  String get authValidationPasswordRequired => 'পাসওয়ার্ড আবশ্যক।';

  @override
  String get authValidationLoginIdentifierRequired =>
      'আপনার ইমেল বা মোবাইল নম্বর লিখুন.';

  @override
  String get authValidationEmailRequired => 'ইমেইল প্রয়োজন।';

  @override
  String get customersTitle => 'গ্রাহক';

  @override
  String get customersAddCustomer => 'গ্রাহক যোগ করুন';

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
  String get customersNoCustomersFound => 'কোনো গ্রাহক পাওয়া যায়নি';

  @override
  String get suppliersTitle => 'সরবরাহকারী';

  @override
  String get suppliersAddSupplier => 'সরবরাহকারী যোগ করুন';

  @override
  String get suppliersNoSuppliersFound => 'কোনো সরবরাহকারী পাওয়া যায়নি';

  @override
  String get inventoryTitle => 'ইনভেন্টরি';

  @override
  String get inventoryAddNewProductDescription =>
      'বর্তমান সক্রিয় শপের সাথে সংযুক্ত একটি পণ্য তৈরি করুন।';

  @override
  String get notFoundTitle => 'পৃষ্ঠা পাওয়া যায়নি';

  @override
  String get notFoundGoBack => 'ফিরে যান';
}
