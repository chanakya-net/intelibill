import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/data/mappers/bank_account_mapper.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';

void main() {
  test('maps a save request and serializes blank optional values as null', () {
    const request = SaveBankAccountRequest(
      bankName: ' HDFC ',
      accountNumber: '001234',
      accountType: 'Savings',
      ifscCode: ' ',
      accountHolderName: ' ',
    );

    final dto = BankAccountMapper.toSaveDto(request);

    expect(dto.bankName, 'HDFC');
    expect(dto.accountNumber, '001234');
    expect(dto.accountType, 'Savings');
    expect(dto.ifscCode, isNull);
    expect(dto.accountHolderName, isNull);
    expect(dto.toJson()['ifscCode'], isNull);
    expect(dto.toJson()['accountHolderName'], isNull);
  });
}
