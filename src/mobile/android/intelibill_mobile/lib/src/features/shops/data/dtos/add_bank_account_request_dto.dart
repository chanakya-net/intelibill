import 'package:freezed_annotation/freezed_annotation.dart';

part 'add_bank_account_request_dto.freezed.dart';
part 'add_bank_account_request_dto.g.dart';

@freezed
sealed class AddBankAccountRequestDto with _$AddBankAccountRequestDto {
  const factory AddBankAccountRequestDto({
    @JsonKey(name: 'bankName') required String bankName,
    @JsonKey(name: 'accountNumber') required String accountNumber,
    @JsonKey(name: 'accountType') required String accountType,
    @JsonKey(name: 'ifscCode') required String ifscCode,
    @JsonKey(name: 'accountHolderName') required String accountHolderName,
  }) = _AddBankAccountRequestDto;

  factory AddBankAccountRequestDto.fromJson(Map<String, dynamic> json) =>
      _$AddBankAccountRequestDtoFromJson(json);
}
