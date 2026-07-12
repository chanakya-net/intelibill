import 'package:freezed_annotation/freezed_annotation.dart';

part 'save_bank_account_request_dto.freezed.dart';
part 'save_bank_account_request_dto.g.dart';

@freezed
sealed class SaveBankAccountRequestDto with _$SaveBankAccountRequestDto {
  const factory SaveBankAccountRequestDto({
    @JsonKey(name: 'bankName') required String bankName,
    @JsonKey(name: 'accountNumber') required String accountNumber,
    @JsonKey(name: 'accountType') required String accountType,
    @JsonKey(name: 'ifscCode') String? ifscCode,
    @JsonKey(name: 'accountHolderName') String? accountHolderName,
  }) = _SaveBankAccountRequestDto;

  factory SaveBankAccountRequestDto.fromJson(Map<String, dynamic> json) =>
      _$SaveBankAccountRequestDtoFromJson(json);
}
