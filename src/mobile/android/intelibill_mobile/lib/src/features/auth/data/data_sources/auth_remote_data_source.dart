import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/auth_result_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/login_request_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/refresh_token_request_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/revoke_token_request_dto.dart';

abstract interface class AuthRemoteDataSource {
  Future<AuthResultDto> login({
    required String identifier,
    required String password,
  });
  Future<AuthResultDto> refreshToken({required String refreshToken});
  Future<void> revokeToken({required String refreshToken});
}

class AuthRemoteDataSourceImpl implements AuthRemoteDataSource {
  AuthRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  static const String _loginEndpoint = '/auth/login';
  static const String _refreshTokenEndpoint = '/auth/token/refresh';
  static const String _revokeTokenEndpoint = '/auth/token/revoke';

  @override
  Future<AuthResultDto> login({
    required String identifier,
    required String password,
  }) async {
    final request = LoginRequestDto(identifier: identifier, password: password);
    final response = await _apiClient.post<Map<String, dynamic>>(
      _loginEndpoint,
      data: request.toJson(),
    );
    return AuthResultDto.fromJson(response.data!);
  }

  @override
  Future<AuthResultDto> refreshToken({required String refreshToken}) async {
    final request = RefreshTokenRequestDto(refreshToken: refreshToken);
    final response = await _apiClient.post<Map<String, dynamic>>(
      _refreshTokenEndpoint,
      data: request.toJson(),
    );
    return AuthResultDto.fromJson(response.data!);
  }

  @override
  Future<void> revokeToken({required String refreshToken}) async {
    final request = RevokeTokenRequestDto(refreshToken: refreshToken);
    await _apiClient.post<void>(
      _revokeTokenEndpoint,
      data: request.toJson(),
    );
  }
}
