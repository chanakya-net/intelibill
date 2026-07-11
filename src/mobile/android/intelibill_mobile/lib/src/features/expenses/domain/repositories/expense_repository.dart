import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_category.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';

interface class ExpenseRepository {
  Future<ExpensePage> getExpenses({int? page, int? pageSize, String? search}) {
    throw UnimplementedError();
  }

  Future<ExpenseDetail> getExpenseDetail(String id) {
    throw UnimplementedError();
  }

  Future<List<ExpenseCategory>> getCategories() {
    throw UnimplementedError();
  }

  Future<ExpenseDetail> recordExpense({
    required String categoryName,
    required double amount,
    required String paidTo,
    String? description,
    required DateTime expenseDate,
  }) {
    throw UnimplementedError();
  }
}
