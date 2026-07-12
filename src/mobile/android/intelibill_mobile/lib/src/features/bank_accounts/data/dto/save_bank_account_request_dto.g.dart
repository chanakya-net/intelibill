// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'save_bank_account_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_SaveBankAccountRequestDto _$SaveBankAccountRequestDtoFromJson(
  Map<String, dynamic> json,
) => _SaveBankAccountRequestDto(
  bankName: json['bankName'] as String,
  accountNumber: json['accountNumber'] as String,
  accountType: json['accountType'] as String,
  ifscCode: json['ifscCode'] as String?,
  accountHolderName: json['accountHolderName'] as String?,
);

Map<String, dynamic> _$SaveBankAccountRequestDtoToJson(
  _SaveBankAccountRequestDto instance,
) => <String, dynamic>{
  'bankName': instance.bankName,
  'accountNumber': instance.accountNumber,
  'accountType': instance.accountType,
  'ifscCode': instance.ifscCode,
  'accountHolderName': instance.accountHolderName,
};
