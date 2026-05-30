// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'add_bank_account_request_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_AddBankAccountRequestDto _$AddBankAccountRequestDtoFromJson(
  Map<String, dynamic> json,
) => _AddBankAccountRequestDto(
  bankName: json['bankName'] as String,
  accountNumber: json['accountNumber'] as String,
  accountType: json['accountType'] as String,
  ifscCode: json['ifscCode'] as String,
  accountHolderName: json['accountHolderName'] as String,
);

Map<String, dynamic> _$AddBankAccountRequestDtoToJson(
  _AddBankAccountRequestDto instance,
) => <String, dynamic>{
  'bankName': instance.bankName,
  'accountNumber': instance.accountNumber,
  'accountType': instance.accountType,
  'ifscCode': instance.ifscCode,
  'accountHolderName': instance.accountHolderName,
};
