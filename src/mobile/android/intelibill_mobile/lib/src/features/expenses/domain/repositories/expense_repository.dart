import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';

interface class ExpenseRepository {
  Future<ExpensesPage> getExpenses() {
    throw UnimplementedError();
  }
}
