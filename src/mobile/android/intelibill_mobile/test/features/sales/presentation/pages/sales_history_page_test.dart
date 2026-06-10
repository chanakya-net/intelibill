import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_list_item.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_summary.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_history_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/pages/sales_history_page.dart';

class _StubSalesHistoryController extends SalesHistoryController {
  _StubSalesHistoryController(this._state);

  final SalesHistoryState _state;

  @override
  SalesHistoryState build() => _state;
}

final _sampleSale = SaleListItem(
  saleId: 'sale-1',
  invoiceNumber: 'INV-2026-001',
  customerId: null,
  paymentMethod: 1,
  soldAt: DateTime.utc(2026, 5, 11, 10, 30),
  paidAmount: 500,
  dueAmount: 0,
  totalBeforeDiscount: 500,
  totalDiscountAmount: 0,
  totalAmount: 500,
  totalTaxAmount: 50,
  customerName: 'John Doe',
  customerPhone: '9999999999',
  itemCount: 2,
  returnNumbers: const [],
  status: 'paid',
  refundAmount: 0,
  dueReductionAmount: 0,
);

Widget _buildApp(SalesHistoryState state) {
  return ProviderScope(
    overrides: [
      salesHistoryControllerProvider.overrideWith(
        () => _StubSalesHistoryController(state),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const SalesHistoryPage(),
    ),
  );
}

void main() {
  group('SalesHistoryPage', () {
    testWidgets('shows loading indicator when loading', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          SalesHistoryState(
            isLoading: true,
            query: defaultSalesHistoryQuery(),
          ),
        ),
      );

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });

    testWidgets('shows KPI row and sale cards when loaded', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          SalesHistoryState(
            sales: [_sampleSale],
            summary: const SalesHistorySummary(
              periodSales: 500,
              invoiceCount: 1,
              refundAmount: 0,
            ),
            query: defaultSalesHistoryQuery(),
            totalCount: 1,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Period sales'), findsOneWidget);
      expect(find.text('INV-2026-001'), findsOneWidget);
      expect(find.text('John Doe'), findsOneWidget);
    });

    testWidgets('opens detail sheet when sale card is tapped', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          SalesHistoryState(
            sales: [_sampleSale],
            summary: const SalesHistorySummary(
              periodSales: 500,
              invoiceCount: 1,
              refundAmount: 0,
            ),
            query: defaultSalesHistoryQuery(),
            totalCount: 1,
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('INV-2026-001'));
      await tester.pumpAndSettle();

      expect(find.text('Sale details'), findsOneWidget);
      expect(find.text('9999999999'), findsWidgets);
    });
  });
}
