import 'package:intelibill_mobile/src/features/expenses/data/dto/expense_category_dto.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expense_detail_dto.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expense_list_item_dto.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expenses_page_dto.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_category.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';

class ExpenseMapper {
  static ExpenseDetail detailToDomain(ExpenseDetailDto dto) {
    return ExpenseDetail(
      id: dto.id,
      shopId: dto.shopId,
      categoryId: dto.categoryId,
      categoryName: dto.categoryName,
      amount: dto.amount,
      paidTo: dto.paidTo,
      description: dto.description,
      expenseDate: dto.expenseDate,
      actorUserId: dto.actorUserId,
      isVoided: dto.isVoided,
      originalExpenseId: dto.originalExpenseId,
      supplierLedgerEntryId: dto.supplierLedgerEntryId,
      createdAt: dto.createdAt.toLocal(),
    );
  }

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

  static ExpensePage pageToDomain(ExpensesPageDto dto) {
    return ExpensePage(
      items: dto.items.map(toDomain).toList(),
      totalCount: dto.totalCount,
      pageNumber: dto.pageNumber,
      pageSize: dto.pageSize,
    );
  }

  static ExpenseCategory categoryToDomain(ExpenseCategoryDto dto) {
    return ExpenseCategory(id: dto.id, name: dto.name);
  }
}
