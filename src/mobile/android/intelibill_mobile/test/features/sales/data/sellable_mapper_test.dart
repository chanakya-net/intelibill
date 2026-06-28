import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sellable_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/mappers/sellable_mapper.dart';

void main() {
  group('SellableMapper', () {
    test('maps service sellable dto to domain model', () {
      final dto = SellableDto.fromJson({
        'kind': 'Service',
        'serviceId': 'svc-1',
        'code': 'SRV-001',
        'name': 'Installation',
        'description': 'On-site setup',
        'price': 150.0,
        'taxRatePercent': 18.0,
        'taxIncluded': true,
      });

      final sellable = SellableMapper.toDomain(dto);

      expect(sellable.id, 'svc-1');
      expect(sellable.kind, 'Service');
      expect(sellable.name, 'Installation');
      expect(sellable.barcode, 'SRV-001');
      expect(sellable.stock, 0);
      expect(sellable.price, 150.0);
      expect(sellable.taxRatePercent, 18.0);
      expect(sellable.taxIncluded, isTrue);
      expect(sellable.isService, isTrue);
    });
  });
}
