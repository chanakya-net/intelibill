import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/cancel_purchase_order_request_dto.dart';

void main() {
  group('CancelPurchaseOrderRequestDto', () {
    test('serializes reason to JSON', () {
      const dto = CancelPurchaseOrderRequestDto(reason: 'No longer needed');
      expect(dto.toJson(), {'reason': 'No longer needed'});
    });

    test('accepts 1-character reason', () {
      const dto = CancelPurchaseOrderRequestDto(reason: 'A');
      expect(dto.reason, 'A');
    });

    test('accepts 500-character reason', () {
      final reason = 'x' * 500;
      final dto = CancelPurchaseOrderRequestDto(reason: reason);
      expect(dto.reason, reason);
    });
  });
}
