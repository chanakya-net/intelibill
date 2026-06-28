import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_return.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';

class RecordSaleReturn {
  const RecordSaleReturn(this._repository);

  final SalesRepository _repository;

  Future<SaleDetail> call({
    required String saleId,
    required RecordSaleReturnRequest request,
  }) {
    return _repository.recordSaleReturn(saleId: saleId, request: request);
  }
}
