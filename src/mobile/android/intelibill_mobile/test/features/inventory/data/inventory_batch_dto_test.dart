import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/inventory_batch_dto.dart';

void main() {
  group('InventoryBatchDto', () {
    test('parses full JSON with all fields present', () {
      final json = {
        'id': 'batch-123',
        'itemId': 'item-456',
        'itemName': 'Rice Basmati',
        'barcode': '8901234567890',
        'itemUom': 'KG',
        'batchNumber': 'BN-001',
        'quantity': 100.0,
        'costPrice': 45.0,
        'mrp': 60.0,
        'salesPrice': 55.0,
        'taxRatePercent': 5.0,
        'taxIncluded': false,
        'expiryDate': '2025-12-31',
        'manufacturingDate': '2024-01-01',
        'referenceNumber': 'REF-001',
        'notes': 'First batch',
        'supplierId': 'sup-789',
        'supplierName': 'Agro Suppliers',
        'isVoided': false,
        'createdAt': '2024-01-15T10:30:00.000Z',
      };

      final dto = InventoryBatchDto.fromJson(json);

      expect(dto.id, 'batch-123');
      expect(dto.itemId, 'item-456');
      expect(dto.itemName, 'Rice Basmati');
      expect(dto.barcode, '8901234567890');
      expect(dto.itemUom, 'KG');
      expect(dto.batchNumber, 'BN-001');
      expect(dto.quantity, 100.0);
      expect(dto.costPrice, 45.0);
      expect(dto.mrp, 60.0);
      expect(dto.salesPrice, 55.0);
      expect(dto.taxRatePercent, 5.0);
      expect(dto.taxIncluded, false);
      expect(dto.expiryDate, isNotNull);
      expect(dto.manufacturingDate, isNotNull);
      expect(dto.referenceNumber, 'REF-001');
      expect(dto.notes, 'First batch');
      expect(dto.supplierId, 'sup-789');
      expect(dto.supplierName, 'Agro Suppliers');
      expect(dto.isVoided, false);
      expect(dto.createdAt, isNotNull);
    });

    test('parses JSON with nullable fields missing', () {
      final json = {
        'id': 'batch-456',
        'itemId': 'item-789',
        'itemName': 'Sugar',
        'barcode': '8901234567891',
        'batchNumber': 'BN-002',
        'quantity': 50.0,
        'costPrice': 30.0,
        'mrp': 40.0,
        'salesPrice': 38.0,
        'createdAt': '2024-06-01T00:00:00.000Z',
      };

      final dto = InventoryBatchDto.fromJson(json);

      expect(dto.expiryDate, isNull);
      expect(dto.manufacturingDate, isNull);
      expect(dto.referenceNumber, isNull);
      expect(dto.notes, isNull);
      expect(dto.supplierId, isNull);
      expect(dto.supplierName, isNull);
    });

    test('defaults taxRatePercent to 0.0, taxIncluded to false, '
        'isVoided to false, itemUom to empty string when missing', () {
      final json = {
        'id': 'batch-789',
        'itemId': 'item-001',
        'itemName': 'Salt',
        'barcode': '8901234567892',
        'batchNumber': 'BN-003',
        'quantity': 200.0,
        'costPrice': 10.0,
        'mrp': 15.0,
        'salesPrice': 13.0,
        'createdAt': '2024-06-01T00:00:00.000Z',
      };

      final dto = InventoryBatchDto.fromJson(json);

      expect(dto.taxRatePercent, 0.0);
      expect(dto.taxIncluded, false);
      expect(dto.isVoided, false);
      expect(dto.itemUom, '');
    });

    test('supports value equality via Freezed', () {
      const now = '2024-01-15T10:30:00.000Z';
      final dto1 = InventoryBatchDto.fromJson({
        'id': 'batch-1',
        'itemId': 'item-1',
        'itemName': 'Rice',
        'barcode': '123',
        'batchNumber': 'BN-001',
        'quantity': 10.0,
        'costPrice': 5.0,
        'mrp': 8.0,
        'salesPrice': 7.0,
        'createdAt': now,
      });
      final dto2 = InventoryBatchDto.fromJson({
        'id': 'batch-1',
        'itemId': 'item-1',
        'itemName': 'Rice',
        'barcode': '123',
        'batchNumber': 'BN-001',
        'quantity': 10.0,
        'costPrice': 5.0,
        'mrp': 8.0,
        'salesPrice': 7.0,
        'createdAt': now,
      });

      expect(dto1, equals(dto2));
    });
  });
}
