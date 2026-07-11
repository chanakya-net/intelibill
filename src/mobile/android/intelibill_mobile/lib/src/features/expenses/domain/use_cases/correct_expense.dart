import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/repositories/expense_repository.dart';

class CorrectExpense {
  const CorrectExpense(this._repository);

  final ExpenseRepository _repository;

  Future<ExpenseDetail> call(
    String id, {
    required String categoryName,
    required double amount,
    required String paidTo,
    String? description,
    required DateTime expenseDate,
  }) {
    return _repository.correctExpense(
      id,
      categoryName: categoryName,
      amount: amount,
      paidTo: paidTo,
      description: description,
      expenseDate: expenseDate,
    );
  }
}
