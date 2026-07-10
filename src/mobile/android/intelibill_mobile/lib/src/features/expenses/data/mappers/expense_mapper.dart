import 'package:intelibill_mobile/src/features/expenses/data/dto/expense_list_item_dto.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expenses_page_dto.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';

class ExpenseMapper {
  const ExpenseMapper._();

  static ExpenseListItem toDomain(ExpenseListItemDto dto) {
    return ExpenseListItem(
      id: dto.id,
      amount: dto.amount,
      categoryName: dto.categoryName,
      paidTo: dto.paidTo,
      expenseDate: dto.expenseDate,
      isVoided: dto.isVoided,
    );
  }

  static ExpensesPage pageToDomain(ExpensesPageDto dto) {
    return ExpensesPage(
      items: dto.items.map(toDomain).toList(),
      totalCount: dto.totalCount,
      pageNumber: dto.pageNumber,
      pageSize: dto.pageSize,
    );
  }
}
