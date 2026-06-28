import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/service_dto.dart';
import 'package:intelibill_mobile/src/features/services/data/mappers/service_mapper.dart';
import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';

void main() {
  group('ServiceMapper', () {
    test('maps dto to domain entity', () {
      const dto = ServiceDto(
        serviceId: 'svc-1',
        code: 'SRV-001',
        name: 'Repair',
        description: 'Desc',
        price: 100,
        hsnCode: 'HSN',
        taxRatePercent: 18,
        taxIncluded: true,
        isActive: false,
      );

      final service = ServiceMapper.toDomain(dto);

      expect(service, isA<Service>());
      expect(service.serviceId, dto.serviceId);
      expect(service.code, dto.code);
      expect(service.name, dto.name);
      expect(service.description, dto.description);
      expect(service.price, dto.price);
      expect(service.hsnCode, dto.hsnCode);
      expect(service.taxRatePercent, dto.taxRatePercent);
      expect(service.taxIncluded, dto.taxIncluded);
      expect(service.isActive, dto.isActive);
    });
  });
}
