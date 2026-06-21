import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/get_sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/void_sale_return.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sale_detail_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_history_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_providers.dart';
import 'package:mocktail/mocktail.dart';

class MockGetSaleDetail extends Mock implements GetSaleDetail {}

class MockVoidSaleReturn extends Mock implements VoidSaleReturn {}

class _TrackingSalesHistoryController extends SalesHistoryController {
  static int refreshCount = 0;
  static int disposeCount = 0;

  static void reset() {
    refreshCount = 0;
    disposeCount = 0;
  }

  @override
  SalesHistoryState build() {
    ref.onDispose(() {
      disposeCount += 1;
    });
    return SalesHistoryState(
      query: SalesHistoryQuery(
        from: DateTime(2026, 1, 1),
        to: DateTime(2026, 1, 1, 23, 59, 59),
      ),
      isLoading: false,
    );
  }

  @override
  Future<void> refresh() async {
    refreshCount += 1;
    state = state.copyWith(isLoading: false, clearError: true);
  }
}

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
    creditNoteRedemptions: const [],
    warnings: const [],
    paidAmount: 500,
    dueAmount: 0,
    totalBeforeDiscount: 500,
    totalDiscountAmount: 0,
    totalAmount: 500,
    totalTaxAmount: 50,
    refundAmount: 0,
    dueReductionAmount: 0,
  );
}

