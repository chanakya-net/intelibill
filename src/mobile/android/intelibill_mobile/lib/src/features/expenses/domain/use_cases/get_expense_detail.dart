import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/repositories/expense_repository.dart';

class GetExpenseDetail {
  const GetExpenseDetail(this._repository);

  final ExpenseRepository _repository;

  Future<ExpenseDetail> call(String id) {
    return _repository.getExpenseDetail(id);
  }
}
