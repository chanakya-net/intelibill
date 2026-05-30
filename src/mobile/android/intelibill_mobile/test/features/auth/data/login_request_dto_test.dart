import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/login_request_dto.dart';

void main() {
  group('LoginRequestDto', () {
    test('serializes to json correctly', () {
      const dto = LoginRequestDto(
        identifier: 'test@example.com',
        password: 'password123',
      );

      final json = dto.toJson();

      expect(json['identifier'], 'test@example.com');
      expect(json['password'], 'password123');
    });

    test('parses json correctly', () {
      final json = {
        'identifier': 'test@example.com',
        'password': 'password123',
      };

      final dto = LoginRequestDto.fromJson(json);

      expect(dto.identifier, 'test@example.com');
      expect(dto.password, 'password123');
    });
  });
}
