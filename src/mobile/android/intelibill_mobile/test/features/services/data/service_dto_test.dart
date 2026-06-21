import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/service_dto.dart';

void main() {
  group('ServiceDto', () {
    test('parses full JSON with all fields', () {
      final json = {
        'serviceId': 'svc-123',
        'code': 'SRV-001',
        'name': 'Repair',
        'description': 'Phone repair service',
        'price': 499.90,
        'hsnCode': 'HSN001',
        'taxRatePercent': 18,
        'taxIncluded': true,
        'isActive': true,
      };

      final dto = ServiceDto.fromJson(json);

      expect(dto.serviceId, 'svc-123');
      expect(dto.code, 'SRV-001');
      expect(dto.name, 'Repair');
      expect(dto.description, 'Phone repair service');
      expect(dto.price, 499.90);
      expect(dto.hsnCode, 'HSN001');
      expect(dto.taxRatePercent, 18);
      expect(dto.taxIncluded, true);
      expect(dto.isActive, true);
    });

    test('defaults numeric values when missing', () {
      final json = {
        'serviceId': 'svc-124',
        'code': 'SRV-002',
        'name': 'Consult',
        'taxIncluded': false,
        'isActive': true,
      };

      final dto = ServiceDto.fromJson(json);

      expect(dto.price, 0.0);
      expect(dto.taxRatePercent, 0.0);
    });

    test('handles null optional fields', () {
      final json = {
        'serviceId': 'svc-125',
        'code': 'SRV-003',
        'name': 'Setup',
        'taxIncluded': true,
        'isActive': false,
      };

      final dto = ServiceDto.fromJson(json);

      expect(dto.description, isNull);
      expect(dto.hsnCode, isNull);
      expect(dto.isActive, false);
    });
  });
}
