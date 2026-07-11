import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';

interface class ExpenseRepository {
  Future<ExpensePage> getExpenses() {
    throw UnimplementedError();
  }

  Future<ExpenseDetail> getExpenseDetail(String id) {
    throw UnimplementedError();
  }
}
