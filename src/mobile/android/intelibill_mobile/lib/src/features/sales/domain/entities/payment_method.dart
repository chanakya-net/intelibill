enum PaymentMethod { cash, upi, card, credit }

const double _paymentSplitTolerance = 0.01;

extension PaymentMethodWireConversion on PaymentMethod {
  int toWireCode() {
    switch (this) {
      case PaymentMethod.cash:
        return 1;
      case PaymentMethod.upi:
        return 2;
      case PaymentMethod.card:
        return 3;
      case PaymentMethod.credit:
        return 4;
    }
  }
}

PaymentMethod paymentMethodFromWireCode(int value) {
  switch (value) {
    case 1:
      return PaymentMethod.cash;
    case 2:
      return PaymentMethod.upi;
    case 3:
      return PaymentMethod.card;
    case 4:
      return PaymentMethod.credit;
    default:
      throw ArgumentError('Unknown payment method code: $value');
  }
}

bool arePaymentAmountsEqual(double paid, double due, double payable) {
  return (paid + due - payable).abs() <= _paymentSplitTolerance;
}
