import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sellable_dto.dart';

void main() {
  group('SellableDto', () {
    test('parses goods sellable json', () {
      final json = {
        'kind': 'Goods',
        'inventoryBatchId': 'batch-1',
        'barcode': 'BAR001',
        'itemName': 'Flour',
        'batchNumber': 'BN-11',
        'quantity': 10,
        'salesPrice': 55.5,
        'mrp': 60.0,
        'taxRatePercent': 5,
        'taxIncluded': false,
        'purchaseTaxIncluded': false,
        'expiryDate': '2026-12-31',
      };

      final dto = SellableDto.fromJson(json);

      expect(dto.kind, 'Goods');
      expect(dto.inventoryBatchId, 'batch-1');
      expect(dto.itemName, 'Flour');
      expect(dto.barcode, 'BAR001');
      expect(dto.batchNumber, 'BN-11');
      expect(dto.quantity, 10);
      expect(dto.salesPrice, 55.5);
      expect(dto.mrp, 60.0);
    });

    test('parses fractional goods quantity', () {
      final dto = SellableDto.fromJson({
        'kind': 'Goods',
        'inventoryBatchId': 'batch-2',
        'itemName': 'Rice',
        'quantity': 1.25,
        'salesPrice': 30.0,
      });

      expect(dto.quantity, 1.25);
      expect(dto.salesPrice, 30.0);
    });
  });
}
