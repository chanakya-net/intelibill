import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expenses.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetExpenses extends Mock implements GetExpenses {}

class _TestExpensesController extends ExpensesController {
  _TestExpensesController(this._initialState);

  final ExpensesState _initialState;

  @override
  ExpensesState build() => _initialState;
}

ExpenseListItem _expense(String id, String category) {
  return ExpenseListItem(
    id: id,
    amount: 100,
    categoryName: category,
    paidTo: 'Payee',
    expenseDate: DateTime(2026, 7),
    isVoided: false,
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

void main() {
  late _MockGetExpenses getExpenses;

  setUp(() {
    getExpenses = _MockGetExpenses();
  });

  ProviderContainer makeContainer(ExpensesState initialState) {
    return ProviderContainer(
      overrides: [
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(initialState),
        ),
      ],
    );
  }

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
}
