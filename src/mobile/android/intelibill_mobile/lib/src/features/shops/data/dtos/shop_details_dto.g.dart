// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'shop_details_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_ShopDetailsDto _$ShopDetailsDtoFromJson(Map<String, dynamic> json) =>
    _ShopDetailsDto(
      shopId: json['shopId'] as String,
      name: json['name'] as String,
      address: json['address'] as String,
      city: json['city'] as String,
      state: json['state'] as String,
      pincode: json['pincode'] as String,
      contactPerson: json['contactPerson'] as String?,
      mobileNumber: json['mobileNumber'] as String?,
      gstNumber: json['gstNumber'] as String?,
      bankName: json['bankName'] as String?,
      bankAccountNumber: json['bankAccountNumber'] as String?,
      bankAccountType: json['bankAccountType'] as String?,
      ifscCode: json['ifscCode'] as String?,
      accountHolderName: json['accountHolderName'] as String?,
    );

Map<String, dynamic> _$ShopDetailsDtoToJson(_ShopDetailsDto instance) =>
    <String, dynamic>{
      'shopId': instance.shopId,
      'name': instance.name,
      'address': instance.address,
      'city': instance.city,
      'state': instance.state,
      'pincode': instance.pincode,
      'contactPerson': instance.contactPerson,
      'mobileNumber': instance.mobileNumber,
      'gstNumber': instance.gstNumber,
      'bankName': instance.bankName,
      'bankAccountNumber': instance.bankAccountNumber,
      'bankAccountType': instance.bankAccountType,
      'ifscCode': instance.ifscCode,
      'accountHolderName': instance.accountHolderName,
    };
