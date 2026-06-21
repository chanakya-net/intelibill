import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/receipts/sale_receipt_view.dart';

void main() {
  testWidgets('renders receipt details and settlement breakups', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.lightTheme,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: const [Locale('en', 'IN')],
        home: Scaffold(
          body: SingleChildScrollView(
            child: SalesReceiptView(sale: _saleReceipt()),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Receipt'), findsOneWidget);
    expect(find.text('INV-REC-001'), findsOneWidget);
    expect(find.text('Alice'), findsOneWidget);
    expect(find.text('9999999999'), findsOneWidget);
    expect(find.text('Line items'), findsOneWidget);
    expect(find.text('Notebook'), findsOneWidget);
    expect(find.textContaining('2 × 50.00'), findsOneWidget);
    expect(find.text('Payment split'), findsOneWidget);
    expect(find.text('Tax'), findsOneWidget);
    expect(find.textContaining('₹36'), findsWidgets);
    expect(find.text('CN-LOYALTY-001'), findsOneWidget);
    // R003: discount section
    expect(find.text('Discounts'), findsOneWidget);
    expect(find.textContaining('Flat'), findsOneWidget);
    // R004: credit note applied branch
    expect(find.text('Credit note applied'), findsOneWidget);
  });

  testWidgets(
    'applies injected formatter to line-item totals and totals section',
    (
      tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.lightTheme,
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: const [Locale('en', 'IN')],
          home: Scaffold(
            body: SingleChildScrollView(
              child: SalesReceiptView(
                sale: _saleReceipt(),
                formatter: (v) => 'FMT$v',
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      // Formatter applied to totals section amounts.
      expect(find.textContaining('FMT'), findsWidgets);
      // Line-item total uses formatter, not a hard-coded ₹.
      expect(find.textContaining('FMT100'), findsOneWidget);
    },
  );
}

SaleDetail _saleReceipt() {
  return SaleDetail(
    saleId: 'sale-100',
    invoiceNumber: 'INV-REC-001',
    customerId: 'cust-1',
    paymentMethod: 1,
    soldAt: DateTime.utc(2026, 6, 10, 10),
    paidAmount: 300,
    dueAmount: 0,
    totalBeforeDiscount: 560,
    totalDiscountAmount: 50,
    totalAmount: 546,
    totalTaxAmount: 36,
    customerName: 'Alice',
    customerPhone: '9999999999',
    items: [
      SaleDetailItem(
        saleItemId: 'item-1',
        lineType: 'Goods',
        lineCode: 'NB-1',
        itemName: 'Notebook',
        quantity: 2,
        salesPrice: 50,
        originalSalesPrice: 50,
        finalSalesPrice: 50,
        preTaxAmountBeforeDiscount: 100,
        itemDiscountAmount: 0,
        saleDiscountAmount: 0,
        taxableAmount: 100,
        taxAmount: 0,
        totalAmount: 100,
        savingsAmount: 0,
        taxRatePercent: 0,
        isPriceIncludingTax: false,
        hasPriceMismatch: false,
        returnedQuantity: 0,
        returnableQuantity: 0,
        returnStatus: 'none',
      ),
    ],
    settlements: [
      SaleDetailSettlement(
        settlementId: 'settlement-1',
        method: 'Cash',
        amount: 300,
        settledAt: DateTime.utc(2026, 6, 10, 10),
      ),
    ],
    discounts: const [
      SaleDetailDiscount(
        discountId: 'discount-1',
        type: 'Flat',
        value: '₹50',
        amount: 50,
      ),
    ],
    returns: const [],
    creditNoteRedemptions: const [
      SaleDetailCreditNoteRedemption(
        creditNoteId: 'cn-1',
        code: 'CN-LOYALTY-001',
        appliedAmount: 50,
      ),
    ],
    warnings: const [],
    status: 'paid',
    refundAmount: 0,
    dueReductionAmount: 0,
    creditNoteAppliedAmount: 36,
  );
}
