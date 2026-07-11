import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/repositories/expense_repository.dart';

class RecordExpense {
  const RecordExpense(this._repository);

  final ExpenseRepository _repository;

  Future<ExpenseDetail> call({
    required String categoryName,
    required double amount,
    required String paidTo,
    String? description,
    required DateTime expenseDate,
  }) {
    return _repository.recordExpense(
      categoryName: categoryName,
      amount: amount,
      paidTo: paidTo,
      description: description,
      expenseDate: expenseDate,
    );
  }
}
