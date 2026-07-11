import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/repositories/expense_repository.dart';

class GetExpenses {
  const GetExpenses(this._repository);

  final ExpenseRepository _repository;

  Future<ExpensePage> call() => _repository.getExpenses();
}
