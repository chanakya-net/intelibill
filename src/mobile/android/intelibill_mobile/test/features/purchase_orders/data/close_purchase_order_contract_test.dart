import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/close_purchase_order_request_dto.dart';

void main() {
  group('ClosePurchaseOrderRequestDto', () {
    test('serializes reason to JSON', () {
      const dto = ClosePurchaseOrderRequestDto(reason: 'Discontinued');

      expect(dto.toJson(), {'reason': 'Discontinued'});
    });

    test('supports the 1 and 500 character boundaries', () {
      expect(const ClosePurchaseOrderRequestDto(reason: 'A').reason, 'A');
      expect(
        ClosePurchaseOrderRequestDto(reason: 'x' * 500).reason.length,
        500,
      );
    });
  });
}
