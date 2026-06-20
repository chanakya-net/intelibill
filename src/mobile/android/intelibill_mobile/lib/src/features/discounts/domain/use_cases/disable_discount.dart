import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discount_repository.dart';

class DisableDiscount {
  const DisableDiscount(this._repository);

  final DiscountRepository _repository;

  Future<void> call({required String discountId}) {
    return _repository.disable(discountId: discountId);
  }
}
