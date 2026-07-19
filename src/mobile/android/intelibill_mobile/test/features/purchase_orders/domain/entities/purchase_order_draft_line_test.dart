import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';

void main() {
  group('PurchaseOrderDraftLine', () {
    test('validates integer quantity must be positive', () {
      expect(
        () => PurchaseOrderDraftLine.validate(
          itemId: 'item-1',
          description: 'Widget',
          expectedQuantity: 0,
          unitCost: 10,
        ),
        throwsArgumentError,
      );
    });

    test('validates unit cost must be non-negative', () {
      expect(
        () => PurchaseOrderDraftLine.validate(
          itemId: 'item-1',
          description: 'Widget',
          expectedQuantity: 1,
          unitCost: -0.01,
        ),
        throwsArgumentError,
      );
    });

    test('validates description length max 255 chars', () {
      expect(
        () => PurchaseOrderDraftLine.validate(
          itemId: 'item-1',
          description: 'x' * 256,
          expectedQuantity: 1,
          unitCost: 10,
        ),
        throwsArgumentError,
      );
    });

    test('allows zero unit cost', () {
      expect(
        () => PurchaseOrderDraftLine.validate(
          itemId: 'item-1',
          description: 'Widget',
          expectedQuantity: 1,
          unitCost: 0,
        ),
        returnsNormally,
      );
    });

    test('calculates line total correctly', () {
      const line = PurchaseOrderDraftLine(
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 5,
        unitCost: 10.5,
      );
      expect(line.lineTotal, 52.5);
    });

    test('calculates line total with zero cost', () {
      const line = PurchaseOrderDraftLine(
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 3,
        unitCost: 0,
      );
      expect(line.lineTotal, 0.0);
    });
  });
}
