import 'package:intelibill_mobile/src/features/services/data/dto/service_dto.dart';
import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';

class ServiceMapper {
  static Service toDomain(ServiceDto dto) {
    return Service(
      serviceId: dto.serviceId,
      code: dto.code,
      name: dto.name,
      description: dto.description,
      price: dto.price,
      hsnCode: dto.hsnCode,
      taxRatePercent: dto.taxRatePercent,
      taxIncluded: dto.taxIncluded,
      isActive: dto.isActive,
    );
  }
}
