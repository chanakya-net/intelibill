import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_receipt.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_receipt_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_receipt_history.dart';

void main() {
  testWidgets('renders every receipt and line value when expanded', (
    tester,
  ) async {
    final firstReceipt = PurchaseOrderReceipt(
      receiptId: 'receipt-1',
      receiptNumber: 'GRN-001',
      receivedAt: DateTime(2026, 7, 14, 11),
      referenceNumber: 'REF-001',
      notes: 'Counted at dock',
      receivedByUserId: 'user-receiver',
      receivedByDisplayName: 'Riya Receiver',
      lines: const [
        PurchaseOrderReceiptLine(
          receiptLineId: 'receipt-line-1',
          purchaseOrderLineId: 'line-1',
          itemId: 'item-1',
          inventoryBatchId: 'batch-1',
          batchNumber: 'BATCH-001',
          batchVoided: true,
          stockTransactionId: 'transaction-1',
          quantity: 2.5,
          totalPurchaseCost: 250,
          unitCost: 100,
          mrp: 150,
          salesPrice: 125,
          taxRatePercent: 5,
          taxIncluded: false,
          purchaseTaxIncluded: true,
        ),
      ],
    );
    final secondReceipt = PurchaseOrderReceipt(
      receiptId: 'receipt-2',
      receiptNumber: 'GRN-002',
      receivedAt: DateTime(2026, 7, 15, 12),
      receivedByUserId: 'user-second-receiver',
      receivedByDisplayName: 'Arjun Receiver',
      lines: const [
        PurchaseOrderReceiptLine(
          receiptLineId: 'receipt-line-2',
          purchaseOrderLineId: 'line-2',
          itemId: 'item-2',
          inventoryBatchId: 'batch-2',
          batchNumber: 'BATCH-002',
          batchVoided: false,
          stockTransactionId: 'transaction-2',
          quantity: 4,
          totalPurchaseCost: 400,
          unitCost: 100,
          mrp: 175,
          salesPrice: 150,
          taxRatePercent: 12,
          taxIncluded: true,
          purchaseTaxIncluded: false,
        ),
      ],
    );

    await tester.pumpWidget(
      MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: const [Locale('en', 'IN')],
        home: SingleChildScrollView(
          child: PurchaseOrderReceiptHistory(
            receipts: [firstReceipt, secondReceipt],
          ),
        ),
      ),
    );
    await tester.tap(find.text('GRN-001'));
    await tester.pumpAndSettle();

    expect(find.text('Received by: Riya Receiver'), findsOneWidget);
    expect(find.text('Reference: REF-001'), findsOneWidget);
    expect(find.text('Notes: Counted at dock'), findsOneWidget);
    expect(find.text('Batch: BATCH-001'), findsOneWidget);
    expect(find.text('Batch state: Voided'), findsOneWidget);
    expect(find.text('Stock transaction: transaction-1'), findsOneWidget);
    expect(find.text('Quantity: 2.5'), findsOneWidget);
    expect(find.text('Total purchase cost: ₹250'), findsOneWidget);
    expect(find.text('Tax rate: 5%'), findsOneWidget);
    expect(find.text('Tax included: No'), findsOneWidget);
    expect(find.text('Purchase tax included: Yes'), findsOneWidget);

    await tester.tap(find.text('GRN-001'));
    await tester.pumpAndSettle();
    final secondTile = find.byType(ExpansionTile).at(1);
    await tester.ensureVisible(secondTile);
    await tester.pumpAndSettle();
    expect(find.text('GRN-002'), findsOneWidget);
    await tester.tap(secondTile);
    await tester.pumpAndSettle();

    expect(find.text('Received by: Arjun Receiver'), findsOneWidget);
    expect(find.text('Batch: BATCH-002'), findsOneWidget);
    expect(find.text('Batch state: Active'), findsOneWidget);
    expect(find.text('Stock transaction: transaction-2'), findsOneWidget);
    expect(find.text('Quantity: 4.0'), findsOneWidget);
  });
}
