import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/inventory_adjustment_dto.dart';

void main() {
  group('InventoryAdjustmentDto', () {
    test('parses full JSON with all fields present', () {
      final json = {
        'adjustmentId': 'adj-123',
        'batchId': 'batch-456',
        'itemId': 'item-789',
        'itemName': 'Rice Basmati',
        'batchNumber': 'BN-001',
        'direction': 'Add',
        'reason': 'StockCount',
        'quantity': 10.0,
        'costImpact': 450.0,
        'notes': 'Monthly count adjustment',
        'performedAt': '2024-06-15T09:00:00.000Z',
        'performedByDisplayName': 'John Manager',
        'isVoided': false,
      };

      final dto = InventoryAdjustmentDto.fromJson(json);

      expect(dto.adjustmentId, 'adj-123');
      expect(dto.batchId, 'batch-456');
      expect(dto.itemId, 'item-789');
      expect(dto.itemName, 'Rice Basmati');
      expect(dto.batchNumber, 'BN-001');
      expect(dto.direction, 'Add');
      expect(dto.reason, 'StockCount');
      expect(dto.quantity, 10.0);
      expect(dto.costImpact, 450.0);
      expect(dto.notes, 'Monthly count adjustment');
      expect(dto.performedAt, isNotNull);
      expect(dto.performedByDisplayName, 'John Manager');
      expect(dto.isVoided, false);
    });

    test('parses JSON with nullable notes missing', () {
      final json = {
        'adjustmentId': 'adj-456',
        'batchId': 'batch-789',
        'itemId': 'item-001',
        'itemName': 'Sugar',
        'batchNumber': 'BN-002',
        'direction': 'Subtract',
        'reason': 'Damaged',
        'quantity': 5.0,
        'costImpact': -150.0,
        'performedAt': '2024-06-16T10:00:00.000Z',
        'performedByDisplayName': 'Jane Owner',
        'isVoided': true,
      };

      final dto = InventoryAdjustmentDto.fromJson(json);

      expect(dto.notes, isNull);
      expect(dto.isVoided, true);
    });

    test('defaults isVoided to false when field is missing', () {
      final json = {
        'adjustmentId': 'adj-789',
        'batchId': 'batch-001',
        'itemId': 'item-002',
        'itemName': 'Salt',
        'batchNumber': 'BN-003',
        'direction': 'Add',
        'reason': 'SupplierReturn',
        'quantity': 20.0,
        'costImpact': 200.0,
        'performedAt': '2024-06-17T11:00:00.000Z',
        'performedByDisplayName': 'Bob Staff',
      };

      final dto = InventoryAdjustmentDto.fromJson(json);

      expect(dto.isVoided, false);
    });

    test('supports value equality via Freezed', () {
      const now = '2024-06-15T09:00:00.000Z';
      final dto1 = InventoryAdjustmentDto.fromJson({
        'adjustmentId': 'adj-1',
        'batchId': 'batch-1',
        'itemId': 'item-1',
        'itemName': 'Rice',
        'batchNumber': 'BN-001',
        'direction': 'Add',
        'reason': 'StockCount',
        'quantity': 5.0,
        'costImpact': 50.0,
        'performedAt': now,
        'performedByDisplayName': 'Manager',
      });
      final dto2 = InventoryAdjustmentDto.fromJson({
        'adjustmentId': 'adj-1',
        'batchId': 'batch-1',
        'itemId': 'item-1',
        'itemName': 'Rice',
        'batchNumber': 'BN-001',
        'direction': 'Add',
        'reason': 'StockCount',
        'quantity': 5.0,
        'costImpact': 50.0,
        'performedAt': now,
        'performedByDisplayName': 'Manager',
      });

      expect(dto1, equals(dto2));
    });
  });
}
