import 'package:equatable/equatable.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';

class ExpensePage extends Equatable {
  const ExpensePage({
    required this.items,
    required this.totalCount,
    required this.pageNumber,
    required this.pageSize,
  });

  final List<ExpenseListItem> items;
  final int totalCount;
  final int pageNumber;
  final int pageSize;

  @override
  List<Object?> get props => [items, totalCount, pageNumber, pageSize];

  ExpensePage copyWith({
    List<ExpenseListItem>? items,
    int? totalCount,
    int? pageNumber,
    int? pageSize,
  }) {
    return ExpensePage(
      items: items ?? this.items,
      totalCount: totalCount ?? this.totalCount,
      pageNumber: pageNumber ?? this.pageNumber,
      pageSize: pageSize ?? this.pageSize,
    );
  }
}
