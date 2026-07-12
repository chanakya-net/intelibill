import 'package:intelibill_mobile/src/features/bank_accounts/data/dto/bank_account_dto.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';

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
}
