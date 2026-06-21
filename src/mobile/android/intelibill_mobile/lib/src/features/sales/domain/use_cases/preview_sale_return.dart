import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_return.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';

class PreviewSaleReturn {
  const PreviewSaleReturn(this._repository);

  final SalesRepository _repository;

  Future<SaleReturnPreview> call({
    required String saleId,
    required PreviewSaleReturnRequest request,
  }) {
    return _repository.previewSaleReturn(saleId: saleId, request: request);
  }
}
