import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_preview.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';

class PreviewSale {
  const PreviewSale(this._repository);

  final SalesRepository _repository;

  Future<SalePreview> call({
    required PreviewSaleRequest request,
  }) {
    return _repository.previewSale(request: request);
  }
}
