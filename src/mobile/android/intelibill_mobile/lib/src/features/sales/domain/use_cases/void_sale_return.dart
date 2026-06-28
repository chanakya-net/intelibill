import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';

class VoidSaleReturn {
  const VoidSaleReturn(this._repository);

  final SalesRepository _repository;

  Future<void> call({
    required String saleReturnId,
    required String reason,
  }) {
    return _repository.voidSaleReturn(
      saleReturnId: saleReturnId,
      reason: reason,
    );
  }
}
