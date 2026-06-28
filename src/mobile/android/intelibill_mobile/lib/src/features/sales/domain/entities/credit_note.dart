import 'package:equatable/equatable.dart';

class CreditNoteVerifyResult extends Equatable {
  const CreditNoteVerifyResult({
    required this.creditNoteId,
    required this.code,
    required this.balance,
    this.customerId,
    this.customerName,
  });

  final String creditNoteId;
  final String code;
  final double balance;
  final String? customerId;
  final String? customerName;

  @override
  List<Object?> get props => [
    creditNoteId,
    code,
    balance,
    customerId,
    customerName,
  ];
}

class AppliedCreditNote extends Equatable {
  const AppliedCreditNote({
    required this.creditNoteId,
    required this.code,
    required this.balance,
    required this.amount,
    this.customerId,
    this.customerName,
  });
  static const _unset = Object();

  final String creditNoteId;
  final String code;
  final double balance;
  final double amount;
  final String? customerId;
  final String? customerName;

  AppliedCreditNote copyWith({
    double? amount,
    Object? customerId = _unset,
    Object? customerName = _unset,
  }) {
    return AppliedCreditNote(
      creditNoteId: creditNoteId,
      code: code,
      balance: balance,
      amount: amount ?? this.amount,
      customerId: identical(customerId, _unset)
          ? this.customerId
          : customerId as String?,
      customerName: identical(customerName, _unset)
          ? this.customerName
          : customerName as String?,
    );
  }

  @override
  List<Object?> get props => [
    creditNoteId,
    code,
    balance,
    amount,
    customerId,
    customerName,
  ];
}
