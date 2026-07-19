import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/receive_purchase_order_input.dart';

void main() {
  group('ReceivePurchaseOrderInput', () {
    test('constructs with required and optional fields', () {
      final line = ReceivePurchaseOrderLineInput(
        purchaseOrderLineId: 'line-1',
        barcode: 'BAR-001',
        batchNumber: 'BN-001',
        quantity: 5,
        totalPurchaseCost: 500,
        unitCost: 100,
        mrp: 150,
        salesPrice: 120,
        taxRatePercent: 10,
        taxIncluded: true,
        purchaseTaxIncluded: false,
        expiryDate: DateTime(2026, 12, 31),
        manufacturingDate: DateTime(2026, 1, 1),
      );

      expect(line.purchaseOrderLineId, 'line-1');
      expect(line.barcode, 'BAR-001');
      expect(line.batchNumber, 'BN-001');
      expect(line.quantity, 5);
      expect(line.totalPurchaseCost, 500);
      expect(line.unitCost, 100);
      expect(line.mrp, 150);
      expect(line.salesPrice, 120);
      expect(line.taxRatePercent, 10);
      expect(line.taxIncluded, true);
      expect(line.purchaseTaxIncluded, false);
      expect(line.expiryDate, DateTime(2026, 12, 31));
      expect(line.manufacturingDate, DateTime(2026, 1, 1));
    });

    test('supports null optional dates', () {
      final line = ReceivePurchaseOrderLineInput(
        purchaseOrderLineId: 'line-1',
        barcode: 'BAR-001',
        batchNumber: 'BN-001',
        quantity: 1,
        totalPurchaseCost: 100,
        unitCost: 100,
        mrp: 150,
        salesPrice: 120,
        taxRatePercent: 0,
        taxIncluded: false,
        purchaseTaxIncluded: false,
      );

      expect(line.expiryDate, isNull);
      expect(line.manufacturingDate, isNull);
    });

    test('supports zero tax rate and nonnegative costs', () {
      final line = ReceivePurchaseOrderLineInput(
        purchaseOrderLineId: 'line-1',
        barcode: 'BAR-001',
        batchNumber: 'BN-001',
        quantity: 1,
        totalPurchaseCost: 0,
        unitCost: 0,
        mrp: 0,
        salesPrice: 0,
        taxRatePercent: 0,
        taxIncluded: false,
        purchaseTaxIncluded: false,
      );

      expect(line.totalPurchaseCost, 0);
      expect(line.taxRatePercent, 0);
      expect(line.mrp, 0);
    });

    test('equatable compares all fields', () {
      const line1 = ReceivePurchaseOrderLineInput(
        purchaseOrderLineId: 'line-1',
        barcode: 'BAR-001',
        batchNumber: 'BN-001',
        quantity: 5,
        totalPurchaseCost: 500,
        unitCost: 100,
        mrp: 150,
        salesPrice: 120,
        taxRatePercent: 10,
        taxIncluded: true,
        purchaseTaxIncluded: false,
      );

      const line2 = ReceivePurchaseOrderLineInput(
        purchaseOrderLineId: 'line-1',
        barcode: 'BAR-001',
        batchNumber: 'BN-001',
        quantity: 5,
        totalPurchaseCost: 500,
        unitCost: 100,
        mrp: 150,
        salesPrice: 120,
        taxRatePercent: 10,
        taxIncluded: true,
        purchaseTaxIncluded: false,
      );

      const line3 = ReceivePurchaseOrderLineInput(
        purchaseOrderLineId: 'line-1',
        barcode: 'BAR-001',
        batchNumber: 'BN-001',
        quantity: 5,
        totalPurchaseCost: 500,
        unitCost: 100,
        mrp: 150,
        salesPrice: 100,
        taxRatePercent: 10,
        taxIncluded: true,
        purchaseTaxIncluded: false,
      );

      expect(line1, line2);
      expect(line1, isNot(line3));
    });
  });

  group('ReceivePurchaseOrderInput', () {
    test('constructs with required header and optional fields', () {
      final line = ReceivePurchaseOrderLineInput(
        purchaseOrderLineId: 'line-1',
        barcode: 'BAR-001',
        batchNumber: 'BN-001',
        quantity: 5,
        totalPurchaseCost: 500,
        unitCost: 100,
        mrp: 150,
        salesPrice: 120,
        taxRatePercent: 10,
        taxIncluded: true,
        purchaseTaxIncluded: false,
      );

      final input = ReceivePurchaseOrderInput(
        referenceNumber: 'REF-001',
        notes: 'Test notes',
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: [line],
      );

      expect(input.referenceNumber, 'REF-001');
      expect(input.notes, 'Test notes');
      expect(input.receivedAt, DateTime.utc(2026, 7, 19));
      expect(input.lines, [line]);
    });

    test('supports null optional reference and notes', () {
      final input = ReceivePurchaseOrderInput(
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: [],
      );

      expect(input.referenceNumber, isNull);
      expect(input.notes, isNull);
    });

    test('equatable compares all fields including all lines', () {
      final line1 = ReceivePurchaseOrderLineInput(
        purchaseOrderLineId: 'line-1',
        barcode: 'BAR-001',
        batchNumber: 'BN-001',
        quantity: 5,
        totalPurchaseCost: 500,
        unitCost: 100,
        mrp: 150,
        salesPrice: 120,
        taxRatePercent: 10,
        taxIncluded: true,
        purchaseTaxIncluded: false,
      );

      final input1 = ReceivePurchaseOrderInput(
        referenceNumber: 'REF-001',
        notes: 'Test',
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: [line1],
      );

      final input2 = ReceivePurchaseOrderInput(
        referenceNumber: 'REF-001',
        notes: 'Test',
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: [line1],
      );

      final input3 = ReceivePurchaseOrderInput(
        referenceNumber: 'REF-002',
        notes: 'Test',
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: [line1],
      );

      expect(input1, input2);
      expect(input1, isNot(input3));
    });
  });
}
