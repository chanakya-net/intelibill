import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expenses.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetExpenses extends Mock implements GetExpenses {}

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

void main() {
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
}
