import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/revoke_token_request_dto.dart';

void main() {
  group('RevokeTokenRequestDto', () {
    test('serializes to json correctly', () {
      const dto = RevokeTokenRequestDto(refreshToken: 'refresh_123');

      final json = dto.toJson();

      expect(json['refreshToken'], 'refresh_123');
    });

    test('parses json correctly', () {
      final json = <String, dynamic>{'refreshToken': 'refresh_123'};

      final dto = RevokeTokenRequestDto.fromJson(json);

      expect(dto.refreshToken, 'refresh_123');
    });
  });
}
