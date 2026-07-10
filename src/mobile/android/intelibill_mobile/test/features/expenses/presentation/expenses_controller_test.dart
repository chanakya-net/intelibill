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

class _MockGetExpenses extends Mock implements GetExpenses {}

class _MockGetExpenseDetail extends Mock implements GetExpenseDetail {}

class _TestExpensesController extends ExpensesController {
  _TestExpensesController(this._initialState);

  final ExpensesState _initialState;

  @override
  ExpensesState build() => _initialState;
}

ExpenseListItem _expense(
  String id,
  String category, {
  bool isVoided = false,
}) {
  return ExpenseListItem(
    id: id,
    amount: 100,
    categoryName: category,
    paidTo: 'Payee',
    expenseDate: DateTime(2026, 7),
    isVoided: isVoided,
  );
}

ExpensesPage _page(List<ExpenseListItem> items) {
  return ExpensesPage(
    items: items,
    totalCount: items.length,
    pageNumber: 1,
    pageSize: 20,
  );
}

ExpenseDetail _detail(String id) {
  return ExpenseDetail(
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
}

void main() {
  late _MockGetExpenses getExpenses;
  late _MockGetExpenseDetail getExpenseDetail;

  setUp(() {
    getExpenses = _MockGetExpenses();
    getExpenseDetail = _MockGetExpenseDetail();
  });

  ProviderContainer makeContainer(ExpensesState initialState) {
    return ProviderContainer(
      overrides: [
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        getExpenseDetailUseCaseProvider.overrideWithValue(getExpenseDetail),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(initialState),
        ),
      ],
    );
  }

  test('all status filter exposes every loaded expense', () {
    final expenses = [
      _expense('expense-1', 'Rent'),
      _expense('expense-2', 'Travel', isVoided: true),
    ];
    final state = ExpensesState(expenses: expenses, totalCount: 2);

    expect(state.statusFilter, ExpenseStatusFilter.all);
    expect(state.filteredExpenses, expenses);
  });

  test('changing status filters never fetches additional rows', () {
    final active = _expense('expense-1', 'Rent');
    final voided = _expense('expense-2', 'Travel', isVoided: true);
    final container = makeContainer(
      ExpensesState(expenses: [active, voided], totalCount: 2),
    );
    addTearDown(container.dispose);

    container
        .read(expensesControllerProvider.notifier)
        .updateStatusFilter(ExpenseStatusFilter.active);

    expect(
      container.read(expensesControllerProvider).filteredExpenses,
      [active],
    );

    container
        .read(expensesControllerProvider.notifier)
        .updateStatusFilter(ExpenseStatusFilter.voided);
    expect(
      container.read(expensesControllerProvider).filteredExpenses,
      [voided],
    );

    container
        .read(expensesControllerProvider.notifier)
        .updateStatusFilter(ExpenseStatusFilter.all);
    expect(
      container.read(expensesControllerProvider).filteredExpenses,
      [active, voided],
    );
    verifyNever(() => getExpenses());
  });

  test('voided status filter keeps only loaded voided rows', () {
    final active = _expense('expense-1', 'Rent');
    final voided = _expense('expense-2', 'Travel', isVoided: true);
    final container = makeContainer(
      ExpensesState(expenses: [active, voided], totalCount: 2),
    );
    addTearDown(container.dispose);

    container
        .read(expensesControllerProvider.notifier)
        .updateStatusFilter(ExpenseStatusFilter.voided);

    final state = container.read(expensesControllerProvider);
    expect(state.statusFilter, ExpenseStatusFilter.voided);
    expect(state.filteredExpenses, [voided]);
  });

  test('refresh keeps existing rows visible while loading page one', () async {
    final completer = Completer<ExpensesPage>();
    when(() => getExpenses()).thenAnswer((_) => completer.future);
    final existing = [_expense('expense-1', 'Rent')];
    final container = makeContainer(
      ExpensesState(expenses: existing, totalCount: 1),
    );
    addTearDown(container.dispose);

    final refresh = container
        .read(expensesControllerProvider.notifier)
        .refresh();

    final refreshing = container.read(expensesControllerProvider);
    expect(refreshing.expenses, existing);
    expect(refreshing.isLoading, isFalse);
    expect(refreshing.isRefreshing, isTrue);

    final replacement = [_expense('expense-2', 'Travel')];
    completer.complete(_page(replacement));
    await refresh;

    final loaded = container.read(expensesControllerProvider);
    expect(loaded.expenses, replacement);
    expect(loaded.totalCount, 1);
    expect(loaded.isRefreshing, isFalse);
    expect(loaded.listFailure, isNull);
  });

  test('refresh failure preserves existing rows and exposes failure', () async {
    const failure = Failure.network(message: 'offline');
    when(
      () => getExpenses(),
    ).thenThrow(AppException(failure: failure));
    final existing = [_expense('expense-1', 'Rent')];
    final container = makeContainer(
      ExpensesState(expenses: existing, totalCount: 1),
    );
    addTearDown(container.dispose);

    await container.read(expensesControllerProvider.notifier).refresh();

    final state = container.read(expensesControllerProvider);
    expect(state.expenses, existing);
    expect(state.totalCount, 1);
    expect(state.isLoading, isFalse);
    expect(state.isRefreshing, isFalse);
    expect(state.listFailure, failure);
  });

  test('initial load failure exposes a full-page list failure', () async {
    const failure = Failure.timeout(message: 'slow');
    when(
      () => getExpenses(),
    ).thenThrow(AppException(failure: failure));
    final container = makeContainer(const ExpensesState());
    addTearDown(container.dispose);

    await container.read(expensesControllerProvider.notifier).refresh();

    final state = container.read(expensesControllerProvider);
    expect(state.expenses, isEmpty);
    expect(state.isLoading, isFalse);
    expect(state.isRefreshing, isFalse);
    expect(state.listFailure, failure);
  });

  test('retry clears initial failure and can resolve to empty', () async {
    final completer = Completer<ExpensesPage>();
    when(() => getExpenses()).thenAnswer((_) => completer.future);
    final container = makeContainer(
      const ExpensesState(
        listFailure: Failure.network(message: 'offline'),
      ),
    );
    addTearDown(container.dispose);

    final retry = container.read(expensesControllerProvider.notifier).refresh();

    final retrying = container.read(expensesControllerProvider);
    expect(retrying.isLoading, isTrue);
    expect(retrying.listFailure, isNull);

    completer.complete(_page(const []));
    await retry;

    final state = container.read(expensesControllerProvider);
    expect(state.expenses, isEmpty);
    expect(state.totalCount, 0);
    expect(state.isLoading, isFalse);
    expect(state.listFailure, isNull);
  });

  test('openExpense loads detail without changing ledger state', () async {
    final completer = Completer<ExpenseDetail>();
    when(
      () => getExpenseDetail('expense-1'),
    ).thenAnswer((_) => completer.future);
    final expenses = [_expense('expense-1', 'Rent')];
    const listFailure = Failure.network(message: 'ledger offline');
    final container = makeContainer(
      ExpensesState(
        expenses: expenses,
        totalCount: 1,
        listFailure: listFailure,
      ),
    );
    addTearDown(container.dispose);

    final opening = container
        .read(expensesControllerProvider.notifier)
        .openExpense('expense-1');

    final loading = container.read(expensesControllerProvider);
    expect(loading.selectedExpenseId, 'expense-1');
    expect(loading.isDetailLoading, isTrue);
    expect(loading.expenses, expenses);
    expect(loading.listFailure, listFailure);

    final detail = _detail('expense-1');
    completer.complete(detail);
    await opening;

    final loaded = container.read(expensesControllerProvider);
    expect(loaded.selectedExpense, detail);
    expect(loaded.isDetailLoading, isFalse);
    expect(loaded.detailFailure, isNull);
    expect(loaded.expenses, expenses);
    expect(loaded.listFailure, listFailure);
  });

  test('detail failure and retry stay operation-specific', () async {
    const detailFailure = Failure.timeout(message: 'detail slow');
    when(
      () => getExpenseDetail('expense-1'),
    ).thenThrow(AppException(failure: detailFailure));
    final expenses = [_expense('expense-1', 'Rent')];
    final container = makeContainer(
      ExpensesState(expenses: expenses, totalCount: 1),
    );
    addTearDown(container.dispose);

    await container
        .read(expensesControllerProvider.notifier)
        .openExpense('expense-1');

    final failed = container.read(expensesControllerProvider);
    expect(failed.detailFailure, detailFailure);
    expect(failed.isDetailLoading, isFalse);
    expect(failed.expenses, expenses);
    expect(failed.listFailure, isNull);

    final detail = _detail('expense-1');
    when(
      () => getExpenseDetail('expense-1'),
    ).thenAnswer((_) async => detail);
    await container
        .read(expensesControllerProvider.notifier)
        .retryExpenseDetail();

    final retried = container.read(expensesControllerProvider);
    expect(retried.selectedExpense, detail);
    expect(retried.detailFailure, isNull);
    expect(retried.expenses, expenses);
  });

  test('stale same-id failure cannot replace reopened detail state', () async {
    final firstRequest = Completer<ExpenseDetail>();
    final secondRequest = Completer<ExpenseDetail>();
    var requestCount = 0;
    when(() => getExpenseDetail('expense-1')).thenAnswer((_) {
      requestCount += 1;
      return requestCount == 1 ? firstRequest.future : secondRequest.future;
    });
    final container = makeContainer(const ExpensesState());
    addTearDown(container.dispose);
    final controller = container.read(expensesControllerProvider.notifier);

    final firstOpening = controller.openExpense('expense-1');
    controller.clearSelectedExpense();
    final secondOpening = controller.openExpense('expense-1');

    final currentDetail = _detail('expense-1');
    secondRequest.complete(currentDetail);
    await secondOpening;

    firstRequest.completeError(
      AppException(
        failure: const Failure.network(message: 'stale failure'),
      ),
    );
    await firstOpening;

    final state = container.read(expensesControllerProvider);
    expect(state.selectedExpenseId, 'expense-1');
    expect(state.selectedExpense, currentDetail);
    expect(state.isDetailLoading, isFalse);
    expect(state.detailFailure, isNull);
  });

  test('clearSelectedExpense removes only detail state', () async {
    final expense = _expense('expense-1', 'Rent');
    final detail = _detail('expense-1');
    final container = makeContainer(
      ExpensesState(
        expenses: [expense],
        totalCount: 1,
        selectedExpenseId: 'expense-1',
        selectedExpense: detail,
      ),
    );
    addTearDown(container.dispose);

    container.read(expensesControllerProvider.notifier).clearSelectedExpense();

    final state = container.read(expensesControllerProvider);
    expect(state.selectedExpenseId, isNull);
    expect(state.selectedExpense, isNull);
    expect(state.detailFailure, isNull);
    expect(state.expenses, [expense]);
  });
}
