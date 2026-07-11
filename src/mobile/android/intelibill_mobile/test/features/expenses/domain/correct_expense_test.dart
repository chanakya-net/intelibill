import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/repositories/expense_repository.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/correct_expense.dart';
import 'package:mocktail/mocktail.dart';

class _MockExpenseRepository extends Mock implements ExpenseRepository {}

void main() {
  late _MockExpenseRepository repository;
  late CorrectExpense useCase;

  setUp(() {
    repository = _MockExpenseRepository();
    useCase = CorrectExpense(repository);
  });

  test('delegates to repository with provided params', () async {
    final replacement = ExpenseDetail(
      id: 'expense-2',
      shopId: 'shop-1',
      categoryId: 'utilities',
      categoryName: 'Utilities',
      amount: 150,
      paidTo: 'Power Co',
      expenseDate: DateTime(2026, 7, 3),
      actorUserId: 'user-1',
      isVoided: false,
      createdAt: DateTime(2026, 7, 3, 8, 30),
      originalExpenseId: 'expense-1',
    );

    when(
      () => repository.correctExpense(
        'expense-1',
        categoryName: 'Utilities',
        amount: 150,
        paidTo: 'Power Co',
        expenseDate: DateTime(2026, 7, 3),
      ),
    ).thenAnswer((_) async => replacement);

    final result = await useCase(
      'expense-1',
      categoryName: 'Utilities',
      amount: 150,
      paidTo: 'Power Co',
      expenseDate: DateTime(2026, 7, 3),
    );

    expect(result.id, 'expense-2');
    expect(result.originalExpenseId, 'expense-1');
    verify(
      () => repository.correctExpense(
        'expense-1',
        categoryName: 'Utilities',
        amount: 150,
        paidTo: 'Power Co',
        expenseDate: DateTime(2026, 7, 3),
      ),
    ).called(1);
  });
}
