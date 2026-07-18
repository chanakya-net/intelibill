import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/documents/purchase_order_pdf_builder.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';

void main() {
  final purchaseOrder = PurchaseOrder(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO / 12:3',
    status: PurchaseOrderStatus.partiallyReceived,
    supplierName: 'Fresh Grocers',
    supplierReferenceNumber: 'REF-77',
    orderDate: DateTime(2026, 7, 11),
    expectedDeliveryDate: DateTime(2026, 7, 14),
    notes: 'Urgent restock',
    lines: const [
      PurchaseOrderLine(
        lineId: 'line-1',
        itemId: 'item-1',
        description: 'Basmati rice',
        expectedQuantity: 10,
        receivedQuantity: 4,
        remainingQuantity: 6,
        unitCost: 70.5,
        lineTotal: 705,
      ),
    ],
    expectedTotal: 705,
    createdAt: DateTime(2026, 7, 10),
  );

  test('builds A4 PDF bytes with complete purchase-order content', () async {
    final builder = PurchaseOrderPdfBuilder();
    final bytes = await builder.build(
      purchaseOrder,
      const ShopDetails(
        id: 'shop-1',
        name: 'Corner Store',
        address: '1 Market Road',
        city: 'Pune',
        state: 'Maharashtra',
        pincode: '411001',
        mobileNumber: '9876543210',
        gstNumber: '27ABCDE1234F1Z5',
        bankAccounts: [],
      ),
    );

    expect(bytes, isNotEmpty);
    expect(bytes.take(4), orderedEquals('%PDF'.codeUnits));
    expect(builder.filenameFor(purchaseOrder), 'purchase-order-PO-12-3.pdf');
    expect(builder.contentFor(purchaseOrder, null), contains('Fresh Grocers'));
    expect(builder.contentFor(purchaseOrder, null), contains('Basmati rice'));
    expect(builder.contentFor(purchaseOrder, null), contains('Expected total'));
  });

  test('omits unavailable shop details without blocking generation', () async {
    final builder = PurchaseOrderPdfBuilder();
    final bytes = await builder.build(purchaseOrder, null);

    expect(bytes, isNotEmpty);
    expect(
      builder.contentFor(purchaseOrder, null),
      isNot(contains('Corner Store')),
    );
  });
}
