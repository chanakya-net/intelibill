enum PurchaseOrderStatus {
  draft('Draft'),
  placed('Placed'),
  cancelled('Cancelled'),
  partiallyReceived('PartiallyReceived'),
  received('Received'),
  closed('Closed');

  const PurchaseOrderStatus(this.wireValue);

  final String wireValue;

  static PurchaseOrderStatus fromWire(String value) {
    for (final status in values) {
      if (status.wireValue == value) return status;
    }
    throw FormatException('Unknown purchase-order status: $value');
  }
}
