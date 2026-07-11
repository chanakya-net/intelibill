import 'package:equatable/equatable.dart';

class ExpenseDetail extends Equatable {
  const ExpenseDetail({
    required this.id,
    required this.shopId,
    required this.categoryId,
    required this.categoryName,
    required this.amount,
    required this.paidTo,
    required this.expenseDate,
    required this.actorUserId,
    required this.isVoided,
    required this.createdAt,
    this.description,
    this.originalExpenseId,
    this.supplierLedgerEntryId,
  });

  final String id;
  final String shopId;
  final String categoryId;
  final String categoryName;
  final double amount;
  final String paidTo;
  final String? description;
  final DateTime expenseDate;
  final String actorUserId;
  final bool isVoided;
  final String? originalExpenseId;
  final String? supplierLedgerEntryId;
  final DateTime createdAt;

  @override
  List<Object?> get props => [
    id,
    shopId,
    categoryId,
    categoryName,
    amount,
    paidTo,
    description,
    expenseDate,
    actorUserId,
    isVoided,
    originalExpenseId,
    supplierLedgerEntryId,
    createdAt,
  ];
}
