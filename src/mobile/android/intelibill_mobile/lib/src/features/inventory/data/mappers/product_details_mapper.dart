import 'package:intelibill_mobile/src/features/inventory/data/dto/product_details_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/product_details.dart';

class ProductDetailsMapper {
  const ProductDetailsMapper._();

  static ProductDetails toDomain(ProductDetailsDto dto) {
    return ProductDetails(
      name: dto.name,
      description: dto.description,
      uom: dto.uom,
      costPrice: dto.costPrice,
      mrp: dto.mrp,
      salesPrice: dto.salesPrice,
      supplierId: dto.supplierId,
      supplierName: dto.supplierName,
      taxIncluded: dto.taxIncluded,
      taxRatePercent: dto.taxRatePercent,
    );
  }
}
