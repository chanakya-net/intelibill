import 'package:intelibill_mobile/src/features/bank_accounts/data/dto/bank_account_dto.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/data/dto/save_bank_account_request_dto.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';

abstract final class BankAccountMapper {
  static BankAccount toDomain(BankAccountDto dto) {
    return BankAccount(
      id: dto.id,
      bankName: dto.bankName,
      accountNumber: dto.accountNumber,
      accountType: dto.accountType,
      ifscCode: dto.ifscCode,
      accountHolderName: dto.accountHolderName,
    );
  }

  static SaveBankAccountRequestDto toSaveDto(SaveBankAccountRequest request) {
    return SaveBankAccountRequestDto(
      bankName: request.bankName.trim(),
      accountNumber: request.accountNumber.trim(),
      accountType: request.accountType,
      ifscCode: _optional(request.ifscCode)?.toUpperCase(),
      accountHolderName: _optional(request.accountHolderName),
    );
  }

  static String? _optional(String? value) {
    final trimmed = value?.trim() ?? '';
    return trimmed.isEmpty ? null : trimmed;
  }
}
