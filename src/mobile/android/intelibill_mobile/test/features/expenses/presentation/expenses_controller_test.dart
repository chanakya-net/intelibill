import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expenses.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetExpenses extends Mock implements GetExpenses {}

class MockGetExpenseDetail extends Mock implements GetExpenseDetail {}

class _TestExpensesController extends ExpensesController {
  _TestExpensesController(this._initialState);

  final ExpensesState _initialState;

  @override
  ExpensesState build() => _initialState;
}

ExpenseDetail _detail(String id) => ExpenseDetail(
  id: id,
  shopId: 'shop-1',
  categoryId: 'category-1',
  categoryName: 'Rent',
  amount: 100,
  paidTo: 'Payee',
  expenseDate: DateTime(2026, 7),
  actorUserId: 'user-1',
  isVoided: false,
  createdAt: DateTime(2026, 7, 1, 8, 30),
);

final _page = ExpensePage(
  items: [
    ExpenseListItem(
      id: 'expense-1',
      amount: 100,
      categoryName: 'Travel',
      paidTo: 'Taxi',
      expenseDate: DateTime(2026, 7),
      isVoided: false,
    ),
  ],
  totalCount: 1,
  pageNumber: 1,
  pageSize: 20,
);

final _pageWithStatuses = ExpensePage(
  items: [
    ExpenseListItem(
      id: 'active-expense',
      amount: 100,
      categoryName: 'Travel',
      paidTo: 'Taxi',
      expenseDate: DateTime(2026, 7),
      isVoided: false,
    ),
    ExpenseListItem(
      id: 'voided-expense',
      amount: 200,
      categoryName: 'Supplies',
      paidTo: 'Vendor',
      expenseDate: DateTime(2026, 7, 2),
      isVoided: true,
    ),
  ],
  totalCount: 2,
  pageNumber: 1,
  pageSize: 20,
);

