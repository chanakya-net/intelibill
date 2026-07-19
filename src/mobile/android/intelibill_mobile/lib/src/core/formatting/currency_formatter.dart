import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

final NumberFormat inrCurrencyFormatter = NumberFormat.currency(
  locale: 'en_IN',
  symbol: '₹',
  decimalDigits: 0,
);

String formatInr(
  num? amount, {
  Locale locale = const Locale('en', 'IN'),
}) {
  if (locale.toLanguageTag() == 'en-IN') {
    return inrCurrencyFormatter.format(amount ?? 0);
  }
  return formatInrForLocale(amount, locale);
}

String formatInrForLocale(num? amount, Locale locale) {
  return NumberFormat.currency(
    locale: locale.toLanguageTag(),
    symbol: '₹',
    decimalDigits: 0,
  ).format(amount ?? 0);
}
