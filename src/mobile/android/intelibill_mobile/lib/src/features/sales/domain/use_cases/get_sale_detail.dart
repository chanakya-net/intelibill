import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';

class GetSaleDetail {
  const GetSaleDetail(this._repository);

  final SalesRepository _repository;

  Future<SaleDetail> call(String saleId) {
    return _repository.getSaleDetail(saleId);
  }
}
