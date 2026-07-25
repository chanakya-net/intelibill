import 'dart:async';
import 'dart:typed_data';

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
import 'package:intelibill_mobile/src/shared/documents/output/document_export_providers.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_output_gateway.dart';
import 'package:mocktail/mocktail.dart';
import 'package:pdf/pdf.dart';

class MockGetSaleDetail extends Mock implements GetSaleDetail {}

class FakeDocumentOutputGateway implements DocumentOutputGateway {
  bool failPrint = false;
  bool failShare = false;
  Completer<void>? printCompleter;
  Completer<void>? shareCompleter;
  int printCallCount = 0;
  int shareCallCount = 0;
  Uint8List? lastPrintBytes;
  Uint8List? lastShareBytes;
  String? lastPrintFilename;
  String? lastShareFilename;

  @override
  Future<void> print({
    required Uint8List bytes,
    required String filename,
  }) async {
    printCallCount += 1;
    lastPrintBytes = bytes;
    lastPrintFilename = filename;
    if (failPrint) {
      throw PlatformPrintFailure(message: 'printer unavailable');
    }
    await printCompleter?.future;
  }

  @override
  Future<void> share({
    required Uint8List bytes,
    required String filename,
    required String title,
  }) async {
    shareCallCount += 1;
    lastShareBytes = bytes;
    lastShareFilename = filename;
    if (failShare) {
      throw PlatformShareFailure(message: 'share unavailable');
    }
    await shareCompleter?.future;
  }
}

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

  testWidgets('exports sanitized 80 mm receipt bytes through print and share', (
    tester,
  ) async {
    final getSaleDetail = MockGetSaleDetail();
    final outputGateway = FakeDocumentOutputGateway();
    when(() => getSaleDetail(any())).thenAnswer(
      (_) async => _saleDetail(invoiceNumber: 'INV / 2026'),
    );

    await tester.pumpWidget(
      _buildPage(
        getSaleDetail: getSaleDetail,
        outputGateway: outputGateway,
      ),
    );
    await _pumpUntilReady(tester);

    final scaffold = tester.widget<DocumentPreviewScaffold>(
      find.byType(DocumentPreviewScaffold),
    );
    final bytes = await scaffold.onBuild(scaffold.descriptor.pdfPageFormat);

    expect(find.byTooltip('Print'), findsOneWidget);
    expect(find.byTooltip('Share'), findsOneWidget);
    expect(_actionButton(tester, 'Print').onPressed, isNotNull);
    expect(_actionButton(tester, 'Share').onPressed, isNotNull);

    await tester.tap(find.byTooltip('Print'));
    await tester.pump();
    await tester.tap(find.byTooltip('Share'));
    await tester.pump();

    expect(scaffold.descriptor.pageFormat, DocumentPageFormat.mm80);
    expect(scaffold.descriptor.filename, 'sale-receipt-INV-2026.pdf');
    expect(bytes.take(4), orderedEquals('%PDF'.codeUnits));
    expect(outputGateway.printCallCount, 1);
    expect(outputGateway.shareCallCount, 1);
    expect(
      outputGateway.lastPrintBytes!.take(4),
      orderedEquals('%PDF'.codeUnits),
    );
    expect(
      outputGateway.lastShareBytes!.take(4),
      orderedEquals('%PDF'.codeUnits),
    );
    expect(outputGateway.lastPrintFilename, 'sale-receipt-INV-2026.pdf');
    expect(outputGateway.lastShareFilename, 'sale-receipt-INV-2026.pdf');
  });

  testWidgets('retains receipt preview and retries after output failures', (
    tester,
  ) async {
    final getSaleDetail = MockGetSaleDetail();
    final outputGateway = FakeDocumentOutputGateway()
      ..failPrint = true
      ..failShare = true;
    when(() => getSaleDetail(any())).thenAnswer(
      (_) async => _saleDetail(invoiceNumber: 'INV-RETRY-OUTPUT'),
    );

    await tester.pumpWidget(
      _buildPage(
        getSaleDetail: getSaleDetail,
        outputGateway: outputGateway,
      ),
    );
    await _pumpUntilReady(tester);

    await tester.tap(find.byTooltip('Print'));
    await tester.pump();
    expect(
      find.text('Could not print the document. Try again.'),
      findsOneWidget,
    );
    expect(find.byType(DocumentPreviewScaffold), findsOneWidget);

    outputGateway.failPrint = false;
    await tester.tap(find.byTooltip('Print'));
    await tester.pump();
    expect(outputGateway.printCallCount, 2);

    await tester.tap(find.byTooltip('Share'));
    await tester.pump();
    expect(find.byType(DocumentPreviewScaffold), findsOneWidget);

    outputGateway.failShare = false;
    await tester.tap(find.byTooltip('Share'));
    await tester.pump();
    expect(outputGateway.shareCallCount, 2);
    verify(() => getSaleDetail('sale-1')).called(1);
  });

  testWidgets('suppresses duplicate in-flight receipt output actions', (
    tester,
  ) async {
    final getSaleDetail = MockGetSaleDetail();
    final outputGateway = FakeDocumentOutputGateway()
      ..printCompleter = Completer<void>()
      ..shareCompleter = Completer<void>();
    when(() => getSaleDetail(any())).thenAnswer(
      (_) async => _saleDetail(),
    );

    await tester.pumpWidget(
      _buildPage(
        getSaleDetail: getSaleDetail,
        outputGateway: outputGateway,
      ),
    );
    await _pumpUntilReady(tester);

    await tester.tap(find.byTooltip('Print'));
    await tester.pump();
    expect(_actionButton(tester, 'Print').onPressed, isNull);
    await tester.tap(find.byTooltip('Print'));
    await tester.pump();
    expect(outputGateway.printCallCount, 1);
    outputGateway.printCompleter!.complete();
    await tester.pump();
    expect(_actionButton(tester, 'Print').onPressed, isNotNull);

    await tester.tap(find.byTooltip('Share'));
    await tester.pump();
    expect(_actionButton(tester, 'Share').onPressed, isNull);
    await tester.tap(find.byTooltip('Share'));
    await tester.pump();
    expect(outputGateway.shareCallCount, 1);
    outputGateway.shareCompleter!.complete();
    await tester.pump();
    expect(_actionButton(tester, 'Share').onPressed, isNotNull);
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
    expect(find.byTooltip('Print'), findsNothing);
    expect(find.byTooltip('Share'), findsNothing);
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
    expect(find.byTooltip('Print'), findsNothing);
    expect(find.byTooltip('Share'), findsNothing);
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

Future<void> _pumpUntilReady(WidgetTester tester) async {
  for (var i = 0; i < 10; i += 1) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

IconButton _actionButton(WidgetTester tester, String tooltip) {
  final icon = tooltip == 'Print' ? Icons.print : Icons.share;
  return tester.widget<IconButton>(
    find.ancestor(
      of: find.byIcon(icon),
      matching: find.byType(IconButton),
    ),
  );
}

Widget _buildPage({
  required GetSaleDetail getSaleDetail,
  SaleDetail? initialSale,
  DocumentOutputGateway? outputGateway,
}) {
  return ProviderScope(
    overrides: [
      getSaleDetailUseCaseProvider.overrideWithValue(getSaleDetail),
      if (outputGateway != null)
        documentOutputGatewayProvider.overrideWithValue(outputGateway),
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
