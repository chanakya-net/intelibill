import 'package:intelibill_mobile/src/features/sales/domain/entities/record_sale.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';

class RecordSale {
  const RecordSale(this._repository);

  final SalesRepository _repository;

  Future<SaleDetail> call({
    required RecordSaleRequest request,
  }) {
    return _repository.recordSale(request: request);
  }
}
