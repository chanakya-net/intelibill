import 'package:equatable/equatable.dart';

class CreditNote extends Equatable {
  const CreditNote({
    required this.creditNoteId,
    required this.code,
    required this.status,
    required this.originalAmount,
    required this.availableBalance,
    required this.expiresAt,
    required this.isVoided,
    required this.saleReturnId,
    required this.reason,
    required this.voidReason,
    required this.returnNumber,
    required this.invoiceNumber,
    required this.customerName,
  });

  final String creditNoteId;
  final String code;
  final String status;
  final double originalAmount;
  final double availableBalance;
  final DateTime? expiresAt;
  final bool isVoided;
  final String saleReturnId;
  final String reason;
  final String? voidReason;
  final String returnNumber;
  final String invoiceNumber;
  final String? customerName;

  bool get isActive => status.toLowerCase() == 'active' && !isVoided;

  @override
  List<Object?> get props => [
    creditNoteId,
    code,
    status,
    originalAmount,
    availableBalance,
    expiresAt,
    isVoided,
    saleReturnId,
    reason,
    voidReason,
    returnNumber,
    invoiceNumber,
    customerName,
  ];
}
