import 'package:intelibill_mobile/src/features/suppliers/data/dto/supplier_dto.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';

class SupplierMapper {
  static Supplier toDomain(SupplierDto dto) {
    return Supplier(
      supplierId: dto.supplierId,
      name: dto.name,
      contactPersonName: dto.contactPersonName,
      contactPersonPhone: dto.contactPersonPhone,
      address: dto.address,
      city: dto.city,
      state: dto.state,
      pin: dto.pin,
      isSystem: dto.isSystem,
      isActive: dto.isActive,
      isPreferred: dto.isPreferred,
      balanceDue: dto.balanceDue,
    );
  }
}
