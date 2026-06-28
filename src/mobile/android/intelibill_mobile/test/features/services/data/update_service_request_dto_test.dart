import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/update_service_request_dto.dart';

void main() {
  group('UpdateServiceRequestDto', () {
    test('serializes to JSON with expected keys', () {
      const dto = UpdateServiceRequestDto(
        name: 'Repair Updated',
        description: 'Updated description',
        price: 599,
        hsnCode: '9988',
        taxRatePercent: 12,
        taxIncluded: false,
      );

      expect(dto.toJson(), {
        'name': 'Repair Updated',
        'description': 'Updated description',
        'price': 599,
        'hsnCode': '9988',
        'taxRatePercent': 12,
        'taxIncluded': false,
      });
    });

    test('serializes null optional fields when omitted', () {
      const dto = UpdateServiceRequestDto(
        name: 'Consultation',
        price: 250,
        taxRatePercent: 0,
        taxIncluded: true,
      );

      expect(dto.toJson(), {
        'name': 'Consultation',
        'description': null,
        'price': 250,
        'hsnCode': null,
        'taxRatePercent': 0,
        'taxIncluded': true,
      });
    });
  });
}
