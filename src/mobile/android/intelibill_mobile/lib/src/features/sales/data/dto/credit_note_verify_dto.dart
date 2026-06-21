import 'package:intelibill_mobile/src/features/sales/domain/entities/credit_note.dart';

class CreditNoteVerifyDto {
  const CreditNoteVerifyDto({
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

  factory CreditNoteVerifyDto.fromJson(Map<String, dynamic> json) {
    return CreditNoteVerifyDto(
      creditNoteId: json['creditNoteId'] as String,
      code: json['code'] as String,
      balance: (json['balance'] as num).toDouble(),
      customerId: json['customerId'] as String?,
      customerName: json['customerName'] as String?,
    );
  }

  CreditNoteVerifyResult toDomain() {
    return CreditNoteVerifyResult(
      creditNoteId: creditNoteId,
      code: code,
      balance: balance,
      customerId: customerId,
      customerName: customerName,
    );
  }
}
