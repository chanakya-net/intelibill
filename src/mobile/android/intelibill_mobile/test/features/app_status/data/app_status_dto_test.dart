import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/app_status/data/dto/app_status_dto.dart';
import 'package:intelibill_mobile/src/features/app_status/data/mappers/app_status_mapper.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/entities/app_status.dart';

void main() {
  group('AppStatusDto', () {
    test('parses json and maps to domain entity', () {
      final dto = AppStatusDto.fromJson({
        'statusText': 'Ready',
        'apiBaseUrl': 'https://api.example.com',
        'timestamp': '2026-05-14T10:00:00.000Z',
        'environment': 'staging',
      });

      expect(dto.statusText, 'Ready');
      expect(dto.apiBaseUrl, 'https://api.example.com');
      expect(dto.timestamp, DateTime.utc(2026, 5, 14, 10));
      expect(dto.environment, 'staging');

      expect(
        AppStatusMapper.toDomain(dto),
        AppStatus(
          statusText: 'Ready',
          apiBaseUrl: 'https://api.example.com',
          timestamp: DateTime.utc(2026, 5, 14, 10),
          environment: 'staging',
        ),
      );
    });

    test('serializes back to json', () {
      final dto = AppStatusDto(
        statusText: 'Ready',
        apiBaseUrl: 'https://api.example.com',
        timestamp: DateTime.utc(2026, 5, 14, 10),
        environment: 'test',
      );

      final json = dto.toJson();

      expect(json['statusText'], 'Ready');
      expect(json['apiBaseUrl'], 'https://api.example.com');
      expect(json['timestamp'], '2026-05-14T10:00:00.000Z');
      expect(json['environment'], 'test');
    });
  });
}
