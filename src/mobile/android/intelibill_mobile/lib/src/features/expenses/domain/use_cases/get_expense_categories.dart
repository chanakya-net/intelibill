import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_category.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/repositories/expense_repository.dart';

class GetExpenseCategories {
  const GetExpenseCategories(this._repository);

  final ExpenseRepository _repository;

  Future<List<ExpenseCategory>> call() => _repository.getCategories();
}
