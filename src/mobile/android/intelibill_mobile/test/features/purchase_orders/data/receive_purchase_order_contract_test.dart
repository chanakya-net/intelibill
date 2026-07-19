import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/receive_purchase_order_request_dto.dart';

void main() {
  group('ReceivePurchaseOrderRequestDto', () {
    test('serializes trimmed headers and offset receivedAt', () {
      final request = ReceivePurchaseOrderRequestDto(
        referenceNumber: '  PO-REF-1  ',
        notes: '  notes  ',
        receivedAt: DateTime.utc(2026, 7, 19, 8, 30),
        lines: [
          ReceivePurchaseOrderLineRequestDto(
            purchaseOrderLineId: 'line-1',
            barcode: '  BAR-001  ',
            batchNumber: '  BN-001  ',
            quantity: 2.5,
            totalPurchaseCost: 100,
            unitCost: 40,
            mrp: 120,
            salesPrice: 100,
            taxRatePercent: 5,
            taxIncluded: true,
            purchaseTaxIncluded: false,
            expiryDate: DateTime(2026, 7, 31),
            manufacturingDate: DateTime(2026, 7, 1),
          ),
        ],
      );

      final json = request.toJson();

      expect(json['referenceNumber'], 'PO-REF-1');
      expect(json['notes'], 'notes');
      expect(
        json['receivedAt'],
        matches(r'^2026-07-19T08:30:00(\.\d+)?Z$'),
      );
      final line = json['lines'][0] as Map<String, dynamic>;
      expect(line['barcode'], 'BAR-001');
      expect(line['batchNumber'], 'BN-001');
      expect(line['expiryDate'], '2026-07-31');
      expect(line['manufacturingDate'], '2026-07-01');
    });

    test('serializes DateOnly values as YYYY-MM-DD', () {
      final request = ReceivePurchaseOrderRequestDto(
        referenceNumber: null,
        notes: null,
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: [
          ReceivePurchaseOrderLineRequestDto(
            purchaseOrderLineId: 'line-1',
            barcode: 'BAR-001',
            batchNumber: 'BN-001',
            quantity: 0,
            totalPurchaseCost: 0,
            unitCost: 0,
            mrp: 0,
            salesPrice: 0,
            taxRatePercent: 0,
            taxIncluded: false,
            purchaseTaxIncluded: true,
            expiryDate: null,
            manufacturingDate: null,
          ),
        ],
      );

      final line = request.toJson()['lines'][0] as Map<String, dynamic>;
      expect(line['expiryDate'], isNull);
      expect(line['manufacturingDate'], isNull);
      expect(request.toJson()['referenceNumber'], isNull);
      expect(request.toJson()['notes'], isNull);
    });

    test('supports multiple lines and preserves the line order', () {
      final request = ReceivePurchaseOrderRequestDto(
        referenceNumber: null,
        notes: null,
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: [
          ReceivePurchaseOrderLineRequestDto(
            purchaseOrderLineId: 'line-a',
            barcode: 'BAR-A',
            batchNumber: 'BN-A',
            quantity: 1,
            totalPurchaseCost: 10,
            unitCost: 10,
            mrp: 20,
            salesPrice: 20,
            taxRatePercent: 0,
            taxIncluded: false,
            purchaseTaxIncluded: false,
          ),
          const ReceivePurchaseOrderLineRequestDto(
            purchaseOrderLineId: 'line-b',
            barcode: 'BAR-B',
            batchNumber: 'BN-B',
            quantity: 2,
            totalPurchaseCost: 20,
            unitCost: 10,
            mrp: 30,
            salesPrice: 30,
            taxRatePercent: 5,
            taxIncluded: true,
            purchaseTaxIncluded: true,
          ),
        ],
      );

      final lineIds = (request.toJson()['lines'] as List<dynamic>)
          .map((json) => json['purchaseOrderLineId'] as String)
          .toList();
      expect(lineIds, ['line-a', 'line-b']);
    });

    test('serializes the exact backend receipt body without shopId', () {
      final request = ReceivePurchaseOrderRequestDto(
        referenceNumber: null,
        notes: null,
        receivedAt: DateTime.utc(2026, 7, 19, 8, 30),
        lines: [
          ReceivePurchaseOrderLineRequestDto(
            purchaseOrderLineId: 'line-1',
            barcode: 'BAR-001',
            batchNumber: 'BN-001',
            quantity: 2,
            totalPurchaseCost: 100,
            unitCost: 50,
            mrp: 120,
            salesPrice: 100,
            taxRatePercent: 5,
            taxIncluded: true,
            purchaseTaxIncluded: false,
            expiryDate: DateTime(2026, 8, 1),
            manufacturingDate: DateTime(2026, 7, 1),
          ),
        ],
      );

      expect(request.toJson(), {
        'referenceNumber': null,
        'notes': null,
        'receivedAt': '2026-07-19T08:30:00.000Z',
        'lines': [
          {
            'purchaseOrderLineId': 'line-1',
            'barcode': 'BAR-001',
            'batchNumber': 'BN-001',
            'quantity': 2.0,
            'totalPurchaseCost': 100.0,
            'mrp': 120.0,
            'salesPrice': 100.0,
            'taxRatePercent': 5.0,
            'taxIncluded': true,
            'purchaseTaxIncluded': false,
            'expiryDate': '2026-08-01',
            'manufacturingDate': '2026-07-01',
          },
        ],
      });
    });
  });
}
