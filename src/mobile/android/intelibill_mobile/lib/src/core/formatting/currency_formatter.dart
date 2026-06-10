import 'package:intl/intl.dart';

final NumberFormat inrCurrencyFormatter = NumberFormat.currency(
  locale: 'en_IN',
  symbol: '₹',
  decimalDigits: 0,
);

String formatInr(num? amount) {
  return inrCurrencyFormatter.format(amount ?? 0);
}
