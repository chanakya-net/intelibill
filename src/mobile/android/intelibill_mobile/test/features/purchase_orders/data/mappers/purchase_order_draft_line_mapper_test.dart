import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/create_purchase_order_draft_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/mappers/purchase_order_mapper.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';

void main() {
  group('PurchaseOrderMapper line mapping', () {
    test('maps domain line to DTO line', () {
      const line = PurchaseOrderDraftLine(
        itemId: 'item-123',
        description: 'Widget A',
        expectedQuantity: 5,
        unitCost: 10.5,
      );

      final dto = PurchaseOrderMapper.lineToRequestDto(line);

      expect(dto.itemId, 'item-123');
      expect(dto.description, 'Widget A');
      expect(dto.expectedQuantity, 5);
      expect(dto.unitCost, 10.5);
    });

    test('maps domain draft with lines to request DTO', () {
      final draft = PurchaseOrderDraft(
        supplierId: 'supplier-1',
        orderDate: DateTime(2026, 7, 20),
        expectedDeliveryDate: DateTime(2026, 8, 20),
        supplierReferenceNumber: 'SR-001',
        notes: 'Rush order',
        lines: const [
          PurchaseOrderDraftLine(
            itemId: 'item-1',
            description: 'Widget A',
            expectedQuantity: 2,
            unitCost: 10.0,
          ),
          PurchaseOrderDraftLine(
            itemId: 'item-2',
            description: 'Widget B',
            expectedQuantity: 3,
            unitCost: 5.0,
          ),
        ],
      );

      final dto = PurchaseOrderMapper.draftToRequestDto(draft);

      expect(dto.supplierId, 'supplier-1');
      expect(dto.orderDate, '2026-07-20');
      expect(dto.expectedDeliveryDate, '2026-08-20');
      expect(dto.supplierReferenceNumber, 'SR-001');
      expect(dto.notes, 'Rush order');
      expect(dto.lines.length, 2);
      expect(dto.lines[0].itemId, 'item-1');
      expect(dto.lines[0].expectedQuantity, 2);
      expect(dto.lines[1].itemId, 'item-2');
      expect(dto.lines[1].expectedQuantity, 3);
    });

    test('omits null optional fields in DTO', () {
      final draft = PurchaseOrderDraft(
        lines: const [
          PurchaseOrderDraftLine(
            itemId: 'item-1',
            description: 'Widget',
            expectedQuantity: 1,
            unitCost: 5.0,
          ),
        ],
      );

      final dto = PurchaseOrderMapper.draftToRequestDto(draft);

      expect(dto.supplierId, isNull);
      expect(dto.orderDate, isNull);
      expect(dto.expectedDeliveryDate, isNull);
      expect(dto.supplierReferenceNumber, isNull);
      expect(dto.notes, isNull);
      expect(dto.lines.length, 1);
    });
  });
}
