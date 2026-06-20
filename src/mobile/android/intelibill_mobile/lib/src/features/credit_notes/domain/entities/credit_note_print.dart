import 'package:equatable/equatable.dart';

class CreditNotePrint extends Equatable {
  const CreditNotePrint({
    required this.creditNoteId,
    required this.code,
    required this.status,
    required this.isUsable,
    required this.originalAmount,
    required this.availableBalance,
    required this.issuedAt,
    required this.expiresAt,
    required this.saleId,
    required this.invoiceNumber,
    required this.saleReturnId,
    required this.returnNumber,
    required this.customerDisplayName,
    required this.reason,
    required this.voidReason,
  });

  final String creditNoteId;
  final String code;
  final String status;
  final bool isUsable;
  final double originalAmount;
  final double availableBalance;
  final DateTime issuedAt;
  final DateTime? expiresAt;
  final String saleId;
  final String invoiceNumber;
  final String saleReturnId;
  final String returnNumber;
  final String customerDisplayName;
  final String reason;
  final String? voidReason;

  @override
  List<Object?> get props => [
    creditNoteId,
    code,
    status,
    isUsable,
    originalAmount,
    availableBalance,
    issuedAt,
    expiresAt,
    saleId,
    invoiceNumber,
    saleReturnId,
    returnNumber,
    customerDisplayName,
    reason,
    voidReason,
  ];
}
