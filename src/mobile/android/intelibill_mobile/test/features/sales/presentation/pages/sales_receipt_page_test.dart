import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/get_sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_providers.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/pages/sales_receipt_page.dart';
import 'package:intelibill_mobile/src/shared/documents/document_page_format.dart';
import 'package:intelibill_mobile/src/shared/documents/document_preview_scaffold.dart';
import 'package:mocktail/mocktail.dart';
import 'package:pdf/pdf.dart';

class MockGetSaleDetail extends Mock implements GetSaleDetail {}

void main() {
  final initial = _saleDetail(invoiceNumber: 'INV-INIT-001');

  testWidgets('renders 80 mm preview with correct descriptor', (tester) async {
    final getSaleDetail = MockGetSaleDetail();
    when(() => getSaleDetail(any())).thenAnswer(
      (_) async => _saleDetail(invoiceNumber: 'INV-LIVE-001'),
    );

    await tester.pumpWidget(_buildPage(getSaleDetail: getSaleDetail));
    await tester.pump(const Duration(milliseconds: 50));

    final scaffold = tester.widget<DocumentPreviewScaffold>(
      find.byType(DocumentPreviewScaffold),
    );

    expect(scaffold.descriptor.pageFormat, DocumentPageFormat.mm80);
    expect(scaffold.descriptor.title, 'Receipt');
    expect(scaffold.descriptor.filename, 'sale-receipt-INV-LIVE-001.pdf');

    final bytes = await scaffold.onBuild(PdfPageFormat.roll80);
    expect(bytes.take(4), orderedEquals('%PDF'.codeUnits));
  });

  testWidgets('previews initialSale then reloads with detail refresh', (
    tester,
  ) async {
    final getSaleDetail = MockGetSaleDetail();
    final completer = Completer<SaleDetail>();
    when(() => getSaleDetail(any())).thenAnswer((_) => completer.future);

    await tester.pumpWidget(
      _buildPage(
        getSaleDetail: getSaleDetail,
        initialSale: initial,
      ),
    );
    await tester.pump();

    final scaffoldBefore = tester.widget<DocumentPreviewScaffold>(
      find.byType(DocumentPreviewScaffold),
    );
    expect(scaffoldBefore.descriptor.filename, 'sale-receipt-INV-INIT-001.pdf');

    completer.complete(_saleDetail(invoiceNumber: 'INV-LIVE-002'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    final scaffoldAfter = tester.widget<DocumentPreviewScaffold>(
      find.byType(DocumentPreviewScaffold),
    );
    expect(scaffoldAfter.descriptor.filename, 'sale-receipt-INV-LIVE-002.pdf');
  });

  testWidgets('shows loading indicator while loading and no sale', (
    tester,
  ) async {
    final getSaleDetail = MockGetSaleDetail();
    final completer = Completer<SaleDetail>();
    when(() => getSaleDetail(any())).thenAnswer((_) => completer.future);

    await tester.pumpWidget(_buildPage(getSaleDetail: getSaleDetail));

    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    expect(find.text('Unable to load sale details'), findsNothing);
  });

  testWidgets('shows failure then retry reloads receipt preview', (
    tester,
  ) async {
    final getSaleDetail = MockGetSaleDetail();
    var attempts = 0;
    when(() => getSaleDetail(any())).thenAnswer((_) async {
      attempts += 1;
      if (attempts == 1) {
        throw Exception('network down');
      }
      return _saleDetail(invoiceNumber: 'INV-RETRY-001');
    });

    await tester.pumpWidget(_buildPage(getSaleDetail: getSaleDetail));
    await tester.pump(const Duration(milliseconds: 50));

    expect(find.text('Unable to load sale details'), findsOneWidget);
    expect(find.text('Retry'), findsOneWidget);
    await tester.tap(find.text('Retry'));
    for (var i = 0; i < 5; i += 1) {
      await tester.pump(const Duration(milliseconds: 20));
    }

    expect(attempts, 2);
    expect(find.byType(DocumentPreviewScaffold), findsOneWidget);
    final scaffold = tester.widget<DocumentPreviewScaffold>(
      find.byType(DocumentPreviewScaffold),
    );
    expect(scaffold.descriptor.filename, 'sale-receipt-INV-RETRY-001.pdf');
  });
}

Widget _buildPage({
  required GetSaleDetail getSaleDetail,
  SaleDetail? initialSale,
}) {
  return ProviderScope(
    overrides: [
      getSaleDetailUseCaseProvider.overrideWithValue(getSaleDetail),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: SalesReceiptPage(
        saleId: 'sale-1',
        initialSale: initialSale,
      ),
    ),
  );
}

SaleDetail _saleDetail({
  String invoiceNumber = 'INV-001',
}) {
  return SaleDetail(
    saleId: 'sale-1',
    invoiceNumber: invoiceNumber,
    customerId: 'cust-1',
    paymentMethod: 1,
    soldAt: DateTime(2026, 6, 10, 10),
    paidAmount: 300,
    dueAmount: 0,
    totalBeforeDiscount: 560,
    totalDiscountAmount: 50,
    totalAmount: 546,
    totalTaxAmount: 36,
    customerName: 'Alice',
    customerPhone: '9999999999',
    items: const [
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
    discounts: const [
      SaleDetailDiscount(
        discountId: 'discount-1',
        type: 'Flat',
        value: '50',
        amount: 50,
      ),
    ],
    creditNoteRedemptions: const [
      SaleDetailCreditNoteRedemption(
        creditNoteId: 'cn-1',
        code: 'CN-LOYALTY-001',
        appliedAmount: 36,
      ),
    ],
    status: 'paid',
    creditNoteAppliedAmount: 36,
  );
}
