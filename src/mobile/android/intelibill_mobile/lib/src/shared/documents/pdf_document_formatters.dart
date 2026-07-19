import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

const pdfDefaultLocale = Locale('en', 'IN');

String formatPdfDate(
  DateTime value, {
  Locale locale = pdfDefaultLocale,
}) {
  return DateFormat.yMMMd(_localeTag(locale)).format(value);
}

String formatPdfDecimal(
  num value, {
  Locale locale = pdfDefaultLocale,
  int decimalDigits = 2,
}) {
  return NumberFormat.decimalPatternDigits(
    locale: _localeTag(locale),
    decimalDigits: decimalDigits,
  ).format(value);
}

String formatPdfPercentage(
  num value, {
  Locale locale = pdfDefaultLocale,
  int decimalDigits = 1,
}) {
  final formatter = NumberFormat.percentPattern(_localeTag(locale))
    ..minimumFractionDigits = decimalDigits
    ..maximumFractionDigits = decimalDigits;
  return formatter.format(value / 100);
}

String formatPdfInr(
  num value, {
  Locale locale = pdfDefaultLocale,
  int decimalDigits = 2,
}) {
  return NumberFormat.currency(
    locale: _localeTag(locale),
    symbol: '₹',
    decimalDigits: decimalDigits,
  ).format(value);
}

String _localeTag(Locale locale) => locale.toLanguageTag();
