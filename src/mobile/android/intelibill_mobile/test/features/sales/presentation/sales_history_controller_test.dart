import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_list_item.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_summary.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/get_sales_history.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_history_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetSalesHistory extends Mock implements GetSalesHistory {}

SaleListItem _sale(String id, String invoice) {
  return SaleListItem(
    saleId: id,
    invoiceNumber: invoice,
    customerId: null,
    paymentMethod: 1,
    soldAt: DateTime.utc(2026, 5, 11, 10),
    paidAmount: 500,
    dueAmount: 0,
    totalBeforeDiscount: 500,
    totalDiscountAmount: 0,
    totalAmount: 500,
    totalTaxAmount: 50,
    customerName: 'John',
    customerPhone: null,
    itemCount: 2,
    returnNumbers: const [],
    status: 'paid',
    refundAmount: 0,
    dueReductionAmount: 0,
  );
}

SalesHistoryResult _result(List<SaleListItem> items) {
  return SalesHistoryResult(
    items: items,
    totalCount: items.length,
    pageNumber: 1,
    pageSize: 20,
    summary: const SalesHistorySummary(
      periodSales: 500,
      invoiceCount: 1,
      refundAmount: 0,
    ),
  );
}

void main() {
  late MockGetSalesHistory getSalesHistory;

  setUpAll(() {
    registerFallbackValue(defaultSalesHistoryQuery());
  });

  setUp(() {
    getSalesHistory = MockGetSalesHistory();
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [getSalesHistoryProvider.overrideWithValue(getSalesHistory)],
    );
  }

  group('SalesHistoryController', () {
    test('starts in loading state', () {
      when(() => getSalesHistory(any())).thenAnswer(
        (_) async => _result([_sale('sale-1', 'INV-001')]),
      );

      final container = makeContainer();
      addTearDown(container.dispose);

      expect(container.read(salesHistoryControllerProvider).isLoading, isTrue);
      expect(container.read(salesHistoryControllerProvider).sales, isEmpty);
    });

    test('loads sales history on refresh', () async {
      when(() => getSalesHistory(any())).thenAnswer(
        (_) async => _result([_sale('sale-1', 'INV-001')]),
      );

      final container = makeContainer();
      addTearDown(container.dispose);

      await container.read(salesHistoryControllerProvider.notifier).refresh();

      final state = container.read(salesHistoryControllerProvider);
      expect(state.isLoading, isFalse);
      expect(state.sales.length, 1);
      expect(state.sales.first.invoiceNumber, 'INV-001');
      expect(state.failure, isNull);
    });
  });
}
