import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sale_detail_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/sale_detail_sheet.dart';

final _detail = SaleDetail(
  saleId: 'sale-1',
  invoiceNumber: 'INV-2026-001',
  customerId: null,
  customerName: 'John Doe',
  customerPhone: '9999999999',
  paymentMethod: 1,
  soldAt: DateTime.utc(2026, 5, 11, 10, 30),
  items: const [
    SaleDetailItem(
      itemId: 'item-1',
      name: 'Notebook',
      quantity: 2,
      rate: 100,
      tax: 18,
      total: 236,
    ),
  ],
  settlements: [
    SaleDetailSettlement(
      settlementId: 'settlement-1',
      method: 'Cash',
      amount: 200,
      settledAt: DateTime.utc(2026, 5, 11, 11),
    ),
  ],
  discounts: const [
    SaleDetailDiscount(
      discountId: 'discount-1',
      type: 'Promo',
      value: '10%',
      amount: 20,
    ),
  ],
  returns: [
    SaleDetailReturn(
      returnId: 'return-1',
      returnNumber: 'RET-1',
      items: [
        SaleDetailReturnItem(
          itemId: 'item-1',
          quantity: 1,
          amount: 100,
        ),
      ],
      amount: 100,
      returnedAt: DateTime.utc(2026, 5, 12, 9),
    ),
  ],
  redemptions: [
    SaleDetailRedemption(
      redemptionId: 'redemption-1',
      type: 'Loyalty',
      amount: 15,
      redeemedAt: DateTime.utc(2026, 5, 11, 11, 30),
    ),
  ],
  warnings: const [
    SaleDetailWarning(
      warningId: 'warning-1',
      type: 'inventory',
      message: 'Low stock detected',
    ),
  ],
  paidAmount: 200,
  dueAmount: 36,
  totalBeforeDiscount: 256,
  totalDiscountAmount: 20,
  totalAmount: 236,
  totalTaxAmount: 18,
  status: 'partiallyPaid',
  refundAmount: 0,
  dueReductionAmount: 0,
);

Widget _buildApp() {
  return ProviderScope(
    overrides: [
      saleDetailControllerProvider('sale-1').overrideWithValue(
        SaleDetailState(detail: _detail, isLoading: false),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const Scaffold(
        body: SaleDetailSheet(saleId: 'sale-1'),
      ),
    ),
  );
}

void main() {
  testWidgets('shows all sale detail sections', (tester) async {
    await tester.pumpWidget(_buildApp());
    await tester.pumpAndSettle();

    expect(find.text('Sale details'), findsOneWidget);
    expect(find.text('Line items'), findsOneWidget);
    expect(find.text('Totals'), findsOneWidget);
    expect(find.text('Discounts'), findsOneWidget);
    expect(find.text('Payment split'), findsOneWidget);
    expect(find.text('Returns'), findsOneWidget);
    expect(find.text('Redemptions'), findsOneWidget);
    expect(find.text('Warnings'), findsOneWidget);
    expect(find.text('Notebook'), findsOneWidget);
    expect(find.text('Low stock detected'), findsOneWidget);
    expect(find.text('Receipt'), findsOneWidget);
  });
}
