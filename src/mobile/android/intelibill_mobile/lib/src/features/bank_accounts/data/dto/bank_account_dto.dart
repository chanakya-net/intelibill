import 'package:freezed_annotation/freezed_annotation.dart';

part 'bank_account_dto.freezed.dart';
part 'bank_account_dto.g.dart';

@freezed
sealed class BankAccountDto with _$BankAccountDto {
  const factory BankAccountDto({
    @JsonKey(name: 'id') required String id,
    @JsonKey(name: 'bankName') required String bankName,
    @JsonKey(name: 'accountNumber') required String accountNumber,
    @JsonKey(name: 'accountType') String? accountType,
    @JsonKey(name: 'ifscCode') String? ifscCode,
    @JsonKey(name: 'accountHolderName') String? accountHolderName,
  }) = _BankAccountDto;

  factory BankAccountDto.fromJson(Map<String, dynamic> json) =>
      _$BankAccountDtoFromJson(json);
}
