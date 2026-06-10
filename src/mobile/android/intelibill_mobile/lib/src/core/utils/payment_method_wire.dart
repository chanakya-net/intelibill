/// Parses payment method values from API JSON (int or string enum).
int paymentMethodFromJson(dynamic value) {
  if (value is int) {
    return value;
  }
  if (value is num) {
    return value.toInt();
  }
  if (value is String) {
    switch (value) {
      case 'Cash':
        return 1;
      case 'UPI':
        return 2;
      case 'Card':
        return 3;
      case 'Credit':
        return 4;
      default:
        final parsed = int.tryParse(value);
        if (parsed != null) {
          return parsed;
        }
        throw FormatException('Unknown payment method: $value');
    }
  }
  throw FormatException('Invalid payment method: $value');
}
