import 'package:equatable/equatable.dart';

class ExpenseListItem extends Equatable {
  const ExpenseListItem({
    required this.id,
    required this.amount,
    required this.categoryName,
    required this.paidTo,
    required this.expenseDate,
    required this.isVoided,
  });

  final String id;
  final double amount;
  final String categoryName;
  final String paidTo;
  final DateTime expenseDate;
  final bool isVoided;

  @override
  List<Object?> get props => [
    id,
    amount,
    categoryName,
    paidTo,
    expenseDate,
    isVoided,
  ];
}
