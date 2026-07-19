import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/documents/purchase_order_pdf_builder.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intl/date_symbol_data_local.dart';

Future<void> main() async {
  TestWidgetsFlutterBinding.ensureInitialized();
  await initializeDateFormatting();
  final l10n = lookupAppLocalizations(const Locale('en', 'IN'));
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
      l10n,
    );

    expect(bytes, isNotEmpty);
    expect(bytes.take(4), orderedEquals('%PDF'.codeUnits));
    expect(builder.filenameFor(purchaseOrder), 'purchase-order-PO-12-3.pdf');
    expect(
      builder.contentFor(purchaseOrder, null, l10n),
      contains('Fresh Grocers'),
    );
    expect(
      builder.contentFor(purchaseOrder, null, l10n),
      contains('Basmati rice'),
    );
    expect(
      builder.contentFor(purchaseOrder, null, l10n),
      contains('Partially received'),
    );
  });

  test('omits unavailable shop details without blocking generation', () async {
    final builder = PurchaseOrderPdfBuilder();
    final bytes = await builder.build(purchaseOrder, null, l10n);

    expect(bytes, isNotEmpty);
    expect(
      builder.contentFor(purchaseOrder, null, l10n),
      isNot(contains('Corner Store')),
    );
  });
}
