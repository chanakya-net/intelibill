import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/get_sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sale_detail_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetSaleDetail extends Mock implements GetSaleDetail {}

SaleDetail _saleDetail(String id) {
  return SaleDetail(
    saleId: id,
    invoiceNumber: 'INV-001',
    customerId: null,
    customerName: 'John',
    customerPhone: '1234567890',
    paymentMethod: 1,
    soldAt: DateTime.utc(2026, 5, 11, 10),
    items: const [],
    settlements: const [],
    discounts: const [],
    returns: const [],
    redemptions: const [],
    warnings: const [],
    paidAmount: 500,
    dueAmount: 0,
    totalBeforeDiscount: 500,
    totalDiscountAmount: 0,
    totalAmount: 500,
    totalTaxAmount: 50,
    status: 'paid',
    refundAmount: 0,
    dueReductionAmount: 0,
  );
}

void main() {
  late MockGetSaleDetail getSaleDetail;

  setUp(() {
    getSaleDetail = MockGetSaleDetail();
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [getSaleDetailProvider.overrideWithValue(getSaleDetail)],
    );
  }

  group('SaleDetailController', () {
    test('starts in loading state', () {
      when(() => getSaleDetail(any())).thenAnswer((_) async => _saleDetail('sale-1'));

      final container = makeContainer();
      addTearDown(container.dispose);

      expect(container.read(saleDetailControllerProvider('sale-1')).isLoading,
          isTrue);
      expect(container.read(saleDetailControllerProvider('sale-1')).detail,
          isNull);
    });

    test('loads sale detail on refresh', () async {
      final detail = _saleDetail('sale-1');
      when(() => getSaleDetail(any())).thenAnswer((_) async => detail);

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(saleDetailControllerProvider('sale-1').notifier)
          .refresh('sale-1');

      final state = container.read(saleDetailControllerProvider('sale-1'));
      expect(state.isLoading, isFalse);
      expect(state.detail, isNotNull);
      expect(state.detail!.invoiceNumber, 'INV-001');
      expect(state.failure, isNull);
    });

    test('handles load failure on refresh', () async {
      final error = AppException(
        failure: const Failure.network(message: 'Network error'),
      );
      when(() => getSaleDetail(any())).thenThrow(error);

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(saleDetailControllerProvider('sale-1').notifier)
          .refresh('sale-1');

      final state = container.read(saleDetailControllerProvider('sale-1'));
      expect(state.isLoading, isFalse);
      expect(state.detail, isNull);
      expect(state.failure, isNotNull);
    });
  });
}
