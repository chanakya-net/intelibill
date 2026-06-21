import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/create_service_request_dto.dart';

void main() {
  group('CreateServiceRequestDto', () {
    test('serializes to JSON with expected keys', () {
      const dto = CreateServiceRequestDto(
        name: 'Repair',
        description: 'Phone repair service',
        price: 499.9,
        hsnCode: '9987',
        taxRatePercent: 18,
        taxIncluded: true,
        isActive: true,
      );

      expect(dto.toJson(), {
        'name': 'Repair',
        'description': 'Phone repair service',
        'price': 499.9,
        'hsnCode': '9987',
        'taxRatePercent': 18,
        'taxIncluded': true,
        'isActive': true,
      });
    });

    test('serializes null optional fields when omitted', () {
      const dto = CreateServiceRequestDto(
        name: 'Consultation',
        price: 250,
        taxRatePercent: 0,
        taxIncluded: false,
        isActive: true,
      );

      expect(dto.toJson(), {
        'name': 'Consultation',
        'description': null,
        'price': 250,
        'hsnCode': null,
        'taxRatePercent': 0,
        'taxIncluded': false,
        'isActive': true,
      });
    });
  });
}
