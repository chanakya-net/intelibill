import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/receive_purchase_order_input.dart';

void main() {
  group('ReceivePurchaseOrderLineInput', () {
    group('barcode validation', () {
      test('allows non-empty barcode', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.barcode, 'BAR123');
      });

      test('allows minimum-length barcode (1 char)', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'A',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.barcode, 'A');
      });

      test('allows very long barcode', () {
        final barcode = 'A' * 100;
        final line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: barcode,
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.barcode, barcode);
      });
    });

    group('batch number validation', () {
      test('allows non-empty batch number', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-001',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.batchNumber, 'BN-001');
      });

      test('allows minimum-length batch (1 char)', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'X',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.batchNumber, 'X');
      });
    });

    group('cost validation', () {
      test('allows zero total purchase cost', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
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
      });

      test('allows positive total purchase cost', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.totalPurchaseCost, 100);
      });

      test('allows zero unit cost', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 0,
          unitCost: 0,
          mrp: 0,
          salesPrice: 0,
          taxRatePercent: 0,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.unitCost, 0);
      });

      test('allows positive unit cost', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.unitCost, 100);
      });
    });

    group('price validation', () {
      test('allows zero MRP', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 0,
          unitCost: 0,
          mrp: 0,
          salesPrice: 0,
          taxRatePercent: 0,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.mrp, 0);
      });

      test('allows sales price less than MRP', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.salesPrice <= line.mrp, true);
      });

      test('allows sales price equal to MRP', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 150,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.salesPrice <= line.mrp, true);
      });
    });

    group('tax validation', () {
      test('allows zero tax rate', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 0,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.taxRatePercent, 0);
      });

      test('allows 100 percent tax rate', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 100,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.taxRatePercent, 100);
      });

      test('allows mid-range tax rate', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 18,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.taxRatePercent, 18);
      });

      test('allows both tax flags as false', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 18,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.taxIncluded, false);
        expect(line.purchaseTaxIncluded, false);
      });

      test('allows taxIncluded true, purchaseTaxIncluded false', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 18,
          taxIncluded: true,
          purchaseTaxIncluded: false,
        );
        expect(line.taxIncluded, true);
        expect(line.purchaseTaxIncluded, false);
      });

      test('allows both tax flags as true', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 18,
          taxIncluded: true,
          purchaseTaxIncluded: true,
        );
        expect(line.taxIncluded, true);
        expect(line.purchaseTaxIncluded, true);
      });
    });

    group('date validation', () {
      test('allows null manufacturing date', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
          manufacturingDate: null,
        );
        expect(line.manufacturingDate, null);
      });

      test('allows valid manufacturing date', () {
        final mfg = DateTime(2026, 1, 15);
        final line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
          manufacturingDate: mfg,
        );
        expect(line.manufacturingDate, mfg);
      });

      test('allows null expiry date', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
          expiryDate: null,
        );
        expect(line.expiryDate, null);
      });

      test('allows valid expiry date', () {
        final exp = DateTime(2027, 12, 31);
        final line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
          expiryDate: exp,
        );
        expect(line.expiryDate, exp);
      });

      test('allows manufacturing before expiry', () {
        final mfg = DateTime(2026, 1, 15);
        final exp = DateTime(2027, 1, 15);
        final line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
          manufacturingDate: mfg,
          expiryDate: exp,
        );
        expect(line.manufacturingDate!.isBefore(line.expiryDate!), true);
      });

      test('allows both dates present with manufacturing before expiry', () {
        final mfg = DateTime(2026, 6, 1);
        final exp = DateTime(2027, 6, 1);
        final line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 1,
          totalPurchaseCost: 100,
          unitCost: 100,
          mrp: 150,
          salesPrice: 120,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
          manufacturingDate: mfg,
          expiryDate: exp,
        );
        expect(line.manufacturingDate != null, true);
        expect(line.expiryDate != null, true);
        expect(mfg.isBefore(exp), true);
      });
    });

    group('quantity validation', () {
      test('allows zero quantity', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 0,
          totalPurchaseCost: 0,
          unitCost: 0,
          mrp: 0,
          salesPrice: 0,
          taxRatePercent: 0,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.quantity, 0);
      });

      test('allows positive quantity', () {
        const line = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 10.5,
          totalPurchaseCost: 100,
          unitCost: 10,
          mrp: 15,
          salesPrice: 12,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line.quantity, 10.5);
      });
    });

    group('equatable', () {
      test('equals when all fields match', () {
        const line1 = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 10,
          totalPurchaseCost: 100,
          unitCost: 10,
          mrp: 15,
          salesPrice: 12,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        const line2 = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 10,
          totalPurchaseCost: 100,
          unitCost: 10,
          mrp: 15,
          salesPrice: 12,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line1, line2);
      });

      test('not equals when barcode differs', () {
        const line1 = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR123',
          batchNumber: 'BN-1',
          quantity: 10,
          totalPurchaseCost: 100,
          unitCost: 10,
          mrp: 15,
          salesPrice: 12,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        const line2 = ReceivePurchaseOrderLineInput(
          purchaseOrderLineId: 'line-1',
          barcode: 'BAR456',
          batchNumber: 'BN-1',
          quantity: 10,
          totalPurchaseCost: 100,
          unitCost: 10,
          mrp: 15,
          salesPrice: 12,
          taxRatePercent: 10,
          taxIncluded: false,
          purchaseTaxIncluded: false,
        );
        expect(line1, isNot(line2));
      });
    });
  });

  group('ReceivePurchaseOrderInput', () {
    test('has expected fields', () {
      final input = ReceivePurchaseOrderInput(
        referenceNumber: 'REF-001',
        notes: 'Good condition',
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: const [
          ReceivePurchaseOrderLineInput(
            purchaseOrderLineId: 'line-1',
            barcode: 'BAR123',
            batchNumber: 'BN-1',
            quantity: 1,
            totalPurchaseCost: 100,
            unitCost: 100,
            mrp: 150,
            salesPrice: 120,
            taxRatePercent: 10,
            taxIncluded: false,
            purchaseTaxIncluded: false,
          ),
        ],
      );
      expect(input.referenceNumber, 'REF-001');
      expect(input.notes, 'Good condition');
      expect(input.receivedAt, DateTime.utc(2026, 7, 19));
      expect(input.lines.length, 1);
    });

    test('equatable works', () {
      final input1 = ReceivePurchaseOrderInput(
        referenceNumber: 'REF-001',
        notes: 'Good',
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: const [
          ReceivePurchaseOrderLineInput(
            purchaseOrderLineId: 'line-1',
            barcode: 'BAR123',
            batchNumber: 'BN-1',
            quantity: 1,
            totalPurchaseCost: 100,
            unitCost: 100,
            mrp: 150,
            salesPrice: 120,
            taxRatePercent: 10,
            taxIncluded: false,
            purchaseTaxIncluded: false,
          ),
        ],
      );
      final input2 = ReceivePurchaseOrderInput(
        referenceNumber: 'REF-001',
        notes: 'Good',
        receivedAt: DateTime.utc(2026, 7, 19),
        lines: const [
          ReceivePurchaseOrderLineInput(
            purchaseOrderLineId: 'line-1',
            barcode: 'BAR123',
            batchNumber: 'BN-1',
            quantity: 1,
            totalPurchaseCost: 100,
            unitCost: 100,
            mrp: 150,
            salesPrice: 120,
            taxRatePercent: 10,
            taxIncluded: false,
            purchaseTaxIncluded: false,
          ),
        ],
      );
      expect(input1, input2);
    });
  });
}
