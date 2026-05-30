import 'package:intelibill_mobile/src/features/shops/domain/entities/add_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/repositories/shop_repository.dart';

class AddBankAccountUseCase {
  const AddBankAccountUseCase(this._repository);

  final ShopRepository _repository;

  Future<void> call(AddBankAccountRequest request) {
    return _repository.addBankAccount(request);
  }
}
