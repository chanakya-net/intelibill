// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'bank_account_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_BankAccountDto _$BankAccountDtoFromJson(Map<String, dynamic> json) =>
    _BankAccountDto(
      id: json['id'] as String,
      bankName: json['bankName'] as String,
      accountNumber: json['accountNumber'] as String,
      accountType: json['accountType'] as String?,
      ifscCode: json['ifscCode'] as String?,
      accountHolderName: json['accountHolderName'] as String?,
    );

Map<String, dynamic> _$BankAccountDtoToJson(_BankAccountDto instance) =>
    <String, dynamic>{
      'id': instance.id,
      'bankName': instance.bankName,
      'accountNumber': instance.accountNumber,
      'accountType': instance.accountType,
      'ifscCode': instance.ifscCode,
      'accountHolderName': instance.accountHolderName,
    };