void main() {
  late MockGetSaleDetail getSaleDetail;
  late MockVoidSaleReturn voidSaleReturn;

  setUp(() {
    getSaleDetail = MockGetSaleDetail();
    voidSaleReturn = MockVoidSaleReturn();
    _TrackingSalesHistoryController.reset();
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        getSaleDetailUseCaseProvider.overrideWithValue(getSaleDetail),
        voidSaleReturnUseCaseProvider.overrideWithValue(voidSaleReturn),
        salesHistoryControllerProvider.overrideWith(
          _TrackingSalesHistoryController.new,
        ),
      ],
    );
  }

  group('SaleDetailController', () {
    test('loads sale detail on initial build', () async {
      final detail = _saleDetail('sale-1');
      when(() => getSaleDetail(any())).thenAnswer((_) async => detail);

      final container = makeContainer();
      addTearDown(container.dispose);

      final subscription = container.listen(
        saleDetailControllerProvider('sale-1'),
        (_, __) {},
        fireImmediately: true,
      );
      addTearDown(subscription.close);
      await Future<void>.delayed(const Duration(milliseconds: 10));

      final state = container.read(saleDetailControllerProvider('sale-1'));
      expect(state.isLoading, isFalse);
      expect(state.detail, detail);
      expect(state.failure, isNull);
    });

    test('starts in loading state', () {
      when(
        () => getSaleDetail(any()),
      ).thenAnswer((_) async => _saleDetail('sale-1'));

      final container = makeContainer();
      addTearDown(container.dispose);

      expect(
        container.read(saleDetailControllerProvider('sale-1')).isLoading,
        isTrue,
      );
      expect(
        container.read(saleDetailControllerProvider('sale-1')).detail,
        isNull,
      );
    });

    test('loads sale detail on refresh', () async {
      final detail = _saleDetail('sale-1');
      when(() => getSaleDetail(any())).thenAnswer((_) async => detail);

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(saleDetailControllerProvider('sale-1').notifier)
          .refresh();

      final state = container.read(saleDetailControllerProvider('sale-1'));
      expect(state.isLoading, isFalse);
      expect(state.detail, isNotNull);
      expect(state.detail!.invoiceNumber, 'INV-001');
      expect(state.failure, isNull);
    });

    test('requires a non-empty void reason', () async {
      final detail = _saleDetail('sale-1');
      when(() => getSaleDetail(any())).thenAnswer((_) async => detail);

      final container = makeContainer();
      addTearDown(container.dispose);
      await container
          .read(saleDetailControllerProvider('sale-1').notifier)
          .refresh();

      final success = await container
          .read(saleDetailControllerProvider('sale-1').notifier)
          .voidSaleReturn(
            saleReturnId: 'return-1',
            reason: '   ',
          );

      expect(success, isFalse);
      verifyNever(
        () => voidSaleReturn(
          saleReturnId: any(named: 'saleReturnId'),
          reason: any(named: 'reason'),
        ),
      );
      final state = container.read(saleDetailControllerProvider('sale-1'));
      expect(state.isVoidSubmitting, isFalse);
      expect(state.voidFailure, isNull);
    });

    test(
      'refreshes sale detail and sales history on successful void',
      () async {
        final detail = _saleDetail('sale-1');
        when(() => getSaleDetail(any())).thenAnswer((_) async => detail);
        when(
          () => voidSaleReturn(
            saleReturnId: 'return-1',
            reason: 'Damaged',
          ),
        ).thenAnswer((_) async {});

        final container = makeContainer();
        addTearDown(container.dispose);
        container.read(salesHistoryControllerProvider);
        final historyController = container.read(
          salesHistoryControllerProvider.notifier,
        );
        historyController.state = historyController.state.copyWith(
          query: SalesHistoryQuery(
            from: DateTime(2026, 1, 10),
            to: DateTime(2026, 1, 20, 23, 59, 59),
          ),
          searchQuery: 'INV',
          statusFilter: 'partiallyPaid',
        );
        final preVoidHistoryState = container.read(
          salesHistoryControllerProvider,
        );

        await container
            .read(saleDetailControllerProvider('sale-1').notifier)
            .refresh();
        clearInteractions(getSaleDetail);

        final success = await container
            .read(saleDetailControllerProvider('sale-1').notifier)
            .voidSaleReturn(
              saleReturnId: 'return-1',
              reason: 'Damaged',
            );

        expect(success, isTrue);
        verify(
          () => voidSaleReturn(
            saleReturnId: 'return-1',
            reason: 'Damaged',
          ),
        ).called(1);
        verify(() => getSaleDetail('sale-1')).called(1);
        final postVoidHistoryState = container.read(
          salesHistoryControllerProvider,
        );
        expect(
          postVoidHistoryState.searchQuery,
          preVoidHistoryState.searchQuery,
        );
        expect(
          postVoidHistoryState.statusFilter,
          preVoidHistoryState.statusFilter,
        );
        expect(postVoidHistoryState.query.from, preVoidHistoryState.query.from);
        expect(postVoidHistoryState.query.to, preVoidHistoryState.query.to);
        expect(_TrackingSalesHistoryController.disposeCount, 0);
        expect(_TrackingSalesHistoryController.refreshCount, 1);
      },
    );

    test(
      'handles required fields absent in backend-style detail payload',
      () async {
        final detail = SaleDetail(
          saleId: 'sale-1',
          invoiceNumber: 'INV-101',
          customerId: null,
          customerName: 'Jane',
          customerPhone: null,
          paymentMethod: 1,
          soldAt: DateTime.utc(2026, 5, 11, 10),
          items: const [],
          paidAmount: 500,
          dueAmount: 0,
          totalBeforeDiscount: 500,
          totalDiscountAmount: 0,
          totalAmount: 500,
          totalTaxAmount: 50,
          refundAmount: 0,
          dueReductionAmount: 0,
        );

        when(() => getSaleDetail(any())).thenAnswer((_) async => detail);

        final container = makeContainer();
        addTearDown(container.dispose);

        await container
            .read(saleDetailControllerProvider('sale-1').notifier)
            .refresh();

        final state = container.read(saleDetailControllerProvider('sale-1'));
        expect(state.failure, isNull);
        expect(state.detail?.status, isNull);
        expect(state.detail?.settlements, isEmpty);
        expect(state.detail?.discounts, isEmpty);
        expect(state.detail?.warnings, isEmpty);
      },
    );

    test('handles load failure on refresh', () async {
      final error = AppException(
        failure: const Failure.network(message: 'Network error'),
      );
      when(() => getSaleDetail(any())).thenThrow(error);

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(saleDetailControllerProvider('sale-1').notifier)
          .refresh();

      final state = container.read(saleDetailControllerProvider('sale-1'));
      expect(state.isLoading, isFalse);
      expect(state.detail, isNull);
      expect(state.failure, isNotNull);
    });
  });
}