void main() {
  ProviderContainer detailContainer(MockGetExpenseDetail getDetail) {
    return ProviderContainer(
      overrides: [
        getExpenseDetailUseCaseProvider.overrideWithValue(getDetail),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(ExpensesState(page: _page)),
        ),
      ],
    );
  }

  test('loads first expenses page', () async {
    final getExpenses = MockGetExpenses();
    when(getExpenses.call).thenAnswer((_) async => _page);
    final container = ProviderContainer(
      overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
    );
    addTearDown(container.dispose);

    expect(container.read(expensesControllerProvider).isLoading, isTrue);
    await container.read(expensesControllerProvider.notifier).refresh();

    final state = container.read(expensesControllerProvider);
    expect(state.page, _page);
    expect(state.isLoading, isFalse);
    expect(state.failure, isNull);
    verify(getExpenses.call).called(greaterThanOrEqualTo(1));
  });

  test('exposes mapped failure when loading fails', () async {
    final getExpenses = MockGetExpenses();
    when(getExpenses.call).thenThrow(
      AppException(failure: const Failure.network()),
    );
    final container = ProviderContainer(
      overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
    );
    addTearDown(container.dispose);

    await container.read(expensesControllerProvider.notifier).refresh();

    final state = container.read(expensesControllerProvider);
    expect(state.isLoading, isFalse);
    expect(state.failure, isA<NetworkFailure>());
  });

  test('preserves prior page data when refresh fails', () async {
    final getExpenses = MockGetExpenses();
    var callCount = 0;
    when(getExpenses.call).thenAnswer((_) async {
      callCount++;
      if (callCount == 1) return _page;
      throw AppException(failure: const Failure.network());
    });
    final container = ProviderContainer(
      overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
    );
    addTearDown(container.dispose);

    await container.read(expensesControllerProvider.notifier).refresh();
    var state = container.read(expensesControllerProvider);
    expect(state.page, _page);
    expect(state.failure, isNull);

    await container.read(expensesControllerProvider.notifier).refresh();

    state = container.read(expensesControllerProvider);
    expect(state.page, _page);
    expect(state.failure, isA<NetworkFailure>());
  });

  test(
    'filters loaded expenses by status without another repository call',
    () async {
      final getExpenses = MockGetExpenses();
      when(getExpenses.call).thenAnswer((_) async => _pageWithStatuses);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();
      clearInteractions(getExpenses);
      final controller = container.read(expensesControllerProvider.notifier);

      controller.updateStatusFilter(ExpenseStatusFilter.active);
      expect(
        container.read(expensesControllerProvider).filteredExpenses,
        hasLength(1),
      );
      expect(
        container.read(expensesControllerProvider).filteredExpenses.single.id,
        'active-expense',
      );

      controller.updateStatusFilter(ExpenseStatusFilter.voided);
      expect(
        container.read(expensesControllerProvider).filteredExpenses.single.id,
        'voided-expense',
      );

      controller.updateStatusFilter(ExpenseStatusFilter.all);
      expect(
        container.read(expensesControllerProvider).filteredExpenses,
        hasLength(2),
      );
      verifyNever(getExpenses.call);
    },
  );

  test(
    'returns empty filtered results without another repository call',
    () async {
      final getExpenses = MockGetExpenses();
      when(getExpenses.call).thenAnswer((_) async => _page);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();
      clearInteractions(getExpenses);

      container
          .read(expensesControllerProvider.notifier)
          .updateStatusFilter(ExpenseStatusFilter.voided);

      expect(
        container.read(expensesControllerProvider).filteredExpenses,
        isEmpty,
      );
      verifyNever(getExpenses.call);
    },
  );

  test('openExpense loads detail without changing ledger state', () async {
    final getDetail = MockGetExpenseDetail();
    final completer = Completer<ExpenseDetail>();
    when(() => getDetail('expense-1')).thenAnswer((_) => completer.future);
    final container = detailContainer(getDetail);
    addTearDown(container.dispose);
    final page = container.read(expensesControllerProvider).page;

    final opening = container
        .read(expensesControllerProvider.notifier)
        .openExpense('expense-1');
    expect(container.read(expensesControllerProvider).isDetailLoading, isTrue);
    expect(container.read(expensesControllerProvider).page, page);

    final detail = _detail('expense-1');
    completer.complete(detail);
    await opening;
    final state = container.read(expensesControllerProvider);
    expect(state.selectedExpense, detail);
    expect(state.isDetailLoading, isFalse);
    expect(state.page, page);
  });

  test('detail failure and retry stay operation-specific', () async {
    final getDetail = MockGetExpenseDetail();
    when(() => getDetail('expense-1')).thenThrow(
      AppException(failure: const Failure.timeout(message: 'detail slow')),
    );
    final container = detailContainer(getDetail);
    addTearDown(container.dispose);

    await container
        .read(expensesControllerProvider.notifier)
        .openExpense('expense-1');
    expect(
      container.read(expensesControllerProvider).detailFailure,
      isA<TimeoutFailure>(),
    );

    final detail = _detail('expense-1');
    when(() => getDetail('expense-1')).thenAnswer((_) async => detail);
    await container
        .read(expensesControllerProvider.notifier)
        .retryExpenseDetail();
    expect(container.read(expensesControllerProvider).selectedExpense, detail);
    expect(container.read(expensesControllerProvider).detailFailure, isNull);
  });

  test('stale same-id failure cannot replace reopened detail state', () async {
    final getDetail = MockGetExpenseDetail();
    final first = Completer<ExpenseDetail>();
    final second = Completer<ExpenseDetail>();
    var calls = 0;
    when(() => getDetail('expense-1')).thenAnswer(
      (_) => ++calls == 1 ? first.future : second.future,
    );
    final container = detailContainer(getDetail);
    addTearDown(container.dispose);
    final controller = container.read(expensesControllerProvider.notifier);

    final firstOpening = controller.openExpense('expense-1');
    controller.clearSelectedExpense();
    final secondOpening = controller.openExpense('expense-1');
    final detail = _detail('expense-1');
    second.complete(detail);
    await secondOpening;
    first.completeError(
      AppException(failure: const Failure.network(message: 'stale')),
    );
    await firstOpening;

    final state = container.read(expensesControllerProvider);
    expect(state.selectedExpense, detail);
    expect(state.detailFailure, isNull);
  });

  test('clearSelectedExpense removes only detail state', () {
    final container = detailContainer(MockGetExpenseDetail());
    addTearDown(container.dispose);
    final controller = container.read(expensesControllerProvider.notifier);
    controller.clearSelectedExpense();
    final state = container.read(expensesControllerProvider);
    expect(state.selectedExpenseId, isNull);
    expect(state.selectedExpense, isNull);
    expect(state.detailFailure, isNull);
  });
}
